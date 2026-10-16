using DotNetConf.App.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotNetConf.Core.Tests;

public sealed class OnDevicePlannerReadinessTests
{
    [Fact]
    public async Task MissingClientIsUnsupportedWithoutAttemptingInference()
    {
        var planner = Create(null);
        Assert.NotNull(planner.UnsupportedReason);
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => planner.CheckReadinessAsync(default));
    }

    [Fact]
    public async Task AnyRegisteredLocalClientCanPassReadinessRegardlessOfPlatform()
    {
        var client = new TestClient(_ => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "READY"))));
        var planner = Create(client);
        Assert.Null(planner.UnsupportedReason);
        await planner.CheckReadinessAsync(default);
        Assert.Equal("Reply with READY.", client.Prompt);
        Assert.True(client.Disposed);
    }

    [Fact]
    public async Task RegistrationAloneDoesNotProveModelReadiness()
    {
        var client = new TestClient(_ => throw new NotSupportedException("Model is not downloaded."));
        await Assert.ThrowsAsync<NotSupportedException>(() => Create(client).CheckReadinessAsync(default));
        Assert.True(client.Disposed);
    }

    [Fact]
    public async Task ReadinessForwardsCancellationAndDisposesTheClient()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = new TestClient(token => Task.FromCanceled<ChatResponse>(token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Create(client).CheckReadinessAsync(cancellation.Token));
        Assert.True(client.Disposed);
    }

    private static OnDevicePlanner Create(IChatClient? client)
    {
        var services = new TestServices(client);
        return new(services, NullLoggerFactory.Instance, services);
    }

    private sealed class TestServices(IChatClient? client) : IServiceProvider, IServiceProviderIsService, IServiceScopeFactory, IServiceScope
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IChatClient) ? client : null;
        public bool IsService(Type serviceType) => serviceType == typeof(IChatClient) && client is not null;
        public IServiceProvider ServiceProvider => this;
        public IServiceScope CreateScope() => this;
        public void Dispose() => client?.Dispose();
    }

    private sealed class TestClient(Func<CancellationToken, Task<ChatResponse>> respond) : IChatClient
    {
        public string? Prompt { get; private set; }
        public bool Disposed { get; private set; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Prompt = Assert.Single(messages).Text;
            return respond(cancellationToken);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => Disposed = true;
    }
}
