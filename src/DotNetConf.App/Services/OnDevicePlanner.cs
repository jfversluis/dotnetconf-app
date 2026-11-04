using DotNetConf.Core;
using System.Globalization;
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

    public async Task<PlannerModelState> CheckReadinessAsync(CancellationToken cancellationToken)
    {
        if (UnsupportedReason is { } reason)
            throw new PlatformNotSupportedException(reason);
        var state = await modelStatus.ReadAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return state;
    }

    public IAsyncEnumerable<PlannerModelDownload> DownloadModelAsync(CancellationToken cancellationToken) =>
        modelStatus.DownloadAsync(cancellationToken);

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
            [new ChatMessage(ChatRole.System, $"Return only a JSON object with values matching this schema, not the schema itself. No markdown or commentary:\n{schema.GetRawText()}"), .. messages],
            new ChatOptions
            {
                ResponseFormat = state.StructuredOutput ? ChatResponseFormat.ForJsonSchema(schema) : ChatResponseFormat.Text,
                MaxOutputTokens = tokens, Temperature = temperature
            }, token);

    private static AvailabilityWindow[] MergeAdjacentDays(AvailabilityWindow[] dates)
    {
        List<AvailabilityWindow> merged = [];
        foreach (var date in dates)
            if (merged.Count > 0 && merged[^1].End == date.Start && merged[^1].Zone == date.Zone)
                merged[^1] = merged[^1] with { End = date.End };
            else
                merged.Add(date);
        return [.. merged];
    }

    private static AvailabilityWindow[] OnRequestedDays(PlannerDailyAvailability limits, AvailabilityWindow[] dates,
        bool openEnded, TimeZoneInfo zone, ConferenceDocument conference)
    {
        if (openEnded)
            return limits.RestrictDatedWindows(MergeAdjacentDays(dates), zone, conference);
        var days = dates.Select(date => date.Start[..10]).ToHashSet(StringComparer.Ordinal);
        var selected = limits.ToWindows(zone, conference).Where(window => days.Contains(window.Start[..10])).ToArray();
        return selected.Length > 0 ? selected
            : throw new InvalidDataException("No conference dates fit the requested dates and clock limits.");
    }

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
        var result = PlannerJson.DeserializeResponse(response.Text, PlannerJsonContext.Default.PlannerStartingPointTopics);
        if (result?.Topics is { } selected && selected.Any(topic => !candidates.Contains(topic, StringComparer.Ordinal)))
            throw new InvalidDataException("The model suggested a topic outside this visit's candidate mix.");
        return PlannerStartingPoints.Ground(conference, result?.Topics);
    }

    public async Task<PlannerResult> ProposeAsync(ConferenceDocument conference, string request,
        TimeZoneInfo zone, string[] savedIds, CancellationToken cancellationToken, bool fillGaps = false,
        PlannerWatchingHours? watchingHours = null)
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
            var interpretation = PlannerJson.DeserializeResponse(extracted.Text, PlannerJsonContext.Default.PlannerInterpretation)
                ?? throw new InvalidDataException("The model returned no interpretation.");
            if (interpretation.SpeakerNames is null
                || interpretation.AvailabilityText is null
                || interpretation.SearchQuery is null
                || interpretation.Quantity is not ("all" or "some"))
                throw new InvalidDataException("The model returned missing or invalid preference fields.");
            AvailabilityWindow[] windows = [];
            var availabilityAssumption = "";
            var requestedZone = PlannerInterpretation.RequestedZone(request);
            var hasTimeHint = PlannerInterpretation.HasAdditionalTimeHint(request);
            if (interpretation.RequiresTimeExtraction(request))
            {
                var dated = PlannerInterpretation.HasExplicitDate(request);
                AvailabilityWindow[] requestedDates = [];
                var openEndedDates = false;
                var knownDates = dated && PlannerInterpretation.TryCalendarDates(request, first.Year, requestedZone,
                    out requestedDates, out openEndedDates, conference, zone);
                PlannerDailyAvailability? datedHours = null;
                var assumedDatedStart = false;
                var useModelDatesOnly = false;
                if (dated)
                {
                    var sourceLimit = new PlannerDailyAvailability("none", "none", requestedZone).Ground(request, sourceOnly: true);
                    if (sourceLimit.DailyStart != "none" || sourceLimit.DailyEnd != "none")
                    {
                        assumedDatedStart = sourceLimit.DailyStart == "none";
                        datedHours = sourceLimit.Complete(watchingHours ?? PlannerWatchingHours.Default);
                        useModelDatesOnly = !hasTimeHint
                            && (datedHours.DailyEnd == "00:00"
                                || string.CompareOrdinal(datedHours.DailyStart, datedHours.DailyEnd) < 0);
                    }
                }
                if (knownDates && !hasTimeHint)
                {
                    availabilityAssumption = datedHours?.Assumption ?? "";
                    windows = datedHours is null ? MergeAdjacentDays(requestedDates)
                        : OnRequestedDays(datedHours, requestedDates, openEndedDates, zone, conference);
                }
                else
                {
                    var extractDaily = !dated || knownDates;
                    var schema = extractDaily ? PlannerSchemas.DailyAvailability : PlannerSchemas.Availability;
                    var target = useModelDatesOnly
                        ? "the calendar dates, ignoring clock times. Use midnight as the start and the exclusive end after the last requested day; keep nonconsecutive dates in separate windows"
                        : extractDaily ? "the recurring daily clock limits, without dates" : "the dated allowed time windows";
                    var timeGuidance = hasTimeHint
                        ? " Extract the bounded interval implied by the named time preference, including both its start and finish. Do not return an unspecified boundary. The app applies explicit clock cutoffs separately."
                        : "";
                    var modelRequest = hasTimeHint ? PlannerInterpretation.WithoutClockLimits(request) : request;
                    if (extractDaily && dated)
                        modelRequest = PlannerInterpretation.WithoutCalendarDates(modelRequest);
                    var available = await JsonResponseAsync(client, state,
                        [new ChatMessage(ChatRole.System, $"Extract ONLY {target}.{timeGuidance} Conference year: {first:yyyy}. Local time means {zone.Id}; use zone local for local or unspecified times, and UTC for UTC. Never convert clock times. Return actual requested values, not schema definitions."),
                         new ChatMessage(ChatRole.User, modelRequest)],
                        schema, 800, 0, cancellationToken);
                    if (!extractDaily)
                    {
                        windows = PlannerJson.DeserializeResponse(available.Text, PlannerJsonContext.Default.PlannerAvailability)
                            ?.InZone(requestedZone, zone).ToWindows(zone, conference)
                            ?? throw new InvalidDataException("The model returned no availability.");
                        if (useModelDatesOnly)
                            windows = windows.Select(PlannerDailyAvailability.CalendarDates).ToArray();
                        else if (hasTimeHint && windows.Any(window =>
                            DateTime.ParseExact(window.End, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture)
                            - DateTime.ParseExact(window.Start, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture) >= TimeSpan.FromMinutes(1439)))
                            throw new InvalidDataException(PlannerDailyAvailability.NeedsExplicitTime);
                        if (datedHours is not null)
                        {
                            availabilityAssumption = datedHours.Assumption;
                            windows = datedHours.RestrictDatedWindows(windows, zone, conference);
                            if (!useModelDatesOnly && availabilityAssumption.Length > 0
                                && !windows.Any(window => assumedDatedStart
                                    ? window.Start.EndsWith($"T{datedHours.DailyStart}", StringComparison.Ordinal)
                                    : window.End.EndsWith($"T{datedHours.DailyEnd}", StringComparison.Ordinal)))
                                availabilityAssumption = "";
                        }
                    }
                    else
                    {
                        var daily = PlannerJson.DeserializeResponse(available.Text, PlannerJsonContext.Default.PlannerDailyAvailability)
                            ?? throw new InvalidDataException("The model returned no daily clock limits.");
                        if (daily.DailyStart is null || daily.DailyEnd is null || daily.Zone is null)
                            throw new InvalidDataException("The model didn't return usable hours. Try stating both the start and finish.");
                        var limits = (daily.Ground(request) with { Zone = requestedZone })
                            .Complete(watchingHours ?? PlannerWatchingHours.Default);
                        availabilityAssumption = limits.Assumption;
                        windows = knownDates ? OnRequestedDays(limits, requestedDates, openEndedDates, zone, conference)
                            : limits.ToWindows(zone, conference);
                    }
                }
                if (hasTimeHint)
                {
                    var times = windows.Select(window =>
                        $"{window.Start[11..]}–{window.End[11..]} ({(window.Zone == "local" ? zone.Id : window.Zone)})")
                        .Distinct(StringComparer.Ordinal).Take(2).ToArray();
                    var interpretationNotice = times.Length == 1
                        ? $"Interpreted hours: {times[0]}. Review the times."
                        : "Review the interpreted dates and times in matching details.";
                    availabilityAssumption = string.Join(" ", new[] { availabilityAssumption, interpretationNotice }
                        .Where(text => text.Length > 0));
                }
            }
            var (speakerNames, _) = interpretation.Ground(conference, request);
            var allSpeakerSessions = interpretation.RequestsAllSpeakers(request, speakerNames) && speakerNames.Length > 0;
            tools.CapturePreferences(speakerNames, windows, allSpeakerSessions);
            var nameErrors = SchedulePlanner.Review(conference, tools.Preferences
                ?? throw new InvalidDataException("Invalid speaker preferences."), zone, savedIds).Issues
                .Where(issue => issue.Code is ProposalIssueCode.UnknownSpeaker or ProposalIssueCode.AmbiguousSpeaker)
                .Select(issue => issue.Message).ToArray();
            if (nameErrors.Length > 0)
                return new(tools.Preferences, string.Join("\n", nameErrors));
            var functions = tools.CreateFunctions(scope.ServiceProvider).OfType<AIFunction>().ToDictionary(f => f.Name);
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
                var intent = PlannerJson.DeserializeResponse(topical.Text, PlannerJsonContext.Default.PlannerTopicIntent)
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
                    var match = PlannerJson.DeserializeResponse(classified.Text, PlannerJsonContext.Default.PlannerTopicClassification)
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
                allSpeakerSessions,
                topicFilter, availabilityAssumption);
            if (tools.Preferences is not { } preferences)
                return new(null, captured);
            cancellationToken.ThrowIfCancellationRequested();
            if (state.ToolCalling)
                await PlannerDiscovery.RunAsync(client, tools, scope.ServiceProvider, request,
                    loggerFactory.CreateLogger<OnDevicePlanner>(), cancellationToken);
            else
            {
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
                    tools.IncludeCandidates(SchedulePlanner.TopicMatches(conference, preferences).Select(s => s.Id));
                else if (preferences.RequiredSpeakers.Length == 0)
                    tools.IncludeCandidates(conference.Sessions.Select(s => s.Id));
            }
            var eligibleIds = tools.EligibleCandidateIds;
            var selectedIds = SchedulePlanner.SelectSuggestions(conference, preferences, eligibleIds, zone, savedIds);
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
