namespace DotNetConf.App.Services;

public static class BackNavigation
{
    public static void Update(Shell shell)
    {
        var stack = shell.Navigation.NavigationStack;
        if (stack.Count < 2)
            return;
        if (stack[^1] is not { } page)
            return;
        var behavior = Shell.GetBackButtonBehavior(page) ?? new BackButtonBehavior();
        // Shell represents the root of a pushed stack with a null page.
        var title = stack[^2]?.Title ?? shell.CurrentItem?.CurrentItem?.CurrentItem?.Title;
        behavior.AccessibilityLabel = string.IsNullOrWhiteSpace(title) ? "Back" : $"Back to {title}";
        Shell.SetBackButtonBehavior(page, behavior);
    }
}
