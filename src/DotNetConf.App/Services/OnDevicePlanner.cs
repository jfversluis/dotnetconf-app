using DotNetConf.Core;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
#if IOS
using Microsoft.Maui.Essentials.AI;
#endif

namespace DotNetConf.App.Services;

public sealed record PlannerResult(ScheduleProposal? Proposal, string Message);

public sealed class OnDevicePlanner(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory)
{
    public static string? UnsupportedReason =>
#if IOS
        OperatingSystem.IsIOSVersionAtLeast(26) ? null :
        "This spike needs iOS 26 or later with Apple Intelligence enabled and its model downloaded.";
#else
        "On-device planning is currently available only on supported Apple Intelligence iPhones running iOS 26+. Android inference is not available in this package.";
#endif

    public async Task CheckReadinessAsync(CancellationToken cancellationToken)
    {
        using var client = CreateClient(null);
        // This package has no public availability API. A real, short local inference
        // checks model readiness, not just the OS version or device name.
        await client.GetResponseAsync("Reply with READY.", new ChatOptions { MaxOutputTokens = 16 }, cancellationToken);
    }

    public async Task<PlannerResult> ProposeAsync(ConferenceDocument conference, string request,
        TimeZoneInfo zone, string[] savedIds, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<PlannerTools>();
        tools.Initialize(conference, zone, savedIds);
        using var client = CreateClient(scope.ServiceProvider);
        var first = TimeZoneInfo.ConvertTime(conference.Sessions.Min(s => s.StartsAt), zone);
        try
        {
            // The native preview constrains the shape but does not include the
            // schema descriptions in its prompt, so send those explicitly too.
            var interpretationSchema = PlannerSchemas.Interpretation(conference);
            var extracted = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.System,
                    $"Turn a software conference attendee's request into query arguments, not recommendations. Never add names or time restrictions. Use none for unmentioned constraints. A topic is not a person. Respond using this schema and its field descriptions:\n{interpretationSchema.GetRawText()}"),
                 new ChatMessage(ChatRole.User, request)],
                new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(interpretationSchema),
                    MaxOutputTokens = 500, Temperature = 0 }, cancellationToken);
            var interpretation = JsonSerializer.Deserialize(extracted.Text, PlannerJsonContext.Default.PlannerInterpretation)
                ?? throw new InvalidDataException("The model returned no interpretation.");
            if (interpretation.SpeakerNames is null
                || interpretation.AvailabilityText is null
                || interpretation.SearchQuery is null
                || interpretation.Quantity is not ("all" or "some"))
                throw new InvalidDataException("The model returned missing or invalid preference fields.");
            AvailabilityWindow[] windows = [];
            if (interpretation.HasAvailability)
            {
                var available = await client.GetResponseAsync(
                    [new ChatMessage(ChatRole.System, $"Extract ONLY the allowed time windows. Conference year: {first:yyyy}. Local time means {zone.Id}; use zone local for local or unspecified times, and UTC for UTC. Never convert the clock times. Respond with JSON using this schema:\n{PlannerSchemas.Availability.GetRawText()}"),
                     new ChatMessage(ChatRole.User, request)],
                    new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(PlannerSchemas.Availability),
                        MaxOutputTokens = 800, Temperature = 0 }, cancellationToken);
                windows = JsonSerializer.Deserialize(available.Text, PlannerJsonContext.Default.PlannerAvailability)?.ToWindows(zone)
                    ?? throw new InvalidDataException("The model returned no availability.");
            }
            var (speakerNames, query) = interpretation.Ground(conference, request);
            var allTopics = false;
            if (speakerNames.Length > 0 || PlannerInterpretation.HasAllQuantifier(request))
            {
                var topicSchema = PlannerSchemas.Topic;
                var topical = await client.GetResponseAsync(
                    [new ChatMessage(ChatRole.System,
                        $"Extract only TOPIC interests and their quantity, separately from speaker requests. A combined request needs both groups, not just their intersection. For speaker-only requests use text none and quantity some. Respond using this schema:\n{topicSchema.GetRawText()}"),
                     new ChatMessage(ChatRole.User, request)],
                    new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(topicSchema),
                        MaxOutputTokens = 300, Temperature = 0 }, cancellationToken);
                var topic = JsonSerializer.Deserialize(topical.Text, PlannerJsonContext.Default.PlannerTopicRequest)
                    ?? throw new InvalidDataException("The model returned no topic interpretation.");
                if (topic.Text is null || topic.Quantity is not ("all" or "some"))
                    throw new InvalidDataException("The model returned invalid topic fields.");
                var hasInterest = topic.HasInterest(conference, request);
                (_, query) = Agenda.GroundPlannerQuery(conference, request, speakerNames,
                    hasInterest ? query : "");
                allTopics = hasInterest && topic.RequestsAll(request);
            }
            var captured = tools.CapturePreferences(speakerNames, windows,
                interpretation.RequestsAllSpeakers(request) && speakerNames.Length > 0,
                allTopics && query.Length > 0 ? query : null);
            if (tools.Preferences is not { } preferences)
                return new(null, captured);
            cancellationToken.ThrowIfCancellationRequested();
            var speakerErrors = SchedulePlanner.Review(conference, preferences, zone, savedIds).Errors
                .Where(error => error.StartsWith("Speaker not found", StringComparison.Ordinal)
                    || error.StartsWith("Ambiguous speaker", StringComparison.Ordinal)).ToArray();
            if (speakerErrors.Length > 0)
                return new(preferences, string.Join("\n", speakerErrors));
            // Dispatch model-selected queries explicitly. Native automatic tool calling
            // in this preview can repeat callbacks indefinitely inside a single request.
            var functions = tools.CreateFunctions().OfType<AIFunction>().ToDictionary(f => f.Name);
            var results = new List<string>();
            foreach (var speaker in preferences.RequiredSpeakers)
            {
                for (var skip = 0; skip < SchedulePlanner.MaximumSessions; skip += 4)
                {
                    var result = await functions["speaker_sessions"].InvokeAsync(
                        new AIFunctionArguments { ["name"] = speaker, ["skip"] = skip }, cancellationToken);
                    var text = result?.ToString() ?? throw new InvalidDataException("The agenda function returned no result.");
                    results.Add(text);
                    if (!text.Contains("Next page:", StringComparison.Ordinal))
                        break;
                }
            }
            if (preferences.RequiredSpeakers.Length == 0 || query.Length > 0)
            {
                for (var skip = 0; skip < SchedulePlanner.MaximumSessions; skip += 4)
                {
                    var result = await functions["search_sessions"].InvokeAsync(
                        new AIFunctionArguments { ["query"] = query, ["skip"] = skip }, cancellationToken);
                    var text = result?.ToString() ?? throw new InvalidDataException("The agenda function returned no result.");
                    results.Add(text);
                    if (preferences.AllSessionsMatchingQuery is null || !text.Contains("Next page:", StringComparison.Ordinal))
                        break;
                }
            }
            string[] selectedIds;
            if (preferences.AllSessionsByRequiredSpeakers || preferences.AllSessionsMatchingQuery is not null
                || tools.CandidateIds.Count == 0)
                selectedIds = tools.CandidateIds.ToArray();
            else
            {
                var selectionSchema = PlannerSchemas.Selection(tools.CandidateIds);
                var response = await client.GetResponseAsync(
                    [new ChatMessage(ChatRole.System, $"Select a non-overlapping schedule from these agenda function results. Times use {zone.Id}. Include the requested speakers. Return only session IDs.\nSchema: {selectionSchema.GetRawText()}\n{string.Join("\n", results)}"),
                     new ChatMessage(ChatRole.User, request)],
                    new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(selectionSchema),
                        MaxOutputTokens = 500, Temperature = 0 }, cancellationToken);
                selectedIds = JsonSerializer.Deserialize(response.Text, PlannerJsonContext.Default.PlannerSelection)?.SessionIds
                    ?? throw new InvalidDataException("The model returned no session selection.");
            }
            var arranged = SchedulePlanner.Arrange(conference, preferences with { SessionIds = selectedIds }, zone, savedIds);
            await functions["submit_proposal"].InvokeAsync(
                new AIFunctionArguments { ["sessionIds"] = arranged.SessionIds }, cancellationToken);
        }
        finally
        {
            loggerFactory.CreateLogger<OnDevicePlanner>().LogInformation(
                "Local planning used {ToolCalls} app function calls.", tools.CallCount);
        }
        return new(tools.Proposal ?? tools.Preferences, "");
    }

    private IChatClient CreateClient(IServiceProvider? services)
    {
        if (UnsupportedReason is { } reason)
            throw new PlatformNotSupportedException(reason);
#if IOS
        if (OperatingSystem.IsIOSVersionAtLeast(26))
#pragma warning disable MAUIAI0001
            return new AppleIntelligenceChatClient(loggerFactory, services);
#pragma warning restore MAUIAI0001
#endif
        throw new PlatformNotSupportedException("Apple Intelligence is unavailable.");
    }
}
