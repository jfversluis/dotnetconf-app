using DotNetConf.Core;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetConf.App.Services;

public sealed record PlannerResult(ScheduleProposal? Proposal, string Message);

public sealed class OnDevicePlanner(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory,
    IServiceProviderIsService registeredServices, IPlannerModelStatus modelStatus) : IOnDevicePlanner
{
    public string? UnsupportedReason => registeredServices.IsService(typeof(IChatClient)) ? null :
        "No on-device chat model is supported by this app on your device. You can build My Schedule using the hearts in the agenda.";

    public async Task CheckReadinessAsync(CancellationToken cancellationToken)
    {
        if (UnsupportedReason is { } reason)
            throw new PlatformNotSupportedException(reason);
        await RequireModelAsync(cancellationToken);
    }

    private async Task<PlannerModelState> RequireModelAsync(CancellationToken token)
    {
        var state = await modelStatus.ReadAsync(token);
        token.ThrowIfCancellationRequested();
        if (!state.Available)
            throw new NotSupportedException(state.Reason);
        return state;
    }

    private static Task<ChatResponse> JsonResponseAsync(IChatClient client, PlannerModelState state,
        IEnumerable<ChatMessage> messages, JsonElement schema, int tokens, float temperature, CancellationToken token) =>
        client.GetResponseAsync(
            [new ChatMessage(ChatRole.System, $"Return only a JSON object matching this schema, without markdown or commentary:\n{schema.GetRawText()}"), .. messages],
            new ChatOptions
            {
                ResponseFormat = state.StructuredOutput ? ChatResponseFormat.ForJsonSchema(schema) : ChatResponseFormat.Text,
                MaxOutputTokens = tokens, Temperature = temperature
            }, token);

    public async Task<IReadOnlyList<PlannerStartingPoint>> SuggestStartingPointsAsync(
        ConferenceDocument conference, CancellationToken cancellationToken)
    {
        var catalog = PlannerCatalog.DescribeTopics(conference);
        var topics = conference.Sessions.SelectMany(s => s.Topics).Distinct().ToArray();
        Random.Shared.Shuffle(topics);
        var candidates = topics.Take(8).ToArray();
        var schema = PlannerStartingPoints.Schema(conference, candidates);
        var state = await RequireModelAsync(cancellationToken);
        using var scope = scopeFactory.CreateScope();
        var client = CreateClient(scope.ServiceProvider);
        var response = await JsonResponseAsync(client, state,
            [new ChatMessage(ChatRole.System,
                $"Choose {Math.Min(5, candidates.Length)} different, inviting starting topics for a software conference attendee from the provided candidate list. Use ONLY exact candidate names. Favor a varied mix, using this agenda as context. Return JSON with a topics array.\n{catalog}"),
             new ChatMessage(ChatRole.User, $"Choose from these candidates:\n{string.Join('\n', candidates)}")],
            schema, 160, 0.8f, cancellationToken);
        var result = JsonSerializer.Deserialize(response.Text, PlannerJsonContext.Default.PlannerStartingPointTopics);
        if (result?.Topics is { } selected && selected.Any(topic => !candidates.Contains(topic, StringComparer.Ordinal)))
            throw new InvalidDataException("The model suggested a topic outside this visit's candidate mix.");
        return PlannerStartingPoints.Ground(conference, result?.Topics);
    }

    public async Task<PlannerResult> ProposeAsync(ConferenceDocument conference, string request,
        TimeZoneInfo zone, string[] savedIds, CancellationToken cancellationToken, bool fillGaps = false)
    {
        using var scope = scopeFactory.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<PlannerTools>();
        tools.Initialize(conference, zone, savedIds, fillGaps);
        var client = CreateClient(scope.ServiceProvider);
        var first = TimeZoneInfo.ConvertTime(conference.Sessions.Min(s => s.StartsAt), zone);
        try
        {
            Agenda.ValidatePlannerRequest(request);
            if (fillGaps)
            {
                var gaps = ScheduleGaps.Find(conference, savedIds, zone);
                if (!conference.Sessions.Any(s => !savedIds.Contains(s.Id) && ScheduleGaps.Fits(s, gaps)))
                    return new(null, "No unsaved sessions fit the free time on your saved conference days. Your schedule is unchanged.");
            }
            var state = await RequireModelAsync(cancellationToken);
            // The native preview constrains the shape but does not include the
            // schema descriptions in its prompt, so send those explicitly too.
            var interpretationSchema = PlannerSchemas.Interpretation(conference);
            var extracted = await JsonResponseAsync(client, state,
                [new ChatMessage(ChatRole.System,
                    $"Turn a software conference attendee's request into query arguments, not recommendations. Default speakerNames and availabilityText to none. Only replace speakerNames when the request names an actual PERSON, even if absent from the agenda. Software concepts, goals and noun phrases are not personal names. Do not copy an interest into speakerNames when no topic matches exactly. Never add names or time restrictions. Respond using this schema and its field descriptions:\n{interpretationSchema.GetRawText()}"),
                 new ChatMessage(ChatRole.User, "I'd like to learn debugging techniques."),
                 new ChatMessage(ChatRole.Assistant,
                     """{"speakerNames":"none","availabilityText":"none","quantity":"some","searchQuery":"none"}"""),
                 new ChatMessage(ChatRole.User, "Every talk from Pat Doe, please."),
                 new ChatMessage(ChatRole.Assistant,
                     """{"speakerNames":"Pat Doe","availabilityText":"none","quantity":"all","searchQuery":"none"}"""),
                 new ChatMessage(ChatRole.User, request)],
                interpretationSchema, 500, 0, cancellationToken);
            var interpretation = JsonSerializer.Deserialize(extracted.Text, PlannerJsonContext.Default.PlannerInterpretation)
                ?? throw new InvalidDataException("The model returned no interpretation.");
            if (interpretation.SpeakerNames is null
                || interpretation.AvailabilityText is null
                || interpretation.SearchQuery is null
                || interpretation.Quantity is not ("all" or "some"))
                throw new InvalidDataException("The model returned missing or invalid preference fields.");
            AvailabilityWindow[] windows = [];
            var requestedZone = PlannerInterpretation.RequestedZone(request);
            if (PlannerInterpretation.TryDatedWindow(request, first.Year, requestedZone, out var sourceWindow))
                windows = [sourceWindow];
            else if (interpretation.RequiresTimeExtraction(request))
            {
                var dated = PlannerInterpretation.HasExplicitDate(request);
                var schema = dated ? PlannerSchemas.Availability : PlannerSchemas.DailyAvailability;
                var available = await JsonResponseAsync(client, state,
                    [new ChatMessage(ChatRole.System, $"Extract ONLY {(dated ? "the dated allowed time windows" : "the recurring daily clock limits, without dates")}. Conference year: {first:yyyy}. Local time means {zone.Id}; use zone local for local or unspecified times, and UTC for UTC. Never convert clock times. Respond with JSON using this schema:\n{schema.GetRawText()}"),
                     new ChatMessage(ChatRole.User, request)],
                    schema, 800, 0, cancellationToken);
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
                ?? throw new InvalidDataException("Invalid speaker preferences."), zone, savedIds).Issues
                .Where(issue => issue.Code is ProposalIssueCode.UnknownSpeaker or ProposalIssueCode.AmbiguousSpeaker)
                .Select(issue => issue.Message).ToArray();
            if (nameErrors.Length > 0)
                return new(tools.Preferences, string.Join("\n", nameErrors));
            var functions = tools.CreateFunctions().OfType<AIFunction>().ToDictionary(f => f.Name);
            var catalog = await functions["catalog_topics"].InvokeAsync(new AIFunctionArguments(), cancellationToken);
            var interests = PlannerCatalog.SemanticInterests(conference, request);
            var topic = new PlannerTopicRequest(request, Agenda.MentionedTopics(conference, request), "some", "any", "independent");
            if (topic.Topics.Length > 0 || interests.Length > 0)
            {
                var topical = await JsonResponseAsync(client, state,
                    [new ChatMessage(ChatRole.System,
                        """
                        Return only quantity and speakerScope.
                        quantity: all if every matching TOPIC session is requested; some for ordinary interests.
                        "Everything from Ortinau and some phone apps" means some, because all applies only to the speaker.
                        speakerScope: independent for separate topic and speaker requests; within-topics for a person's
                        talks about a topic; unsupported for different speaker/topic pairs.
                        """),
                     new ChatMessage(ChatRole.User, request)],
                    PlannerSchemas.TopicIntent, 150, 0, cancellationToken);
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
                    var classified = await JsonResponseAsync(client, state,
                        [new ChatMessage(ChatRole.System, $"""
                            Choose the ONE most specific agenda topic for this interest. Understand typos, shorthand and goals.
                            Reducing allocations and bottlenecks means Performance, not APIs, Testing or Cloud.
                            HTTP endpoints means APIs. Screen readers and bigger text means Accessibility.
                            Use none if unrelated. Respond with the topic field only.
                            {catalog}
                            """),
                         new ChatMessage(ChatRole.User, phrase)],
                        schema, 100, 0, cancellationToken);
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
                int? skip = 0;
                while (skip is { } offset)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    skip = tools.SpeakerSessionsPage(speaker, offset).NextSkip;
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

    private IChatClient CreateClient(IServiceProvider services)
    {
        if (UnsupportedReason is { } reason)
            throw new PlatformNotSupportedException(reason);
        return services.GetRequiredService<IChatClient>();
    }
}
