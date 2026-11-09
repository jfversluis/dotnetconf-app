using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;

namespace DotNetConf.DeviceTests;

[Instrumentation(Name = "dev.jfversluis.dotnetconf.devicetests.TestInstrumentation")]
public sealed class TestInstrumentation(nint handle, JniHandleOwnership ownership) : Instrumentation(handle, ownership)
{
    public override void OnCreate(Bundle? arguments)
    {
        base.OnCreate(arguments);
        Start();
    }

    public override async void OnStart()
    {
        base.OnStart();
        var result = new Bundle();
        try
        {
            var context = TargetContext ?? throw new InvalidOperationException("The native test application is unavailable.");
            context.StartActivity(new Intent(context, typeof(MainActivity)).AddFlags(ActivityFlags.NewTask));
            await NativeTestApplication.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var run = await NativeTestRunner.RunAsync();
            result.PutInt("exitCode", run.ExitCode);
            result.PutString("resultsPath", run.ResultsPath);
            Finish(run.ExitCode == 0 ? Result.Ok : Result.Canceled, result);
        }
        catch (Exception exception)
        {
            result.PutString("error", exception.ToString());
            Finish(Result.Canceled, result);
        }
    }
}
