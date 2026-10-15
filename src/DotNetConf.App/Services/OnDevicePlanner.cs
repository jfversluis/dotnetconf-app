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
            if (interpretation.RequiresTimeExtraction(request))
            {
                var dated = PlannerInterpretation.HasExplicitDate(request);
                var schema = dated ? PlannerSchemas.Availability : PlannerSchemas.DailyAvailability;
                var requestedZone = PlannerInterpretation.RequestedZone(request);
                var available = await client.GetResponseAsync(
                    [new ChatMessage(ChatRole.System, $"Extract ONLY {(dated ? "the dated allowed time windows" : "the recurring daily clock limits, without dates")}. Conference year: {first:yyyy}. Local time means {zone.Id}; use zone local for local or unspecified times, and UTC for UTC. Never convert clock times. Respond with JSON using this schema:\n{schema.GetRawText()}"),
                     new ChatMessage(ChatRole.User, request)],
                    new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(schema),
                        MaxOutputTokens = 800, Temperature = 0 }, cancellationToken);
                if (dated)
                    windows = JsonSerializer.Deserialize(available.Text, PlannerJsonContext.Default.PlannerAvailability)
                        ?.InZone(requestedZone, zone).ToWindows(zone, conference)
                        ?? throw new InvalidDataException("The model returned no availability.");
                else
                {
                    var daily = JsonSerializer.Deserialize(available.Text, PlannerJsonContext.Default.PlannerDailyAvailability)
                        ?? throw new InvalidDataException("The model returned no daily clock limits.");
                    windows = (daily.Ground(request) with { Zone = requestedZone }).ToWindows(zone, conference);
                }
            }
            var (speakerNames, _) = interpretation.Ground(conference, request);
            tools.CapturePreferences(speakerNames, windows, interpretation.RequestsAllSpeakers(request) && speakerNames.Length > 0);
            var nameErrors = SchedulePlanner.Review(conference, tools.Preferences
                ?? throw new InvalidDataException("Invalid speaker preferences."), zone, savedIds).Errors
                .Where(error => error.StartsWith("Speaker not found", StringComparison.Ordinal)
                    || error.StartsWith("Ambiguous speaker", StringComparison.Ordinal)).ToArray();
            if (nameErrors.Length > 0)
                return new(tools.Preferences, string.Join("\n", nameErrors));
            var functions = tools.CreateFunctions().OfType<AIFunction>().ToDictionary(f => f.Name);
            var catalog = await functions["catalog_topics"].InvokeAsync(new AIFunctionArguments(), cancellationToken);
            var interests = PlannerCatalog.SemanticInterests(conference, request);
            var topic = new PlannerTopicRequest(request, Agenda.MentionedTopics(conference, request), "some", "any", "independent");
            if (topic.Topics.Length > 0 || interests.Length > 0)
            {
                var topical = await client.GetResponseAsync(
                    [new ChatMessage(ChatRole.System,
                        """
                        Return only quantity and speakerScope.
                        quantity: all if every matching TOPIC session is requested; some for ordinary interests.
                        "Everything from Ortinau and some phone apps" means some, because all applies only to the speaker.
                        speakerScope: independent for separate topic and speaker requests; within-topics for a person's
                        talks about a topic; unsupported for different speaker/topic pairs.
                        """),
                     new ChatMessage(ChatRole.User, request)],
                    new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(PlannerSchemas.TopicIntent),
                        MaxOutputTokens = 150, Temperature = 0 }, cancellationToken);
                var intent = JsonSerializer.Deserialize(topical.Text, PlannerJsonContext.Default.PlannerTopicIntent)
                    ?? throw new InvalidDataException("The model returned no topic intent.");
                topic = topic with { Quantity = intent.Quantity, SpeakerScope = intent.SpeakerScope };
            }
            if (interests.Length > 0)
            {
                List<string> mapped = [.. topic.Topics];
                var schema = PlannerSchemas.ClassifyTopic(conference);
                foreach (var phrase in interests)
                {
                    var classified = await client.GetResponseAsync(
                        [new ChatMessage(ChatRole.System, $"""
                            Choose the ONE most specific agenda topic for this interest. Understand typos, shorthand and goals.
                            Reducing allocations and bottlenecks means Performance, not APIs, Testing or Cloud.
                            HTTP endpoints means APIs. Screen readers and bigger text means Accessibility.
                            Use none if unrelated. Respond with the topic field only.
                            {catalog}
                            """),
                         new ChatMessage(ChatRole.User, phrase)],
                        new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(schema),
                            MaxOutputTokens = 100, Temperature = 0 }, cancellationToken);
                    var match = JsonSerializer.Deserialize(classified.Text, PlannerJsonContext.Default.PlannerTopicClassification)
                        ?? throw new InvalidDataException("The model returned no interest classification.");
                    if (match.Topic != "none")
                        mapped.Add(match.Topic);
                }
                topic = topic with { Topics = mapped.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
            }
            else if (topic.Topics.Length == 0)
                topic = topic with { Text = "none" };
            var topicFilter = topic.ToFilter(conference, request, speakerNames.Length > 0);
            var captured = tools.CapturePreferences(speakerNames, windows,
                interpretation.RequestsAllSpeakers(request) && speakerNames.Length > 0,
                topicFilter);
            if (tools.Preferences is not { } preferences)
                return new(null, captured);
            cancellationToken.ThrowIfCancellationRequested();
            // Dispatch model-selected queries explicitly. Native automatic tool calling
            // in this preview can repeat callbacks indefinitely inside a single request.
            foreach (var speaker in preferences.RequiredSpeakers)
            {
                for (var skip = 0; skip < conference.Sessions.Count; skip += 4)
                {
                    var result = await functions["speaker_sessions"].InvokeAsync(
                        new AIFunctionArguments { ["name"] = speaker, ["skip"] = skip }, cancellationToken);
                    var text = result?.ToString() ?? throw new InvalidDataException("The agenda function returned no result.");
                    if (!text.Contains("Next page:", StringComparison.Ordinal))
                        break;
                }
            }
            if (topicFilter is not null)
            {
                tools.IncludeCandidates(SchedulePlanner.TopicMatches(conference, preferences).Select(s => s.Id));
            }
            else if (preferences.RequiredSpeakers.Length == 0)
                tools.IncludeCandidates(conference.Sessions.Select(s => s.Id));
            string[] selectedIds;
            var eligibleIds = tools.EligibleCandidateIds;
            if (preferences.AllSessionsByRequiredSpeakers || topicFilter?.IncludeAll == true || eligibleIds.Length == 0)
                selectedIds = eligibleIds;
            else
            {
                selectedIds = SchedulePlanner.Recommend(conference, preferences, eligibleIds, zone, savedIds);
            }
            var arranged = SchedulePlanner.Arrange(conference, preferences with { SessionIds = selectedIds }, zone, savedIds);
            await functions["submit_proposal"].InvokeAsync(
                new AIFunctionArguments { ["sessionIds"] = arranged.SessionIds }, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            loggerFactory.CreateLogger<OnDevicePlanner>().LogWarning(exception, "Could not interpret local planner constraints.");
            return new(null, $"Couldn't interpret this request safely: {exception.Message} Edit it and try again. Nothing has been saved.");
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
