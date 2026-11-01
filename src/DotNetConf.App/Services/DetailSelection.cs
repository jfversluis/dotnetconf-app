namespace DotNetConf.App.Services;

public enum DetailKind { Session, Speaker }

public sealed record DetailDestination(DetailKind Kind, string Id)
{
    public string Route => Kind == DetailKind.Session ? "session-detail" : "speaker-detail";
}

public sealed class DetailSelection(DetailKind listKind)
{
    public DetailKind ListKind { get; } = listKind;
    public DetailDestination? Current { get; private set; }
    public string? ListItemId { get; private set; }

    public void Select(DetailDestination destination)
    {
        Current = destination;
        if (destination.Kind == ListKind)
            ListItemId = destination.Id;
    }
}
