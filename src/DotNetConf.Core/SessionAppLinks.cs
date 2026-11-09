namespace DotNetConf.Core;

public static class SessionAppLinks
{
    public const string Scheme = "dotnetconf";
    public const string ReminderPrefix = "session-reminder:";

    public static string Create(string sessionId)
    {
        ValidateId(sessionId);
        return $"{Scheme}://session/{Uri.EscapeDataString(sessionId)}";
    }

    public static string Parse(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) ||
            uri.Scheme != Scheme || uri.Host != "session" || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new UriFormatException("Expected a companion session link.");
        var pathStart = link.IndexOf('/', link.IndexOf("://", StringComparison.Ordinal) + 3);
        if (pathStart < 0)
            throw new UriFormatException("A session identifier is required.");
        var id = Uri.UnescapeDataString(link[(pathStart + 1)..]);
        ValidateId(id);
        return id;
    }

    public static void ValidateId(string sessionId)
    {
        if (!IsValidId(sessionId))
            throw new UriFormatException("The session identifier is not a valid path segment.");
    }

    public static bool IsValidId(string? sessionId) =>
        !string.IsNullOrWhiteSpace(sessionId) && sessionId.Length <= 256 &&
        sessionId == sessionId.Trim() && sessionId is not "." and not ".." &&
        !sessionId.Any(char.IsControl) && sessionId.IndexOfAny(['/', '\\', '?', '#', '%']) < 0;
}
