using DotNetConf.App.Controls;
using DotNetConf.App.Services;

namespace DotNetConf.DeviceTests;

[TestClass]
public sealed class NativeRegressionTests
{
    private Shell shell = null!;

    [TestInitialize]
    public async Task ResetNavigation()
    {
        shell = await NativeTestApplication.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await shell.Dispatcher.DispatchAsync(async () => await shell.GoToAsync("//sessions", false));
    }

    [TestMethod]
    public async Task RouteTemplateCapturesIdAndPreservesOneBackDestination()
    {
        await shell.Dispatcher.DispatchAsync(async () =>
        {
            await shell.GoToAsync(SessionRoutes.ForSession("synthetic-first"), false);
            var first = (RouteResultPage)shell.CurrentPage;
            Assert.AreEqual("synthetic-first", first.SessionId);
            Assert.IsNotNull(first.Handler?.PlatformView);
            Assert.AreEqual("Back to Sessions", Shell.GetBackButtonBehavior(first).AccessibilityLabel);
            await shell.GoToAsync(SessionRoutes.ForSession("synthetic-second"), false);
            Assert.AreEqual(2, shell.Navigation.NavigationStack.Count);
            Assert.AreEqual("synthetic-second", ((RouteResultPage)shell.CurrentPage).SessionId);
            await shell.GoToAsync("..", false);
            Assert.AreEqual("Sessions", shell.CurrentPage.Title);
            Assert.AreEqual(1, shell.Navigation.NavigationStack.Count);
        });
    }

    [TestMethod]
    public async Task BackLabelChangesWithoutReplacingCustomBackBehavior()
    {
        await shell.Dispatcher.DispatchAsync(async () =>
        {
            await shell.GoToAsync(SessionRoutes.ForSession("synthetic-back"), false);
            var command = new Command(() => { });
            var behavior = new BackButtonBehavior { Command = command, IsEnabled = false };
            Shell.SetBackButtonBehavior(shell.CurrentPage, behavior);
            BackNavigation.Update(shell);
            BackNavigation.Update(shell);
            Assert.AreSame(behavior, Shell.GetBackButtonBehavior(shell.CurrentPage));
            Assert.AreSame(command, behavior.Command);
            Assert.IsFalse(behavior.IsEnabled);
            Assert.AreEqual("Back to Sessions", behavior.AccessibilityLabel);
        });
    }

    [TestMethod]
    public async Task SafeContentUsesTheOwningNativePageAndDoesNotInsetOrdinaryPhones()
    {
        await shell.Dispatcher.DispatchAsync(async () =>
        {
            var content = new DuoSafeAreaContent
            {
                Content = new Label { Text = "Synthetic safe-content fixture", FontSize = 22 }
            };
            var page = (ContentPage)shell.CurrentPage;
            page.SafeAreaEdges = SafeAreaEdges.None;
            page.Content = new ScrollView { SafeAreaEdges = SafeAreaEdges.None, Content = content };
            await WaitUntil(() => content.Handler?.PlatformView is not null && content.Width > 0);
#if IOS
            var view = (UIKit.UIView)content.Handler!.PlatformView!;
            var nativePage = (UIKit.UIView)page.Handler!.PlatformView!;
            var frame = view.ConvertRectToView(view.Bounds, nativePage);
            var safe = nativePage.SafeAreaLayoutGuide.LayoutFrame;
            var isDuo = OperatingSystem.IsIOSVersionAtLeast(27) &&
                (Environment.GetEnvironmentVariable("SIMULATOR_MODEL_IDENTIFIER") ?? DeviceInfo.Model) == "iPhone19,4";
            var expected = isDuo ? DuoSafeAreaContent.CalculateHorizontalPadding(
                new Rect(frame.X, frame.Y, frame.Width, frame.Height),
                new Rect(safe.X, safe.Y, safe.Width, safe.Height)) : new Thickness(0);
            await WaitUntil(() => content.Padding == expected);
            Assert.AreEqual(expected, content.Padding);
#else
            Assert.AreEqual(new Thickness(0), content.Padding);
#endif
        });
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            if (condition())
                return;
            await Task.Delay(50);
        }
        Assert.Fail("The native layout did not converge within six seconds.");
    }

}
