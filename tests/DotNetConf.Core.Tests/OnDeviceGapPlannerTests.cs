using DotNetConf.App.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DotNetConf.Core.Tests;

[Trait("Category", "AIAttributes")]
public sealed class OnDeviceGapPlannerTests
{
    [Theory]
    [InlineData("I see gaps in my schedule. What sessions fit in here?", "none", false)]
    [InlineData("Fill my free slots with AI", "none", true)]
    [InlineData("What fits around my saved talks by Real Speaker?", "Real Speaker", false)]
    public async Task GroundedGapFlowWorksWithStructuredAndTextOnlyProviders(string request, string speaker, bool topic)
    {
        foreach (var (structured, fenced) in new[] { (true, false), (false, false), (false, true) })
        {
            var client = Substitute.For<IChatClient>();
            var responses = new Queue<string>([
                $$"""{"speakerNames":"{{speaker}}","availabilityText":"none","quantity":"some","searchQuery":"none"}""",
                """{"quantity":"some","speakerScope":"independent"}"""
            ]);
            client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var options = call.Arg<ChatOptions>();
                    var messages = call.Arg<IEnumerable<ChatMessage>>().ToArray();
                    Assert.Contains("JSON object", messages[0].Text);
                    Assert.NotNull(options.ResponseFormat);
                    Assert.Equal(!structured, ReferenceEquals(ChatResponseFormat.Text, options.ResponseFormat));
                    var json = responses.Dequeue();
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant, fenced ? $"```json\n{json}\n```" : json));
                });
            using var services = new TestServices(client);
            var planner = new OnDevicePlanner(services,
                NullLoggerFactory.Instance, services,
                new TestPlannerModelStatus(structuredOutput: structured));
            var agenda = Agenda();
            var result = await planner.ProposeAsync(agenda, request, TimeZoneInfo.Utc, ["saved"], default, true);
            Assert.NotNull(result.Proposal);
            Assert.Equal(["saved"], result.Proposal.GapAnchorIds!);
            Assert.Equal(topic ? ["ai"] : new[] { "after", "ai", "before" }, result.Proposal.SessionIds.Order().ToArray());
            Assert.DoesNotContain("saved", result.Proposal.MatchedSessionIds);
            Assert.True(SchedulePlanner.Review(agenda, result.Proposal, TimeZoneInfo.Utc, ["saved"]).CanApply);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task StartingPointsUseTheSameTypedParserForStructuredAndFencedResponses(bool structured, bool fenced)
    {
        var client = Substitute.For<IChatClient>();
        const string json = """{"topics":["Synthetic AI","Synthetic Web","Synthetic Mobile"]}""";
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, fenced ? $"```json\n{json}\n```" : json)));
        using var services = new TestServices(client);
        var planner = new OnDevicePlanner(services, NullLoggerFactory.Instance, services,
            new TestPlannerModelStatus(structuredOutput: structured));
        var agenda = new ConferenceDocument
        {
            Sessions = [new() { Id = "synthetic-session", Topics = ["Synthetic AI", "Synthetic Web", "Synthetic Mobile"] }]
        };
        var suggestions = await planner.SuggestStartingPointsAsync(agenda, default);
        Assert.Equal(3, suggestions.Count);
        Assert.All(suggestions, suggestion => Assert.Contains(suggestion.Request,
            new[] { "I'm interested in Synthetic AI.", "I'm interested in Synthetic Web.", "I'm interested in Synthetic Mobile." }));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DailyAvailabilityAndSemanticClassificationKeepTheirConstraintsAcrossProviders(bool structured, bool fenced)
    {
        var client = Substitute.For<IChatClient>();
        var responses = new Queue<string>([
            """{"speakerNames":"none","availabilityText":"before 10am","quantity":"some","searchQuery":"none"}""",
            """{"dailyStart":"none","dailyEnd":"10:00","zone":"UTC"}""",
            """{"quantity":"some","speakerScope":"independent"}""",
            """{"topic":"AI"}"""
        ]);
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var options = call.Arg<ChatOptions>();
                Assert.Equal(!structured, ReferenceEquals(ChatResponseFormat.Text, options.ResponseFormat));
                var json = responses.Dequeue();
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, fenced ? $"```json\n{json}\n```" : json));
            });
        using var services = new TestServices(client);
        var planner = new OnDevicePlanner(services, NullLoggerFactory.Instance, services,
            new TestPlannerModelStatus(structuredOutput: structured));
        var agenda = Agenda();
        const string request = "I want to learn machine learning, before 10am UTC.";
        Assert.NotEmpty(PlannerCatalog.SemanticInterests(agenda, request));
        var result = await planner.ProposeAsync(agenda, request, TimeZoneInfo.Utc, ["saved"], default, true);
        Assert.Empty(responses);
        Assert.NotNull(result.Proposal);
        Assert.Equal(["ai"], result.Proposal.SessionIds);
        Assert.Equal(["AI"], result.Proposal.TopicFilter!.Topics);
        Assert.NotEmpty(result.Proposal.Availability);
        Assert.True(SchedulePlanner.Review(agenda, result.Proposal, TimeZoneInfo.Utc, ["saved"]).CanApply);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DatedAvailabilityKeepsTheSameWindowAcrossProviders(bool structured, bool fenced)
    {
        var client = Substitute.For<IChatClient>();
        var responses = new Queue<string>([
            """{"speakerNames":"none","availabilityText":"November 10","quantity":"some","searchQuery":"none"}""",
            """{"quantity":"some","speakerScope":"independent"}"""
        ]);
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var json = responses.Dequeue();
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, fenced ? $"```json\n{json}\n```" : json));
            });
        using var services = new TestServices(client);
        var planner = new OnDevicePlanner(services, NullLoggerFactory.Instance, services,
            new TestPlannerModelStatus(structuredOutput: structured));
        var agenda = Agenda();
        var result = await planner.ProposeAsync(agenda, "Fill gaps with AI on November 10 UTC.",
            TimeZoneInfo.Utc, ["saved"], default, true);
        Assert.Empty(responses);
        Assert.NotNull(result.Proposal);
        Assert.Equal(["ai"], result.Proposal.SessionIds);
        Assert.Equal(new AvailabilityWindow("2026-11-10T00:00", "2026-11-11T00:00", "UTC"),
            Assert.Single(result.Proposal.Availability));
        Assert.True(SchedulePlanner.Review(agenda, result.Proposal, TimeZoneInfo.Utc, ["saved"]).CanApply);
    }

    [Theory]
    [InlineData("Fill gaps in my schedule")]
    [InlineData("What fits my free slots?")]
    [InlineData("Help me find something around my saved talks")]
    public void GapTaskWordsDoNotInventAnInterest(string request) =>
        Assert.Empty(PlannerCatalog.SemanticInterests(Agenda(), request));

    [Fact]
    public void FreeTimeDoesNotAuthorizeInventedClockRestrictions()
    {
        var interpretation = new PlannerInterpretation("none", "09:00 to 17:00", "none", "some");
        Assert.False(interpretation.RequiresTimeExtraction("Fill my free slots"));
        Assert.False(interpretation.RequiresTimeExtraction("I see gaps in my schedule. What fits here?"));
        Assert.True(interpretation.RequiresTimeExtraction("Only evening sessions"));
    }

    [Theory]
    [InlineData("Only before 9", true)]
    [InlineData("Only between 9 and 11", true)]
    [InlineData("Only after 17", false)]
    [InlineData("Before 09:00", false)]
    [InlineData("Before 9am", false)]
    public void ClockCutoffsAreNeverDiscardedAndAmbiguousHoursNeedClarification(string request, bool ambiguous)
    {
        var interpretation = new PlannerInterpretation("none", "none", "none", "some");
        Assert.True(interpretation.RequiresTimeExtraction(request));
        var generated = new PlannerDailyAvailability("none", "23:00", "local");
        if (ambiguous)
            Assert.Throws<InvalidDataException>(() => generated.Ground(request));
        else
        {
            var grounded = generated.Ground(request);
            Assert.Equal(request.Contains("17") ? "17:00" : "none", grounded.DailyStart);
            Assert.Equal(request.Contains("17") ? "none" : "09:00", grounded.DailyEnd);
        }
    }

    [Theory]
    [InlineData("November 10, 2026 from 20:00 to 23:00 UTC", "2026-11-10T20:00", "2026-11-10T23:00")]
    [InlineData("2026-11-10 between 9:00 and 11:30 UTC", "2026-11-10T09:00", "2026-11-10T11:30")]
    [InlineData("November 10th from 8pm to 11pm UTC", "2026-11-10T20:00", "2026-11-10T23:00")]
    public void ExactDatedWindowsUseTheLiteralSourceNotGeneratedPadding(string request, string start, string end)
    {
        Assert.True(PlannerInterpretation.TryCalendarDates(request, 2026, "UTC", out var dates, out _));
        var limits = new PlannerDailyAvailability("none", "none", "UTC").Ground(request, sourceOnly: true);
        var window = Assert.Single(limits.ToWindows(TimeZoneInfo.Utc, Agenda()),
            value => value.Start.StartsWith(dates[0].Start[..10], StringComparison.Ordinal));
        Assert.Equal(new AvailabilityWindow(start, end, "UTC"), window);
    }

    [Fact]
    public void InvalidDatesAndTimesDoNotBecomeSuccess()
    {
        Assert.Throws<InvalidDataException>(() =>
            PlannerInterpretation.TryCalendarDates("November 31, 2026 from 20:00 to 23:00", 2026, "UTC", out _, out _));
        Assert.Throws<InvalidDataException>(() =>
            new PlannerDailyAvailability("none", "none", "UTC").Ground("November 10, 2026 from 25:00 to 26:00", sourceOnly: true));
        Assert.Throws<InvalidDataException>(() =>
            new PlannerDailyAvailability("none", "none", "UTC").Ground("November 10 from 9am to 9am", sourceOnly: true));
    }

    [Fact]
    public void ExplicitOvernightHoursStayAnchoredToTheirRequestedDate()
    {
        var limits = new PlannerDailyAvailability("none", "none", "UTC")
            .Ground("November 10, 2026 from 23:00 to 01:00", sourceOnly: true);
        var window = Assert.Single(limits.ToWindows(TimeZoneInfo.Utc, Agenda()),
            value => value.Start.StartsWith("2026-11-10", StringComparison.Ordinal));
        Assert.Equal(new AvailabilityWindow("2026-11-10T23:00", "2026-11-11T01:00", "UTC"), window);
    }

    [Theory]
    [InlineData("November 10 from 9 to 11pm")]
    [InlineData("November 10 from 9am to 11")]
    [InlineData("November 10 from 9 to 11")]
    public void AmbiguousDatedClockRangesRequireExplicitPeriodsOrClockMinutes(string request) =>
        Assert.Throws<InvalidDataException>(() => new PlannerDailyAvailability("none", "none", "UTC").Ground(request, sourceOnly: true));

    [Theory]
    [InlineData("November 10 from 9pm to 11pm", "21:00", "23:00")]
    [InlineData("November 10 from 09:00 to 11:00", "09:00", "11:00")]
    [InlineData("November 10 from 20 to 23", "20:00", "23:00")]
    public void UnambiguousDatedClockRangesKeepTheirRequestedTimes(string request, string start, string end)
    {
        var limits = new PlannerDailyAvailability("none", "none", "UTC").Ground(request, sourceOnly: true);
        Assert.Equal(start, limits.DailyStart);
        Assert.Equal(end, limits.DailyEnd);
    }

    [Fact]
    public void PhoneAppsMapToTheRealMobileTopicWhenThereIsNoSeparateMobileCategory()
    {
        var agenda = Agenda();
        agenda.Sessions[0] = agenda.Sessions[0] with { Topics = [".NET MAUI"] };
        Assert.Equal([".NET MAUI"], AgendaHelperTopics(agenda));
    }

    private static string[] AgendaHelperTopics(ConferenceDocument agenda) =>
        Core.Agenda.MentionedTopics(agenda, "Find talks about building phone apps that fit in my gaps");

    [Fact]
    public async Task LiveAgendaAdapterReturnsTheRepositorySnapshotNotBundledMetadata()
    {
        var repository = Substitute.For<IConferenceRepository>();
        var agenda = Agenda();
        repository.LoadAsync(Arg.Any<CancellationToken>()).Returns(new ConferenceSnapshot(agenda, null, null, true, []));
        Assert.Same(agenda, await new ConferenceAgendaDataSource(repository).LoadAsync());
        await repository.DidNotReceive().RefreshAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LiveAgendaAdapterWaitsForTheFirstDownloadInsteadOfReturningEmptyMetadata()
    {
        var repository = Substitute.For<IConferenceRepository>();
        repository.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(new ConferenceSnapshot(new(), null, null, false, []));
        var downloaded = new TaskCompletionSource<ConferenceSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.RefreshAsync(Arg.Any<CancellationToken>()).Returns(downloaded.Task);

        var load = new ConferenceAgendaDataSource(repository).LoadAsync();
        Assert.False(load.IsCompleted);
        var agenda = Agenda();
        downloaded.SetResult(new(agenda, null, null, false, []));
        Assert.Same(agenda, await load);
    }

    [Fact]
    public async Task CancellingAPlannerWaitDoesNotCancelTheSharedAgendaDownload()
    {
        var repository = Substitute.For<IConferenceRepository>();
        repository.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(new ConferenceSnapshot(new(), null, null, false, []));
        var downloaded = new TaskCompletionSource<ConferenceSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.RefreshAsync(Arg.Any<CancellationToken>()).Returns(downloaded.Task);
        using var cancellation = new CancellationTokenSource();
        var source = new ConferenceAgendaDataSource(repository);
        var cancelledLoad = source.LoadAsync(cancellation.Token);
        var continuingLoad = source.LoadAsync();

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledLoad);
        Assert.False(continuingLoad.IsCompleted);
        var agenda = Agenda();
        downloaded.SetResult(new(agenda, null, null, false, []));
        Assert.Same(agenda, await continuingLoad);
        await repository.DidNotReceive().RefreshAsync(Arg.Is<CancellationToken>(token => token.CanBeCanceled));
    }

    [Fact]
    public async Task AnOfflineFirstDownloadKeepsAnHonestEmptyAgenda()
    {
        var repository = Substitute.For<IConferenceRepository>();
        var empty = new ConferenceSnapshot(new(), null, null, false,
            [new(ConferenceRepository.MicrosoftSource, new HttpRequestException("Synthetic offline failure"))]);
        repository.LoadAsync(Arg.Any<CancellationToken>()).Returns(empty);
        repository.RefreshAsync(Arg.Any<CancellationToken>()).Returns(empty);
        var agenda = await new ConferenceAgendaDataSource(repository).LoadAsync();
        Assert.Same(empty.Document, agenda);
        Assert.Empty(agenda.Sessions);
        Assert.Empty(agenda.Speakers);
    }

    private static ConferenceDocument Agenda() => new()
    {
        Speakers = [new() { Id = "person", Name = "Real Speaker" }],
        Sessions = new[] { ("before", 7, "C#"), ("saved", 8, "C#"), ("ai", 9, "AI"), ("after", 10, "C#") }
            .Select(value => new Session
            {
                Id = value.Item1, Title = value.Item1, SpeakerIds = ["person"], Topics = [value.Item3],
                StartsAt = new(2026, 11, 10, value.Item2, 0, 0, TimeSpan.Zero),
                EndsAt = new(2026, 11, 10, value.Item2 + 1, 0, 0, TimeSpan.Zero)
            }).ToList()
    };

    private sealed class TestServices(IChatClient client)
        : IServiceProvider, IServiceProviderIsService, IServiceScopeFactory, IServiceScope
    {
        private readonly PlannerTools tools = new();
        public object? GetService(Type serviceType) => serviceType == typeof(IChatClient) ? client
            : serviceType == typeof(PlannerTools) ? tools : null;
        public bool IsService(Type serviceType) => serviceType == typeof(IChatClient);
        public IServiceProvider ServiceProvider => this;
        public IServiceScope CreateScope() => this;
        public void Dispose() => client.Dispose();
    }
}
