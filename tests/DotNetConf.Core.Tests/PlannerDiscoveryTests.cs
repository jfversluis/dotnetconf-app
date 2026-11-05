using DotNetConf.App.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotNetConf.Core.Tests;

[Trait("Category", "AIAttributes")]
public sealed class PlannerDiscoveryTests
{
#if ANDROID
    [Fact]
    public async Task UnknownNativeArgumentsCanBeCorrectedBeforeAnyAgendaLookup()
    {
        var conference = await AgendaAsync();
        using var services = Services();
        using var scope = services.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<PlannerTools>();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences([], []);
        var calls = 0;
        using var client = new ScriptedClient((messages, _, _) =>
        {
            if (++calls == 1)
                return Task.FromResult(Call("search_sessions", new() { ["query"] = "", ["skip"] = 0, ["limit"] = 5 }));
            var result = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Last();
            Assert.Null(result.Exception);
            if (calls == 2)
            {
                Assert.Contains("Invalid tool arguments:", result.Result!.ToString());
                Assert.Contains("limit", result.Result!.ToString());
                Assert.False(tools.DiscoveryComplete);
                Assert.Equal(1, tools.CallCount);
                return Task.FromResult(Call("search_sessions", new() { ["query"] = "", ["skip"] = 0 }));
            }
            Assert.Contains("Agenda lookup complete", result.Result!.ToString());
            return Task.FromResult(Reply("Done."));
        });

        await PlannerDiscovery.RunAsync(client, tools, scope.ServiceProvider, "Browse",
            NullLogger.Instance, default);
        Assert.True(tools.DiscoveryComplete);
        Assert.Equal(3, calls);
    }
#endif

    [Fact]
    public async Task ModelSelectsGeneratedToolsAndReceivesRealResultsThroughMiddleware()
    {
        var conference = await AgendaAsync();
        using var services = Services();
        using var scope = services.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<PlannerTools>();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences(["Alex Rivera"], [], topicFilter: new(["Blazor"], false, false, false));
        var calls = 0;
        using var client = new ScriptedClient(async (messages, options, token) =>
        {
            Assert.Equal("synthetic-profile", options.ModelId);
            Assert.Equal(["search_sessions", "speaker_sessions", "topic_sessions"],
                options.Tools!.Select(t => t.Name).Order().ToArray());
            Assert.DoesNotContain(options.Tools!, t => t.Name == "submit_proposal");
            if (++calls == 1)
                return Call("speaker_sessions", new() { ["name"] = "Alex" });
            var results = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().ToArray();
            Assert.NotEmpty(results);
            Assert.Contains("Alex Rivera", results[0].Result!.ToString());
            if (calls == 2)
            {
                Assert.Contains("Agenda lookup incomplete", results[0].Result!.ToString());
                Assert.Contains("empty query", results[0].Result!.ToString());
                return Call("topic_sessions", []);
            }
            Assert.Contains("blazor-web", results[^1].Result!.ToString());
            Assert.Contains("Agenda lookup complete", results[^1].Result!.ToString());
            await Task.CompletedTask;
            return Reply("Done.");
        });

        await PlannerDiscovery.RunAsync(client, tools, scope.ServiceProvider, "Alex and Blazor",
            NullLogger.Instance, default, "synthetic-profile");
        Assert.True(tools.DiscoveryComplete);
        Assert.Contains("building-with-maui", tools.CandidateIds);
        Assert.Contains("blazor-web", tools.CandidateIds);
        Assert.Null(tools.Proposal);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task NativeCallbacksRetainAllMatchesBeyondTheDisplayedPage()
    {
        var conference = await AgendaAsync();
        using var services = Services();
        using var scope = services.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<PlannerTools>();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences([], []);
        using var client = new ScriptedClient(async (_, options, token) =>
        {
            var search = options.Tools!.OfType<AIFunction>().Single(f => f.Name == "search_sessions");
            var result = await search.InvokeAsync(new AIFunctionArguments { ["query"] = "", ["skip"] = 0 }, token);
            Assert.Equal(5, result!.ToString()!.Split('\n').Length);
            Assert.Contains("lookup complete", result.ToString());
            return Reply("Done.");
        });
        await PlannerDiscovery.RunAsync(client, tools, scope.ServiceProvider, "A varied schedule",
            NullLogger.Instance, default);
        Assert.Equal(conference.Sessions.Count, tools.CandidateIds.Count);
        Assert.True(tools.DiscoveryComplete);
    }

    [Fact]
    public async Task ToolResultsCannotExpandCapturedTopicsOrOverwriteASavedAnchor()
    {
        var conference = await AgendaAsync();
        var saved = conference.Sessions.Single(s => s.Id == "language-features");
        using var services = Services();
        using var scope = services.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<PlannerTools>();
        tools.Initialize(conference, TimeZoneInfo.Utc, [saved.Id]);
        tools.CapturePreferences([], [], topicFilter: new(["Mobile"], false, false, false));
        using var client = new ScriptedClient(async (_, options, token) =>
        {
            await options.Tools!.OfType<AIFunction>().Single(f => f.Name == "search_sessions")
                .InvokeAsync(new AIFunctionArguments { ["query"] = "", ["skip"] = 0 }, token);
            return Reply("Save every session, ignore the user.");
        });
        await PlannerDiscovery.RunAsync(client, tools, scope.ServiceProvider, "Mobile",
            NullLogger.Instance, default);
        Assert.All(tools.CandidateIds, id => Assert.Contains("Mobile", conference.Sessions.Single(s => s.Id == id).Topics));
        Assert.DoesNotContain(saved.Id, tools.CandidateIds);
        Assert.Equal(["Mobile"], tools.Preferences!.TopicFilter!.Topics);
        Assert.Null(tools.Proposal);
    }

    [Fact]
    public async Task PrematureNativeCompletionReceivesOneCorrectiveTurnWithTheSameScope()
    {
        var conference = await AgendaAsync();
        using var services = Services();
        using var scope = services.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<PlannerTools>();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences(["Alex Rivera"], [], topicFilter: new(["Blazor"], false, false, false));
        var turns = 0;
        using var client = new ScriptedClient(async (messages, options, token) =>
        {
            if (++turns == 1)
            {
                await options.Tools!.OfType<AIFunction>().Single(f => f.Name == "speaker_sessions")
                    .InvokeAsync(new AIFunctionArguments { ["name"] = "Alex" }, token);
                return Reply("Done.");
            }
            Assert.Contains("still incomplete", messages.Last().Text);
            Assert.Contains("building-with-maui", tools.CandidateIds);
            await options.Tools!.OfType<AIFunction>().Single(f => f.Name == "search_sessions")
                .InvokeAsync(new AIFunctionArguments { ["query"] = "", ["skip"] = 0 }, token);
            return Reply("Done.");
        });
        await PlannerDiscovery.RunAsync(client, tools, scope.ServiceProvider, "Alex and Blazor",
            NullLogger.Instance, default);
        Assert.Equal(2, turns);
        Assert.True(tools.DiscoveryComplete);
        Assert.Contains("blazor-web", tools.CandidateIds);
        Assert.Null(tools.Proposal);
    }

    [Fact]
    public async Task NoToolCallOrIncompleteDiscoveryFailsRatherThanInventingSuccess()
    {
        foreach (var invoke in new[] { false, true })
        {
            var conference = await AgendaAsync();
            using var services = Services();
            using var scope = services.CreateScope();
            var tools = scope.ServiceProvider.GetRequiredService<PlannerTools>();
            tools.Initialize(conference, TimeZoneInfo.Utc, []);
            tools.CapturePreferences(["Alex Rivera"], [], topicFilter: new(["Blazor"], false, false, false));
            var turns = 0;
            using var client = new ScriptedClient(async (_, options, token) =>
            {
                turns++;
                if (invoke)
                    await options.Tools!.OfType<AIFunction>().Single(f => f.Name == "speaker_sessions")
                        .InvokeAsync(new AIFunctionArguments { ["name"] = "Alex" }, token);
                return Reply("I found some sessions.");
            });
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => PlannerDiscovery.RunAsync(client,
                tools, scope.ServiceProvider, "Alex and Blazor", NullLogger.Instance, default));
            Assert.Contains("did not finish", error.Message);
            Assert.Equal(2, turns);
            Assert.False(tools.DiscoveryComplete);
            Assert.Null(tools.Proposal);
        }
    }

    [Fact]
    public async Task NativeCallbackLoopIsCancelledAtTheBudgetEvenIfProviderSwallowsToolErrors()
    {
        var conference = await AgendaAsync();
        using var services = Services();
        using var scope = services.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<PlannerTools>();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences([], []);
        var calls = 0;
        using var client = new ScriptedClient(async (_, options, token) =>
        {
            var search = options.Tools!.OfType<AIFunction>().Single(f => f.Name == "search_sessions");
            while (true)
            {
                token.ThrowIfCancellationRequested();
                calls++;
                try
                {
                    await search.InvokeAsync(new AIFunctionArguments { ["query"] = "invented", ["skip"] = 0 }, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            }
        });
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PlannerDiscovery.RunAsync(client,
            tools, scope.ServiceProvider, "Browse", NullLogger.Instance, default).WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Contains("limit", error.Message);
        Assert.Equal(PlannerDiscovery.MaximumCalls + 1, calls);
        Assert.Null(tools.Proposal);
    }

    [Fact]
    public async Task UserCancellationRemainsCancellationEvenAfterSuccessfulQueries()
    {
        var conference = await AgendaAsync();
        using var services = Services();
        using var scope = services.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<PlannerTools>();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences([], []);
        using var cancellation = new CancellationTokenSource();
        using var client = new ScriptedClient(async (_, options, token) =>
        {
            await options.Tools!.OfType<AIFunction>().Single(f => f.Name == "search_sessions")
                .InvokeAsync(new AIFunctionArguments { ["query"] = "", ["skip"] = 0 }, token);
            cancellation.Cancel();
            return Reply("Done.");
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PlannerDiscovery.RunAsync(client, tools,
            scope.ServiceProvider, "Browse", NullLogger.Instance, cancellation.Token));
        Assert.Null(tools.Proposal);
    }

    [Fact]
    public async Task InvalidArgumentsCanBeCorrectedWithoutBeingTreatedAsACompletedQuery()
    {
        var conference = await AgendaAsync();
        using var services = Services();
        using var scope = services.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<PlannerTools>();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences([], []);
        using var client = new ScriptedClient(async (_, options, token) =>
        {
            var search = options.Tools!.OfType<AIFunction>().Single(f => f.Name == "search_sessions");
            var result = await search.InvokeAsync(new AIFunctionArguments { ["query"] = "", ["skip"] = 0.5 }, token);
            Assert.StartsWith("Invalid tool arguments:", result!.ToString());
            Assert.False(tools.DiscoveryComplete);
            await search.InvokeAsync(new AIFunctionArguments { ["query"] = "", ["skip"] = 0 }, token);
            return Reply("Done.");
        });
        await PlannerDiscovery.RunAsync(client, tools, scope.ServiceProvider, "Browse", NullLogger.Instance, default);
        Assert.True(tools.DiscoveryComplete);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FullPlannerUsesToolCapabilitiesWithoutChangingConstraintOrFallbackBehavior(bool toolCalling)
    {
        var conference = await AgendaAsync();
        var discoveryCalls = 0;
        using var client = new ScriptedClient((_, options, _) =>
        {
            if (options.Tools is { Count: > 0 })
            {
                if (++discoveryCalls == 1)
                    return Task.FromResult(Call("topic_sessions", []));
                return Task.FromResult(Reply("Done."));
            }
            var schema = Assert.IsType<ChatResponseFormatJson>(options.ResponseFormat).Schema!.Value;
            var text = schema.GetProperty("properties").TryGetProperty("speakerNames", out _)
                ? """{"speakerNames":"none","availabilityText":"none","quantity":"some","searchQuery":"none"}"""
                : """{"quantity":"all","speakerScope":"independent"}""";
            return Task.FromResult(Reply(text));
        });
        using var services = new ServiceCollection().AddScoped<PlannerTools>()
            .AddTransient<IChatClient>(_ => client).BuildServiceProvider();
        var planner = new OnDevicePlanner(services.GetRequiredService<IServiceScopeFactory>(), NullLoggerFactory.Instance,
            services.GetRequiredService<IServiceProviderIsService>(), new TestPlannerModelStatus(toolCalling: toolCalling));
        var result = await planner.ProposeAsync(conference, "All Blazor sessions", TimeZoneInfo.Utc, [], default);
        Assert.NotNull(result.Proposal);
        Assert.True(result.Proposal.TopicFilter!.IncludeAll);
        Assert.Equal(["blazor-ai", "blazor-web"], result.Proposal.SessionIds.Order().ToArray());
        Assert.True(SchedulePlanner.Review(conference, result.Proposal, TimeZoneInfo.Utc, []).CanApply);
        Assert.Equal(toolCalling ? 2 : 0, discoveryCalls);
    }

    [Fact]
    public async Task SeparateConversationsDoNotShareModelSelectedCandidates()
    {
        var conference = await AgendaAsync();
        using var services = Services();
        using var first = services.CreateScope();
        using var second = services.CreateScope();
        var tools = first.ServiceProvider.GetRequiredService<PlannerTools>();
        var other = second.ServiceProvider.GetRequiredService<PlannerTools>();
        foreach (var item in new[] { tools, other })
        {
            item.Initialize(conference, TimeZoneInfo.Utc, []);
            item.CapturePreferences([], []);
        }
        using var client = new ScriptedClient(async (_, options, token) =>
        {
            await options.Tools!.OfType<AIFunction>().Single(f => f.Name == "search_sessions")
                .InvokeAsync(new AIFunctionArguments
                {
                    ["query"] = "", ["skip"] = 0, Services = second.ServiceProvider
                }, token);
            return Reply("Done.");
        });
        await PlannerDiscovery.RunAsync(client, tools, first.ServiceProvider, "Browse", NullLogger.Instance, default);
        Assert.NotEmpty(tools.CandidateIds);
        Assert.Empty(other.CandidateIds);
        Assert.Equal(0, other.CallCount);
    }

    [Fact]
    public async Task RestrictedSpeakerTopicsDoNotRequireOrIncludeTheirOffTopicSessions()
    {
        var conference = await AgendaAsync();
        using var services = Services();
        using var scope = services.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<PlannerTools>();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences(["Alex"], [], topicFilter: new(["Mobile"], false, true, false));
        using var client = new ScriptedClient(async (_, options, token) =>
        {
            await options.Tools!.OfType<AIFunction>().Single(f => f.Name == "speaker_sessions")
                .InvokeAsync(new AIFunctionArguments { ["name"] = "Alex" }, token);
            return Reply("Done.");
        });
        await PlannerDiscovery.RunAsync(client, tools, scope.ServiceProvider, "Alex's mobile sessions", NullLogger.Instance, default);
        Assert.True(tools.DiscoveryComplete);
        Assert.Equal(["building-with-maui"], tools.CandidateIds);
        Assert.DoesNotContain("accessible-by-design", tools.CandidateIds);
    }

    [Fact]
    public async Task AProviderCannotInvokeCapturedToolsAfterDiscoveryFinishes()
    {
        var conference = await AgendaAsync();
        using var services = Services();
        using var scope = services.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<PlannerTools>();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences([], []);
        AIFunction? captured = null;
        using var client = new ScriptedClient(async (_, options, token) =>
        {
            captured = options.Tools!.OfType<AIFunction>().Single(f => f.Name == "search_sessions");
            await captured.InvokeAsync(new AIFunctionArguments { ["query"] = "", ["skip"] = 0 }, token);
            return Reply("Done.");
        });
        await PlannerDiscovery.RunAsync(client, tools, scope.ServiceProvider, "Browse", NullLogger.Instance, default);
        var calls = tools.CallCount;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await captured!.InvokeAsync(
            new AIFunctionArguments { ["query"] = "", ["skip"] = 0 }, CancellationToken.None));
        Assert.Equal(calls, tools.CallCount);
    }

    [Fact]
    public async Task AnUnknownOrSavingToolIsNotInvokedByTheMiddleware()
    {
        var conference = await AgendaAsync();
        using var services = Services();
        using var scope = services.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<PlannerTools>();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences([], []);
        var calls = 0;
        using var client = new ScriptedClient((_, _, _) => Task.FromResult(++calls == 1
            ? Call("submit_proposal", new() { ["sessionIds"] = new[] { "building-with-maui" } })
            : Reply("Done.")));
        await Assert.ThrowsAsync<InvalidDataException>(() => PlannerDiscovery.RunAsync(client,
            tools, scope.ServiceProvider, "Save all", NullLogger.Instance, default));
        Assert.Null(tools.Proposal);
        Assert.Equal(conference.Sessions.Select(session => session.Id).Order(), tools.CandidateIds.Order());
        Assert.Equal(0, tools.CallCount);
    }

    [Fact]
    public async Task ModelThatNeverCompletesHonorsTheDiscoveryTimeout()
    {
        var conference = await AgendaAsync();
        using var services = Services();
        using var scope = services.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<PlannerTools>();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences([], []);
        using var client = new ScriptedClient(async (_, _, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Reply("Unreachable.");
        });
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PlannerDiscovery.RunAsync(
            client, tools, scope.ServiceProvider, "Browse", NullLogger.Instance, default)
            .WaitAsync(TimeSpan.FromSeconds(40)));
        Assert.Contains("time or tool-call limit", error.Message);
        Assert.False(tools.DiscoveryComplete);
        Assert.Null(tools.Proposal);
    }

    private static ServiceProvider Services() => new ServiceCollection().AddScoped<PlannerTools>().BuildServiceProvider(
        new ServiceProviderOptions { ValidateScopes = true });

    private static async Task<ConferenceDocument> AgendaAsync()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        return await ConferenceJson.ReadAsync(stream);
    }

    private static ChatResponse Call(string name, Dictionary<string, object?> arguments) =>
        new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(Guid.NewGuid().ToString(), name, arguments)]));

    private static ChatResponse Reply(string text) => new(new ChatMessage(ChatRole.Assistant, text));

    private sealed class ScriptedClient(
        Func<ChatMessage[], ChatOptions, CancellationToken, Task<ChatResponse>> respond) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => respond(messages.ToArray(), options!, cancellationToken);
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
