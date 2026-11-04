using DotNetConf.App.ViewModels;
using DotNetConf.Core;
#if ANDROID
using Android.Content;
using Android.Views.InputMethods;
using AndroidView = Android.Views.View;
#endif

namespace DotNetConf.App.Controls;

public partial class AskSheetView : ContentView
{
    private bool deactivating;
    public event EventHandler? CloseRequested;
    public event EventHandler? ScheduleRequested;
    public AskSheetView()
    {
        InitializeComponent();
#if IOS
        SemanticProperties.SetDescription(Input, "Ask about the conference");
#endif
#if DEBUG
        var toggle = new Switch { AutomationId = "ask-trace-toggle" };
        toggle.MinimumHeightRequest = 48;
        toggle.SetBinding(Switch.IsToggledProperty, nameof(AskViewModel.ShowTrace), mode: BindingMode.TwoWay);
        SemanticProperties.SetDescription(toggle, "Show local tool trace. Argument values are hidden.");
        var header = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            ],
            ColumnSpacing = 12
        };
        header.Add(new Label { Text = "Developer tool trace", VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.WordWrap, AutomationId = "ask-trace-label" });
        Grid.SetColumn(toggle, 1);
        header.Add(toggle);
        DebugSlot.Add(header);
        var trace = new Label { FontSize = 12, AutomationId = "ask-trace" };
        trace.SetBinding(Label.TextProperty, nameof(AskViewModel.TraceText));
        trace.SetBinding(IsVisibleProperty, nameof(AskViewModel.ShowTrace));
        DebugSlot.Add(trace);
#endif
    }

    public void FocusInput()
    {
        deactivating = false;
        Input.Focus();
    }
    public void RestoreMotion() => Glow.SetBinding(AskGlow.IsWorkingProperty, nameof(AskViewModel.IsBusy));
    public void Deactivate()
    {
        deactivating = true;
#if ANDROID
        if (Input.Handler?.PlatformView is AndroidView nativeInput && nativeInput.WindowToken is { } windowToken)
        {
            using var manager = nativeInput.Context?.GetSystemService(Context.InputMethodService) as InputMethodManager;
            if (manager?.InvokeIsActive(nativeInput) == true)
                manager.HideSoftInputFromWindow(windowToken, HideSoftInputFlags.None);
        }
#endif
        Input.Unfocus();
        Glow.IsWorking = false;
    }
    private void InputFocused(object? sender, FocusEventArgs e) => Glow.Opacity = 1;
    private void InputUnfocused(object? sender, FocusEventArgs e) => Glow.Opacity = .8;
    private async void InputCompleted(object? sender, EventArgs e)
    {
        if (!deactivating && BindingContext is AskViewModel { IsBusy: false } model &&
            !string.IsNullOrWhiteSpace(model.Request) && model.SubmitCommand.CanExecute(null))
            await model.SubmitCommand.ExecuteAsync(null);
    }
    private void CloseClicked(object? sender, EventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void ViewScheduleClicked(object? sender, EventArgs e) => ScheduleRequested?.Invoke(this, EventArgs.Empty);
    private void ChoiceClicked(object? sender, EventArgs e)
    {
        if (BindingContext is AskViewModel vm && sender is Button { CommandParameter: AskChoice choice })
            vm.ChooseCommand.Execute(choice);
    }
}
