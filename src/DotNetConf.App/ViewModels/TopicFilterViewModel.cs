using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DotNetConf.App.ViewModels;

public partial class TopicFilterViewModel(string name, Action selectionChanged) : ObservableObject
{
    public string Name { get; } = name;
    public string AutomationId => $"filter-topic-{Name}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value) => selectionChanged();

    [RelayCommand]
    private void Toggle() => IsSelected = !IsSelected;
}
