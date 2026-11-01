namespace DotNetConf.App.Services;

public interface IFoldableRegionSource
{
    Rect WindowRegion { get; }
    event EventHandler? Changed;
}
