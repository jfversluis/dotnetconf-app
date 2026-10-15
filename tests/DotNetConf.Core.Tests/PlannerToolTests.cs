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
        foreach (var function in functions.Append(Assert.IsAssignableFrom<AIFunction>(tools.CreatePreferenceFunction())))
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

        var capturedArguments = JsonSerializer.Deserialize<AIFunctionArguments>(
            """
            {"requiredSpeakers":["Alex Rivera"],"availabilityStarts":["2026-11-10T16:00"],"availabilityEnds":["2026-11-10T18:00"],"availabilityZones":["UTC"]}
            """, AIJsonUtilities.DefaultOptions)!;
        await Assert.IsAssignableFrom<AIFunction>(tools.CreatePreferenceFunction()).InvokeAsync(capturedArguments);
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
        Assert.Equal(4, tools.SearchSessions("", 0).Split('\n').Length);
        Assert.Contains("not found", tools.SpeakerSessions("not a speaker"));
        tools.CapturePreferences(["not a speaker"], []);
        Assert.Contains("Required speaker not uniquely found", tools.SubmitProposal(["building-with-maui"]));
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
    public async Task InvalidPreferenceArgumentsAreExplicitAndCanBeCorrectedWithoutRelaxingThem()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var tools = new PlannerTools();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        var capture = Assert.IsAssignableFrom<AIFunction>(tools.CreatePreferenceFunction());
        var invalid = JsonSerializer.Deserialize<AIFunctionArguments>(
            """{"requiredSpeakers":["Alex Rivera"],"availabilityStarts":[],"availabilityEnds":[],"availabilityZones":["local"]}""",
            AIJsonUtilities.DefaultOptions)!;
        Assert.Contains("must have the same length", Assert.IsType<string>(await capture.InvokeAsync(invalid)));
        Assert.Null(tools.Preferences);
        var corrected = JsonSerializer.Deserialize<AIFunctionArguments>(
            """{"requiredSpeakers":["Alex Rivera"],"availabilityStarts":[],"availabilityEnds":[],"availabilityZones":[]}""",
            AIJsonUtilities.DefaultOptions)!;
        await capture.InvokeAsync(corrected);
        Assert.Equal("Alex Rivera", Assert.Single(tools.Preferences!.RequiredSpeakers));
        Assert.Empty(tools.Preferences.Availability);
        Assert.Equal(2, tools.CallCount);
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
}
