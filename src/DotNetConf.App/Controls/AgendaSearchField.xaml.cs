using System.Windows.Input;

namespace DotNetConf.App.Controls;

public partial class AgendaSearchField : ContentView
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(AgendaSearchField), "", BindingMode.TwoWay,
        propertyChanged: (bindable, _, _) => ((AgendaSearchField)bindable).OnPropertyChanged(nameof(HasQuery)));
    public static readonly BindableProperty ApplyQueryCommandProperty = BindableProperty.Create(
        nameof(ApplyQueryCommand), typeof(ICommand), typeof(AgendaSearchField));
    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder), typeof(string), typeof(AgendaSearchField), "Search");
    public static readonly BindableProperty SearchDescriptionProperty = BindableProperty.Create(
        nameof(SearchDescription), typeof(string), typeof(AgendaSearchField), "Search");
    public static readonly BindableProperty SearchAutomationIdProperty = BindableProperty.Create(
        nameof(SearchAutomationId), typeof(string), typeof(AgendaSearchField), "");
    public static readonly BindableProperty ClearAutomationIdProperty = BindableProperty.Create(
        nameof(ClearAutomationId), typeof(string), typeof(AgendaSearchField), "");

    public AgendaSearchField()
    {
        InitializeComponent();
#if IOS
        SearchInput.SetBinding(SemanticProperties.DescriptionProperty,
            Binding.Create(static (AgendaSearchField field) => field.SearchDescription, source: this));
#endif
    }

    public ICommand? ApplyQueryCommand
    {
        get => (ICommand?)GetValue(ApplyQueryCommandProperty);
        set => SetValue(ApplyQueryCommandProperty, value);
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public string SearchDescription { get => (string)GetValue(SearchDescriptionProperty); set => SetValue(SearchDescriptionProperty, value); }
    public string SearchAutomationId { get => (string)GetValue(SearchAutomationIdProperty); set => SetValue(SearchAutomationIdProperty, value); }
    public string ClearAutomationId { get => (string)GetValue(ClearAutomationIdProperty); set => SetValue(ClearAutomationIdProperty, value); }
    public bool HasQuery => !string.IsNullOrEmpty(Text);
    public bool IsSearchFocused => SearchInput.IsFocused;
#if IOS
    internal UIKit.UIView? NativeInputAccessory =>
        (SearchInput.Handler?.PlatformView as UIKit.UITextField)?.InputAccessoryView;
#endif
#if ANDROID
    internal bool IsSoftInputShowing => SearchInput.IsSoftInputShowing();
    internal bool HasActiveInputConnection => SearchInput.HasActiveInputConnection;
    internal Task<bool> HideKeyboardAsync() => SearchInput.HideSoftInputAsync(CancellationToken.None);
#endif
    internal void UnfocusSearch() => SearchInput.Unfocus();
    public event EventHandler<FocusEventArgs>? SearchFocusChanged;

    private void OnFieldTapped(object? sender, TappedEventArgs e) => SearchInput.Focus();

    private void OnClearClicked(object? sender, EventArgs e)
    {
        Text = "";
        ApplyQuery();
        SearchInput.Focus();
    }

    private void OnCompleted(object? sender, EventArgs e)
    {
        ApplyQuery();
        SearchInput.Unfocus();
    }

    private void ApplyQuery()
    {
        if (ApplyQueryCommand?.CanExecute(null) is true)
            ApplyQueryCommand.Execute(null);
    }

#if ANDROID
    private async void OnFocusChanged(object? sender, FocusEventArgs e)
#else
    private void OnFocusChanged(object? sender, FocusEventArgs e)
#endif
    {
        Outline.SetAppTheme<Brush>(Border.StrokeProperty,
            new SolidColorBrush((Color)Application.Current!.Resources[e.IsFocused ? "Purple" : "LightOutline"]),
            new SolidColorBrush((Color)Application.Current!.Resources[e.IsFocused ? "Lavender" : "DarkOutline"]));
        SearchFocusChanged?.Invoke(this, e);
#if ANDROID
        if (!e.IsFocused && IsSoftInputShowing && HasActiveInputConnection)
            await HideKeyboardAsync();
#endif
    }
}
