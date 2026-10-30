using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Controls;

public sealed class PortraitImage : Image, IImageSourcePartEvents
{
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
