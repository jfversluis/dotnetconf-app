using System.Text.Json;
using DotNetConf.App.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace DotNetConf.Core.Tests;

public sealed class GeminiToolCompatibilityTests
{
    [Fact]
    public void GeneratedSchemasKeepEveryOtherPropertyAndMetadata()
    {
        foreach (var function in PlannerToolContext.Default.Tools.Cast<AIFunction>())
        {
            Assert.Equal(JsonValueKind.False, function.JsonSchema.GetProperty("additionalProperties").ValueKind);
            var adapted = GeminiToolCompatibility.Adapt(function);

            Assert.False(adapted.JsonSchema.TryGetProperty("additionalProperties", out _));
            foreach (var property in function.JsonSchema.EnumerateObject().Where(p => p.Name != "additionalProperties"))
                Assert.True(JsonElement.DeepEquals(property.Value, adapted.JsonSchema.GetProperty(property.Name)));
            Assert.Equal(function.Name, adapted.Name);
            Assert.Equal(function.Description, adapted.Description);
            Assert.Equal(function.ReturnJsonSchema, adapted.ReturnJsonSchema);
            Assert.Same(function.AdditionalProperties, adapted.AdditionalProperties);
        }
    }

    [Fact]
    public async Task UnknownArgumentsAreRejectedBeforeTheToolRuns()
    {
        var tools = new PlannerTools();
        using var services = new ServiceCollection().AddSingleton(tools).BuildServiceProvider();
        var search = GeminiToolCompatibility.Adapt(
            tools.CreateFunctions(services).Cast<AIFunction>().Single(f => f.Name == "search_sessions"));

        var arguments = new AIFunctionArguments { ["query"] = "Mobile", ["skip"] = 0, ["extra"] = true };
#if ANDROID
        var result = Assert.IsType<string>(await search.InvokeAsync(arguments));
        Assert.Contains("Invalid tool arguments:", result);
        Assert.Contains("extra", result);
        Assert.Equal(1, tools.CallCount);
#else
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () => await search.InvokeAsync(arguments));

        Assert.Contains("extra", exception.Message);
        Assert.Equal(0, tools.CallCount);
#endif
        Assert.Empty(tools.CandidateIds);
    }

    [Fact]
    public async Task AdaptedToolsAcceptParameterlessAndOptionalArguments()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var tools = new PlannerTools();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences([], []);
        using var services = new ServiceCollection().AddSingleton(tools).BuildServiceProvider();
        var functions = tools.CreateFunctions(services).Cast<AIFunction>()
            .Select(GeminiToolCompatibility.Adapt).ToDictionary(function => function.Name);

        Assert.Contains("Mobile", Assert.IsType<string>(
            await functions["catalog_topics"].InvokeAsync(new AIFunctionArguments())));
        Assert.Contains("Alex Rivera", Assert.IsType<string>(
            await functions["speaker_sessions"].InvokeAsync(new AIFunctionArguments { ["name"] = "Alex" })));
    }

    [Fact]
    public async Task CancelledInvocationDoesNotReachTheTool()
    {
        var tools = new PlannerTools();
        using var services = new ServiceCollection().AddSingleton(tools).BuildServiceProvider();
        var search = GeminiToolCompatibility.Adapt(
            tools.CreateFunctions(services).Cast<AIFunction>().Single(f => f.Name == "search_sessions"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await search.InvokeAsync(new AIFunctionArguments { ["query"] = "Mobile", ["skip"] = 0 },
                cancellation.Token));
        Assert.Equal(0, tools.CallCount);
    }

    [Fact]
    public async Task AdaptedGeneratedToolRoundTripsThroughFunctionInvocation()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var tools = new PlannerTools();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences([], []);
        using var services = new ServiceCollection().AddSingleton(tools).BuildServiceProvider();
        var search = GeminiToolCompatibility.Adapt(
            tools.CreateFunctions(services).Cast<AIFunction>().Single(f => f.Name == "search_sessions"));
        var inner = Substitute.For<IChatClient>();
        var calls = 0;
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (++calls == 1)
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("call", search.Name, new Dictionary<string, object?>
                        {
                            ["query"] = "Mobile", ["skip"] = 0
                        })]));
                var result = Assert.Single(call.Arg<IEnumerable<ChatMessage>>()
                    .SelectMany(message => message.Contents).OfType<FunctionResultContent>());
                Assert.Contains("building-with-maui", Assert.IsType<string>(result.Result));
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "Synthetic completion"));
            });
        using var client = inner.AsBuilder().UseFunctionInvocation().Build(services);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Synthetic request")],
            new ChatOptions { Tools = [search], AllowMultipleToolCalls = false });

        Assert.Equal(2, calls);
        Assert.Equal(1, tools.CallCount);
        Assert.Contains("Synthetic completion", response.Text);
    }

    [Fact]
    public async Task ApprovalRequiredToolsStillProduceApprovalRequests()
    {
        var tools = new PlannerTools();
        using var services = new ServiceCollection().AddSingleton(tools).BuildServiceProvider();
        var search = tools.CreateFunctions(services).Cast<AIFunction>().Single(f => f.Name == "search_sessions");
        var approval = new ApprovalRequiredAIFunction(search);
        var adapted = GeminiToolCompatibility.Adapt(approval);
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call", search.Name, new Dictionary<string, object?>
                {
                    ["query"] = "Mobile", ["skip"] = 0
                })])));
        using var client = inner.AsBuilder().UseFunctionInvocation().Build(services);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Synthetic request")],
            new ChatOptions { Tools = [adapted], AllowMultipleToolCalls = false });

        Assert.Same(approval, adapted.GetService<ApprovalRequiredAIFunction>());
        Assert.Single(response.Messages.SelectMany(message => message.Contents).OfType<ToolApprovalRequestContent>());
        Assert.Equal(0, tools.CallCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApprovedCallsStillEnforceTheOriginalArgumentPolicy(bool unknownArgument)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var tools = new PlannerTools();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences([], []);
        using var services = new ServiceCollection().AddSingleton(tools).BuildServiceProvider();
        var search = tools.CreateFunctions(services).Cast<AIFunction>().Single(f => f.Name == "search_sessions");
        var adapted = GeminiToolCompatibility.Adapt(new ApprovalRequiredAIFunction(search));
        var arguments = new Dictionary<string, object?> { ["query"] = "Mobile", ["skip"] = 0 };
        if (unknownArgument)
            arguments["extra"] = true;
        var inner = Substitute.For<IChatClient>();
        var calls = 0;
        FunctionResultContent? result = null;
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (++calls == 1)
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("call", search.Name, arguments)]));
                result = Assert.Single(call.Arg<IEnumerable<ChatMessage>>()
                    .SelectMany(message => message.Contents).OfType<FunctionResultContent>());
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "Synthetic completion"));
            });
        using var client = inner.AsBuilder().UseFunctionInvocation().Build(services);
        var options = new ChatOptions { Tools = [adapted], AllowMultipleToolCalls = false };
        ChatMessage[] initial = [new(ChatRole.User, "Synthetic request")];

        var response = await client.GetResponseAsync(initial, options);
        var request = Assert.Single(response.Messages.SelectMany(message => message.Contents)
            .OfType<ToolApprovalRequestContent>());
        await client.GetResponseAsync([.. initial, .. response.Messages,
            new ChatMessage(ChatRole.User, [request.CreateResponse(true)])], options);

        Assert.Equal(2, calls);
        Assert.NotNull(result);
        if (unknownArgument)
        {
#if ANDROID
            Assert.Null(result.Exception);
            Assert.Contains("Invalid tool arguments:", Assert.IsType<string>(result.Result));
            Assert.Contains("extra", result.Result.ToString());
            Assert.Equal(1, tools.CallCount);
#else
            Assert.IsType<ArgumentException>(result.Exception);
            Assert.NotEmpty(Assert.IsType<string>(result.Result));
            Assert.Equal(0, tools.CallCount);
#endif
            Assert.Empty(tools.CandidateIds);
        }
        else
        {
            Assert.Contains("building-with-maui", Assert.IsType<string>(result.Result));
            Assert.Equal(1, tools.CallCount);
        }
    }

    [Fact]
    public void SchemasWithoutTheKeywordAreUnchangedAndUnrepresentablePoliciesFail()
    {
        var function = PlannerToolContext.Default.Tools.Cast<AIFunction>().First();
        using var openSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""");
        var open = new SchemaOverride(function, openSchema.RootElement.Clone());
        Assert.Same(open, GeminiToolCompatibility.Adapt(open));

        using var allowedSchema = JsonDocument.Parse("""{"type":"object","additionalProperties":true}""");
        var allowed = new SchemaOverride(function, allowedSchema.RootElement.Clone());
        Assert.Throws<NotSupportedException>(() => GeminiToolCompatibility.Adapt(allowed));

        using var invalidSchema = JsonDocument.Parse("""{"type":"object","properties":false,"additionalProperties":false}""");
        var invalid = new SchemaOverride(function, invalidSchema.RootElement.Clone());
        Assert.Throws<InvalidDataException>(() => GeminiToolCompatibility.Adapt(invalid));
    }

    private sealed class SchemaOverride(AIFunction inner, JsonElement schema) : DelegatingAIFunction(inner)
    {
        public override JsonElement JsonSchema => schema;
    }
}
