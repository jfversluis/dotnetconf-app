using System.ComponentModel;
using IosPage = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page;

namespace DotNetConf.App.Controls;

public class SafeAreaFooter : ContentView
{
    public static readonly BindableProperty PageProperty = BindableProperty.Create(
        nameof(Page), typeof(Page), typeof(SafeAreaFooter), null,
        propertyChanged: (bindable, oldValue, newValue) =>
        {
            var footer = (SafeAreaFooter)bindable;
            if (oldValue is Page previous)
                previous.PropertyChanged -= footer.OnPagePropertyChanged;
            if (newValue is Page page && footer.IsLoaded)
                page.PropertyChanged += footer.OnPagePropertyChanged;
            footer.UpdateHeight();
        });

    public Page? Page { get => (Page?)GetValue(PageProperty); set => SetValue(PageProperty, value); }

    public SafeAreaFooter()
    {
        SafeAreaEdges = SafeAreaEdges.None;
        AutomationProperties.SetIsInAccessibleTree(this, false);
        Loaded += (_, _) =>
        {
            if (Page is not null)
                Page.PropertyChanged += OnPagePropertyChanged;
            UpdateHeight();
        };
        Unloaded += (_, _) =>
        {
            if (Page is not null)
                Page.PropertyChanged -= OnPagePropertyChanged;
        };
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == IosPage.SafeAreaInsetsProperty.PropertyName)
            UpdateHeight();
    }

    // MAUI's iOS CollectionView disables automatic scroll insets; use its page's actual inset.
    private void UpdateHeight() => HeightRequest =
        DeviceInfo.Platform == DevicePlatform.iOS && Page is not null ? IosPage.GetSafeAreaInsets(Page).Bottom : 0;
}
