using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetConf.Core;

public interface IConferenceRepository
{
    Task<ConferenceSnapshot> LoadAsync(CancellationToken cancellationToken = default);
    Task<ConferenceSnapshot> RefreshAsync(CancellationToken cancellationToken = default,
        IProgress<ConferenceSnapshot>? progress = null);
}

public sealed record ConferenceSnapshot(
    ConferenceDocument Document,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset? LastUpdatedAt,
    bool IsCached,
    IReadOnlyList<ConferenceDataIssue> Issues);

public sealed record ConferenceDataIssue(string Source, Exception Exception);

/// <summary>Cache-first reference data. Call LoadAsync before rendering, then RefreshAsync in the background.</summary>
public sealed class ConferenceRepository(
    IConferenceDataSource metadataSource, HttpClient httpClient, IConferenceCacheStore cacheStore, TimeProvider? timeProvider = null)
    : IConferenceRepository
{
    public ConferenceRepository(IConferenceDataSource metadataSource, HttpClient httpClient,
        string cachePath, TimeProvider? timeProvider = null)
        : this(metadataSource, httpClient, new FileConferenceCacheStore(cachePath), timeProvider) { }

    public const string MicrosoftSource = "Microsoft";
    public const string CommunitySource = "Sessionize";
    public const string CacheSource = "Cache";
    public const string MicrosoftUrl = "https://dotnet.microsoft.com/blob-assets/json/conf/conference-2026.json";
    private const int CacheSchemaVersion = 6;
    private const int MaximumFeedBytes = 4 * 1024 * 1024;
    private const int MaximumCacheBytes = 16 * 1024 * 1024;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object refreshLock = new();
    private Task<ConferenceSnapshot>? refresh;
    private ConferenceCache? state;
    private ConferenceSnapshot? snapshot;

    public async Task<ConferenceSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = Volatile.Read(ref snapshot);
        if (current is not null)
            return current;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            return snapshot!;
        }
        finally { gate.Release(); }
    }

    public Task<ConferenceSnapshot> RefreshAsync(CancellationToken cancellationToken = default,
        IProgress<ConferenceSnapshot>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (refreshLock)
        {
            if (refresh is null || refresh.IsCompleted)
            {
                refresh = RefreshCoreAsync(cancellationToken, progress);
                return refresh;
            }
            return refresh.WaitAsync(cancellationToken);
        }
    }

    private async Task LoadCoreAsync(CancellationToken token)
    {
        if (state is not null)
            return;
        var issues = new List<ConferenceDataIssue>();
        var migrate = false;
        ConferenceCache? legacy = null;
        try
        {
            var bytes = await cacheStore.ReadAsync(token).ConfigureAwait(false);
            if (bytes is null)
                throw new FileNotFoundException();
            if (bytes.Length > MaximumCacheBytes)
                throw new InvalidDataException("The agenda cache exceeds the size limit.");
            using var content = new MemoryStream(bytes, writable: false);
            var cached = await JsonSerializer.DeserializeAsync(content,
                RemoteAgendaJsonContext.Default.ConferenceCache, token).ConfigureAwait(false)
                ?? throw new InvalidDataException("The agenda cache is null.");
            if (cached.SchemaVersion is 1 or 2 or 3 or 4 or 5)
            {
                if (cached.SchemaVersion is 2 or 3 or 4 or 5)
                {
                    ValidateCache(cached, legacy: true);
                    legacy = cached with
                    {
                        Microsoft = SeparateLegacyProfiles(cached.Microsoft) with { RequiresProfileRefresh = true },
                        Community = SeparateLegacyProfiles(cached.Community!)
                    };
                }
                migrate = true;
            }
            else
            {
                ValidateCache(cached);
                try
                {
                    var metadata = await LoadMetadataAsync(token).ConfigureAwait(false);
                    var changed = metadata.StartDate != cached.Metadata.StartDate;
                    var updated = ApplyMetadata(cached, metadata);
                    ValidateCache(updated);
                    cached = updated;
                    if (changed)
                        await SaveAsync(cached, token).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsDataFailure(exception))
                {
                    issues.Add(new(CacheSource, exception));
                }
                state = cached;
                Publish(Snapshot(state, true, issues));
                return;
            }
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception exception) when (IsDataFailure(exception))
        {
            issues.Add(new(CacheSource, exception));
        }

        var document = await LoadMetadataAsync(token).ConfigureAwait(false);
        var seeded = new ConferenceCache
        {
            SchemaVersion = CacheSchemaVersion,
            Metadata = document,
            Microsoft = DownloadedOrEmpty(legacy?.Microsoft),
            Community = legacy is null ? null : DownloadedOrEmpty(legacy.Community)
        };
        try
        {
            seeded = LegacyAgendaMigration.Upgrade(seeded);
            ValidateCache(seeded);
        }
        catch (Exception exception) when (IsDataFailure(exception))
        {
            issues.Add(new(CacheSource, exception));
            migrate = false;
            seeded = new ConferenceCache
            {
                SchemaVersion = CacheSchemaVersion, Metadata = document,
                Microsoft = DownloadedOrEmpty(null)
            };
        }
        if (migrate)
        {
            // Discard untraceable legacy content and shipped seeds, never the separate bookmarks.
            try { await SaveAsync(seeded, token).ConfigureAwait(false); }
            catch (Exception exception) when (IsDataFailure(exception))
            {
                issues.Add(new(CacheSource, exception));
            }
        }
        state = seeded;
        Publish(Snapshot(state, legacy is not null, issues));
    }

    private async Task<ConferenceDocument> LoadMetadataAsync(CancellationToken token)
    {
        var document = await metadataSource.LoadAsync(token).ConfigureAwait(false);
        ConferenceJson.Validate(document);
        if (document.Sessions.Count != 0 || document.Speakers.Count != 0 || document.WithdrawnSpeakers.Count != 0 ||
            document.WithdrawnSessions.Count != 0 || document.CommunityEvents.Count != 0 || document.SessionAliases.Count != 0)
            throw new InvalidDataException("App metadata must not contain sessions, speaker profiles, aliases or community events.");
        if (document.StartDate is null)
            throw new InvalidDataException("Conference metadata must specify the published start date.");
        return document;
    }

    private static ConferenceCache ApplyMetadata(ConferenceCache cache, ConferenceDocument metadata)
    {
        if (metadata.StartDate == cache.Metadata.StartDate)
            return cache with { Metadata = metadata };
        var pacific = MicrosoftAgendaJson.GetPacificTimeZone();
        var oldStart = cache.Metadata.StartDate!.Value;
        var newStart = metadata.StartDate!.Value;
        DateTimeOffset Shift(DateTimeOffset value)
        {
            var oldLocal = TimeZoneInfo.ConvertTime(value, pacific);
            DateTime local;
            try
            {
                var day = DateOnly.FromDateTime(oldLocal.DateTime).DayNumber - oldStart.DayNumber;
                local = newStart.AddDays(day).ToDateTime(TimeOnly.FromDateTime(oldLocal.DateTime), DateTimeKind.Unspecified);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new InvalidDataException("The corrected conference start date exceeds the supported calendar.", exception);
            }
            if (pacific.IsInvalidTime(local) || pacific.IsAmbiguousTime(local))
                throw new InvalidDataException("The corrected conference start date produces an invalid or ambiguous Pacific time.");
            return new DateTimeOffset(local, pacific.GetUtcOffset(local));
        }
        Session ShiftSession(Session session) => session with { StartsAt = Shift(session.StartsAt), EndsAt = Shift(session.EndsAt) };
        return LegacyAgendaMigration.Resolve(cache with
        {
            Metadata = metadata,
            Microsoft = cache.Microsoft with
            {
                Sessions = cache.Microsoft.Sessions.Select(ShiftSession).ToList(),
                WithdrawnSessions = cache.Microsoft.WithdrawnSessions.Select(session =>
                    cache.LegacySessionIds.Contains(session.Id, StringComparer.Ordinal) ? session : ShiftSession(session)).ToList(),
                RequiresProfileRefresh = true
            }
        });
    }

    private static CachedAgenda DownloadedOrEmpty(CachedAgenda? agenda) =>
        agenda is { PayloadHash.Length: > 0, LastCheckedAt: not null }
            ? agenda : new() { Sessions = [], Speakers = [], PayloadHash = "" };

    private async Task<ConferenceSnapshot> RefreshCoreAsync(CancellationToken token, IProgress<ConferenceSnapshot>? progress)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await LoadCoreAsync(token).ConfigureAwait(false);
            var result = await FetchAsync(state!.Microsoft, state.Metadata, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var issues = snapshot!.Issues.Where(issue => issue.Source == CacheSource).ToList();
            if (result.Issue is not null)
                issues.Add(result.Issue);
            if (result.Agenda is null)
            {
                Publish(snapshot! with { Issues = issues.ToArray() });
                return snapshot!;
            }
            ConferenceCache next;
            try
            {
                next = LegacyAgendaMigration.Resolve(state! with { Microsoft = result.Agenda }, retireUnmatched: result.Issue is null);
                ValidateCache(next);
            }
            catch (Exception exception) when (IsDataFailure(exception))
            {
                issues.Add(new(MicrosoftSource, exception));
                Publish(snapshot! with { Issues = issues.ToArray() });
                return snapshot!;
            }
            state = next;
            Publish(Snapshot(next, false, issues.ToArray()));
            try
            {
                await SaveAsync(next, CancellationToken.None).ConfigureAwait(false);
                issues.RemoveAll(issue => issue.Source == CacheSource);
            }
            catch (Exception exception) when (IsDataFailure(exception))
            {
                issues.Add(new(CacheSource, exception));
            }
            Publish(Snapshot(next, false, issues.ToArray()));
            progress?.Report(snapshot!);
            token.ThrowIfCancellationRequested();
            return snapshot!;
        }
        finally { gate.Release(); }
    }

    private async Task<FetchResult> FetchAsync(
        CachedAgenda previous, ConferenceDocument metadata, CancellationToken token)
    {
        const string source = MicrosoftSource;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var requestToken = timeout.Token;
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    // Custom revalidation headers require a CORS preflight that the public feeds do not support.
                    var browser = OperatingSystem.IsBrowser();
                    using var request = new HttpRequestMessage(HttpMethod.Get,
                        browser ? $"{MicrosoftUrl}?refresh={Guid.NewGuid():N}" : MicrosoftUrl);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    if (!browser)
                    {
                        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
                        if (!previous.RequiresProfileRefresh && previous.ETag is not null)
                            request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(previous.ETag));
                        if (!previous.RequiresProfileRefresh && previous.LastModified is not null)
                            request.Headers.IfModifiedSince = previous.LastModified;
                    }
                    using var response = await httpClient.SendAsync(
                        request, HttpCompletionOption.ResponseHeadersRead, requestToken).ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.NotModified)
                    {
                        if (previous.PayloadHash.Length == 0 || previous.RequiresProfileRefresh)
                            throw new InvalidDataException("Received 304 without a previously validated remote payload.");
                        return new(source, previous with
                        {
                            LastCheckedAt = RetainedCheckTime(clock.GetUtcNow(), previous.LastUpdatedAt),
                            ETag = response.Headers.ETag?.ToString() ?? previous.ETag,
                            LastModified = response.Content.Headers.LastModified ?? previous.LastModified,
                            PendingReductionHash = null
                        }, null);
                    }
                    response.EnsureSuccessStatusCode();
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw new InvalidDataException("The agenda server did not return a complete document.");
                    if (response.Content.Headers.ContentLength > MaximumFeedBytes)
                        throw new InvalidDataException("The remote agenda exceeds the size limit.");
                    await using var body = await response.Content.ReadAsStreamAsync(requestToken).ConfigureAwait(false);
                    await using var content = await ReadBoundedAsync(body, MaximumFeedBytes, requestToken).ConfigureAwait(false);
                    var hash = Convert.ToHexString(SHA256.HashData(content.GetBuffer().AsSpan(0, (int)content.Length)));
                    var priorDocument = metadata with
                    {
                        Sessions = previous.Sessions, Speakers = previous.Speakers,
                        WithdrawnSessions = previous.WithdrawnSessions, WithdrawnSpeakers = previous.WithdrawnSpeakers
                    };
                    ConferenceDocument document;
                    if (hash == previous.PayloadHash && !previous.RequiresProfileRefresh)
                        document = priorDocument;
                    else
                    {
                        var profileFallback = priorDocument with { Speakers = AllSpeakers(previous), WithdrawnSpeakers = [] };
                        document = await MicrosoftAgendaJson.ReadAsync(content, profileFallback, requestToken).ConfigureAwait(false);
                        if (document.Sessions.Count == 0)
                            throw new InvalidDataException("An empty remote agenda cannot replace last-known-good sessions.");
                    }
                    document = ArchiveWithdrawals(document, previous);
                    ConferenceJson.Validate(document);
                    if ((long)document.Sessions.Count * 4 < (long)previous.Sessions.Count * 3 &&
                        hash != previous.PendingReductionHash)
                        return new(source, previous with { PendingReductionHash = hash }, new(source, new InvalidDataException(
                            $"The {source} agenda shrank from {previous.Sessions.Count} to {document.Sessions.Count} sessions. " +
                            "A reduction greater than 25% requires a second identical complete response before replacing last-known-good data.")));
                    var updated = previous with
                    {
                        Sessions = document.Sessions,
                        Speakers = document.Speakers,
                        WithdrawnSpeakers = document.WithdrawnSpeakers,
                        WithdrawnSessions = document.WithdrawnSessions,
                        PendingReductionHash = null,
                        RequiresProfileRefresh = false
                    };
                    var changed = Fingerprint(previous) != Fingerprint(updated);
                    var checkedAt = RetainedCheckTime(clock.GetUtcNow(), changed ? null : previous.LastUpdatedAt);
                    return new(source, updated with
                    {
                        PayloadHash = hash, ETag = response.Headers.ETag?.ToString(),
                        LastModified = response.Content.Headers.LastModified,
                        LastCheckedAt = checkedAt,
                        LastUpdatedAt = changed ? checkedAt : previous.LastUpdatedAt
                    }, null);
                }
                catch (HttpRequestException exception) when (attempt < 2 && IsTransient(exception))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * (1 << attempt) + Random.Shared.Next(25)),
                        requestToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (attempt < 2 && !requestToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * (1 << attempt)),
                        requestToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return new(source, null, new(source, new TimeoutException("The agenda request timed out.")));
        }
        catch (Exception exception) when (IsDataFailure(exception) || exception is HttpRequestException)
        {
            return new(source, IsDataFailure(exception) && previous.PendingReductionHash is not null
                ? previous with { PendingReductionHash = null } : null, new(source, exception));
        }
    }

    private static bool IsTransient(HttpRequestException exception) =>
        exception.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)exception.StatusCode >= 500;

    private static DateTimeOffset RetainedCheckTime(DateTimeOffset observed, DateTimeOffset? lastUpdatedAt) =>
        lastUpdatedAt is { } updated && updated > observed ? updated : observed;

    private static bool IsDataFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException;

    private static string Fingerprint(CachedAgenda agenda) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(agenda with
        {
            PayloadHash = "", ETag = null, LastModified = null, LastCheckedAt = null, LastUpdatedAt = null,
            PendingReductionHash = null, RequiresProfileRefresh = false
        }, RemoteAgendaJsonContext.Default.CachedAgenda)));

    private static ConferenceDocument ArchiveWithdrawals(ConferenceDocument document, CachedAgenda previous)
    {
        var activeIds = document.Sessions.Select(session => session.Id).ToHashSet(StringComparer.Ordinal);
        var withdrawn = previous.WithdrawnSessions.Concat(previous.Sessions)
            .Where(session => !activeIds.Contains(session.Id))
            .DistinctBy(session => session.Id, StringComparer.Ordinal).ToList();
        var neededSpeakers = withdrawn.SelectMany(session => session.SpeakerIds).ToHashSet(StringComparer.Ordinal);
        var currentSpeakerIds = document.Speakers.Select(speaker => speaker.Id).ToHashSet(StringComparer.Ordinal);
        return document with
        {
            WithdrawnSessions = withdrawn,
            WithdrawnSpeakers = [.. AllSpeakers(previous).Where(speaker =>
                neededSpeakers.Contains(speaker.Id) && !currentSpeakerIds.Contains(speaker.Id))]
        };
    }

    private static List<Speaker> AllSpeakers(CachedAgenda agenda) => [.. agenda.Speakers, .. agenda.WithdrawnSpeakers];

    private static CachedAgenda SeparateLegacyProfiles(CachedAgenda agenda)
    {
        var activeIds = agenda.Sessions.SelectMany(session => session.SpeakerIds).ToHashSet(StringComparer.Ordinal);
        var withdrawnIds = agenda.WithdrawnSessions.SelectMany(session => session.SpeakerIds).ToHashSet(StringComparer.Ordinal);
        var speakers = AllSpeakers(agenda);
        var archived = speakers.Where(speaker => !activeIds.Contains(speaker.Id) && withdrawnIds.Contains(speaker.Id)).ToList();
        return agenda with
        {
            Speakers = speakers.Where(speaker => activeIds.Contains(speaker.Id) || !withdrawnIds.Contains(speaker.Id)).ToList(),
            WithdrawnSpeakers = archived,
            // Older caches mixed upstream and archived profiles; a full parse resolves ambiguous membership.
            RequiresProfileRefresh = archived.Count != 0
        };
    }

    private static ConferenceDocument Combine(ConferenceCache cache)
    {
        var fallbackIds = cache.LegacyActiveSessionIds.ToHashSet(StringComparer.Ordinal);
        var fallback = cache.Microsoft.WithdrawnSessions.Where(session => fallbackIds.Contains(session.Id)).ToList();
        var fallbackSpeakers = fallback.SelectMany(session => session.SpeakerIds).ToHashSet(StringComparer.Ordinal);
        return cache.Metadata with
        {
            Speakers = [.. cache.Microsoft.Speakers, .. cache.Microsoft.WithdrawnSpeakers.Where(speaker => fallbackSpeakers.Contains(speaker.Id))],
            WithdrawnSpeakers = cache.Microsoft.WithdrawnSpeakers.Where(speaker => !fallbackSpeakers.Contains(speaker.Id)).ToList(),
            Sessions = [.. cache.Microsoft.Sessions, .. fallback],
            WithdrawnSessions = cache.Microsoft.WithdrawnSessions.Where(session => !fallbackIds.Contains(session.Id)).ToList(),
            SessionAliases = cache.SessionAliases
        };
    }

    private static void ValidateCache(ConferenceCache cache, bool legacy = false)
    {
        if ((legacy ? cache.SchemaVersion is not (2 or 3 or 4 or 5) : cache.SchemaVersion != CacheSchemaVersion) ||
            cache.Metadata is null || cache.Microsoft is null ||
            (legacy ? cache.Community is null : cache.Community is not null) ||
            cache.SessionAliases is null || cache.LegacySessionIds is null || cache.LegacyActiveSessionIds is null)
            throw new InvalidDataException("Unsupported or incomplete agenda cache.");
        ConferenceJson.Validate(cache.Metadata);
        ValidatePhotos(cache.Metadata.Speakers.Concat(cache.Metadata.WithdrawnSpeakers));
        if ((!legacy || cache.SchemaVersion >= 4) && (cache.Metadata.Sessions.Count != 0 || cache.Metadata.Speakers.Count != 0 ||
            cache.Metadata.WithdrawnSpeakers.Count != 0 ||
            cache.Metadata.WithdrawnSessions.Count != 0 || cache.Metadata.CommunityEvents.Count != 0))
            throw new InvalidDataException("Cached app metadata must not contain an agenda or community events.");
        if (!legacy && (cache.Metadata.StartDate is null || cache.Metadata.SessionAliases.Count != 0))
            throw new InvalidDataException("Cached metadata requires a start date and must not contain session aliases.");
        if (cache.LegacyPayloadHash is { } legacyHash &&
            (legacyHash.Length != 64 || !legacyHash.All(Uri.IsHexDigit)))
            throw new InvalidDataException("Legacy provenance requires a valid remote payload hash.");
        if (!legacy && cache.LegacySessionIds.Count > 0 && cache.LegacyPayloadHash is null)
            throw new InvalidDataException("Legacy archived sessions require validated remote provenance.");
        var sources = new List<(string Source, CachedAgenda Agenda)> { (MicrosoftSource, cache.Microsoft) };
        if (legacy)
            sources.Add((CommunitySource, cache.Community!));
        foreach (var (source, agenda) in sources)
        {
            if (agenda.Sessions is null || agenda.WithdrawnSessions is null || agenda.Speakers is null ||
                agenda.WithdrawnSpeakers is null || agenda.PayloadHash is null)
                throw new InvalidDataException($"{source} cache requires session/speaker collections and PayloadHash.");
            if (agenda.PendingReductionHash is { } pendingHash &&
                (pendingHash.Length != 64 || !pendingHash.All(Uri.IsHexDigit) || agenda.PayloadHash.Length == 0))
                throw new InvalidDataException($"{source} cache has an invalid pending reduction hash.");
            ConferenceJson.Validate(cache.Metadata with
            {
                Sessions = agenda.Sessions, Speakers = legacy && cache.SchemaVersion < 5 ? AllSpeakers(agenda) : agenda.Speakers,
                WithdrawnSpeakers = legacy && cache.SchemaVersion < 5 ? [] : agenda.WithdrawnSpeakers,
                WithdrawnSessions = agenda.WithdrawnSessions
            });
            if (!legacy)
            {
                var withdrawnSpeakerIds = agenda.WithdrawnSessions.SelectMany(session => session.SpeakerIds).ToHashSet(StringComparer.Ordinal);
                if (agenda.WithdrawnSpeakers.Any(speaker => speaker is null || !withdrawnSpeakerIds.Contains(speaker.Id)))
                    throw new InvalidDataException($"{source} cache has archived profiles without withdrawn sessions.");
            }
            if (agenda.PayloadHash.Length != 0 && (agenda.PayloadHash.Length != 64 || !agenda.PayloadHash.All(Uri.IsHexDigit)))
                throw new InvalidDataException($"{source} cache PayloadHash must be empty or a 64-character hexadecimal SHA-256 hash.");
            if (agenda.PayloadHash.Length != 0 && agenda.Sessions.Count == 0)
                throw new InvalidDataException($"{source} cache has a remote PayloadHash but no active sessions.");
            var legacyArchivesOnly = !legacy && cache.LegacyPayloadHash is not null &&
                agenda.WithdrawnSessions.All(session => cache.LegacySessionIds.Contains(session.Id, StringComparer.Ordinal));
            if ((!legacy || cache.SchemaVersion >= 4) && agenda.PayloadHash.Length == 0 && (agenda.Sessions.Count != 0 ||
                agenda.Speakers.Count != 0 || (!legacyArchivesOnly && (agenda.WithdrawnSpeakers.Count != 0 || agenda.WithdrawnSessions.Count != 0)) ||
                agenda.LastCheckedAt is not null || agenda.LastUpdatedAt is not null))
                throw new InvalidDataException($"{source} cache contains content without a validated remote payload.");
            if ((!legacy || cache.SchemaVersion >= 4) && agenda.PayloadHash.Length != 0 && agenda.LastCheckedAt is null)
                throw new InvalidDataException($"{source} cache has a remote payload without a check time.");
            if (agenda.PayloadHash.Length == 0 && (agenda.ETag is not null || agenda.LastModified is not null))
                throw new InvalidDataException($"{source} cache has ETag/LastModified validators without a remote PayloadHash.");
            if (agenda.ETag is not null && !EntityTagHeaderValue.TryParse(agenda.ETag, out _))
                throw new InvalidDataException($"{source} cache ETag is invalid.");
            if (agenda.LastUpdatedAt is not null && agenda.LastCheckedAt is null)
                throw new InvalidDataException($"{source} cache has LastUpdatedAt without LastCheckedAt.");
            if (agenda.LastUpdatedAt > agenda.LastCheckedAt)
                throw new InvalidDataException(
                    $"{source} cache LastUpdatedAt ({agenda.LastUpdatedAt:O}) exceeds LastCheckedAt ({agenda.LastCheckedAt:O}).");
            ValidatePhotos(AllSpeakers(agenda));
        }

        var document = Combine(cache);
        if (legacy)
            document = document with
            {
                Speakers = [.. AllSpeakers(cache.Microsoft), .. AllSpeakers(cache.Community!)],
                WithdrawnSpeakers = [],
                Sessions = [.. cache.Microsoft.Sessions, .. cache.Community!.Sessions],
                WithdrawnSessions = [.. cache.Microsoft.WithdrawnSessions, .. cache.Community.WithdrawnSessions]
            };
        ConferenceJson.Validate(document);
        if (!legacy && (cache.LegacySessionIds.Distinct(StringComparer.Ordinal).Count() != cache.LegacySessionIds.Count ||
            cache.LegacySessionIds.Any(id => !cache.Microsoft.WithdrawnSessions.Any(session => session.Id == id))))
            throw new InvalidDataException("Legacy session IDs must identify distinct retained withdrawn sessions.");
        if (!legacy && (cache.LegacyActiveSessionIds.Distinct(StringComparer.Ordinal).Count() != cache.LegacyActiveSessionIds.Count ||
            cache.LegacyActiveSessionIds.Any(id => !cache.LegacySessionIds.Contains(id, StringComparer.Ordinal))))
            throw new InvalidDataException("Legacy fallback IDs must identify distinct unresolved legacy sessions.");
    }

    private static void ValidatePhotos(IEnumerable<Speaker> speakers)
    {
        if (speakers.Any(speaker => !string.IsNullOrEmpty(speaker.ProfilePicture) &&
            SpeakerPhotoUrl.Normalize(speaker.ProfilePicture).Length == 0))
            throw new InvalidDataException("The agenda cache contains a prohibited speaker portrait URL.");
    }

    private static ConferenceSnapshot Snapshot(ConferenceCache cache, bool isCached, IReadOnlyList<ConferenceDataIssue> issues) =>
        new(Combine(cache),
            cache.Microsoft.LastCheckedAt, cache.Microsoft.LastUpdatedAt, isCached, issues);

    private void Publish(ConferenceSnapshot current) => Volatile.Write(ref snapshot, current);

    private async Task SaveAsync(ConferenceCache cache, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var content = JsonSerializer.SerializeToUtf8Bytes(cache, RemoteAgendaJsonContext.Default.ConferenceCache);
        if (content.Length > MaximumCacheBytes)
            throw new InvalidDataException("The agenda cache exceeds the size limit.");
        await cacheStore.WriteAsync(content, token).ConfigureAwait(false);
    }

    private static async Task<MemoryStream> ReadBoundedAsync(Stream stream, int maximumBytes, CancellationToken token)
    {
        var content = new MemoryStream();
        try
        {
            var buffer = new byte[16384];
            int count;
            while ((count = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
            {
                if (content.Length + count > maximumBytes)
                    throw new InvalidDataException("The agenda exceeds the size limit.");
                await content.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            }
            content.Position = 0;
            return content;
        }
        catch { content.Dispose(); throw; }
    }

    private sealed record FetchResult(string Source, CachedAgenda? Agenda, ConferenceDataIssue? Issue);
}

public sealed record ConferenceCache
{
    public required int SchemaVersion { get; init; }
    public required ConferenceDocument Metadata { get; init; }
    public required CachedAgenda Microsoft { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CachedAgenda? Community { get; init; }
    public Dictionary<string, string> SessionAliases { get; init; } = new(StringComparer.Ordinal);
    public List<string> LegacySessionIds { get; init; } = [];
    public List<string> LegacyActiveSessionIds { get; init; } = [];
    public string? LegacyPayloadHash { get; init; }
}

public sealed record CachedAgenda
{
    public required List<Session> Sessions { get; init; }
    public required List<Speaker> Speakers { get; init; }
    public List<Speaker> WithdrawnSpeakers { get; init; } = [];
    public List<Session> WithdrawnSessions { get; init; } = [];
    public required string PayloadHash { get; init; }
    public string? PendingReductionHash { get; init; }
    public bool RequiresProfileRefresh { get; init; }
    public string? ETag { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public DateTimeOffset? LastCheckedAt { get; init; }
    public DateTimeOffset? LastUpdatedAt { get; init; }
}
