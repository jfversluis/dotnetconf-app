using System.Text.Json;
using DotNetConf.App.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotNetConf.Core.Tests;

public sealed class OnDevicePlannerGroundingTests
{
    [Theory]
    [InlineData("Show me sessions for Ada Lovelace", "Ada Lovelace")]
    [InlineData("Ada Lovelace, please", "Ada Lovelace")]
    [InlineData("Put Ada on my schedule", "Ada")]
    [InlineData("I like AI and don't miss Ada Lovelace", "Ada Lovelace")]
    public async Task AbsentSpeakerStopsPlanningBeforeQueriesOrRecommendations(string request, string name)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var client = new ExtractionClient(name);
        var services = new TestServices(client);
        var planner = new OnDevicePlanner(services, NullLoggerFactory.Instance, services);

        var result = await planner.ProposeAsync(conference, request, TimeZoneInfo.Utc, [], default);

        Assert.NotNull(result.Proposal);
        Assert.Equal([name], result.Proposal.RequiredSpeakers);
        Assert.Empty(result.Proposal.SessionIds);
        Assert.Contains("Speaker not found", result.Message);
        Assert.False(SchedulePlanner.Review(conference, result.Proposal, TimeZoneInfo.Utc, []).CanApply);
        Assert.Equal(1, client.Calls);
        Assert.Equal(0, services.Tools.CallCount);
        Assert.True(client.Disposed);
    }

    private sealed class TestServices(IChatClient client)
        : IServiceProvider, IServiceProviderIsService, IServiceScopeFactory, IServiceScope
    {
        public PlannerTools Tools { get; } = new();
        public object? GetService(Type serviceType) => serviceType == typeof(IChatClient) ? client
            : serviceType == typeof(PlannerTools) ? Tools : null;
        public bool IsService(Type serviceType) => serviceType == typeof(IChatClient);
        public IServiceProvider ServiceProvider => this;
        public IServiceScope CreateScope() => this;
        public void Dispose() => client.Dispose();
    }

    private sealed class ExtractionClient(string name) : IChatClient
    {
        public int Calls { get; private set; }
        public bool Disposed { get; private set; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(1, ++Calls);
            var text = JsonSerializer.Serialize(new PlannerInterpretation(name, "none", "none", "some"),
                PlannerJsonContext.Default.PlannerInterpretation);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => Disposed = true;
    }
}
