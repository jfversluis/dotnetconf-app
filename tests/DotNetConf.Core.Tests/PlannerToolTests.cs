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
        Assert.Equal(3, functions.Length);
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
        tools.CapturePreferences([], [], allSessionsMatchingQuery: "Mobile");
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
        tools.CapturePreferences(["Ortinau"], [], true, "C#");
        tools.SpeakerSessions("Ortinau");
        var speakerIds = tools.CandidateIds.ToArray();
        Assert.Contains("You requested all sessions matching 'C#'", tools.SubmitProposal(speakerIds));
        tools.SearchSessions("C#", 0);
        Assert.Equal(6, tools.CandidateIds.Count);
        Assert.StartsWith("Preview ready.", tools.SubmitProposal(tools.CandidateIds.ToArray()));
        Assert.Equal("C#", tools.Proposal!.AllSessionsMatchingQuery);
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
        tools.CapturePreferences([], [], allSessionsMatchingQuery: "Mobile");
        Assert.Contains("End of results", tools.SearchSessions("Mobile", 0));
        Assert.Equal(4, tools.CandidateIds.Count);
        Assert.StartsWith("Preview ready.", tools.SubmitProposal(tools.CandidateIds.ToArray()));

        tools.CapturePreferences([], [new("2026-11-10T15:00", "2026-11-10T20:00", "UTC")],
            allSessionsMatchingQuery: "Mobile");
        tools.SearchSessions("Mobile", 0);
        Assert.Contains("You requested all sessions matching", tools.SubmitProposal(tools.CandidateIds.ToArray()));
        var proposal = tools.Proposal!;
        Assert.Same(proposal, SchedulePlanner.Arrange(conference, proposal, TimeZoneInfo.Utc, []));
    }

    [Fact]
    public async Task RealNamesAreOnlyAssignedExplicitlyFictionalDemoParticipation()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var realNames = conference.Speakers.Where(s => s.Tagline.StartsWith("Real name")).ToArray();
        Assert.Equal(6, realNames.Length);
        Assert.All(realNames, speaker =>
        {
            Assert.Contains("Fictional demo participation", speaker.Tagline);
            Assert.Contains("not announced or endorsed", speaker.Bio);
            Assert.All(conference.Sessions.Where(s => s.SpeakerIds.Contains(speaker.Id)),
                session => Assert.Contains("Fictional demo talk", session.Description));
        });
    }
}
