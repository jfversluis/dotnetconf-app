using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotNetConf.Core;

public sealed record ConferenceLink(string Title, string Url);

public static partial class ConferenceLinks
{
    public const string Website = "https://dot.net/conf";
    public const string Registration = "https://aka.ms/dotnetconf2026";
    public const string YouTube = "https://www.youtube.com/dotnet";
    public const string Hashtag = "#dotnetconf";
    public static IReadOnlyList<ConferenceLink> SocialChannels { get; } =
    [
        new(".NET on YouTube", YouTube),
        new(".NET on X", "https://twitter.com/dotnet"),
        new(".NET on Bluesky", "https://bsky.app/profile/dot.net"),
        new(".NET LinkedIn group", "https://www.linkedin.com/groups/40949/")
    ];

    public static string SessionUrl(string id) => PublishedSessionId().IsMatch(id)
        ? $"https://dotnet.microsoft.com/en-us/conf/2026/agenda/{id.ToLowerInvariant()}"
        : Website;

    public static bool IsSafeUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo) &&
        !uri.IsLoopback && Uri.CheckHostName(uri.Host) == UriHostNameType.Dns;

    public static bool IsYouTubeUrl(string value)
    {
        if (!IsSafeUrl(value))
            return false;
        var uri = new Uri(value);
        if (uri.IdnHost.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
            return VideoId().IsMatch(uri.AbsolutePath.Trim('/'));
        if (uri.IdnHost.ToLowerInvariant() is not ("youtube.com" or "www.youtube.com" or "m.youtube.com"))
            return false;
        if (uri.AbsolutePath == "/watch")
        {
            var video = uri.Query.TrimStart('?').Split('&').Where(part => part.StartsWith("v=", StringComparison.Ordinal)).ToArray();
            return video.Length == 1 && VideoId().IsMatch(video[0][2..]);
        }
        var path = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return path.Length == 2 && path[0] is "embed" or "live" or "shorts" && VideoId().IsMatch(path[1]);
    }

    internal static string ReadRecording(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return "";
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("The published recording must be a YouTube URL.");
        var text = value.GetString()!.Trim();
        if (IsUnpublished(text))
            return "";
        if (!IsYouTubeUrl(text))
            throw new InvalidDataException("The published recording must be an absolute HTTPS YouTube URL.");
        return text;
    }

    internal static List<ConferenceLink> ReadPublished(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return [];
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().SelectMany(ReadPublished).DistinctBy(link => link.Url).ToList();
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Published links must contain URL strings.");
        var text = value.GetString()!.Trim();
        if (IsUnpublished(text))
            return [];
        var matches = PublishedLink().Matches(text);
        if (matches.Count == 0 || !string.IsNullOrWhiteSpace(PublishedLink().Replace(text, "").Trim(',', ';', '|', ' ', '\r', '\n')))
            throw new InvalidDataException("Published links must be HTTPS URLs or labeled Markdown links.");
        return matches.Select(match =>
        {
            var url = match.Groups["url"].Value;
            if (!IsSafeUrl(url))
                throw new InvalidDataException("Published links must be absolute HTTPS URLs without credentials.");
            var label = match.Groups["title"].Value.Trim();
            return new ConferenceLink(label.Length == 0 ? new Uri(url).Host : label, url);
        }).DistinctBy(link => link.Url).ToList();
    }

    private static bool IsUnpublished(string value) => value.Length == 0 ||
        value.Equals("*No response*", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("No response", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("TBD", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("TODO", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^(?:KEY|BRK|CLT)\d{3}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PublishedSessionId();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant)]
    private static partial Regex VideoId();

    [GeneratedRegex(@"\[(?<title>[^\]\r\n]+)\]\((?<url>https://[^\s)]+)\)|(?<url>https://[^\s<>()\[\]]+)", RegexOptions.CultureInvariant)]
    private static partial Regex PublishedLink();
}
