using DotNetConf.App.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DotNetConf.Core.Tests;

public sealed class PlannerRelevanceTests
{
    private static ConferenceDocument SyntheticAgenda() => new()
    {
        IsMock = true,
        Speakers = [new() { Id = "synthetic-person", Name = "Synthetic Presenter" }],
        Sessions =
        [
            Talk("focused", "Synthetic Blazor workshop", "", ["Blazor"], 17),
            Talk("untagged", "Synthetic component workshop", "Build with Blazor.", [], 18),
            Talk("broad", "Synthetic security roundup", "Security bugs and developer advice.", ["Blazor", "Security", "C#"], 19),
            Talk("overnight", "Synthetic AI lab", "", ["AI"], 1),
            Talk("daytime", "Synthetic AI workshop", "", ["AI"], 20),
            Talk("crossing", "Synthetic AI evening lab", "", ["AI"], 22, 23.5)
        ]
    };

    private static Session Talk(string id, string title, string description, List<string> topics, double start, double? end = null) =>
        new()
        {
            Id = id, Title = title, Description = description, Topics = topics, SpeakerIds = ["synthetic-person"],
            StartsAt = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero).AddHours(start),
            EndsAt = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero).AddHours(end ?? start + 0.5)
        };

    private static ServiceProvider CreateServices(bool structured, Queue<string> responses)
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var json = responses.Dequeue();
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, structured ? json : $"```json\n{json}\n```"));
            });
        return new ServiceCollection().AddScoped<PlannerTools>().AddTransient(_ => client).BuildServiceProvider();
    }

    private static OnDevicePlanner CreatePlanner(ServiceProvider services, bool structured) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), NullLoggerFactory.Instance,
            services.GetRequiredService<IServiceProviderIsService>(), new TestPlannerModelStatus(structuredOutput: structured));

    [Fact]
    public void FocusedInterestsFindUntaggedContentAndDoNotBackfillCoarseTags()
    {
        var agenda = SyntheticAgenda();
        var preferences = new ScheduleProposal([], [], []) { TopicFilter = new(["Blazor"], false, false, false) };
        Assert.Equal(["focused", "untagged"], SchedulePlanner.TopicMatches(agenda, preferences).Select(s => s.Id));
        Assert.Equal(["focused", "broad"], Agenda.Search(agenda, null, null, ["Blazor"], TimeZoneInfo.Utc).Select(s => s.Id));
        Assert.Contains(SchedulePlanner.Review(agenda, preferences with { SessionIds = ["broad"] }, TimeZoneInfo.Utc, []).Issues,
            issue => issue.Code == ProposalIssueCode.OutsideTopicScope);
        var tools = new PlannerTools();
        tools.Initialize(agenda, TimeZoneInfo.Utc, []);
        tools.CapturePreferences([], [], topicFilter: preferences.TopicFilter);
        tools.SearchSessions("", 0);
        tools.SearchSessions("", 4);
        Assert.DoesNotContain("broad", tools.EligibleCandidateIds);
        Assert.DoesNotContain("daytime", tools.EligibleCandidateIds);
    }

    [Fact]
    public void AllTopicSessionsKeepTagOnlyMatchesWithCompletenessChecks()
    {
        var agenda = SyntheticAgenda();
        var preferences = new ScheduleProposal(["focused", "untagged"], [], [])
        {
            TopicFilter = new(["Blazor"], false, false, true)
        };
        Assert.Equal(["focused", "untagged", "broad"], SchedulePlanner.TopicMatches(agenda, preferences).Select(s => s.Id));
        Assert.Contains(SchedulePlanner.Review(agenda, preferences, TimeZoneInfo.Utc, []).Issues,
            issue => issue.Code == ProposalIssueCode.IncompleteTopicSessions);
    }

    [Fact]
    public void SpeakerScopedInterestsAcceptContentMatchesWithoutTags()
    {
        var agenda = SyntheticAgenda();
        var proposal = new ScheduleProposal(["untagged"], ["Synthetic Presenter"], [])
        {
            TopicFilter = new(["Blazor"], false, true, false)
        };

        Assert.True(SchedulePlanner.Review(agenda, proposal, TimeZoneInfo.Utc, []).CanApply);
    }

    [Fact]
    public void AllSpeakerSessionsKeepEveryContentMatchEvenBeyondTheSuggestionTarget()
    {
        var agenda = SyntheticAgenda() with
        {
            Sessions = Enumerable.Range(0, 14)
                .Select(index => Talk($"synthetic-{index}", $"Synthetic Blazor session {index}", "",
                    index == 0 ? ["Blazor"] : [], 1 + index * 0.5))
                .ToList()
        };
        var preferences = new ScheduleProposal([], ["Synthetic Presenter"], [])
        {
            AllSessionsByRequiredSpeakers = true,
            TopicFilter = new(["Blazor"], false, true, false)
        };

        var ids = SchedulePlanner.SelectSuggestions(agenda, preferences, agenda.Sessions.Select(session => session.Id),
            TimeZoneInfo.Utc, []);

        Assert.Equal(14, ids.Length);
        Assert.True(SchedulePlanner.Review(agenda, preferences with { SessionIds = ids }, TimeZoneInfo.Utc, []).CanApply);
    }

    [Fact]
    public void FallbackIsPerTopicAndSpeakerScopeNotPerAvailability()
    {
        var agenda = SyntheticAgenda();
        var filter = new PlannerTopicFilter(["Blazor", "C#"], false, false, false);
        var preferences = new ScheduleProposal([], [], []) { TopicFilter = filter };
        Assert.Equal(["focused", "untagged", "broad"], SchedulePlanner.TopicMatches(agenda, preferences).Select(s => s.Id));
        Assert.Empty(SchedulePlanner.TopicMatches(agenda, preferences with { TopicFilter = filter with { MatchAll = true } }));
        agenda.Speakers.Add(new() { Id = "other", Name = "Synthetic Other" });
        agenda.Sessions[2] = agenda.Sessions[2] with { SpeakerIds = ["other"] };
        Assert.Equal(["broad"], SchedulePlanner.TopicMatches(agenda, preferences with
        {
            RequiredSpeakers = ["Synthetic Other"], TopicFilter = new(["Blazor"], false, true, false)
        }).Select(s => s.Id));
        preferences = preferences with
        {
            TopicFilter = new(["Blazor"], false, false, false),
            Availability = [new("2026-11-10T19:00", "2026-11-10T20:00", "UTC")]
        };
        Assert.DoesNotContain("broad", SchedulePlanner.TopicMatches(agenda, preferences).Select(s => s.Id));
        Assert.Contains(SchedulePlanner.Review(agenda, preferences with { SessionIds = ["broad"] }, TimeZoneInfo.Utc, []).Issues,
            issue => issue.Code == ProposalIssueCode.OutsideTopicScope);
    }

    [Theory]
    [InlineData("C#", "Synthetic C# tips", true)]
    [InlineData("C#", "Synthetic C tips", false)]
    [InlineData(".NET", "Synthetic .NET runtime", true)]
    [InlineData("AI", "Synthetic email tips", false)]
    [InlineData("AI", "Synthetic AI workshop", true)]
    [InlineData("Blazor", "Synthetic blazorange", false)]
    [InlineData(".NET MAUI", "Synthetic MAUI workshop", true)]
    public void ContentUsesWholeTopicTokensNotSubstringOrFuzzyMatches(string topic, string title, bool matches)
    {
        var agenda = SyntheticAgenda();
        Assert.Equal(matches, Agenda.HasTopicContent(agenda, Talk("test", title, "", [], 10), topic));
    }

    [Theory]
    [InlineData("before 11pm", "07:00", "23:00")]
    [InlineData("finish by 21:30", "07:00", "21:30")]
    [InlineData("after 6pm", "18:00", "23:00")]
    [InlineData("before noon", "07:00", "12:00")]
    [InlineData("until midnight", "07:00", "00:00")]
    [InlineData("before 6am", "00:00", "06:00")]
    [InlineData("before 7am", "00:00", "07:00")]
    [InlineData("after 11pm", "23:00", "00:00")]
    [InlineData("after 23:30", "23:30", "00:00")]
    [InlineData("no sessions before 9am", "09:00", "23:00")]
    [InlineData("no sessions after 9pm", "07:00", "21:00")]
    [InlineData("No talks after 10pm", "07:00", "22:00")]
    [InlineData("Not after 9pm", "07:00", "21:00")]
    [InlineData("Without sessions after 10pm", "07:00", "22:00")]
    [InlineData("Don't show talks after 10pm", "07:00", "22:00")]
    [InlineData("Avoiding sessions that end after 10pm", "07:00", "22:00")]
    [InlineData("Except after 10pm", "07:00", "22:00")]
    [InlineData("Exclude sessions if they are after 10pm for me", "07:00", "22:00")]
    [InlineData("Before 11pm, nothing too advanced", "07:00", "23:00")]
    [InlineData("Nothing too advanced, show sessions before 11pm", "07:00", "23:00")]
    [InlineData("Before 11pm, not including overnight", "07:00", "23:00")]
    [InlineData("Before 11pm, without including overnight", "07:00", "23:00")]
    [InlineData("I can't watch AI sessions before 9am", "09:00", "23:00")]
    [InlineData("I can't watch any of the AI sessions after 5pm", "07:00", "17:00")]
    [InlineData("I cannot attend after 10pm", "07:00", "22:00")]
    [InlineData("I won't watch after 10pm", "07:00", "22:00")]
    [InlineData("I am unavailable before 9am", "09:00", "23:00")]
    [InlineData("I'm not able to watch before 9am", "09:00", "23:00")]
    [InlineData("I can't wait to see the Blazor and Aspire talks after 5pm", "17:00", "23:00")]
    [InlineData("I cannot wait for Blazor talks after 5pm", "17:00", "23:00")]
    [InlineData("I can't make Friday so show me Blazor talks after 5pm", "17:00", "23:00")]
    [InlineData("I can't make Friday so I can watch Blazor after 5pm", "17:00", "23:00")]
    [InlineData("I can't make Friday, so I'll watch Blazor talks after 5pm", "17:00", "23:00")]
    [InlineData("I can't make Friday so I'm looking for Blazor talks after 5pm", "17:00", "23:00")]
    [InlineData("I can't make Friday so I'd like Blazor talks after 5pm", "17:00", "23:00")]
    [InlineData("I can't make Friday so I prefer Blazor talks after 5pm", "17:00", "23:00")]
    [InlineData("I can't make Friday so I can't watch Blazor after 5pm", "07:00", "17:00")]
    [InlineData("I can attend Sunday and I can't watch after 5pm", "07:00", "17:00")]
    [InlineData("I can't watch AI, I can't watch after 5pm", "07:00", "17:00")]
    [InlineData("I can't watch sessions so late after 5pm", "07:00", "17:00")]
    [InlineData("Avoid beginner sessions; show talks after 9am", "09:00", "23:00")]
    [InlineData("No sessions about Java, but show sessions before 11pm", "07:00", "23:00")]
    public void MissingBoundariesUseDefaultsWithoutOverrulingExplicitEarlyOrLateLimits(string request, string start, string end)
    {
        var actual = new PlannerDailyAvailability("10:00", "20:00", "local").Ground(request).Complete(PlannerWatchingHours.Default);
        Assert.Equal(start, actual.DailyStart);
        Assert.Equal(end, actual.DailyEnd);
        Assert.Contains("Assumed daily", actual.Assumption);
    }

    [Theory]
    [InlineData("between 1am and 5am", "01:00", "05:00")]
    [InlineData("from 10pm to 2am", "22:00", "02:00")]
    [InlineData("from 10pm until 2am", "22:00", "02:00")]
    [InlineData("10pm to 2am", "22:00", "02:00")]
    [InlineData("from midnight to 5am", "00:00", "05:00")]
    [InlineData("from 12am to 12pm", "00:00", "12:00")]
    [InlineData("from 12pm to 12am", "12:00", "00:00")]
    [InlineData("between noon and midnight", "12:00", "00:00")]
    [InlineData("from 11:59pm to 12:01am", "23:59", "00:01")]
    [InlineData("I'm free from 13 to 17 for Blazor talks", "13:00", "17:00")]
    [InlineData("Show talks between 13 and 17 about Blazor", "13:00", "17:00")]
    [InlineData("anytime before 11pm", "00:00", "23:00")]
    [InlineData("before 11pm, including overnight", "00:00", "23:00")]
    public void ExplicitHoursOverrideDefaultsAndDoNotClaimAnAssumption(string request, string start, string end)
    {
        var actual = new PlannerDailyAvailability("09:00", "17:00", "local").Ground(request).Complete(PlannerWatchingHours.Default);
        Assert.Equal(start, actual.DailyStart);
        Assert.Equal(end, actual.DailyEnd);
        Assert.Empty(actual.Assumption);
    }

    [Theory]
    [InlineData("I can't watch before 9am or after 5pm", "09:00", "17:00")]
    [InlineData("I can't watch AI sessions before 9am or after 5pm", "09:00", "17:00")]
    [InlineData("I'm unavailable before 9am and after 5pm", "09:00", "17:00")]
    [InlineData("I can't watch before 9am and I can watch after 5pm", "17:00", "23:00")]
    [InlineData("I can't watch before 9am and I can't watch after 5pm", "09:00", "17:00")]
    [InlineData("I can't watch before 5pm and I must finish before 9pm", "17:00", "21:00")]
    [InlineData("I can't watch before 5pm and must finish before 9pm", "17:00", "21:00")]
    [InlineData("I can't watch before 5pm and finish before 9pm", "17:00", "21:00")]
    [InlineData("I can't watch any of the AI sessions, after 5pm", "07:00", "17:00")]
    [InlineData("I can't make Friday, show me Blazor talks after 5pm", "17:00", "23:00")]
    [InlineData("I can't watch before 9am, or after 5pm", "09:00", "17:00")]
    public void SeparatelyPhrasedCutoffsKeepNegationScope(string request, string start, string end)
    {
        var actual = new PlannerDailyAvailability("none", "none", "local").Ground(request).Complete(PlannerWatchingHours.Default);
        Assert.Equal(start, actual.DailyStart);
        Assert.Equal(end, actual.DailyEnd);
    }

    [Theory]
    [InlineData("AI sessions, but nothing between 9am and 5pm UTC")]
    [InlineData("AI on November 10, nothing between 9am and 5pm UTC")]
    [InlineData("AI sessions, not from 9am until 5pm UTC")]
    [InlineData("I am not available between 9am and 5pm UTC")]
    [InlineData("I cannot attend between 9am and 5pm UTC")]
    [InlineData("I can't watch AI sessions from 9am to 5pm")]
    [InlineData("I am not able to watch AI between 9am and 5pm UTC")]
    [InlineData("I won't watch AI between 9am and 5pm UTC")]
    public void NegatedTimeRangesAreNotTreatedAsAllowed(string request)
    {
        Assert.Throws<InvalidDataException>(() => Agenda.ValidatePlannerRequest(request));
    }

    [Theory]
    [InlineData("AI in the afternoons before 11pm", "12:00", "18:00")]
    [InlineData("AI after-work before 11pm", "17:00", "21:00")]
    [InlineData("AI after 9am, only in the evening", "18:00", "22:00")]
    [InlineData("AI in the evening before 11pm", "18:00", "00:00", "18:00", "23:00")]
    [InlineData("AI after 9am during my commute", "07:00", "10:00", "09:00", "10:00")]
    [InlineData("AI in the afternoon from 7am to 11pm", "12:00", "17:00")]
    [InlineData("AI in the afternoon after 1pm and before 5pm", "12:00", "18:00", "13:00", "17:00")]
    [InlineData("AI at night before 2am", "20:00", "06:00", "20:00", "02:00")]
    [InlineData("AI at night before 8am", "20:00", "06:00", "20:00", "06:00")]
    [InlineData("AI at night after 9pm", "20:00", "06:00", "21:00", "06:00")]
    [InlineData("AI at night before 11pm", "20:00", "06:00", "20:00", "23:00")]
    public void QualitativeHoursAndExplicitCutoffsAreBothRespected(string request, string start, string end,
        string? expectedStart = null, string? expectedEnd = null)
    {
        var actual = new PlannerDailyAvailability(start, end, "local").Ground(request).Complete(PlannerWatchingHours.Default);
        Assert.Equal(expectedStart ?? start, actual.DailyStart);
        Assert.Equal(expectedEnd ?? end, actual.DailyEnd);
    }

    [Fact]
    public void ConflictingOrAmbiguousQualitativeHoursAreRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            new PlannerDailyAvailability("18:00", "22:00", "local").Ground("in the evening before 5pm"));
        Assert.Throws<InvalidDataException>(() =>
            new PlannerDailyAvailability("22:00", "02:00", "local").Ground("at night after 1am"));
        Assert.Throws<InvalidDataException>(() =>
            new PlannerDailyAvailability("20:00", "06:00", "local").Ground("at night from 7am to 5pm"));
        Assert.Throws<InvalidDataException>(() =>
            new PlannerDailyAvailability("20:00", "06:00", "local").Ground("at night before 8pm"));
        var missingHours = Assert.Throws<InvalidDataException>(() =>
            new PlannerDailyAvailability("none", "23:00", "local").Ground("in the afternoons before 11pm"));
        Assert.Equal("Use explicit start and finish times for this time preference.", missingHours.Message);
        Assert.Throws<InvalidDataException>(() =>
            new PlannerDailyAvailability("09:00", "none", "local").Ground("after 9am during my commute"));
        Assert.Throws<InvalidDataException>(() =>
            new PlannerDailyAvailability("00:00", "00:00", "local").Ground("in the afternoon before 11pm"));
        Assert.Throws<InvalidDataException>(() =>
            new PlannerDailyAvailability("07:00", "07:00", "local").Ground("in the afternoon"));
        Assert.Throws<InvalidDataException>(() =>
            new PlannerDailyAvailability("00:00", "23:59", "local").Ground("in the afternoon"));
    }

    [Theory]
    [InlineData("After-work AI sessions on November 10 before 11pm", true)]
    [InlineData("AI during my commute on November 10 before 11pm", true)]
    [InlineData("AI during work on November 10 before 11pm", true)]
    [InlineData("AI during business hours on November 10 before 11pm", true)]
    [InlineData("AI during the day on November 10 before 11pm", true)]
    [InlineData("AI in the afternoons on November 10 before 11pm", true)]
    [InlineData("AI early in the day on November 10 before 11pm", true)]
    [InlineData("AI for early-career developers on November 10 before 11pm", false)]
    public void TimeHintsDescribeTimeRatherThanUnrelatedWords(string request, bool expected) =>
        Assert.Equal(expected, PlannerInterpretation.HasAdditionalTimeHint(request));

    [Fact]
    public void PluralDayPartsTriggerTimeExtractionButEarlyCareerDoesNot()
    {
        var interpretation = new PlannerInterpretation("none", "afternoons", "none", "some");
        Assert.True(interpretation.RequiresTimeExtraction("Show sessions in the afternoons"));
        Assert.True((interpretation with { AvailabilityText = "none" }).RequiresTimeExtraction("Show sessions in the afternoons"));
        Assert.False(interpretation.RequiresTimeExtraction("AI for early-career developers"));
    }

    [Theory]
    [InlineData("AI on November 10 in the afternoons before 11pm UTC", "AI on November 10 in the afternoons  UTC")]
    [InlineData("AI in the afternoons from 7am to 11pm UTC", "AI in the afternoons  UTC")]
    [InlineData("AI after 9am during work", "AI  during work")]
    public void QualitativeExtractionDoesNotSeeExplicitClockLimits(string request, string expected) =>
        Assert.Equal(expected, PlannerInterpretation.WithoutClockLimits(request));

    [Theory]
    [InlineData("AI at night before November 12 before 2am UTC", "AI at night  before 2am UTC")]
    [InlineData("AI on November 10 and 12 in the afternoons", "AI  in the afternoons")]
    public void QualitativeExtractionDoesNotSeeCalendarDates(string request, string expected) =>
        Assert.Equal(expected, PlannerInterpretation.WithoutCalendarDates(request));

    [Theory]
    [InlineData("AI on 11/10 and 11/12 before 11pm UTC")]
    [InlineData("Show AI sessions before 11pm, nothing too advanced.")]
    [InlineData("I am interested in AI, nothing too basic.")]
    [InlineData("Only AI sessions suitable for beginners")]
    [InlineData("Find advanced AI sessions")]
    public void AmbiguousDatesAndUnavailableSkillLevelsAreReported(string request) =>
        Assert.Throws<InvalidDataException>(() => Agenda.ValidatePlannerRequest(request));

    [Fact]
    public void SeparatelyPhrasedClockCutoffsAreBothGrounded()
    {
        var limits = new PlannerDailyAvailability("none", "none", "UTC")
            .Ground("AI on November 10 after 5pm and before 7pm", sourceOnly: true);
        Assert.Equal("17:00", limits.DailyStart);
        Assert.Equal("19:00", limits.DailyEnd);
    }

    [Theory]
    [InlineData("AI after November 10 from 9am to 5pm UTC")]
    [InlineData("AI before November 10 from 9am to 5pm UTC")]
    public void OpenEndedDatesWithClockRangesDoNotBecomeExactDays(string request) =>
        Assert.False(PlannerInterpretation.TryCalendarDates(request, 2026, "UTC", out _, out _));

    [Fact]
    public void CustomAndOvernightDefaultsAreRespectedAndInvalidDefaultsAreRejected()
    {
        Assert.Equal((new TimeOnly(9, 0), new TimeOnly(21, 0)),
            new PlannerWatchingHours(new(9, 0), new(20, 0)).Complete(null, new(21, 0)));
        Assert.Equal((new TimeOnly(22, 0), new TimeOnly(4, 0)),
            new PlannerWatchingHours(new(22, 0), new(6, 0)).Complete(null, new(4, 0)));
        Assert.Equal((new TimeOnly(22, 0), new TimeOnly(23, 0)),
            new PlannerWatchingHours(new(22, 0), new(6, 0)).Complete(null, new(23, 0)));
        Assert.Equal((new TimeOnly(5, 0), new TimeOnly(6, 0)),
            new PlannerWatchingHours(new(22, 0), new(6, 0)).Complete(new(5, 0), null));
        Assert.Throws<InvalidDataException>(() => new PlannerWatchingHours(new(7, 0), new(7, 0)).Complete(null, new(23, 0)));
        Assert.Throws<InvalidDataException>(() => PlannerWatchingHours.Default.Complete(null, null));
    }

    [Theory]
    [InlineData(22, true)]
    [InlineData(21, true)]
    [InlineData(6, false)]
    [InlineData(8, false)]
    public void AmbiguousCutoffsCannotTurnOvernightDefaultsIntoAlmostFullDays(int hour, bool finish)
    {
        var hours = new PlannerWatchingHours(new(22, 0), new(6, 0));
        Assert.Throws<InvalidDataException>(() => finish
            ? hours.Complete(null, new(hour, 0))
            : hours.Complete(new(hour, 0), null));
    }

    [Fact]
    public void OvernightLimitsIncludeTheWindowBeforeTheFirstLocalConferenceDay()
    {
        var agenda = SyntheticAgenda();
        agenda.Sessions.Clear();
        agenda.Sessions.Add(Talk("first", "Synthetic AI", "", ["AI"], 16));
        var daily = new PlannerDailyAvailability("22:00", "02:00", "Asia/Tokyo");
        AvailabilityWindow[] requested = [new("2026-11-10T22:00", "2026-11-11T02:00", "Asia/Tokyo")];

        Assert.Equal(requested, daily.RestrictDatedWindows(requested, TimeZoneInfo.Utc, agenda));
    }

    [Theory]
    [InlineData("AI on November 10", "10")]
    [InlineData("Nov 10", "10")]
    [InlineData("Nov. 10", "10")]
    [InlineData("AI on November 10 and November 12 before 11pm UTC", "10,12")]
    [InlineData("AI on Nov. 10 and 12", "10,12")]
    [InlineData("AI on November 10, 11 and 12", "10,11,12")]
    [InlineData("AI from November 10-12", "10,11,12")]
    [InlineData("AI from November 10 through 12", "10,11,12")]
    [InlineData("AI from November 10 through November 12 before 11pm UTC", "10,11,12")]
    [InlineData("AI between November 10 and November 12 before 11pm UTC", "10,11,12")]
    [InlineData("AI between 2026-11-10 and 2026-11-12", "10,11,12")]
    [InlineData("AI on 2026-11-10 and 2026-11-12", "10,12")]
    [InlineData("AI on Nov 10-12 sessions", "10,11,12")]
    [InlineData("AI on Nov 10 to 12 talks", "10,11,12")]
    [InlineData("AI on Nov 10 through 12 of these sessions", "10,11,12")]
    [InlineData("AI on Nov 10, 11, 12 sessions", "10,11,12")]
    [InlineData("AI on Nov 10, 11, 12 of the sessions", "10,11,12")]
    [InlineData("AI on November 10 or 11", "10,11")]
    [InlineData("AI on Tuesday, November 10", "10")]
    [InlineData("AI on Nov 10 (Tuesday) and Nov 11 (Wednesday)", "10,11")]
    [InlineData("AI on Thursday, Nov 12, Friday, Nov 13", "12,13")]
    [InlineData("AI on Nov 12 (Thursday), Nov 13 (Friday)", "12,13")]
    [InlineData("AI on Nov 12, Thursday, Nov 13, Friday", "12,13")]
    [InlineData("AI on November 10, Tuesday", "10")]
    [InlineData("AI on November 10 (Tuesday) to 12 UTC", "10,11,12")]
    [InlineData("AI on November 10 (Tuesday), 11, 12 UTC", "10,11,12")]
    public void ExplicitCalendarDatesComeFromTheRequest(string request, string days)
    {
        Assert.True(PlannerInterpretation.HasExplicitDate(request));
        Assert.True(PlannerInterpretation.TryCalendarDates(request, 2026, "UTC", out var windows, out _));
        Assert.Equal(days.Split(',').Select(day => $"2026-11-{day}T00:00"), windows.Select(window => window.Start));
        Assert.All(windows, window => Assert.Equal("UTC", window.Zone));
    }

    [Theory]
    [InlineData("Blazor on November 12 with .NET 20")]
    [InlineData("Blazor on November 12, up to 20 sessions")]
    [InlineData("Blazor on November 12 with 15 to 20 sessions")]
    [InlineData("Blazor on November 12 with 5 to 10 sessions")]
    [InlineData("Blazor on November 12, 5-10 sessions")]
    [InlineData("Blazor on November 12, 10 to 12 of the sessions")]
    [InlineData("Blazor on November 12, 10 or 12 of the sessions")]
    [InlineData("Blazor between 5 and 10 sessions")]
    [InlineData("Blazor between 5 or 10 sessions")]
    [InlineData("Blazor from 3 to 8 talks")]
    public void NonTimeNumbersDoNotBecomeClockLimits(string request)
    {
        var limits = new PlannerDailyAvailability("none", "none", "local").Ground(request, sourceOnly: true);
        Assert.Equal("none", limits.DailyStart);
        Assert.Equal("none", limits.DailyEnd);
    }

    [Fact]
    public void QuantityRangeDoesNotHideASeparateClockRange()
    {
        var limits = new PlannerDailyAvailability("none", "none", "local")
            .Ground("Show 5 to 10 AI sessions from 9am to 5pm", sourceOnly: true);
        Assert.Equal("09:00", limits.DailyStart);
        Assert.Equal("17:00", limits.DailyEnd);
        Assert.Throws<InvalidDataException>(() => new PlannerDailyAvailability("none", "none", "local")
            .Ground("Show 5 to 10 AI sessions from 9am to 5", sourceOnly: true));
    }

    [Theory]
    [InlineData("Talks by Jan about Blazor", false)]
    [InlineData("Talks by Jan on January 12", true)]
    [InlineData("Talks in January", true)]
    public void MonthAbbreviationsDoNotTurnNamesIntoDates(string request, bool hasDate)
    {
        Assert.Equal(hasDate, PlannerInterpretation.HasExplicitDate(request));
        Assert.Equal(hasDate, new PlannerInterpretation("none", "none", "none", "some").RequiresTimeExtraction(request));
    }

    [Theory]
    [InlineData("I can't watch AI sessions on November 10 UTC")]
    [InlineData("I can't watch any of the AI sessions on November 10 UTC")]
    [InlineData("I am not able to watch AI on November 10 UTC")]
    [InlineData("I can't watch AI before 9am or on November 10 UTC")]
    [InlineData("AI after November 10 and before November 12 UTC")]
    [InlineData("AI from 10pm November 10 to 2am November 11 UTC")]
    [InlineData("AI on November 10 before 5pm and November 11 after 10am UTC")]
    [InlineData("AI on November 10 in the morning and November 11 in the evening UTC")]
    [InlineData("AI before 5pm on November 10 and November 11 after 10am UTC")]
    [InlineData("AI in the morning on November 10 and November 11 in the evening UTC")]
    [InlineData("AI before 5pm on November 10 and 11 after 10am UTC")]
    [InlineData("AI on November 10, 11 and 12 sessions")]
    [InlineData("AI on November 12, 10 sessions")]
    [InlineData("AI on November 12 and 10 AI sessions")]
    [InlineData("AI on November 12, 10 to 12 sessions")]
    [InlineData("AI on November 12 and 5-10 AI sessions")]
    [InlineData("AI on November 12, 5 and 10 talks")]
    [InlineData("AI on November 12, 10 to 12 of the sessions")]
    [InlineData("AI on November 12, 10 or 11 sessions")]
    [InlineData("AI on November 12, 11 keynotes")]
    [InlineData("AI on November 12, 10 of the 20 sessions")]
    [InlineData("AI on November 12, 10 out of the sessions")]
    [InlineData("AI on November 10 (Tuesday), 11 sessions")]
    [InlineData("AI on November 12 and 5-10 of these AI talks")]
    [InlineData("AI on November 12-10 sessions")]
    [InlineData("AI on November 12 through November 10")]
    [InlineData("AI between November 12 and 10")]
    [InlineData("AI between November 12 and November 10")]
    [InlineData("AI on November 11 and after")]
    [InlineData("AI on November 11 onwards")]
    [InlineData("AI on November 11 or later")]
    [InlineData("Skip November 10, show AI on November 11 UTC")]
    [InlineData("AI on Wednesday, November 10 UTC")]
    [InlineData("AI on Tuesday, November 10, Tuesday, November 11 UTC")]
    [InlineData("AI on November 10-12, not 11 UTC")]
    [InlineData("AI on November 10, 11, 12 except 11 UTC")]
    public void AmbiguousDateConstraintsAreNotSilentlyWidened(string request) =>
        Assert.Throws<InvalidDataException>(() =>
            PlannerInterpretation.TryCalendarDates(request, 2026, "UTC", out _, out _, SyntheticAgenda(), TimeZoneInfo.Utc));

    [Theory]
    [InlineData("I can't watch AI before 9am on November 10 UTC")]
    [InlineData("I am not able to watch AI before 9am on November 10 UTC")]
    [InlineData("I can't watch AI on November 10 before 9am UTC")]
    public void NegatedClockCutoffDoesNotExcludeItsDate(string request)
    {
        Assert.True(PlannerInterpretation.TryCalendarDates(
            request, 2026, "UTC", out var windows, out _, SyntheticAgenda(), TimeZoneInfo.Utc));
        Assert.Equal("2026-11-10T00:00", Assert.Single(windows).Start);
        var hours = new PlannerDailyAvailability("none", "none", "UTC").Ground(request, sourceOnly: true);
        Assert.Equal("09:00", hours.DailyStart);
    }

    [Theory]
    [InlineData("AI before November 10")]
    [InlineData("AI after November 10")]
    [InlineData("AI starting November 10")]
    public void OpenEndedDatesDoNotBecomeSingleCalendarDays(string request) =>
        Assert.False(PlannerInterpretation.TryCalendarDates(request, 2026, "UTC", out _, out _));

    [Fact]
    public void MixedShorthandDateRangeAndListIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => PlannerInterpretation.TryCalendarDates(
            "AI on November 10 to 12 and 14", 2026, "UTC", out _, out _));
    }

    [Fact]
    public void MaximumCalendarDateDoesNotOverflow()
    {
        Assert.Throws<InvalidDataException>(() => PlannerInterpretation.TryCalendarDates(
            "AI on December 31, 9999", 2026, "UTC", out _, out _));
    }

    [Theory]
    [InlineData("AI before November 10", "09")]
    [InlineData("AI after November 10", "11,12,13")]
    [InlineData("AI starting November 10", "10,11,12,13")]
    [InlineData("AI until November 10", "09,10")]
    [InlineData("AI on or after November 10", "10,11,12,13")]
    [InlineData("AI on or before November 10", "09,10")]
    [InlineData("AI on   or   before November 10", "09,10")]
    [InlineData("AI by November 10", "09,10")]
    [InlineData("AI through November 10", "09,10")]
    public void OpenEndedDateRestrictionsKeepEveryEligibleDay(string request, string days)
    {
        var agenda = SyntheticAgenda();
        agenda.Sessions.Clear();
        for (var day = -1; day <= 3; day++)
            agenda.Sessions.Add(Talk($"day-{day}", "Synthetic AI", "", ["AI"], day * 24 + 12));

        Assert.True(PlannerInterpretation.TryCalendarDates(
            request, 2026, "UTC", out var windows, out var openEnded, agenda, TimeZoneInfo.Utc));
        Assert.True(openEnded);
        Assert.Equal(days.Split(',').Select(day => $"2026-11-{day}T00:00"), windows.Select(window => window.Start));
    }

    [Fact]
    public void DatedCutoffsApplyWatchingHoursOnEachDateWithoutFillingTheNightsBetween()
    {
        var agenda = SyntheticAgenda();
        agenda.Sessions.Add(Talk("next-day", "Synthetic AI", "", ["AI"], 45));
        var daily = new PlannerDailyAvailability("none", "none", "UTC").Ground("November 10 and November 11 before 11pm")
            .Complete(PlannerWatchingHours.Default);
        var actual = daily.RestrictDatedWindows(
            [PlannerDailyAvailability.CalendarDates(new("2026-11-10T00:00", "2026-11-12T00:00", "UTC"))], TimeZoneInfo.Utc, agenda);
        Assert.Equal([
            new AvailabilityWindow("2026-11-10T07:00", "2026-11-10T23:00", "UTC"),
            new AvailabilityWindow("2026-11-11T07:00", "2026-11-11T23:00", "UTC")
        ], actual);
        Assert.Throws<InvalidDataException>(() => daily.RestrictDatedWindows(
            [PlannerDailyAvailability.CalendarDates(new("2026-11-12T01:00", "2026-11-12T05:00", "UTC"))], TimeZoneInfo.Utc, agenda));
    }

    [Theory]
    [InlineData("09:00", "23:00")]
    [InlineData("01:00", "05:00")]
    [InlineData("00:00", "00:00")]
    [InlineData("23:00", "07:00")]
    public void DatedCutoffsDoNotKeepInventedModelTimes(string modelStart, string modelEnd)
    {
        var daily = new PlannerDailyAvailability("none", "none", "UTC")
            .Ground("November 10 before 11pm UTC").Complete(PlannerWatchingHours.Default);

        var actual = daily.RestrictDatedWindows(
            [PlannerDailyAvailability.CalendarDates(new($"2026-11-10T{modelStart}", $"2026-11-10T{modelEnd}", "UTC"))], TimeZoneInfo.Utc, SyntheticAgenda());

        Assert.Equal(new AvailabilityWindow("2026-11-10T07:00", "2026-11-10T23:00", "UTC"), Assert.Single(actual));
        Assert.Contains("07:00", daily.Assumption);
    }

    [Fact]
    public void DatedCutoffsKeepSeparateDatesAndMidnightExclusive()
    {
        var agenda = SyntheticAgenda();
        agenda.Sessions.Add(Talk("third-day", "Synthetic AI", "", ["AI"], 68));
        var daily = new PlannerDailyAvailability("none", "none", "UTC")
            .Ground("November 10 and November 12 before 11pm UTC").Complete(PlannerWatchingHours.Default);

        var actual = daily.RestrictDatedWindows([
            PlannerDailyAvailability.CalendarDates(new("2026-11-10T09:00", "2026-11-10T23:00", "UTC")),
            PlannerDailyAvailability.CalendarDates(new("2026-11-12T09:00", "2026-11-13T00:00", "UTC"))
        ], TimeZoneInfo.Utc, agenda);

        Assert.Equal([
            new AvailabilityWindow("2026-11-10T07:00", "2026-11-10T23:00", "UTC"),
            new AvailabilityWindow("2026-11-12T07:00", "2026-11-12T23:00", "UTC")
        ], actual);
    }

    [Fact]
    public void DatedCutoffsKeepTheRequestedTimeZone()
    {
        var daily = new PlannerDailyAvailability("none", "none", "Europe/Prague")
            .Ground("November 10 before 11pm Europe/Prague").Complete(PlannerWatchingHours.Default);

        var actual = daily.RestrictDatedWindows(
            [PlannerDailyAvailability.CalendarDates(new("2026-11-10T09:00", "2026-11-10T23:00", "Europe/Prague"))], TimeZoneInfo.Utc, SyntheticAgenda());

        Assert.Equal(new AvailabilityWindow("2026-11-10T07:00", "2026-11-10T23:00", "Europe/Prague"),
            Assert.Single(actual));
    }

    [Fact]
    public void AmbiguousModelDateSpansCannotAddAnUnrequestedDay()
    {
        var agenda = SyntheticAgenda();
        agenda.Sessions.Add(Talk("next-day", "Synthetic AI", "", ["AI"], 45));
        var daily = new PlannerDailyAvailability("none", "none", "UTC")
            .Ground("November 10 before 11pm UTC").Complete(PlannerWatchingHours.Default);

        Assert.Throws<InvalidDataException>(() => daily.RestrictDatedWindows(
            [PlannerDailyAvailability.CalendarDates(new("2026-11-10T09:00", "2026-11-11T01:00", "UTC"))], TimeZoneInfo.Utc, agenda));
    }

    [Fact]
    public void ExplicitOvernightWindowsKeepBothRequestedClockTimes()
    {
        var agenda = SyntheticAgenda();
        agenda.Sessions.Add(Talk("next-day", "Synthetic AI", "", ["AI"], 45));
        var daily = new PlannerDailyAvailability("none", "none", "UTC")
            .Ground("November 10 and November 11 from 10pm to 2am UTC").Complete(PlannerWatchingHours.Default);
        AvailabilityWindow[] requested = [
            new("2026-11-10T22:00", "2026-11-11T02:00", "UTC"),
            new("2026-11-11T22:00", "2026-11-12T02:00", "UTC")
        ];

        Assert.Empty(daily.Assumption);
        Assert.Equal(requested, daily.RestrictDatedWindows(requested, TimeZoneInfo.Utc, agenda));
    }

    [Theory]
    [InlineData("Show AI sessions before 11pm, nothing too advanced.")]
    [InlineData("I am interested in AI, nothing too basic.")]
    [InlineData("Show AI talks, nothing too complicated.")]
    [InlineData("Show AI talks before 11pm, including overnight.")]
    [InlineData("Show AI talks before 11pm, not including overnight.")]
    [InlineData("AI talks and sessions in the evening UTC")]
    [InlineData("AI talks and sessions in the mornings UTC")]
    [InlineData("AI talks, nothing after 11pm")]
    [InlineData("AI talks, without sessions after 11pm")]
    [InlineData("AI talks, avoiding sessions after 11pm")]
    [InlineData("AI talks, unavailable before 9am")]
    [InlineData("AI talks, I won't watch after 11pm")]
    [InlineData("AI on November 12, before midnight UTC")]
    [InlineData("AI talks, before noon UTC")]
    public void NonTopicQualifiersDoNotBecomeAdditionalInterests(string request) =>
        Assert.Empty(PlannerCatalog.SemanticInterests(SyntheticAgenda(), request));

    [Theory]
    [InlineData("November 23, 2026 before 11pm")]
    [InlineData("2026-11-23 before 11pm")]
    [InlineData("11/23 before 11pm")]
    public void DateNumbersCannotBecomeClockTimesOrHideAnExplicitCutoff(string request)
    {
        var limits = new PlannerDailyAvailability("none", "none", "UTC").Ground(request, sourceOnly: true).Complete(PlannerWatchingHours.Default);
        Assert.Equal("07:00", limits.DailyStart);
        Assert.Equal("23:00", limits.DailyEnd);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WatchingDefaultsNeverRestrictAnUnrestrictedTopicRequest(bool structured)
    {
        var client = Substitute.For<IChatClient>();
        var responses = new Queue<string>([
            """{"speakerNames":"none","availabilityText":"none","quantity":"some","searchQuery":"none"}""",
            """{"quantity":"some","speakerScope":"independent"}"""
        ]);
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, responses.Dequeue())));
        using var services = new ServiceCollection().AddScoped<PlannerTools>().AddTransient(_ => client).BuildServiceProvider();
        var planner = new OnDevicePlanner(services.GetRequiredService<IServiceScopeFactory>(), NullLoggerFactory.Instance,
            services.GetRequiredService<IServiceProviderIsService>(), new TestPlannerModelStatus(structuredOutput: structured));
        var result = await planner.ProposeAsync(SyntheticAgenda(), "Blazor", TimeZoneInfo.Utc, [], default,
            watchingHours: new(new(12, 0), new(13, 0)));
        Assert.NotNull(result.Proposal);
        Assert.Equal(["focused", "untagged"], result.Proposal.SessionIds);
        Assert.Empty(result.Proposal.Availability);
        Assert.Empty(result.Proposal.AvailabilityAssumption);
    }

    [Theory]
    [InlineData(true, "AI sessions before 11pm UTC")]
    [InlineData(false, "AI sessions before 11pm UTC")]
    [InlineData(true, "AI sessions before 11pm UTC, not including overnight.")]
    [InlineData(false, "AI sessions before 11pm UTC, not including overnight.")]
    public async Task ProvidersKeepDaytimeLimitsAndRejectFullSessionsCrossingTheCutoff(bool structured, string request)
    {
        var agenda = SyntheticAgenda();
        var responses = new Queue<string>([
            """{"speakerNames":"none","availabilityText":"before 11pm","quantity":"some","searchQuery":"none"}""",
            """{"dailyStart":"09:00","dailyEnd":"23:00","zone":"local"}""",
            """{"quantity":"some","speakerScope":"independent"}"""
        ]);
        using var services = CreateServices(structured, responses);
        var planner = CreatePlanner(services, structured);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
        var result = await planner.ProposeAsync(agenda, request, zone, [], default);
        Assert.Empty(responses);
        Assert.NotNull(result.Proposal);
        Assert.Equal(["daytime"], result.Proposal.SessionIds);
        Assert.Equal(["daytime"], result.Proposal.MatchedSessionIds);
        Assert.Equal(["AI"], result.Proposal.TopicFilter!.Topics);
        Assert.Equal("Assumed daily start: 07:00 (UTC).", result.Proposal.AvailabilityAssumption);
        Assert.All(result.Proposal.Availability, window =>
        {
            Assert.EndsWith("T07:00", window.Start);
            Assert.EndsWith("T23:00", window.End);
            Assert.Equal("UTC", window.Zone);
        });
        Assert.True(SchedulePlanner.Review(agenda, result.Proposal, zone, []).CanApply);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProvidersKeepTheCompleteExplicitOffsetZone(bool structured)
    {
        var responses = new Queue<string>([
            """{"speakerNames":"none","availabilityText":"before 11pm","quantity":"some","searchQuery":"none"}""",
            """{"dailyStart":"none","dailyEnd":"23:00","zone":"local"}""",
            """{"quantity":"some","speakerScope":"independent"}"""
        ]);
        using var services = CreateServices(structured, responses);
        var planner = CreatePlanner(services, structured);

        var result = await planner.ProposeAsync(SyntheticAgenda(), "AI before 11pm Etc/GMT+2", TimeZoneInfo.Utc, [], default);

        Assert.Empty(responses);
        Assert.NotNull(result.Proposal);
        Assert.Equal("Assumed daily start: 07:00 (Etc/GMT+2).", result.Proposal.AvailabilityAssumption);
        Assert.All(result.Proposal.Availability, window => Assert.Equal("Etc/GMT+2", window.Zone));
        Assert.Contains("crossing", result.Proposal.MatchedSessionIds);
        Assert.DoesNotContain("overnight", result.Proposal.MatchedSessionIds);
    }

    [Theory]
    [InlineData(true, "Etc/GMT+")]
    [InlineData(false, "Etc/GMT+")]
    [InlineData(true, "Etc/GMT+100")]
    [InlineData(false, "Etc/GMT+100")]
    public async Task ProvidersRejectTheWholeInvalidTimeZone(bool structured, string requestedZone)
    {
        var responses = new Queue<string>([
            """{"speakerNames":"none","availabilityText":"before 11pm","quantity":"some","searchQuery":"none"}""",
            """{"dailyStart":"none","dailyEnd":"23:00","zone":"local"}"""
        ]);
        using var services = CreateServices(structured, responses);
        var planner = CreatePlanner(services, structured);

        var result = await planner.ProposeAsync(SyntheticAgenda(), $"AI before 11pm {requestedZone}", TimeZoneInfo.Utc, [], default);

        Assert.Null(result.Proposal);
        Assert.Contains("A time zone wasn't recognized. Use a valid time zone, such as UTC or Europe/Paris.", result.Message);
    }

    [Theory]
    [InlineData(true, "I can't watch AI before 9am and I can't watch after 5pm UTC", "09:00", "17:00")]
    [InlineData(false, "I can't watch AI before 9am and I can't watch after 5pm UTC", "09:00", "17:00")]
    [InlineData(true, "I can't watch AI before 5pm and I must finish before 9pm UTC", "17:00", "21:00")]
    [InlineData(false, "I can't watch AI before 5pm and I must finish before 9pm UTC", "17:00", "21:00")]
    [InlineData(true, "I can't watch AI before 5pm and must finish before 9pm UTC", "17:00", "21:00")]
    [InlineData(false, "I can't watch AI before 5pm and must finish before 9pm UTC", "17:00", "21:00")]
    [InlineData(true, "I can't watch any of the AI sessions, after 5pm UTC", "07:00", "17:00")]
    [InlineData(false, "I can't watch any of the AI sessions, after 5pm UTC", "07:00", "17:00")]
    public async Task ProvidersKeepNegationScopedToItsCutoff(bool structured, string request, string start, string end)
    {
        var agenda = SyntheticAgenda();
        agenda.Sessions.Add(Talk("noon", "Synthetic AI", "", ["AI"], 12));
        var responses = new Queue<string>([
            """{"speakerNames":"none","availabilityText":"time limits","quantity":"some","searchQuery":"none"}""",
            $$"""{"dailyStart":"{{start}}","dailyEnd":"{{end}}","zone":"UTC"}""",
            """{"quantity":"some","speakerScope":"independent"}"""
        ]);
        using var services = CreateServices(structured, responses);
        var planner = CreatePlanner(services, structured);

        var result = await planner.ProposeAsync(agenda, request, TimeZoneInfo.Utc, [], default);

        Assert.Empty(responses);
        Assert.NotNull(result.Proposal);
        Assert.NotEmpty(result.Proposal.Availability);
        Assert.All(result.Proposal.Availability, window =>
        {
            Assert.EndsWith($"T{start}", window.Start);
            Assert.EndsWith($"T{end}", window.End);
            Assert.Equal("UTC", window.Zone);
        });
        Assert.True(SchedulePlanner.Review(agenda, result.Proposal, TimeZoneInfo.Utc, []).CanApply);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProvidersReportUnsupportedSkillLevelWithoutCallingTheModel(bool structured)
    {
        var client = Substitute.For<IChatClient>();
        using var services = new ServiceCollection().AddScoped<PlannerTools>().AddTransient(_ => client).BuildServiceProvider();
        var planner = new OnDevicePlanner(services.GetRequiredService<IServiceScopeFactory>(), NullLoggerFactory.Instance,
            services.GetRequiredService<IServiceProviderIsService>(), new TestPlannerModelStatus(structuredOutput: structured));

        var result = await planner.ProposeAsync(SyntheticAgenda(), "AI before 11pm, nothing too advanced.",
            TimeZoneInfo.Utc, [], default);

        Assert.Null(result.Proposal);
        Assert.Contains("difficulty", result.Message);
        await client.DidNotReceive().GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, "I can't watch any of the AI sessions on November 10 UTC")]
    [InlineData(false, "I can't watch any of the AI sessions on November 10 UTC")]
    [InlineData(true, "AI before 5pm on November 10 and November 11 after 10am UTC")]
    [InlineData(false, "AI before 5pm on November 10 and November 11 after 10am UTC")]
    [InlineData(true, "AI in the morning on November 10 and November 11 in the evening UTC")]
    [InlineData(false, "AI in the morning on November 10 and November 11 in the evening UTC")]
    [InlineData(true, "AI on November 10, 11 and 12 sessions UTC")]
    [InlineData(false, "AI on November 10, 11 and 12 sessions UTC")]
    [InlineData(true, "Skip November 10, show AI on November 11 UTC")]
    [InlineData(false, "Skip November 10, show AI on November 11 UTC")]
    [InlineData(true, "AI on Wednesday, November 10 UTC")]
    [InlineData(false, "AI on Wednesday, November 10 UTC")]
    [InlineData(true, "AI on November 10-12, not 11 UTC")]
    [InlineData(false, "AI on November 10-12, not 11 UTC")]
    [InlineData(true, "AI on November 12-10 sessions UTC")]
    [InlineData(false, "AI on November 12-10 sessions UTC")]
    [InlineData(true, "AI on November 12 through November 10 UTC")]
    [InlineData(false, "AI on November 12 through November 10 UTC")]
    [InlineData(true, "AI on November 12, 10 or 11 sessions UTC")]
    [InlineData(false, "AI on November 12, 10 or 11 sessions UTC")]
    [InlineData(true, "AI on November 12, 10 out of the 20 sessions UTC")]
    [InlineData(false, "AI on November 12, 10 out of the 20 sessions UTC")]
    public async Task ProvidersRejectUnsupportedDateConstraintsWithoutAProposal(bool structured, string request)
    {
        var client = Substitute.For<IChatClient>();
        const string interpretation = """{"speakerNames":"none","availabilityText":"dates and times","quantity":"some","searchQuery":"none"}""";
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                structured ? interpretation : $"```json\n{interpretation}\n```")));
        using var services = new ServiceCollection().AddScoped<PlannerTools>().AddTransient(_ => client).BuildServiceProvider();
        var planner = new OnDevicePlanner(services.GetRequiredService<IServiceScopeFactory>(), NullLoggerFactory.Instance,
            services.GetRequiredService<IServiceProviderIsService>(), new TestPlannerModelStatus(structuredOutput: structured));

        var result = await planner.ProposeAsync(SyntheticAgenda(), request, TimeZoneInfo.Utc, [], default);

        Assert.Null(result.Proposal);
        Assert.Contains("Nothing has been saved", result.Message);
        await client.Received(1).GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NamedClockClauseDoesNotIntroduceAnUnrelatedTopic(bool structured)
    {
        var agenda = SyntheticAgenda();
        agenda.Sessions.Add(Talk("third-day", "Synthetic AI", "", ["AI"], 68));
        var responses = new Queue<string>([
            """{"speakerNames":"none","availabilityText":"November 12 before midnight","quantity":"some","searchQuery":"none"}""",
            """{"quantity":"some","speakerScope":"independent"}"""
        ]);
        using var services = CreateServices(structured, responses);
        var planner = CreatePlanner(services, structured);

        var result = await planner.ProposeAsync(agenda, "AI on November 12, before midnight UTC", TimeZoneInfo.Utc, [], default);

        Assert.NotNull(result.Proposal);
        Assert.Equal(["AI"], result.Proposal.TopicFilter!.Topics);
        Assert.Empty(responses);
    }

    [Theory]
    [InlineData(true, "AI on Tuesday, November 10 UTC", "2026-11-10T00:00", "2026-11-11T00:00")]
    [InlineData(false, "AI on Tuesday, November 10 UTC", "2026-11-10T00:00", "2026-11-11T00:00")]
    [InlineData(true, "AI on November 10-12 sessions UTC", "2026-11-10T00:00", "2026-11-13T00:00")]
    [InlineData(false, "AI on November 10-12 sessions UTC", "2026-11-10T00:00", "2026-11-13T00:00")]
    [InlineData(true, "AI on November 10 (Tuesday) to 12 UTC", "2026-11-10T00:00", "2026-11-13T00:00")]
    [InlineData(false, "AI on November 10 (Tuesday) to 12 UTC", "2026-11-10T00:00", "2026-11-13T00:00")]
    public async Task ProvidersKeepNamedWeekdaysAndCompleteDateRanges(bool structured, string request, string start, string end)
    {
        var agenda = SyntheticAgenda();
        agenda.Sessions.Add(Talk("next-day", "Synthetic AI", "", ["AI"], 45));
        agenda.Sessions.Add(Talk("third-day", "Synthetic AI", "", ["AI"], 68));
        var responses = new Queue<string>([
            """{"speakerNames":"none","availabilityText":"dates","quantity":"some","searchQuery":"none"}""",
            """{"quantity":"some","speakerScope":"independent"}"""
        ]);
        using var services = CreateServices(structured, responses);
        var planner = CreatePlanner(services, structured);

        var result = await planner.ProposeAsync(agenda, request, TimeZoneInfo.Utc, [], default);

        Assert.Empty(responses);
        Assert.NotNull(result.Proposal);
        Assert.Equal(["AI"], result.Proposal.TopicFilter!.Topics);
        Assert.Equal(new AvailabilityWindow(start, end, "UTC"), Assert.Single(result.Proposal.Availability));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProvidersReportAnEchoedClockSchemaAsAnUnusableModelResponse(bool structured)
    {
        var responses = new Queue<string>([
            """{"speakerNames":"none","availabilityText":"afternoons","quantity":"some","searchQuery":"none"}""",
            """{"title":"ConferenceDailyClockLimits","type":"object","properties":{}}"""
        ]);
        using var services = CreateServices(structured, responses);
        var planner = CreatePlanner(services, structured);

        var result = await planner.ProposeAsync(SyntheticAgenda(), "AI in the afternoons before 11pm UTC",
            TimeZoneInfo.Utc, [], default);

        Assert.Null(result.Proposal);
        Assert.Contains("didn't return usable hours", result.Message);
        Assert.Empty(responses);
    }

    [Theory]
    [InlineData("23:00", "07:00")]
    [InlineData("00:00", "00:00")]
    public void DateOnlyModelEndpointsNormalizeBeforeCheckingCoverage(string modelStart, string modelEnd)
    {
        var dates = new[] { "10", "12" }.Select(day => PlannerDailyAvailability.CalendarDates(
            new AvailabilityWindow($"2026-11-{day}T{modelStart}", $"2026-11-{day}T{modelEnd}", "UTC"))).ToArray();
        Assert.Equal(new AvailabilityWindow("2026-11-10T07:00", "2026-11-10T23:00", "UTC"),
            Assert.Single(new PlannerDailyAvailability("07:00", "23:00", "UTC")
                .RestrictDatedWindows(dates, TimeZoneInfo.Utc, SyntheticAgenda())));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DatedTimePreferencesKeepEveryRequestedDayAcrossProviders(bool structured)
    {
        var agenda = SyntheticAgenda();
        agenda.Sessions.Add(Talk("third-day", "Synthetic AI", "", ["AI"], 68));
        var responses = new Queue<string>([
            """{"speakerNames":"none","availabilityText":"afternoons on November 10 and 12","quantity":"some","searchQuery":"none"}""",
            """{"dailyStart":"12:00","dailyEnd":"17:00","zone":"UTC"}""",
            """{"quantity":"some","speakerScope":"independent"}"""
        ]);
        using var services = CreateServices(structured, responses);
        var planner = CreatePlanner(services, structured);

        var result = await planner.ProposeAsync(agenda, "AI in the afternoons on November 10 and 12 UTC",
            TimeZoneInfo.Utc, [], default);

        Assert.NotNull(result.Proposal);
        Assert.Equal([
            new AvailabilityWindow("2026-11-10T12:00", "2026-11-10T17:00", "UTC"),
            new AvailabilityWindow("2026-11-12T12:00", "2026-11-12T17:00", "UTC")
        ], result.Proposal.Availability);
        Assert.Empty(responses);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NamedNightStaysAnchoredAcrossMidnight(bool structured)
    {
        var agenda = SyntheticAgenda();
        agenda.Sessions.Add(Talk("next-night", "Synthetic AI after midnight", "", ["AI"], 25));
        var responses = new Queue<string>([
            """{"speakerNames":"none","availabilityText":"November 10 at night before 2am","quantity":"some","searchQuery":"none"}""",
            """{"dailyStart":"20:00","dailyEnd":"06:00","zone":"UTC"}""",
            """{"quantity":"some","speakerScope":"independent"}"""
        ]);
        using var services = CreateServices(structured, responses);
        var planner = CreatePlanner(services, structured);

        var result = await planner.ProposeAsync(agenda, "AI on November 10 at night before 2am UTC",
            TimeZoneInfo.Utc, [], default);

        Assert.NotNull(result.Proposal);
        Assert.Equal(new AvailabilityWindow("2026-11-10T20:00", "2026-11-11T02:00", "UTC"),
            Assert.Single(result.Proposal.Availability));
        Assert.Contains("next-night", result.Proposal.SessionIds);
        Assert.DoesNotContain("overnight", result.Proposal.SessionIds);
        Assert.Contains("20:00–02:00 (UTC)", result.Proposal.AvailabilityAssumption);
        Assert.Empty(responses);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpenEndedDatesKeepEveryEligibleConferenceDay(bool structured)
    {
        var agenda = SyntheticAgenda();
        agenda.Sessions.Add(Talk("next-day", "Synthetic AI", "", ["AI"], 45));
        var client = Substitute.For<IChatClient>();
        var call = 0;
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(info =>
            {
                var messages = info.Arg<IEnumerable<ChatMessage>>();
                var json = call++ == 0
                    ? """{"speakerNames":"none","availabilityText":"before November 12 before 11pm","quantity":"some","searchQuery":"none"}"""
                    : messages.Any(message => message.Text?.Contains("calendar dates, ignoring clock times", StringComparison.Ordinal) == true)
                        ? """{"windows":[{"start":"2026-11-11T00:00","end":"2026-11-12T00:00","zone":"UTC"}]}"""
                        : """{"quantity":"some","speakerScope":"independent"}""";
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, structured ? json : $"```json\n{json}\n```"));
            });
        using var services = new ServiceCollection().AddScoped<PlannerTools>().AddTransient(_ => client).BuildServiceProvider();
        var planner = new OnDevicePlanner(services.GetRequiredService<IServiceScopeFactory>(), NullLoggerFactory.Instance,
            services.GetRequiredService<IServiceProviderIsService>(), new TestPlannerModelStatus(structuredOutput: structured));

        var result = await planner.ProposeAsync(agenda, "AI before November 12 before 11pm UTC", TimeZoneInfo.Utc, [], default);

        Assert.NotNull(result.Proposal);
        Assert.Equal([
            new AvailabilityWindow("2026-11-10T07:00", "2026-11-10T23:00", "UTC"),
            new AvailabilityWindow("2026-11-11T07:00", "2026-11-11T23:00", "UTC")
        ], result.Proposal.Availability);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpenEndedNightIsClippedOnlyAtTheDateBoundary(bool structured)
    {
        var agenda = SyntheticAgenda();
        agenda.Sessions.Add(Talk("next-day", "Synthetic AI", "", ["AI"], 45));
        var responses = new Queue<string>([
            """{"speakerNames":"none","availabilityText":"at night before November 12","quantity":"some","searchQuery":"none"}""",
            """{"dailyStart":"20:00","dailyEnd":"06:00","zone":"UTC"}""",
            """{"quantity":"some","speakerScope":"independent"}"""
        ]);
        using var services = CreateServices(structured, responses);
        var planner = CreatePlanner(services, structured);

        var result = await planner.ProposeAsync(agenda, "AI at night before November 12 UTC", TimeZoneInfo.Utc, [], default);

        Assert.NotNull(result.Proposal);
        Assert.Equal([
            new AvailabilityWindow("2026-11-10T00:00", "2026-11-10T06:00", "UTC"),
            new AvailabilityWindow("2026-11-10T20:00", "2026-11-11T06:00", "UTC"),
            new AvailabilityWindow("2026-11-11T20:00", "2026-11-12T00:00", "UTC")
        ], result.Proposal.Availability);
        Assert.Empty(responses);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdjacentFullDaysAllowSessionsThatCrossMidnight(bool structured)
    {
        var agenda = SyntheticAgenda();
        agenda.Sessions.Add(Talk("straddling", "Synthetic AI late session", "", ["AI"], 23.5, 24.5));
        var responses = new Queue<string>([
            """{"speakerNames":"none","availabilityText":"November 10 and 11","quantity":"some","searchQuery":"none"}""",
            """{"quantity":"some","speakerScope":"independent"}"""
        ]);
        using var services = CreateServices(structured, responses);
        var planner = CreatePlanner(services, structured);

        var result = await planner.ProposeAsync(agenda, "AI on November 10 and 11 UTC", TimeZoneInfo.Utc, [], default);

        Assert.NotNull(result.Proposal);
        Assert.Equal(new AvailabilityWindow("2026-11-10T00:00", "2026-11-12T00:00", "UTC"),
            Assert.Single(result.Proposal.Availability));
        Assert.Contains("straddling", result.Proposal.SessionIds);
        Assert.Empty(responses);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdditionalCutoffsAreNotDiscardedByAnExplicitDatedRange(bool structured)
    {
        var agenda = SyntheticAgenda();
        agenda.Sessions.Add(Talk("noon", "Synthetic AI session", "", ["AI"], 12));
        var responses = new Queue<string>([
            """{"speakerNames":"none","availabilityText":"November 10 from 9am to 5pm and before 3pm","quantity":"some","searchQuery":"none"}""",
            """{"quantity":"some","speakerScope":"independent"}"""
        ]);
        using var services = CreateServices(structured, responses);
        var planner = CreatePlanner(services, structured);

        var result = await planner.ProposeAsync(agenda, "AI on November 10 from 9am to 5pm and before 3pm UTC",
            TimeZoneInfo.Utc, [], default);

        Assert.Null(result.Proposal);
        Assert.Contains("clock time", result.Message);
        Assert.Contains("Nothing has been saved", result.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DatedCutoffsUseTheSameGroundedHoursAcrossProviders(bool structured)
    {
        var agenda = SyntheticAgenda();
        agenda.Sessions.Add(Talk("morning", "Synthetic AI breakfast session", "", ["AI"], 8));
        var responses = new Queue<string>([
            """{"speakerNames":"none","availabilityText":"November 10 before 11pm","quantity":"some","searchQuery":"none"}""",
            """{"quantity":"some","speakerScope":"independent"}"""
        ]);
        using var services = CreateServices(structured, responses);
        var planner = CreatePlanner(services, structured);

        var result = await planner.ProposeAsync(agenda, "AI on November 10 before 11pm UTC", TimeZoneInfo.Utc, [], default);

        Assert.Empty(responses);
        Assert.NotNull(result.Proposal);
        Assert.Contains("morning", result.Proposal.SessionIds);
        Assert.DoesNotContain("overnight", result.Proposal.SessionIds);
        Assert.DoesNotContain("crossing", result.Proposal.SessionIds);
        Assert.Equal(new AvailabilityWindow("2026-11-10T07:00", "2026-11-10T23:00", "UTC"),
            Assert.Single(result.Proposal.Availability));
        Assert.Equal("Assumed daily start: 07:00 (UTC).", result.Proposal.AvailabilityAssumption);
        Assert.True(SchedulePlanner.Review(agenda, result.Proposal, TimeZoneInfo.Utc, []).CanApply);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AmbiguousModelDateSpansProduceAVisibleErrorAcrossProviders(bool structured)
    {
        var agenda = SyntheticAgenda();
        agenda.Sessions.Add(Talk("next-day", "Synthetic AI", "", ["AI"], 45));
        var responses = new Queue<string>([
            """{"speakerNames":"none","availabilityText":"tomorrow before 11pm","quantity":"some","searchQuery":"none"}""",
            """{"windows":[{"start":"2026-11-10T09:00","end":"2026-11-11T01:00","zone":"UTC"}]}"""
        ]);
        using var services = CreateServices(structured, responses);
        var planner = CreatePlanner(services, structured);

        var result = await planner.ProposeAsync(agenda, "AI tomorrow before 11pm UTC", TimeZoneInfo.Utc, [], default);

        Assert.Empty(responses);
        Assert.Null(result.Proposal);
        Assert.Contains("ambiguous", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nothing has been saved", result.Message);
    }

    [Theory]
    [InlineData(true, "AI on November 10 and November 12 before 11pm UTC")]
    [InlineData(false, "AI on November 10 and November 12 before 11pm UTC")]
    [InlineData(true, "AI on November 10, Tuesday, November 12, Thursday before 11pm UTC")]
    [InlineData(false, "AI on November 10, Tuesday, November 12, Thursday before 11pm UTC")]
    public async Task SeparateRequestedDatesCannotExpandToTheMiddleDay(bool structured, string request)
    {
        var agenda = SyntheticAgenda();
        agenda.Sessions.Add(Talk("next-day", "Synthetic AI", "", ["AI"], 45));
        agenda.Sessions.Add(Talk("third-day", "Synthetic AI", "", ["AI"], 69));
        var responses = new Queue<string>([
            """{"speakerNames":"none","availabilityText":"November 10 and November 12 before 11pm","quantity":"some","searchQuery":"none"}""",
            """{"quantity":"some","speakerScope":"independent"}"""
        ]);
        using var services = CreateServices(structured, responses);
        var planner = CreatePlanner(services, structured);

        var result = await planner.ProposeAsync(agenda, request, TimeZoneInfo.Utc, [], default);

        Assert.Empty(responses);
        Assert.NotNull(result.Proposal);
        Assert.Equal([
            new AvailabilityWindow("2026-11-10T07:00", "2026-11-10T23:00", "UTC"),
            new AvailabilityWindow("2026-11-12T07:00", "2026-11-12T23:00", "UTC")
        ], result.Proposal.Availability);
    }

    [Theory]
    [InlineData(true, "AI on November 12, 10 to 12 sessions UTC")]
    [InlineData(false, "AI on November 12, 10 to 12 sessions UTC")]
    [InlineData(true, "AI on November 12, 10 to 12 of the sessions UTC")]
    [InlineData(false, "AI on November 12, 10 to 12 of the sessions UTC")]
    public async Task AmbiguousSessionCountCannotBecomeAnExtraCalendarDay(bool structured, string request)
    {
        var client = Substitute.For<IChatClient>();
        const string interpretation = """{"speakerNames":"none","availabilityText":"November 12","quantity":"some","searchQuery":"none"}""";
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                structured ? interpretation : $"```json\n{interpretation}\n```")));
        using var services = new ServiceCollection().AddScoped<PlannerTools>().AddTransient(_ => client).BuildServiceProvider();
        var planner = new OnDevicePlanner(services.GetRequiredService<IServiceScopeFactory>(), NullLoggerFactory.Instance,
            services.GetRequiredService<IServiceProviderIsService>(), new TestPlannerModelStatus(structuredOutput: structured));

        var result = await planner.ProposeAsync(SyntheticAgenda(), request, TimeZoneInfo.Utc, [], default);

        Assert.Null(result.Proposal);
        Assert.Contains("could be a day or a session count", result.Message);
        await client.Received(1).GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, "00:00")]
    [InlineData(false, "07:00")]
    public async Task AllDayModelResponseCannotDiscardAnExplicitDatedTimePreference(bool structured, string clock)
    {
        var responses = new Queue<string>([
            """{"speakerNames":"none","availabilityText":"November 10 in the afternoon before 11pm","quantity":"some","searchQuery":"none"}""",
            $$"""{"dailyStart":"{{clock}}","dailyEnd":"{{clock}}","zone":"UTC"}"""
        ]);
        using var services = CreateServices(structured, responses);
        var planner = CreatePlanner(services, structured);

        var result = await planner.ProposeAsync(SyntheticAgenda(), "AI on November 10 in the afternoon before 11pm UTC", TimeZoneInfo.Utc, [], default);

        Assert.Empty(responses);
        Assert.Null(result.Proposal);
        Assert.Contains("Use explicit start and finish times for this time preference.", result.Message);
        Assert.Contains("Nothing has been saved", result.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DatesOutsideTheConferenceDoNotHideValidRequestedDays(bool structured)
    {
        var responses = new Queue<string>([
            """{"speakerNames":"none","availabilityText":"November 10 and November 15 before 11pm","quantity":"some","searchQuery":"none"}""",
            """{"quantity":"some","speakerScope":"independent"}"""
        ]);
        using var services = CreateServices(structured, responses);
        var planner = CreatePlanner(services, structured);

        var result = await planner.ProposeAsync(SyntheticAgenda(), "AI on November 10 and November 15 before 11pm UTC", TimeZoneInfo.Utc, [], default);

        Assert.Empty(responses);
        Assert.NotNull(result.Proposal);
        Assert.Equal(new AvailabilityWindow("2026-11-10T07:00", "2026-11-10T23:00", "UTC"),
            Assert.Single(result.Proposal.Availability));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DatedExplicitRangeDoesNotDiscardARequestedDayPart(bool structured)
    {
        var responses = new Queue<string>([
            """{"speakerNames":"none","availabilityText":"November 10 in the afternoon from 7am to 11pm","quantity":"some","searchQuery":"none"}""",
            """{"dailyStart":"12:00","dailyEnd":"17:00","zone":"UTC"}""",
            """{"quantity":"some","speakerScope":"independent"}"""
        ]);
        using var services = CreateServices(structured, responses);
        var planner = CreatePlanner(services, structured);

        var result = await planner.ProposeAsync(SyntheticAgenda(), "AI on November 10 in the afternoon from 7am to 11pm UTC", TimeZoneInfo.Utc, [], default);

        Assert.Empty(responses);
        Assert.NotNull(result.Proposal);
        Assert.Equal(new AvailabilityWindow("2026-11-10T12:00", "2026-11-10T17:00", "UTC"),
            Assert.Single(result.Proposal.Availability));
    }

    [Theory]
    [InlineData(true, "in the afternoon", "13:00", "18:00")]
    [InlineData(false, "in the afternoon", "13:00", "18:00")]
    [InlineData(true, "during lunch", "12:00", "13:00")]
    [InlineData(false, "after work", "18:00", "23:00")]
    public async Task DatedCutoffsKeepAnAdditionalRequestedDayPart(
        bool structured, string dayPart, string start, string end)
    {
        var agenda = SyntheticAgenda();
        agenda.Sessions.Add(Talk("morning", "Synthetic AI breakfast session", "", ["AI"], 8));
        agenda.Sessions.Add(Talk("lunch", "Synthetic AI lunch session", "", ["AI"], 12));
        var client = Substitute.For<IChatClient>();
        var responses = new Queue<string>([
            $$"""{"speakerNames":"none","availabilityText":"November 10 {{dayPart}} before 11pm","quantity":"some","searchQuery":"none"}""",
            $$"""{"dailyStart":"{{start}}","dailyEnd":"{{end}}","zone":"UTC"}""",
            """{"quantity":"some","speakerScope":"independent"}"""
        ]);
        var callNumber = 0;
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (callNumber++ == 1)
                {
                    Assert.Contains(call.Arg<IEnumerable<ChatMessage>>(),
                        message => message.Text?.Contains("recurring daily clock limits", StringComparison.Ordinal) == true);
                    Assert.Contains(call.Arg<IEnumerable<ChatMessage>>(),
                        message => message.Text?.Contains("bounded interval", StringComparison.Ordinal) == true);
                    Assert.Contains(call.Arg<IEnumerable<ChatMessage>>(),
                        message => message.Role == ChatRole.User && message.Text?.Contains(dayPart, StringComparison.Ordinal) == true
                            && !message.Text.Contains("11pm", StringComparison.Ordinal)
                            && !message.Text.Contains("November", StringComparison.Ordinal));
                }
                var json = responses.Dequeue();
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, structured ? json : $"```json\n{json}\n```"));
            });
        using var services = new ServiceCollection().AddScoped<PlannerTools>().AddTransient(_ => client).BuildServiceProvider();
        var planner = new OnDevicePlanner(services.GetRequiredService<IServiceScopeFactory>(), NullLoggerFactory.Instance,
            services.GetRequiredService<IServiceProviderIsService>(), new TestPlannerModelStatus(structuredOutput: structured));

        var result = await planner.ProposeAsync(agenda, $"AI on November 10 {dayPart} before 11pm UTC",
            TimeZoneInfo.Utc, [], default);

        Assert.Empty(responses);
        Assert.NotNull(result.Proposal);
        Assert.DoesNotContain("morning", result.Proposal.SessionIds);
        Assert.Equal(new AvailabilityWindow($"2026-11-10T{start}", $"2026-11-10T{end}", "UTC"),
            Assert.Single(result.Proposal.Availability));
        Assert.Contains($"Interpreted hours: {start}–{end} (UTC)", result.Proposal.AvailabilityAssumption);
    }
}
