using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Controls;

public class DetailRow : ContentView
{
    public static readonly BindableProperty SelectedIdProperty = BindableProperty.Create(
        nameof(SelectedId), typeof(string), typeof(DetailRow), null,
        propertyChanged: (bindable, _, _) => ((DetailRow)bindable).UpdateSelection());
    public static readonly BindableProperty IsCompactSearchProperty = BindableProperty.Create(
        nameof(IsCompactSearch), typeof(bool), typeof(DetailRow), false);
    public string? SelectedId { get => (string?)GetValue(SelectedIdProperty); set => SetValue(SelectedIdProperty, value); }
    public bool IsCompactSearch { get => (bool)GetValue(IsCompactSearchProperty); set => SetValue(IsCompactSearchProperty, value); }
    public bool IsSelected { get; private set; }
    public string SelectionSuffix => IsSelected ? " Selected." : string.Empty;

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var id = BindingContext switch
        {
            SessionViewModel session => session.Id,
            SpeakerViewModel speaker => speaker.Id,
            _ => null
        };
        var selected = SelectedId is not null && id == SelectedId;
        if (selected == IsSelected)
            return;

        IsSelected = selected;
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(SelectionSuffix));
    }
}
