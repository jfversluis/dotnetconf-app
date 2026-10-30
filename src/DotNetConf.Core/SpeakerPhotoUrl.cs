namespace DotNetConf.Core;

public static class SpeakerPhotoUrl
{
    public static string Normalize(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0)
            return "";
        string host;
        // TryCreate can accept Unicode hosts that fail subsequent IDN validation.
        try { host = uri.IdnHost.TrimEnd('.'); }
        catch (UriFormatException) { return ""; }
        foreach (var blocked in new[] { "github.com", "githubusercontent.com", "githubassets.com", "github.io" })
            if (host.Equals(blocked, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + blocked, StringComparison.OrdinalIgnoreCase))
                return "";
        if (host.StartsWith("github-", StringComparison.OrdinalIgnoreCase) &&
            host.EndsWith(".amazonaws.com", StringComparison.OrdinalIgnoreCase))
            return "";
        return uri.AbsoluteUri;
    }
}
