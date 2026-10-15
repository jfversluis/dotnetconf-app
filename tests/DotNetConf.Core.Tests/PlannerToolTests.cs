using System.Text.Json;
using DotNetConf.App.Services;
using DotNetConf.Core;
using Microsoft.Extensions.AI;

namespace DotNetConf.Core.Tests;

public sealed class PlannerToolTests
{
    [Fact]
    public async Task ToolsHaveSchemasAndInvokeWithoutReflectionSerialization()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var tools = new PlannerTools();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences(["Alex Rivera"], []);
        var functions = tools.CreateFunctions().Cast<AIFunction>().ToArray();
        Assert.Equal(4, functions.Length);
        foreach (var function in functions)
        {
            Assert.Equal(JsonValueKind.Object, function.JsonSchema.ValueKind);
            Assert.NotNull(function.ReturnJsonSchema);
        }
        var search = functions.Single(f => f.Name == "search_sessions");
        var searchResult = await search.InvokeAsync(new AIFunctionArguments
        {
            ["query"] = "Mobile", ["skip"] = 0
        });
        Assert.Contains("building-with-maui", Assert.IsType<string>(searchResult));

        var submit = functions.Single(f => f.Name == "submit_proposal");
        var result = await submit.InvokeAsync(new AIFunctionArguments
        {
            ["sessionIds"] = JsonSerializer.SerializeToElement(new[] { "building-with-maui" },
                ConferenceJsonContext.Default.StringArray),
        });
        Assert.StartsWith("Preview ready.", Assert.IsType<string>(result));
        Assert.NotNull(tools.Proposal);
        var nativeArguments = JsonSerializer.Deserialize<AIFunctionArguments>(
            """{"query":"Mobile","skip":0}""", AIJsonUtilities.DefaultOptions)!;
        Assert.Contains("building-with-maui", Assert.IsType<string>(await search.InvokeAsync(nativeArguments)));
        Assert.Contains("Preview ready.", JsonSerializer.Serialize(result, AIJsonUtilities.DefaultOptions));

        tools.CapturePreferences(["Alex Rivera"], [new("2026-11-10T16:00", "2026-11-10T18:00", "UTC")]);
        var constrainedArguments = JsonSerializer.Deserialize<AIFunctionArguments>(
            """{"sessionIds":["building-with-maui"]}""", AIJsonUtilities.DefaultOptions)!;
        Assert.StartsWith("Preview ready.", Assert.IsType<string>(await submit.InvokeAsync(constrainedArguments)));
        Assert.Equal("UTC", Assert.Single(tools.Proposal!.Availability).Zone);
    }

    [Fact]
    public async Task SearchesAreBoundedAndUnknownSpeakersStayVisible()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var tools = new PlannerTools();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences([], []);
        Assert.Equal(5, tools.SearchSessions("", 0).Split('\n').Length);
        Assert.Contains("not found", tools.SpeakerSessions("not a speaker"));
        tools.CapturePreferences(["not a speaker"], []);
        Assert.Contains("Speaker not found", tools.SubmitProposal(["building-with-maui"]));
        Assert.NotNull(tools.Proposal);
        Assert.Contains("Invalid arguments", tools.SubmitProposal(null!));
        Assert.Null(tools.Proposal);
    }

    [Fact]
    public async Task SearchesFilterAvailabilityAndProposalCannotChangeCapturedConstraints()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var tools = new PlannerTools();
        tools.Initialize(conference, TimeZoneInfo.Local, []);
        tools.CapturePreferences(["Alex Rivera"], [new("2026-11-10T16:00", "2026-11-10T18:00", "UTC")]);
        var rows = tools.SearchSessions("", 0);
        Assert.Contains("building-with-maui", rows);
        Assert.DoesNotContain("accessible-by-design", rows);
        Assert.Contains("accessible-by-design", tools.SpeakerSessions("Alex"));
        Assert.DoesNotContain("accessible-by-design", tools.EligibleCandidateIds);
        Assert.Contains("Outside your availability", tools.SubmitProposal(["accessible-by-design"]));
        Assert.Contains("Required speaker has no proposed session", tools.SubmitProposal(["language-features"]));
        Assert.Equal("Alex Rivera", Assert.Single(tools.Proposal!.RequiredSpeakers));
        Assert.Equal("UTC", Assert.Single(tools.Proposal.Availability).Zone);
    }

    [Fact]
    public async Task CancelledToolInvocationCannotSubmitAProposal()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var tools = new PlannerTools();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences([], []);
        var submit = tools.CreateFunctions().Cast<AIFunction>().Single(f => f.Name == "submit_proposal");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await submit.InvokeAsync(new AIFunctionArguments { ["sessionIds"] = new[] { "building-with-maui" } },
                cancellation.Token));
        Assert.Null(tools.Proposal);
        Assert.Equal(0, tools.CallCount);
    }

    [Fact]
    public async Task InvalidPreferenceArgumentsClearPreviousPreferencesAndCandidates()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var tools = new PlannerTools();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences([], []);
        tools.SearchSessions("", 0);
        Assert.NotEmpty(tools.CandidateIds);
        Assert.Contains("Invalid arguments", tools.CapturePreferences(null!, []));
        Assert.Null(tools.Preferences);
        Assert.Empty(tools.CandidateIds);
        tools.CapturePreferences(["Alex Rivera"], []);
        Assert.Equal("Alex Rivera", Assert.Single(tools.Preferences!.RequiredSpeakers));
        Assert.Empty(tools.Preferences.Availability);
    }

    [Fact]
    public async Task ToolCallBudgetStopsFurtherNativeInvocations()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var tools = new PlannerTools();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences([], []);
        var search = tools.CreateFunctions().Cast<AIFunction>().Single(f => f.Name == "search_sessions");
        for (var i = 0; i < 10; i++)
            await search.InvokeAsync(new AIFunctionArguments { ["query"] = "", ["skip"] = 0 });
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await search.InvokeAsync(new AIFunctionArguments { ["query"] = "", ["skip"] = 0 }));
    }

    [Theory]
    [InlineData("Alex", "Alex Rivera", 3)]
    [InlineData("mads", "Mads Torgersen", 2)]
    [InlineData("Ortinau", "David Ortinau", 2)]
    [InlineData("Scott Hanselman", "Scott Hanselman", 2)]
    public async Task PartialSpeakerRequestsResolveConsistently(string query, string canonicalName, int count)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var tools = new PlannerTools();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences([query], [], allSessionsByRequiredSpeakers: true);
        Assert.Equal(canonicalName, Assert.Single(tools.Preferences!.RequiredSpeakers));
        var speaker = Assert.Single(Agenda.FindSpeakers(conference, query));
        var ids = conference.Sessions.Where(s => s.SpeakerIds.Contains(speaker.Id)).Select(s => s.Id).ToArray();
        Assert.Equal(count, ids.Length);
        Assert.Contains(canonicalName, tools.SpeakerSessions(query));
        Assert.StartsWith("Preview ready.", tools.SubmitProposal(ids));
        Assert.Contains("You requested all sessions", tools.SubmitProposal(ids.Take(1).ToArray()));
    }

    [Fact]
    public async Task AmbiguousSpeakerMustNotSilentlyChooseOneScott()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var tools = new PlannerTools();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences(["Scott"], []);
        Assert.Equal(2, Agenda.FindSpeakers(conference, "Scott").Count);
        Assert.Empty(Agenda.FindSpeakers(conference, "cott"));
        Assert.Contains("Scott Hanselman", tools.SpeakerSessions("Scott"));
        Assert.Contains("Scott Hunter", tools.SpeakerSessions("Scott"));
        Assert.Contains("Ambiguous speaker", tools.SubmitProposal(["dotnet-next"]));
    }

    [Fact]
    public void SpeakerPaginationDoesNotSilentlyTruncateAllSessions()
    {
        var conference = new ConferenceDocument
        {
            Speakers = [new() { Id = "alex", Name = "Alex Rivera" }],
            Sessions = Enumerable.Range(0, 6).Select(i => new Session
            {
                Id = $"talk-{i}", Title = $"Talk {i}", SpeakerIds = ["alex"], Topics = ["Mobile"],
                StartsAt = new DateTimeOffset(2026, 11, 10, 10 + i, 0, 0, TimeSpan.Zero),
                EndsAt = new DateTimeOffset(2026, 11, 10, 10 + i, 45, 0, TimeSpan.Zero)
            }).ToList()
        };
        var tools = new PlannerTools();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences(["Alex"], [], true);
        var first = tools.SpeakerSessions("Alex");
        Assert.Contains("Next page: skip=4", first);
        Assert.DoesNotContain("talk-4 |", first);
        var last = tools.SpeakerSessions("Alex", 4);
        Assert.Contains("talk-4 |", last);
        Assert.Contains("talk-5 |", last);
        Assert.Contains("End of results", last);
        Assert.Equal(6, tools.CandidateIds.Count);
        tools.CapturePreferences([], [], topicFilter: new(["Mobile"], false, false, true));
        Assert.Contains("Next page: skip=4", tools.SearchSessions("Mobile", 0));
        Assert.Contains("You requested all sessions matching", tools.SubmitProposal(tools.CandidateIds.ToArray()));
        Assert.Contains("End of results", tools.SearchSessions("Mobile", 4));
        Assert.Equal(6, tools.CandidateIds.Count);
        Assert.StartsWith("Preview ready.", tools.SubmitProposal(tools.CandidateIds.ToArray()));
    }

    [Fact]
    public async Task MixedAllTopicAndSpeakerRequestsRequireTheUnionNotJustSpeakerResults()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var tools = new PlannerTools();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences(["Ortinau"], [], true, new(["C#"], false, false, true));
        tools.SpeakerSessions("Ortinau");
        var speakerIds = tools.CandidateIds.ToArray();
        Assert.Contains("You requested all sessions matching 'C#'", tools.SubmitProposal(speakerIds));
        tools.TopicSessions(0);
        tools.TopicSessions(4);
        Assert.Equal(7, tools.CandidateIds.Count);
        Assert.StartsWith("Preview ready.", tools.SubmitProposal(tools.CandidateIds.ToArray()));
        Assert.Equal(["C#"], tools.Proposal!.TopicFilter!.Topics);
        Assert.True(tools.Proposal.AllSessionsByRequiredSpeakers);
        Assert.Contains("You requested all sessions by David Ortinau",
            tools.SubmitProposal(tools.CandidateIds.Except(speakerIds).ToArray()));
    }

    [Fact]
    public async Task AllTopicAvailabilityCannotSilentlyTruncateResults()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var tools = new PlannerTools();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences([], [], topicFilter: new(["Mobile"], false, false, true));
        Assert.Contains("Next page", tools.TopicSessions(0));
        Assert.Contains("End of results", tools.TopicSessions(4));
        Assert.Equal(7, tools.CandidateIds.Count);
        Assert.StartsWith("Preview ready.", tools.SubmitProposal(tools.CandidateIds.ToArray()));

        tools.CapturePreferences([], [new("2026-11-10T15:00", "2026-11-10T20:00", "UTC")],
            topicFilter: new(["Mobile"], false, false, true));
        tools.TopicSessions(0);
        tools.TopicSessions(4);
        Assert.StartsWith("Preview ready.", tools.SubmitProposal(tools.EligibleCandidateIds));
        Assert.True(tools.Proposal!.MatchedSessionIds.Length > tools.Proposal.SessionIds.Length);
        var proposal = tools.Proposal!;
        Assert.Same(proposal, SchedulePlanner.Arrange(conference, proposal, TimeZoneInfo.Utc, []));
    }

    [Fact]
    public async Task TopicOrAndSpeakerIntersectionAreSeparateAndNeverMatchAiInsideMaui()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var tools = new PlannerTools();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences([], [], topicFilter: new(["AI", ".NET MAUI", "Blazor"], false, false, true));
        for (var skip = 0; skip < 12; skip += 4)
            tools.TopicSessions(skip);
        Assert.Equal(11, tools.CandidateIds.Count);
        Assert.StartsWith("Preview ready.", tools.SubmitProposal(tools.EligibleCandidateIds));
        tools.CapturePreferences([], [], topicFilter: new(["AI"], false, false, true));
        tools.TopicSessions(0);
        tools.TopicSessions(4);
        Assert.Equal(5, tools.CandidateIds.Count);
        Assert.DoesNotContain("building-with-maui", tools.CandidateIds);
        tools.CapturePreferences([], [], topicFilter: new(["AI", ".NET MAUI"], true, false, true));
        tools.TopicSessions();
        Assert.Equal(["maddy-maui-ai", "maui-local-ai"], tools.CandidateIds.Order());
        tools.CapturePreferences(["Maddy"], [], true, new([".NET MAUI"], false, true, true));
        tools.SpeakerSessions("Maddy");
        tools.TopicSessions();
        Assert.Equal(["maddy-maui-ai", "maddy-maui-roadmap"], tools.CandidateIds.Order());
        Assert.StartsWith("Preview ready.", tools.SubmitProposal(tools.EligibleCandidateIds));
    }

    [Fact]
    public async Task LateMatchesRemainVisibleButUnselectedAndRequiredSpeakerConflictsStayExplicit()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var windows = new PlannerAvailability([], DailyEnd: "22:00").ToWindows(zone, conference);
        var tools = new PlannerTools();
        tools.Initialize(conference, zone, []);
        tools.CapturePreferences([], windows, topicFilter: new(["C#"], false, false, true));
        tools.TopicSessions();
        Assert.Contains("Warning:", tools.TopicSessions(4));
        Assert.Contains("gerald-csharp-ai", tools.CandidateIds);
        Assert.DoesNotContain("gerald-csharp-ai", tools.EligibleCandidateIds);
        Assert.Equal(4, tools.EligibleCandidateIds.Length);
        Assert.StartsWith("Preview ready.", tools.SubmitProposal(tools.EligibleCandidateIds));
        Assert.Contains("gerald-csharp-ai", tools.Proposal!.MatchedSessionIds);
        tools.CapturePreferences(["Gerald"], windows, true);
        tools.SpeakerSessions("Gerald");
        Assert.Contains("Missing:", tools.SubmitProposal(tools.EligibleCandidateIds));
        Assert.Equal(2, tools.Proposal!.MatchedSessionIds.Length);
        Assert.Equal(["gerald-maui-testing"], tools.Proposal.SessionIds);
    }

    [Fact]
    public void CutoffIsInclusiveForFinishTimeAndRejectsCrossMidnightSessions()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var conference = new ConferenceDocument
        {
            Speakers = [new() { Id = "alex", Name = "Alex Rivera" }],
            Sessions = new[] { ("exact-before-dst", 31, 21, 0, -4), ("exact-after-dst", 1, 21, 0, -5),
                ("late", 1, 21, 15, -5), ("midnight", 1, 23, 30, -5) }.Select(x =>
            {
                var start = new DateTimeOffset(2026, x.Item2 == 31 ? 10 : 11, x.Item2, x.Item3, x.Item4, 0, TimeSpan.FromHours(x.Item5));
                return new Session
                {
                    Id = x.Item1, Title = x.Item1, StartsAt = start, EndsAt = start.AddHours(1),
                    SpeakerIds = ["alex"], Topics = ["C#"]
                };
            }).ToList()
        };
        var windows = new PlannerAvailability([], DailyEnd: "22:00").ToWindows(zone, conference);
        var tools = new PlannerTools();
        tools.Initialize(conference, zone, []);
        tools.CapturePreferences([], windows, topicFilter: new(["C#"], false, false, true));
        tools.TopicSessions();
        Assert.Equal(4, tools.CandidateIds.Count);
        Assert.Equal(["exact-before-dst", "exact-after-dst"], tools.EligibleCandidateIds);
        Assert.StartsWith("Preview ready.", tools.SubmitProposal(tools.EligibleCandidateIds));
    }

    [Fact]
    public void OversizedCompleteMatchesRemainVisibleInsteadOfBeingTruncatedIntoSuccess()
    {
        var conference = new ConferenceDocument
        {
            Speakers = [new() { Id = "alex", Name = "Alex Rivera" }],
            Sessions = Enumerable.Range(0, 16).Select(i => new Session
            {
                Id = $"talk-{i}", Title = $"Talk {i}", SpeakerIds = ["alex"], Topics = ["C#"],
                StartsAt = DateTimeOffset.Parse("2026-11-10T08:00Z").AddHours(i),
                EndsAt = DateTimeOffset.Parse("2026-11-10T08:45Z").AddHours(i)
            }).ToList()
        };
        var tools = new PlannerTools();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences([], [], topicFilter: new(["C#"], false, false, true));
        for (var skip = 0; skip < 16; skip += 4)
            tools.TopicSessions(skip);
        Assert.Contains("between 1 and 12", tools.SubmitProposal(tools.EligibleCandidateIds));
        Assert.Equal(16, tools.Proposal!.MatchedSessionIds.Length);
        Assert.Equal(16, SchedulePlanner.Review(conference, tools.Proposal, TimeZoneInfo.Utc, []).Sessions.Count);
    }

    [Fact]
    public async Task RealNamesAreOnlyAssignedExplicitlyFictionalDemoParticipation()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var realNames = conference.Speakers.Where(s => s.Tagline.StartsWith("Real name")).ToArray();
        Assert.Equal(8, realNames.Length);
        Assert.All(realNames, speaker =>
        {
            Assert.Contains("Fictional demo participation", speaker.Tagline);
            Assert.Contains("not announced or endorsed", speaker.Bio);
            Assert.All(conference.Sessions.Where(s => s.SpeakerIds.Contains(speaker.Id)),
                session => Assert.Contains("Fictional demo talk", session.Description));
        });
    }
}
