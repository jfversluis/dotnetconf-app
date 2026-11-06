#if AVALONIA
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
#endif

namespace DotNetConf.App.Services;

public interface IConferenceSharing
{
    bool CanShare { get; }
    Task ShareAsync(string title, string text);
    Task CopyAsync(string text);
}

public sealed class ConferenceSharing : IConferenceSharing
{
#if AVALONIA
    public bool CanShare => false;
    public Task ShareAsync(string title, string text) => CopyAsync(text);

    public Task CopyAsync(string text)
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime switch
        {
            IClassicDesktopStyleApplicationLifetime desktop => desktop.MainWindow,
            ISingleViewApplicationLifetime single => TopLevel.GetTopLevel(single.MainView),
            _ => null
        };
        return (topLevel?.Clipboard ?? throw new FeatureNotSupportedException("Clipboard access isn't available."))
            .SetTextAsync(text);
    }
#else
    public bool CanShare => true;
    public Task ShareAsync(string title, string text) =>
        Share.Default.RequestAsync(new ShareTextRequest { Title = title, Text = text });
    public Task CopyAsync(string text) => Clipboard.Default.SetTextAsync(text);
#endif
}
