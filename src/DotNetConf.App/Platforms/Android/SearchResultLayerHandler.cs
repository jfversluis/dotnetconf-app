using Microsoft.Maui.Handlers;

namespace DotNetConf.App.Platforms.Android;

public sealed class SearchResultLayerHandler : ContentViewHandler
{
    // Keep the container stable when input transparency changes around a live RecyclerView.
    public override bool NeedsContainer => true;
}
