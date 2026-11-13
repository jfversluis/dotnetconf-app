using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Controls;

public sealed class PortraitImage : Image, IImageSourcePartEvents
{
    public static readonly BindableProperty PortraitSourceProperty = BindableProperty.Create(
        nameof(PortraitSource), typeof(ImageSource), typeof(PortraitImage),
        propertyChanged: (bindable, _, value) =>
        {
            // UriImageSource shares cancellation state; concurrent portraits need separate instances.
            ((PortraitImage)bindable).Source = value is UriImageSource uri
                ? new UriImageSource
                {
                    Uri = uri.Uri,
                    CachingEnabled = uri.CachingEnabled,
                    CacheValidity = uri.CacheValidity
                }
                : (ImageSource?)value;
        });

    public ImageSource? PortraitSource
    {
        get => (ImageSource?)GetValue(PortraitSourceProperty);
        set => SetValue(PortraitSourceProperty, value);
    }

    void IImageSourcePartEvents.LoadingStarted() { }
    void IImageSourcePartEvents.LoadingCompleted(bool successful) { }
    void IImageSourcePartEvents.LoadingFailed(Exception exception)
    {
        var source = Source;
        var speaker = BindingContext as SpeakerViewModel;
        Dispatcher.Dispatch(() =>
        {
            if (speaker is not null && ReferenceEquals(BindingContext, speaker) && ReferenceEquals(Source, source))
                speaker.UseFallbackPicture(exception);
        });
    }
}
