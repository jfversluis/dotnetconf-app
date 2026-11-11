using Microsoft.Testing.Extensions;
using Microsoft.Testing.Platform.Builder;
using Microsoft.Testing.Platform.Extensions;
using Microsoft.Testing.Platform.Extensions.Messages;

[assembly: DoNotParallelize]

namespace DotNetConf.DeviceTests;

internal static class NativeTestRunner
{
    internal static async Task<(int ExitCode, string? ResultsPath)> RunAsync()
    {
        var consumer = new Results();
#if ANDROID
        var resultsRoot = Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath
            ?? throw new InvalidOperationException("The native test results directory is unavailable.");
#else
        var resultsRoot = FileSystem.AppDataDirectory;
#endif
        var builder = await TestApplication.CreateBuilderAsync([
            "--results-directory", Path.Combine(resultsRoot, "TestResults"), "--report-trx"
        ]);
        builder.AddMSTest(() => [typeof(NativeRegressionTests).Assembly]);
        builder.AddTrxReportProvider();
        builder.TestHost.AddDataConsumer(_ => consumer);
        using var app = await builder.BuildAsync();
        var exitCode = await app.RunAsync();
        Console.WriteLine($"Native results: passed={consumer.Passed}, failed={consumer.Failed}");
        return (exitCode == 0 && consumer.Passed > 0 && consumer.Failed == 0 ? 0 : 1, consumer.ResultsPath);
    }

    private sealed class Results : IDataConsumer
    {
        internal int Passed;
        internal int Failed;
        internal int Skipped;
        internal string? ResultsPath;
        public string Uid => nameof(Results);
        public string DisplayName => nameof(Results);
        public string Description => "Native regression results";
        public string Version => "1.0";
        public Type[] DataTypesConsumed => [typeof(TestNodeUpdateMessage), typeof(SessionFileArtifact)];
        public Task<bool> IsEnabledAsync() => Task.FromResult(true);

        public Task ConsumeAsync(IDataProducer producer, IData value, CancellationToken cancellationToken)
        {
            if (value is SessionFileArtifact artifact)
            {
                ResultsPath = artifact.FileInfo.FullName;
                Console.WriteLine($"Results: passed={Passed}, failed={Failed}, skipped={Skipped}");
                Console.WriteLine($"TRX report: {artifact.FileInfo.FullName}");
            }
            if (value is TestNodeUpdateMessage { TestNode: var node })
            {
                var state = node.Properties.SingleOrDefault<TestNodeStateProperty>();
                if (state is PassedTestNodeStateProperty)
                    Interlocked.Increment(ref Passed);
                else if (state is FailedTestNodeStateProperty or ErrorTestNodeStateProperty or TimeoutTestNodeStateProperty)
                    Interlocked.Increment(ref Failed);
                else if (state is SkippedTestNodeStateProperty)
                    Interlocked.Increment(ref Skipped);
                else
                    return Task.CompletedTask;
                var outcome = state is PassedTestNodeStateProperty ? "PASSED" :
                    state is SkippedTestNodeStateProperty ? "SKIPPED" : "FAILED";
                var id = node.Properties.SingleOrDefault<TestMethodIdentifierProperty>();
                var name = id is null ? node.DisplayName : $"{id.Namespace}.{id.TypeName}.{id.MethodName}";
                Console.WriteLine($"[{outcome}] {name}");
            }
            return Task.CompletedTask;
        }
    }
}
