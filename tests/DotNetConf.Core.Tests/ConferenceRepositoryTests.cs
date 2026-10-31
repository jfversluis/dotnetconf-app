using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotNetConf.Core;
using static DotNetConf.Core.Tests.RemoteAgendaTests;

namespace DotNetConf.Core.Tests;

// Test-only agenda prose below is synthetic.
public sealed class ConferenceRepositoryTests : IDisposable
{
    private const string LegacyId = "1234";
    private const string TalkId = "BRK050";
    private const string TalkCanonicalId = "20261050";
    private readonly string directory = Path.Combine(
        Directory.GetCurrentDirectory(), ".agenda-cache-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeClock clock = new();
    private readonly MetadataSource metadata = new();
    private readonly Handler handler = new();
    private readonly HttpClient client;
    private string CachePath => Path.Combine(directory, "agenda.json");

    public ConferenceRepositoryTests() => client = new(handler);
    private ConferenceRepository Repository(string? path = null) => new(metadata, client, path ?? CachePath, clock);

    public void Dispose()
    {
        client.Dispose();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private static HttpResponseMessage Ok(string json, string? etag = "\"v1\"")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (etag is not null)
            response.Headers.ETag = EntityTagHeaderValue.Parse(etag);
        response.Content.Headers.LastModified = DateTimeOffset.Parse("2026-10-28T12:00:00Z");
        response.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = TimeSpan.FromDays(365) };
        return response;
    }

    private static HttpResponseMessage DefaultResponse(HttpRequestMessage request) => Ok(Json(SizedMicrosoft(70)));

    private static HttpResponseMessage NotModified() => new(HttpStatusCode.NotModified);

    private void Serve(MicrosoftAgenda agenda, string? etag = "\"v1\"") =>
        handler.Respond = (_, _) => Task.FromResult(Ok(Json(agenda), etag));

    private async Task<ConferenceCache> ReadCacheAsync() =>
        JsonSerializer.Deserialize(await File.ReadAllTextAsync(CachePath), RemoteAgendaJsonContext.Default.ConferenceCache)!;

    private async Task WriteCacheAsync(ConferenceCache cache)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(CachePath, JsonSerializer.Serialize(cache, RemoteAgendaJsonContext.Default.ConferenceCache));
    }

    private void AssertOnlyMicrosoftRequests()
    {
        Assert.All(handler.Requests, url =>
        {
            Assert.StartsWith(ConferenceRepository.MicrosoftUrl, url);
            Assert.DoesNotContain("sessionize", url, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static MicrosoftAgenda SizedMicrosoft(int count, int days = 5)
    {
        var data = RemoteAgendaTests.Microsoft;
        return data with
        {
            Sessions = Enumerable.Range(1, count).Select(number => data.Sessions[0] with
            {
                Id = $"BRK{number:D3}", Title = $"Microsoft session {number}", Day = (number - 1) * days / count + 1
            }).ToList()
        };
    }

    // Replaces or adds one talk with a published speaker so legacy records can be matched by title, time and speaker.
    private static MicrosoftAgenda WithTalk(MicrosoftAgenda data, string id = TalkId, string title = "Community",
        string speakerName = "Community Speaker", string start = "08:00:00", string end = "08:30:00", int day = 3)
    {
        var talk = data.Sessions[0] with
        {
            Id = id, Title = title, Day = day, StartTime = start, EndTime = end, SpeakerIds = ["speaker-community"]
        };
        return data with
        {
            Sessions = [.. data.Sessions.Where(session => session.Id != id), talk],
            Speakers =
            [
                .. data.Speakers.Where(speaker => speaker.Id != "speaker-community"),
                new() { Id = "speaker-community", Name = speakerName, PrimaryBio = "Synthetic test biography" }
            ]
        };
    }

    private static Session LegacyTalk(string id = LegacyId, string title = "Community",
        string start = "2026-11-12T08:00:00-08:00", string end = "2026-11-12T08:30:00-08:00",
        string speaker = "legacy-speaker") => new()
    {
        Id = id, Title = title, Description = "Synthetic legacy abstract", StartsAt = DateTimeOffset.Parse(start),
        EndsAt = DateTimeOffset.Parse(end), SpeakerIds = [speaker]
    };

    private static Speaker LegacySpeaker(string id = "legacy-speaker", string name = "Community Speaker") =>
        new() { Id = id, Name = name, Bio = "Synthetic legacy biography" };

    private CachedAgenda LegacySource(IEnumerable<Session> active, IEnumerable<Session>? withdrawn = null,
        IEnumerable<Speaker>? speakers = null, IEnumerable<Speaker>? withdrawnSpeakers = null) => new()
    {
        Sessions = [.. active], WithdrawnSessions = [.. withdrawn ?? []], Speakers = [.. speakers ?? []],
        WithdrawnSpeakers = [.. withdrawnSpeakers ?? []], PayloadHash = new string('A', 64), ETag = "\"legacy\"",
        LastCheckedAt = clock.Now.AddHours(10), LastUpdatedAt = clock.Now.AddHours(9)
    };

    // Writes a schema 2-5 cache holding a downloaded Microsoft agenda and the retired second source.
    private async Task<ConferenceCache> SeedLegacyAsync(MicrosoftAgenda feed, CachedAgenda community, int schema = 5)
    {
        Serve(feed);
        await Repository().RefreshAsync();
        var current = await ReadCacheAsync();
        var legacySpeakers = schema == 5 ? community : community with
        {
            Speakers = [.. community.Speakers, .. community.WithdrawnSpeakers], WithdrawnSpeakers = []
        };
        var legacy = current with { SchemaVersion = schema, Community = legacySpeakers };
        await WriteCacheAsync(legacy);
        handler.Requests.Clear();
        return legacy;
    }

    private static string[] Ids(IEnumerable<Session> sessions) => [.. sessions.Select(session => session.Id)];

    [Fact]
    public async Task FirstRefreshUsesOnlyMicrosoftAndPersistsAllSeventyTalksInSchemaSix()
    {
        var result = await Repository().RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(70, result.Document.Sessions.Count);
        Assert.Empty(result.Document.WithdrawnSessions);
        Assert.Empty(result.Document.SessionAliases);
        Assert.Equal(clock.Now, result.LastCheckedAt);
        Assert.Equal(clock.Now, result.LastUpdatedAt);
        Assert.Single(handler.Requests);
        AssertOnlyMicrosoftRequests();
        var cache = await ReadCacheAsync();
        Assert.Equal(6, cache.SchemaVersion);
        Assert.Equal(70, cache.Microsoft.Sessions.Count);
        Assert.Equal(64, cache.Microsoft.PayloadHash.Length);
        Assert.Null(cache.Community);
        Assert.Empty(cache.SessionAliases);
        Assert.Null(cache.LegacyPayloadHash);
        Assert.Null(JsonNode.Parse(await File.ReadAllTextAsync(CachePath))!["community"]);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(12)]
    public async Task ConferenceDaysComeFromTheRecordsNotAFixedFourDayLimit(int days)
    {
        Serve(SizedMicrosoft(70, days));
        var result = await Repository().RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(70, result.Document.Sessions.Count);
        var dates = result.Document.Sessions.Select(session => DateOnly.FromDateTime(session.StartsAt.Date)).Distinct().Order().ToList();
        Assert.Equal(Enumerable.Range(0, days).Select(offset => new DateOnly(2026, 11, 10).AddDays(offset)), dates);
        Assert.All(result.Document.Sessions, session => Assert.Equal(TimeSpan.FromMinutes(30), session.EndsAt - session.StartsAt));
        var restart = await Repository().LoadAsync();
        Assert.Equal(days, restart.Document.Sessions.Select(session => session.StartsAt.Date).Distinct().Count());
    }

    [Fact]
    public async Task LaterDaysAreAddedWithoutChangingExistingIdsOrWithdrawingTalks()
    {
        var repository = Repository();
        Serve(SizedMicrosoft(40, 4));
        var first = await repository.RefreshAsync();
        clock.Advance();
        var extended = SizedMicrosoft(40, 4);
        Serve(extended with
        {
            Sessions = [.. extended.Sessions, extended.Sessions[0] with { Id = "BRK101", Title = "Synthetic day six", Day = 6 }]
        }, "\"six-days\"");
        var updated = await repository.RefreshAsync();
        Assert.Empty(updated.Issues);
        Assert.Empty(updated.Document.WithdrawnSessions);
        Assert.Equal(41, updated.Document.Sessions.Count);
        Assert.Equal(Ids(first.Document.Sessions), Ids(updated.Document.Sessions.Take(40)));
        Assert.Equal(new DateOnly(2026, 11, 15), DateOnly.FromDateTime(updated.Document.Sessions.Single(s => s.Id == "20261101").StartsAt.Date));
        Assert.Equal(clock.Now, updated.LastUpdatedAt);
        Assert.Equivalent(updated.Document, (await Repository().LoadAsync()).Document with { Speakers = updated.Document.Speakers });
    }

    [Fact]
    public async Task FirstLoadContainsOnlyMetadataWithoutNetworkOrCacheWrite()
    {
        var result = await Repository().LoadAsync();
        Assert.Empty(result.Document.Sessions);
        Assert.Empty(result.Document.Speakers);
        Assert.Empty(result.Document.WithdrawnSessions);
        Assert.Empty(result.Document.SessionAliases);
        Assert.Empty(result.Issues);
        Assert.False(result.IsCached);
        Assert.Null(result.LastCheckedAt);
        Assert.Null(result.LastUpdatedAt);
        Assert.Empty(handler.Requests);
        Assert.Equal(1, metadata.Calls);
        Assert.False(File.Exists(CachePath));
    }

    [Fact]
    public async Task FreshOfflineRefreshShowsHonestEmptyStateWithOneMicrosoftIssue()
    {
        handler.Respond = (_, _) => throw new HttpRequestException("Offline");
        var result = await Repository().RefreshAsync();
        Assert.Empty(result.Document.Sessions);
        Assert.Empty(result.Document.Speakers);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(ConferenceRepository.MicrosoftSource, issue.Source);
        Assert.Null(result.LastCheckedAt);
        Assert.False(File.Exists(CachePath));
    }

    [Fact]
    public async Task NoAgendaProfilesOrCommunityEventsCanArriveWithTheAppMetadata()
    {
        metadata.Document = Bundle;
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Repository().LoadAsync());
        Assert.Contains("must not contain", error.Message);
        Assert.Empty(handler.Requests);
        Assert.False(File.Exists(CachePath));

        await using var fixture = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var examples = await ConferenceJson.ReadAsync(fixture);
        metadata.Document = new MetadataSource().Document with { CommunityEvents = examples.CommunityEvents };
        Assert.Contains("community events", (await Assert.ThrowsAsync<InvalidDataException>(() => Repository().LoadAsync())).Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task InvalidMetadataFailsLoudlyRatherThanInventingEmptySuccess()
    {
        metadata.Document = metadata.Document with { SchemaVersion = 999 };
        await Assert.ThrowsAsync<InvalidDataException>(() => Repository().LoadAsync());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RestartLoadsTheCacheOnceReadingMetadataButMakingNoNetworkRequests()
    {
        var result = await Repository().RefreshAsync();
        var requests = handler.Requests.Count;
        var calls = metadata.Calls;
        var restart = await Repository().LoadAsync();
        Assert.True(restart.IsCached);
        Assert.Empty(restart.Issues);
        Assert.Equal(Ids(result.Document.Sessions), Ids(restart.Document.Sessions));
        Assert.Equal(result.LastCheckedAt, restart.LastCheckedAt);
        Assert.Equal(requests, handler.Requests.Count);
        Assert.Equal(calls + 1, metadata.Calls);
        await Repository().LoadAsync();
        Assert.Equal(calls + 2, metadata.Calls);
    }

    private static DateOnly FirstDate(ConferenceSnapshot snapshot) =>
        snapshot.Document.Sessions.Select(session => DateOnly.FromDateTime(session.StartsAt.Date)).Min();

    private void CorrectStartDate(DateOnly date) => metadata.Document = metadata.Document with { StartDate = date };

    [Fact]
    public async Task CorrectedStartDateRebasesCachedTimesOfflineAndPersistsWithoutNetwork()
    {
        await Repository().RefreshAsync();
        handler.Requests.Clear();
        handler.Respond = (_, _) => throw new HttpRequestException("Offline");
        CorrectStartDate(new DateOnly(2026, 11, 11));
        var calls = metadata.Calls;
        var result = await Repository().LoadAsync();
        Assert.Empty(result.Issues);
        Assert.Empty(handler.Requests);
        Assert.Equal(calls + 1, metadata.Calls);
        Assert.Equal(70, result.Document.Sessions.Count);
        Assert.Equal(new DateOnly(2026, 11, 11), FirstDate(result));
        Assert.Equal(new DateOnly(2026, 11, 15), result.Document.Sessions.Max(session => DateOnly.FromDateTime(session.StartsAt.Date)));
        Assert.All(result.Document.Sessions, session =>
        {
            Assert.Equal(new TimeSpan(11, 30, 0), session.StartsAt.TimeOfDay);
            Assert.Equal(TimeSpan.FromHours(-8), session.StartsAt.Offset);
            Assert.Equal(TimeSpan.FromMinutes(30), session.EndsAt - session.StartsAt);
        });
        var cache = await ReadCacheAsync();
        Assert.Equal(new DateOnly(2026, 11, 11), cache.Metadata.StartDate);
        Assert.True(cache.Microsoft.RequiresProfileRefresh);
        Assert.Equal(new DateOnly(2026, 11, 11), FirstDate(await Repository().LoadAsync()));
    }

    [Fact]
    public async Task CorrectedStartDateForcesFullReparseEvenWhenPayloadHashAndValidatorsAreUnchanged()
    {
        await Repository().RefreshAsync();
        CorrectStartDate(new DateOnly(2026, 11, 11));
        handler.Requests.Clear();
        var repository = Repository();
        await repository.LoadAsync();
        var original = (await ReadCacheAsync()).Microsoft;
        clock.Advance();
        var validators = new List<bool>();
        handler.Respond = (request, _) =>
        {
            var conditional = request.Headers.IfNoneMatch.Count != 0 || request.Headers.IfModifiedSince is not null;
            validators.Add(conditional);
            return Task.FromResult(conditional ? NotModified() : Ok(Json(SizedMicrosoft(70))));
        };
        var result = await repository.RefreshAsync();
        Assert.Equal([false], validators);
        Assert.Empty(result.Issues);
        Assert.Equal(new DateOnly(2026, 11, 11), FirstDate(result));
        var cache = await ReadCacheAsync();
        Assert.Equal(original.PayloadHash, cache.Microsoft.PayloadHash);
        Assert.Equal(original.ETag, cache.Microsoft.ETag);
        Assert.False(cache.Microsoft.RequiresProfileRefresh);

        clock.Advance();
        await repository.RefreshAsync();
        Assert.Equal([false, true], validators);
    }

    [Fact]
    public async Task LegacyCacheWithKnownStartDateIsRebasedBeforeMigration()
    {
        var community = LegacySource([LegacyTalk()], speakers: [LegacySpeaker()]);
        var legacy = await SeedLegacyAsync(SizedMicrosoft(70), community);
        Assert.Equal(new DateOnly(2026, 11, 10), legacy.Metadata.StartDate);
        CorrectStartDate(new DateOnly(2026, 11, 11));
        handler.Respond = (_, _) => throw new HttpRequestException("Offline");

        var result = await Repository().LoadAsync();

        Assert.Empty(result.Issues);
        Assert.Equal(new DateOnly(2026, 11, 11), FirstDate(result));
        var migrated = await ReadCacheAsync();
        Assert.Equal(new DateOnly(2026, 11, 11), migrated.Metadata.StartDate);
        Assert.True(migrated.Microsoft.RequiresProfileRefresh);
    }

    [Fact]
    public async Task LegacyCacheWithoutDateBasisRequiresAnUnconditionalAgendaRevalidation()
    {
        var community = LegacySource([LegacyTalk()], speakers: [LegacySpeaker()]);
        var legacy = await SeedLegacyAsync(SizedMicrosoft(70), community);
        await WriteCacheAsync(legacy with
        {
            Metadata = legacy.Metadata with { StartDate = null }
        });
        handler.Respond = (_, _) => throw new HttpRequestException("Offline");

        var result = await Repository().LoadAsync();

        Assert.Equal(71, result.Document.Sessions.Count);
        var migrated = await ReadCacheAsync();
        Assert.True(migrated.Microsoft.RequiresProfileRefresh);
        Assert.Equal(new DateOnly(2026, 11, 10), migrated.Metadata.StartDate);
    }

    [Fact]
    public async Task CorrectedStartDateKeepsUnresolvedLegacyTimesButRebasesMicrosoftArchives()
    {
        var community = LegacySource([LegacyTalk()], speakers: [LegacySpeaker()]);
        await SeedLegacyAsync(SizedMicrosoft(70), community);
        await Repository().LoadAsync();
        CorrectStartDate(new DateOnly(2026, 11, 12));
        var result = await Repository().LoadAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(new DateOnly(2026, 11, 12), FirstDate(result));
        Assert.Empty(result.Document.WithdrawnSessions);
        var legacy = result.Document.Sessions.Single(session => session.Id == LegacyId);
        Assert.Equal(DateTimeOffset.Parse("2026-11-12T08:00:00-08:00"), legacy.StartsAt);
        Assert.Equal([LegacyId], (await ReadCacheAsync()).LegacySessionIds);
    }

    [Fact]
    public async Task LegacyMatchingUsesCanonicalUnicodeEquivalenceAcrossTitlesAndSpeakerNames()
    {
        var feed = WithTalk(SizedMicrosoft(70), title: "Community Caf\u00e9", speakerName: "Synthetic Andr\u00e9");
        var legacy = LegacyTalk(title: "Community Cafe\u0301");
        await SeedLegacyAsync(feed, LegacySource([legacy], speakers: [LegacySpeaker(name: "Synthetic Andre\u0301")]));

        var result = await Repository().LoadAsync();

        Assert.Empty(result.Issues);
        Assert.Equal(feed.Sessions.Count, result.Document.Sessions.Count);
        Assert.Equal(TalkCanonicalId, result.Document.SessionAliases[LegacyId]);
    }

    [Fact]
    public async Task LegacyMatchingDoesNotGuessCompatibilityOnlyUnicodeEquivalence()
    {
        var feed = WithTalk(SizedMicrosoft(70));
        await SeedLegacyAsync(feed, LegacySource([LegacyTalk(title: "\uFF23ommunity")], speakers: [LegacySpeaker()]));

        var result = await Repository().LoadAsync();

        Assert.Empty(result.Issues);
        Assert.Empty(result.Document.SessionAliases);
        Assert.Equal(feed.Sessions.Count + 1, result.Document.Sessions.Count);
    }

    [Fact]
    public async Task CorrectedStartDateResolvesNewlyMatchingLegacyFallbackWithoutNetwork()
    {
        var feed = WithTalk(SizedMicrosoft(70));
        var legacy = LegacyTalk(start: "2026-11-13T08:00:00-08:00", end: "2026-11-13T08:30:00-08:00");
        await SeedLegacyAsync(feed, LegacySource([legacy], speakers: [LegacySpeaker()]));
        var before = await Repository().LoadAsync();
        Assert.Equal(feed.Sessions.Count + 1, before.Document.Sessions.Count);
        Assert.Empty(before.Document.SessionAliases);

        CorrectStartDate(new DateOnly(2026, 11, 11));
        handler.Requests.Clear();
        handler.Respond = (_, _) => throw new HttpRequestException("Offline");
        var corrected = await Repository().LoadAsync();

        Assert.Empty(corrected.Issues);
        Assert.Empty(handler.Requests);
        Assert.Equal(feed.Sessions.Count, corrected.Document.Sessions.Count);
        Assert.Equal(TalkCanonicalId, corrected.Document.SessionAliases[LegacyId]);
        Assert.DoesNotContain(corrected.Document.Sessions, session => session.Id == LegacyId);
        Assert.Empty((await ReadCacheAsync()).LegacyActiveSessionIds);
        Assert.Equal(corrected.Document.SessionAliases, (await Repository().LoadAsync()).Document.SessionAliases);
    }

    [Fact]
    public async Task InvalidCorrectedStartDateRetainsExactLastGoodFileAndAgenda()
    {
        var feed = SizedMicrosoft(70);
        feed = feed with { Sessions = [.. feed.Sessions.Select(session => session with { StartTime = "01:30:00", EndTime = "02:00:00" })] };
        Serve(feed);
        var first = await Repository().RefreshAsync();
        var bytes = await File.ReadAllBytesAsync(CachePath);
        // 2026-11-01 01:30 occurs twice in Pacific time.
        CorrectStartDate(new DateOnly(2026, 11, 1));
        var result = await Repository().LoadAsync();
        var issue = Assert.Single(result.Issues);
        Assert.Equal(ConferenceRepository.MetadataSource, issue.Source);
        Assert.IsType<InvalidDataException>(issue.Exception);
        Assert.Equal(Ids(first.Document.Sessions), Ids(result.Document.Sessions));
        Assert.Equal(first.Document.Sessions.Select(s => s.StartsAt), result.Document.Sessions.Select(s => s.StartsAt));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(CachePath));
        Assert.True(result.IsCached);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnusableBundledMetadataWarningSurvivesASuccessfulCacheWrite(bool missingStartDate)
    {
        await Repository().RefreshAsync();
        var bytes = await File.ReadAllBytesAsync(CachePath);
        metadata.Document = missingStartDate
            ? metadata.Document with { StartDate = null } : metadata.Document with { SchemaVersion = 999 };
        handler.Requests.Clear();
        var repository = Repository();
        var result = await repository.LoadAsync();
        var issue = Assert.Single(result.Issues);
        Assert.Equal(ConferenceRepository.MetadataSource, issue.Source);
        Assert.Equal(70, result.Document.Sessions.Count);
        Assert.True(result.IsCached);
        Assert.Empty(handler.Requests);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(CachePath));

        clock.Advance();
        handler.Respond = (_, _) => Task.FromResult(NotModified());
        var refreshed = await repository.RefreshAsync();
        Assert.Equal(ConferenceRepository.MetadataSource, Assert.Single(refreshed.Issues).Source);
        var checkedCache = await ReadCacheAsync();
        Assert.True(checkedCache.Microsoft.LastCheckedAt > JsonSerializer.Deserialize(
            bytes, RemoteAgendaJsonContext.Default.ConferenceCache)!.Microsoft.LastCheckedAt);
    }

    [Fact]
    public async Task UTCOverflowFromCorrectedStartDateRetainsLastGoodCacheAsMetadataIssue()
    {
        await Repository().RefreshAsync();
        var cache = await ReadCacheAsync();
        var first = cache.Microsoft.Sessions[0];
        var sessions = cache.Microsoft.Sessions.ToList();
        sessions[0] = first with
        {
            StartsAt = DateTimeOffset.Parse("2026-11-10T16:30:00-08:00"),
            EndsAt = DateTimeOffset.Parse("2026-11-10T17:00:00-08:00")
        };
        await WriteCacheAsync(cache with { Microsoft = cache.Microsoft with { Sessions = sessions } });
        var bytes = await File.ReadAllBytesAsync(CachePath);
        CorrectStartDate(new DateOnly(9999, 12, 31));

        var result = await Repository().LoadAsync();

        var issue = Assert.Single(result.Issues);
        Assert.Equal(ConferenceRepository.MetadataSource, issue.Source);
        Assert.IsType<InvalidDataException>(issue.Exception);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(CachePath));
    }

    [Fact]
    public async Task StartDateCorrectionPersistenceFailureKeepsRebasedAgendaInMemoryWithAWarning()
    {
        var store = new MemoryCacheStore();
        await new ConferenceRepository(metadata, client, store, clock).RefreshAsync();
        var bytes = store.Content!.ToArray();
        CorrectStartDate(new DateOnly(2026, 11, 11));
        store.WriteException = new IOException("read only");
        handler.Requests.Clear();
        var repository = new ConferenceRepository(metadata, client, store, clock);
        var result = await repository.LoadAsync();
        var issue = Assert.Single(result.Issues);
        Assert.Equal(ConferenceRepository.CacheSource, issue.Source);
        Assert.Equal(new DateOnly(2026, 11, 11), FirstDate(result));
        Assert.Equal(70, result.Document.Sessions.Count);
        Assert.Equal(bytes, store.Content);
        Assert.Empty(handler.Requests);

        clock.Advance();
        handler.Respond = (request, _) =>
        {
            Assert.Empty(request.Headers.IfNoneMatch);
            return Task.FromResult(Ok(Json(SizedMicrosoft(70))));
        };
        var refreshed = await repository.RefreshAsync();
        Assert.Equal(new DateOnly(2026, 11, 11), FirstDate(refreshed));
        Assert.Contains(refreshed.Issues, item => item.Source == ConferenceRepository.CacheSource);
    }

    [Fact]
    public async Task RevalidationSendsValidatorsAndNotModifiedKeepsContentAndUpdatedTime()
    {
        var repository = Repository();
        var first = await repository.RefreshAsync();
        var cached = await File.ReadAllBytesAsync(CachePath);
        clock.Advance();
        handler.Respond = (request, _) =>
        {
            Assert.True(request.Headers.CacheControl!.NoCache);
            Assert.Equal("\"v1\"", Assert.Single(request.Headers.IfNoneMatch).Tag);
            Assert.Equal(DateTimeOffset.Parse("2026-10-28T12:00:00Z"), request.Headers.IfModifiedSince);
            return Task.FromResult(NotModified());
        };
        var result = await repository.RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(clock.Now, result.LastCheckedAt);
        Assert.Equal(first.LastUpdatedAt, result.LastUpdatedAt);
        Assert.Equal(Ids(first.Document.Sessions), Ids(result.Document.Sessions));
        Assert.NotEqual(cached, await File.ReadAllBytesAsync(CachePath));
        Assert.Equal(first.LastUpdatedAt, (await ReadCacheAsync()).Microsoft.LastUpdatedAt);
        AssertOnlyMicrosoftRequests();
    }

    [Fact]
    public async Task NotModifiedCanReplaceValidatorWithoutChangingContentTimestamp()
    {
        var repository = Repository();
        var first = await repository.RefreshAsync();
        clock.Advance();
        handler.Respond = (_, _) =>
        {
            var response = NotModified();
            response.Headers.ETag = EntityTagHeaderValue.Parse("\"updated-validator\"");
            return Task.FromResult(response);
        };
        var result = await repository.RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(first.LastUpdatedAt, result.LastUpdatedAt);
        Assert.Equal("\"updated-validator\"", (await ReadCacheAsync()).Microsoft.ETag);
    }

    [Fact]
    public async Task NotModifiedWithoutValidatedRemoteDataIsAnError()
    {
        handler.Respond = (_, _) => Task.FromResult(NotModified());
        var result = await Repository().RefreshAsync();
        Assert.IsType<InvalidDataException>(Assert.Single(result.Issues).Exception);
        Assert.Empty(result.Document.Sessions);
        Assert.False(File.Exists(CachePath));
    }

    [Fact]
    public async Task HashComparisonWorksWithoutEtagsAndWhitespaceDoesNotChangeUpdatedTime()
    {
        handler.Respond = (_, _) => Task.FromResult(Ok(Json(SizedMicrosoft(70)), null));
        var repository = Repository();
        var first = await repository.RefreshAsync();
        var originalHash = (await ReadCacheAsync()).Microsoft.PayloadHash;
        clock.Advance();
        var same = await repository.RefreshAsync();
        Assert.Equal(originalHash, (await ReadCacheAsync()).Microsoft.PayloadHash);
        Assert.Equal(first.LastUpdatedAt, same.LastUpdatedAt);
        handler.Respond = (_, _) => Task.FromResult(Ok("  " + Json(SizedMicrosoft(70)), null));
        clock.Advance();
        var result = await repository.RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.NotEqual(originalHash, (await ReadCacheAsync()).Microsoft.PayloadHash);
        Assert.Equal(first.LastUpdatedAt, result.LastUpdatedAt);
        Assert.Equal(clock.Now, result.LastCheckedAt);
    }

    [Fact]
    public async Task ChangedContentUpdatesDetailsKeepsIdentitiesAndAdvancesUpdatedTime()
    {
        var repository = Repository();
        await repository.RefreshAsync();
        clock.Advance();
        var data = SizedMicrosoft(70);
        Serve(data with
        {
            Sessions = [data.Sessions[0] with { Title = "Renamed synthetic talk", Abstract = "New synthetic abstract" }, .. data.Sessions.Skip(1)]
        }, "\"v2\"");
        var result = await repository.RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(clock.Now, result.LastUpdatedAt);
        Assert.Equal("Renamed synthetic talk", result.Document.Sessions.Single(session => session.Id == "20261001").Title);
        Assert.Empty(result.Document.WithdrawnSessions);
        Assert.Equal("\"v2\"", (await ReadCacheAsync()).Microsoft.ETag);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnchangedResponsesClampCheckTimeToRetainedUpdateAfterClockRegression(bool notModified)
    {
        var repository = Repository();
        var first = await repository.RefreshAsync();
        clock.Rewind();
        handler.Respond = (_, _) => Task.FromResult(notModified ? NotModified() : Ok(Json(SizedMicrosoft(70))));
        var result = await repository.RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(first.LastUpdatedAt, result.LastCheckedAt);
        Assert.Equal(first.LastUpdatedAt, result.LastUpdatedAt);
    }

    [Fact]
    public async Task OfflineRefreshRetainsOriginalBytesTimestampsAndReadableCachedData()
    {
        await Repository().RefreshAsync();
        var bytes = await File.ReadAllBytesAsync(CachePath);
        var repository = Repository();
        var loaded = await repository.LoadAsync();
        clock.Advance();
        handler.Respond = (_, _) => throw new HttpRequestException("No network");
        var result = await repository.RefreshAsync();
        var issue = Assert.Single(result.Issues);
        Assert.Equal(ConferenceRepository.MicrosoftSource, issue.Source);
        Assert.True(result.IsCached);
        Assert.Equal(loaded.LastCheckedAt, result.LastCheckedAt);
        Assert.Equal(70, result.Document.Sessions.Count);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(CachePath));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("""{"sessions":[],"speakers":[]}""")]
    [InlineData("""{"sessions":null,"speakers":null}""")]
    public async Task InvalidRemoteDataDoesNotRetryOrDestroyExistingCache(string json)
    {
        var repository = Repository();
        await repository.RefreshAsync();
        var bytes = await File.ReadAllBytesAsync(CachePath);
        handler.Requests.Clear();
        handler.Respond = (_, _) => Task.FromResult(Ok(json));
        var result = await repository.RefreshAsync();
        Assert.Single(result.Issues);
        Assert.Single(handler.Requests);
        Assert.Equal(70, result.Document.Sessions.Count);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(CachePath));
    }

    [Theory]
    [InlineData(204)]
    [InlineData(206)]
    public async Task IncompleteSuccessStatusesAreNotAccepted(int status)
    {
        handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(Json(SizedMicrosoft(70)))
        });
        var result = await Repository().RefreshAsync();
        Assert.Single(result.Issues);
        Assert.Single(handler.Requests);
        Assert.False(File.Exists(CachePath));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task PermanentHttpErrorsAreReportedWithoutRetry(int status)
    {
        handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        var result = await Repository().RefreshAsync();
        Assert.Single(handler.Requests);
        Assert.Equal((HttpStatusCode)status, Assert.IsType<HttpRequestException>(Assert.Single(result.Issues).Exception).StatusCode);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task TransientStatusRetriesAreBounded(int status)
    {
        handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        var result = await Repository().RefreshAsync();
        Assert.Equal(3, handler.Requests.Count);
        Assert.Single(result.Issues);
        AssertOnlyMicrosoftRequests();
    }

    [Fact]
    public async Task TransientFailureCanRecoverWithinRetryBudget()
    {
        var attempts = 0;
        handler.Respond = (request, _) => Task.FromResult(Interlocked.Increment(ref attempts) == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : DefaultResponse(request));
        var result = await Repository().RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(70, result.Document.Sessions.Count);
        Assert.True(File.Exists(CachePath));
    }

    [Fact]
    public async Task HttpClientTimeoutIsReportedNotMistakenForUserCancellation()
    {
        handler.Respond = (_, _) => throw new TaskCanceledException("HTTP timeout");
        var result = await Repository().RefreshAsync();
        Assert.IsType<TimeoutException>(Assert.Single(result.Issues).Exception);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task CallerCancellationIsNotSwallowedDoesNotWriteAndDoesNotPoisonLaterRefresh()
    {
        var repository = Repository();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Respond = async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        };
        using var cancellation = new CancellationTokenSource();
        var refresh = repository.RefreshAsync(cancellation.Token);
        await started.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.False(File.Exists(CachePath));
        handler.Respond = (request, _) => Task.FromResult(DefaultResponse(request));
        var result = await repository.RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(70, result.Document.Sessions.Count);
    }

    [Fact]
    public async Task CancellingAWaitingCallerDoesNotCancelTheSharedRefresh()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Respond = async (request, token) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            return DefaultResponse(request);
        };
        var repository = Repository();
        var first = repository.RefreshAsync();
        await started.Task;
        using var cancellation = new CancellationTokenSource();
        var waiting = repository.RefreshAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        release.SetResult();
        Assert.Equal(70, (await first).Document.Sessions.Count);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SlowRefreshDoesNotBlockCachedLoadAndConcurrentRefreshIsSingleFlight()
    {
        await Repository().RefreshAsync();
        handler.Requests.Clear();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Respond = async (request, token) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            return DefaultResponse(request);
        };
        var repository = Repository();
        await repository.LoadAsync();
        var first = repository.RefreshAsync();
        await started.Task;
        var second = repository.RefreshAsync();
        var loaded = await repository.LoadAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(70, loaded.Document.Sessions.Count);
        release.SetResult();
        Assert.Same(await first, await second);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PreCancelledLoadDoesNotReadMetadataOrNetwork()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Repository().LoadAsync(cancellation.Token));
        Assert.Equal(0, metadata.Calls);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ProgressReportsTheAcceptedMicrosoftUpdateAndEndsWithTheReturnedSnapshot()
    {
        var reports = new List<ConferenceSnapshot>();
        var result = await Repository().RefreshAsync(progress: new SnapshotProgress(reports.Add));
        var report = Assert.Single(reports);
        Assert.Same(result, report);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OversizedResponsesAreRejectedWithoutClearingReadableData(bool contentLength)
    {
        var repository = Repository();
        await repository.RefreshAsync();
        var bytes = await File.ReadAllBytesAsync(CachePath);
        handler.Respond = (_, _) =>
        {
            var response = Ok(new string(' ', 4 * 1024 * 1024 + 1));
            if (!contentLength)
                response.Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(new string(' ', 4 * 1024 * 1024 + 1))));
            return Task.FromResult(response);
        };
        var result = await repository.RefreshAsync();
        Assert.IsType<InvalidDataException>(Assert.Single(result.Issues).Exception);
        Assert.Equal(70, result.Document.Sessions.Count);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(CachePath));
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(39, true)]
    [InlineData(29, false)]
    [InlineData(1, false)]
    public async Task ContractionGuardAllowsUpToOneQuarterAndOtherwiseNeedsAnIdenticalSecondResponse(int nextCount, bool immediate)
    {
        var repository = Repository();
        Serve(SizedMicrosoft(40));
        await repository.RefreshAsync();
        clock.Advance();
        Serve(SizedMicrosoft(nextCount), "\"v2\"");
        var first = await repository.RefreshAsync();
        var cache = await ReadCacheAsync();
        if (immediate)
        {
            Assert.Empty(first.Issues);
            Assert.Equal(40 - nextCount, first.Document.WithdrawnSessions.Count);
            Assert.Equal(nextCount, cache.Microsoft.Sessions.Count);
            return;
        }
        var issue = Assert.Single(first.Issues);
        Assert.Equal(ConferenceRepository.MicrosoftSource, issue.Source);
        Assert.Contains("greater than 25%", issue.Exception.Message);
        Assert.Equal(40, first.Document.Sessions.Count);
        Assert.Empty(first.Document.WithdrawnSessions);
        Assert.Equal("\"v1\"", cache.Microsoft.ETag);
        Assert.NotNull(cache.Microsoft.PendingReductionHash);

        clock.Advance();
        var confirmed = await Repository().RefreshAsync();
        Assert.Empty(confirmed.Issues);
        Assert.Equal(nextCount, confirmed.Document.Sessions.Count);
        Assert.Equal(40 - nextCount, confirmed.Document.WithdrawnSessions.Count);
        Assert.Null((await ReadCacheAsync()).Microsoft.PendingReductionHash);
        Assert.Equal(40 - nextCount, (await Repository().LoadAsync()).Document.WithdrawnSessions.Count);
    }

    [Fact]
    public async Task DifferentReductionsEachRequireTheirOwnIdenticalConfirmation()
    {
        Serve(SizedMicrosoft(40));
        await Repository().RefreshAsync();
        foreach (var count in new[] { 1, 2, 1 })
        {
            Serve(SizedMicrosoft(count), "\"reduced\"");
            Assert.Single((await Repository().RefreshAsync()).Issues);
            Assert.Equal(40, (await ReadCacheAsync()).Microsoft.Sessions.Count);
        }
        Assert.Empty((await Repository().RefreshAsync()).Issues);
        Assert.Single((await ReadCacheAsync()).Microsoft.Sessions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidOrNotModifiedResponseClearsPendingReductionConfirmation(bool malformed)
    {
        Serve(SizedMicrosoft(40));
        await Repository().RefreshAsync();
        Serve(SizedMicrosoft(1), "\"reduced\"");
        Assert.Single((await Repository().RefreshAsync()).Issues);
        handler.Respond = (_, _) => Task.FromResult(malformed ? Ok("{}") : NotModified());
        await Repository().RefreshAsync();
        Assert.Null((await ReadCacheAsync()).Microsoft.PendingReductionHash);
        Serve(SizedMicrosoft(1), "\"reduced\"");
        Assert.Single((await Repository().RefreshAsync()).Issues);
        Assert.Equal(40, (await ReadCacheAsync()).Microsoft.Sessions.Count);
    }

    [Fact]
    public async Task RepeatedEmptyResponsesNeverConfirmTotalAgendaRemoval()
    {
        await Repository().RefreshAsync();
        var old = await File.ReadAllBytesAsync(CachePath);
        handler.Respond = (_, _) => Task.FromResult(Ok("{}"));
        for (var attempt = 0; attempt < 2; attempt++)
            Assert.Single((await Repository().RefreshAsync()).Issues);
        Assert.Equal(old, await File.ReadAllBytesAsync(CachePath));
    }

    [Fact]
    public async Task WithdrawnTalksKeepProfilesOutsideTheCurrentDirectoryUntilTheyReturn()
    {
        var repository = Repository();
        var baseline = WithTalk(SizedMicrosoft(70), "BRK071", "Synthetic extra", "Archived Presenter");
        Serve(baseline);
        await repository.RefreshAsync();
        clock.Advance();
        var without = SizedMicrosoft(70);
        Serve(without, "\"v2\"");
        var withdrawn = await repository.RefreshAsync();
        Assert.Empty(withdrawn.Issues);
        Assert.Equal("20261071", Assert.Single(withdrawn.Document.WithdrawnSessions).Id);
        Assert.DoesNotContain(withdrawn.Document.Speakers, speaker => speaker.Name == "Archived Presenter");
        Assert.Contains(withdrawn.Document.WithdrawnSpeakers, speaker => speaker.Name == "Archived Presenter");
        Assert.DoesNotContain(withdrawn.Document.Sessions, session => session.Id == "20261071");

        var restart = await Repository().LoadAsync();
        Assert.Equal("20261071", Assert.Single(restart.Document.WithdrawnSessions).Id);
        Assert.DoesNotContain(restart.Document.Speakers, speaker => speaker.Name == "Archived Presenter");
        clock.Advance();
        var unchanged = await Repository().RefreshAsync();
        Assert.Single(unchanged.Document.WithdrawnSessions);

        clock.Advance();
        Serve(baseline, "\"v3\"");
        var returned = await Repository().RefreshAsync();
        Assert.Empty(returned.Document.WithdrawnSessions);
        Assert.Empty(returned.Document.WithdrawnSpeakers);
        Assert.Contains(returned.Document.Sessions, session => session.Id == "20261071");
        Assert.Contains(returned.Document.Speakers, speaker => speaker.Name == "Archived Presenter");
    }

    [Fact]
    public async Task PublishedUnscheduledSpeakersRemainCurrentWhenTheirLastTalkIsWithdrawn()
    {
        var repository = Repository();
        var baseline = WithTalk(SizedMicrosoft(70), "BRK071", "Synthetic extra", "Still Published");
        Serve(baseline);
        await repository.RefreshAsync();
        Serve(baseline with { Sessions = [.. baseline.Sessions.Where(session => session.Id != "BRK071")] }, "\"v2\"");
        var result = await repository.RefreshAsync();
        Assert.Single(result.Document.WithdrawnSessions);
        Assert.Contains(result.Document.Speakers, speaker => speaker.Name == "Still Published");
        Assert.Empty(result.Document.WithdrawnSpeakers);
    }

    [Fact]
    public async Task RemovedSessionIdsCannotBeReusedByAnArchivedIdentity()
    {
        var repository = Repository();
        await repository.RefreshAsync();
        var cache = await ReadCacheAsync();
        var duplicate = cache.Microsoft.Sessions[0];
        await WriteCacheAsync(cache with { Microsoft = cache.Microsoft with { WithdrawnSessions = [duplicate] } });
        var result = await Repository().LoadAsync();
        Assert.Single(result.Issues);
        Assert.Equal(ConferenceRepository.CacheSource, result.Issues[0].Source);
        Assert.Empty(result.Document.Sessions);
    }

    [Fact]
    public async Task CorruptOrInvalidCacheFallsBackWithVisibleIssueAndIsReplacedOnlyOnSuccess()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(CachePath, "{ not json");
        var loaded = await Repository().LoadAsync();
        var issue = Assert.Single(loaded.Issues);
        Assert.Equal(ConferenceRepository.CacheSource, issue.Source);
        Assert.Empty(loaded.Document.Sessions);
        Assert.Equal("{ not json", await File.ReadAllTextAsync(CachePath));
        var refreshed = await Repository().RefreshAsync();
        Assert.Empty(refreshed.Issues);
        Assert.Equal(6, (await ReadCacheAsync()).SchemaVersion);
    }

    [Fact]
    public async Task CachedSessionsWithoutRemotePayloadProvenanceAreNeverTrusted()
    {
        await Repository().RefreshAsync();
        var cache = await ReadCacheAsync();
        await WriteCacheAsync(cache with { Microsoft = cache.Microsoft with { PayloadHash = "" } });
        var result = await Repository().LoadAsync();
        Assert.Equal(ConferenceRepository.CacheSource, Assert.Single(result.Issues).Source);
        Assert.Empty(result.Document.Sessions);
    }

    [Fact]
    public async Task OversizedCacheFromCustomStoreIsRejectedBeforeDeserialization()
    {
        var store = new MemoryCacheStore { Content = new byte[16 * 1024 * 1024 + 1] };
        var result = await new ConferenceRepository(metadata, client, store, clock).LoadAsync();
        Assert.Equal(ConferenceRepository.CacheSource, Assert.Single(result.Issues).Source);
        Assert.Empty(result.Document.Sessions);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task CacheStoreSeamRoundTripsAndPersistenceFailureStillShowsRemoteData()
    {
        var store = new MemoryCacheStore();
        var repository = new ConferenceRepository(metadata, client, store, clock);
        Assert.Empty((await repository.RefreshAsync()).Issues);
        Assert.Equal(1, store.Writes);
        Assert.Equal(70, (await new ConferenceRepository(metadata, client, store, clock).LoadAsync()).Document.Sessions.Count);

        var failing = new MemoryCacheStore { WriteException = new IOException("disk full") };
        var result = await new ConferenceRepository(metadata, client, failing, clock).RefreshAsync();
        Assert.Equal(70, result.Document.Sessions.Count);
        Assert.Equal(ConferenceRepository.CacheSource, Assert.Single(result.Issues).Source);
        Assert.Equal(clock.Now, result.LastCheckedAt);
    }

    [Fact]
    public async Task AtomicWriteFailureKeepsOriginalCacheBytes()
    {
        await Repository().RefreshAsync();
        var bytes = await File.ReadAllBytesAsync(CachePath);
        var store = new MemoryCacheStore { Content = bytes, WriteException = new IOException("write failed") };
        var repository = new ConferenceRepository(metadata, client, store, clock);
        await repository.LoadAsync();
        Serve(SizedMicrosoft(70) with { Sessions = [.. SizedMicrosoft(70).Sessions.Select(s => s with { Abstract = "Changed" })] }, "\"v2\"");
        var result = await repository.RefreshAsync();
        Assert.Equal(ConferenceRepository.CacheSource, Assert.Single(result.Issues).Source);
        Assert.Equal(bytes, store.Content);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task LegacyDownloadedCachesAreRetainedWithoutNetworkAndSecondSourceIsRetired(int schema)
    {
        var community = LegacySource([LegacyTalk()], speakers: [LegacySpeaker()]);
        await SeedLegacyAsync(SizedMicrosoft(70), community, schema);
        var result = await Repository().LoadAsync();
        Assert.Empty(result.Issues);
        Assert.True(result.IsCached);
        Assert.Empty(handler.Requests);
        Assert.Equal(71, result.Document.Sessions.Count);
        Assert.Equal("Synthetic legacy abstract", result.Document.Sessions.Single(session => session.Id == LegacyId).Description);
        Assert.Empty(result.Document.WithdrawnSessions);
        Assert.Contains(result.Document.Speakers, speaker => speaker.Id == "legacy-speaker");
        Assert.Empty(result.Document.WithdrawnSpeakers);

        var migrated = await ReadCacheAsync();
        Assert.Equal(6, migrated.SchemaVersion);
        Assert.Null(migrated.Community);
        Assert.Equal([LegacyId], migrated.LegacySessionIds);
        Assert.Equal([LegacyId], migrated.LegacyActiveSessionIds);
        Assert.Equal(community.PayloadHash, migrated.LegacyPayloadHash);
        Assert.True(migrated.Microsoft.RequiresProfileRefresh);
        Assert.Contains(migrated.Microsoft.WithdrawnSessions, session => session.Id == LegacyId);
        Assert.Null(JsonNode.Parse(await File.ReadAllTextAsync(CachePath))!["community"]);
        var again = await Repository().LoadAsync();
        Assert.Equal(Ids(result.Document.Sessions), Ids(again.Document.Sessions));
        Assert.Empty(again.Document.WithdrawnSessions);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SchemaOneIsStillDiscardedAndUndownloadedSecondSourceSeedsAreNotRetained()
    {
        await Repository().RefreshAsync();
        var old = await ReadCacheAsync();
        await WriteCacheAsync(old with { SchemaVersion = 1 });
        var discarded = await Repository().LoadAsync();
        Assert.Empty(discarded.Document.Sessions);
        Assert.Empty(discarded.Document.WithdrawnSessions);

        var seed = LegacySource([LegacyTalk()], speakers: [LegacySpeaker()]) with
        {
            PayloadHash = "", ETag = null, LastCheckedAt = null, LastUpdatedAt = null
        };
        await WriteCacheAsync(old with { SchemaVersion = 2, Community = seed });
        var result = await Repository().LoadAsync();
        Assert.Equal(70, result.Document.Sessions.Count);
        Assert.Empty(result.Document.WithdrawnSessions);
        Assert.Null((await ReadCacheAsync()).Community);
    }

    [Fact]
    public async Task LegacyActiveAndWithdrawnRecordsBecomeArchivedAndStayVisibleToSavedDetails()
    {
        var community = LegacySource(
            [LegacyTalk()], [LegacyTalk("5678", "Gone talk", speaker: "gone-speaker")],
            [LegacySpeaker()], [LegacySpeaker("gone-speaker", "Gone Speaker")]);
        await SeedLegacyAsync(SizedMicrosoft(70), community);
        var result = await Repository().LoadAsync();
        Assert.Equal(71, result.Document.Sessions.Count);
        Assert.Contains(result.Document.Sessions, session => session.Id == LegacyId);
        Assert.Equal("5678", Assert.Single(result.Document.WithdrawnSessions).Id);
        Assert.Contains(result.Document.Speakers, speaker => speaker.Id == "legacy-speaker");
        Assert.Equal("gone-speaker", Assert.Single(result.Document.WithdrawnSpeakers).Id);
        Assert.Equal([LegacyId], (await ReadCacheAsync()).LegacyActiveSessionIds);
        Assert.Equivalent(new[] { LegacyId, "5678" }, (await ReadCacheAsync()).LegacySessionIds);
        Assert.Empty(result.Document.SessionAliases);
        Assert.Equal((await ReadCacheAsync()).Microsoft.LastCheckedAt, result.LastCheckedAt);
        Assert.Equal((await ReadCacheAsync()).Microsoft.LastUpdatedAt, result.LastUpdatedAt);
    }

    [Fact]
    public async Task SnapshotTimestampsComeOnlyFromMicrosoftEvenWhenRetiredSourceIsNewer()
    {
        var community = LegacySource([LegacyTalk()], speakers: [LegacySpeaker()]);
        var legacy = await SeedLegacyAsync(SizedMicrosoft(70), community);
        var result = await Repository().LoadAsync();
        Assert.True(community.LastCheckedAt > legacy.Microsoft.LastCheckedAt);
        Assert.Equal(legacy.Microsoft.LastCheckedAt, result.LastCheckedAt);
        Assert.Equal(legacy.Microsoft.LastUpdatedAt, result.LastUpdatedAt);
    }

    [Fact]
    public async Task CachedMicrosoftMatchCreatesDurableAliasAtLoadAndRemovesTheDuplicateArchive()
    {
        var community = LegacySource([LegacyTalk()], speakers: [LegacySpeaker()]);
        await SeedLegacyAsync(WithTalk(SizedMicrosoft(70)), community);
        var result = await Repository().LoadAsync();
        Assert.Empty(handler.Requests);
        Assert.Equal(TalkCanonicalId, result.Document.SessionAliases[LegacyId]);
        Assert.Empty(result.Document.WithdrawnSessions);
        Assert.Empty(result.Document.WithdrawnSpeakers);
        Assert.Contains(result.Document.Sessions, session => session.Id == TalkCanonicalId);
        var cache = await ReadCacheAsync();
        Assert.Equal(TalkCanonicalId, cache.SessionAliases[LegacyId]);
        Assert.Empty(cache.LegacySessionIds);
        Assert.Equal(TalkCanonicalId, (await Repository().LoadAsync()).Document.SessionAliases[LegacyId]);
    }

    [Fact]
    public async Task UnpairedLegacyTalkIsPairedByLaterMicrosoftRefreshAndPersistsAcrossRestartsAndUnchangedRefreshes()
    {
        var community = LegacySource([LegacyTalk()], speakers: [LegacySpeaker()]);
        await SeedLegacyAsync(SizedMicrosoft(70), community);
        var repository = Repository();
        var before = await repository.LoadAsync();
        Assert.Contains(before.Document.Sessions, session => session.Id == LegacyId);
        Assert.Empty(before.Document.WithdrawnSessions);
        clock.Advance();
        var matching = WithTalk(SizedMicrosoft(70));
        Serve(matching, "\"v2\"");
        var paired = await repository.RefreshAsync();
        Assert.Empty(paired.Issues);
        Assert.Equal(TalkCanonicalId, paired.Document.SessionAliases[LegacyId]);
        Assert.Empty(paired.Document.WithdrawnSessions);
        AssertOnlyMicrosoftRequests();
        Assert.Equal(TalkCanonicalId, (await ReadCacheAsync()).SessionAliases[LegacyId]);

        clock.Advance();
        handler.Respond = (_, _) => Task.FromResult(NotModified());
        var unchanged = await Repository().RefreshAsync();
        Assert.Equal(TalkCanonicalId, unchanged.Document.SessionAliases[LegacyId]);
        Assert.Empty(unchanged.Document.WithdrawnSessions);
        Assert.Equal(TalkCanonicalId, (await Repository().LoadAsync()).Document.SessionAliases[LegacyId]);
    }

    [Fact]
    public async Task AliasesSurviveSourceRenamesTimeDriftAndSpeakerRenames()
    {
        var community = LegacySource([LegacyTalk()], speakers: [LegacySpeaker()]);
        await SeedLegacyAsync(WithTalk(SizedMicrosoft(70)), community);
        var repository = Repository();
        await repository.LoadAsync();
        foreach (var (title, start, end, speaker) in new[]
        {
            ("Renamed community talk", "08:00:00", "08:30:00", "Community Speaker"),
            ("Renamed community talk", "09:00:00", "09:45:00", "Community Speaker"),
            ("Renamed community talk", "09:00:00", "09:45:00", "Renamed Speaker")
        })
        {
            clock.Advance();
            Serve(WithTalk(SizedMicrosoft(70), title: title, start: start, end: end, speakerName: speaker), $"\"{title}{start}{speaker}\"");
            var result = await repository.RefreshAsync();
            Assert.Empty(result.Issues);
            Assert.Equal(TalkCanonicalId, result.Document.SessionAliases[LegacyId]);
            Assert.Empty(result.Document.WithdrawnSessions);
            Assert.Single(result.Document.SessionAliases);
            Assert.Equal(title, result.Document.Sessions.Single(session => session.Id == TalkCanonicalId).Title);
            var restart = await Repository().LoadAsync();
            Assert.Equal(TalkCanonicalId, restart.Document.SessionAliases[LegacyId]);
            Assert.Empty(restart.Document.WithdrawnSessions);
        }
    }

    [Fact]
    public async Task MatchingNormalizesCaseAndWhitespaceButRequiresExactTitleTimeAndSpeakerSets()
    {
        var community = LegacySource([LegacyTalk(title: "  Community   TALK ")], speakers: [LegacySpeaker(name: "community  speaker")]);
        await SeedLegacyAsync(WithTalk(SizedMicrosoft(70), title: "community talk", speakerName: "COMMUNITY SPEAKER"), community);
        Assert.Equal(TalkCanonicalId, (await Repository().LoadAsync()).Document.SessionAliases[LegacyId]);
    }

    [Theory]
    [InlineData("Different title", "08:00:00", "08:30:00", "Community Speaker")]
    [InlineData("Community", "08:01:00", "08:30:00", "Community Speaker")]
    [InlineData("Community", "08:00:00", "08:31:00", "Community Speaker")]
    [InlineData("Community", "08:00:00", "08:30:00", "Another Speaker")]
    public async Task NearMatchesAreNeverFalselyMergedAndKeepTheLegacyRecordArchived(
        string title, string start, string end, string speaker)
    {
        var community = LegacySource([LegacyTalk(title: "Community")], speakers: [LegacySpeaker()]);
        await SeedLegacyAsync(WithTalk(SizedMicrosoft(70), title: title, start: start, end: end, speakerName: speaker), community);
        var result = await Repository().LoadAsync();
        Assert.Empty(result.Document.SessionAliases);
        Assert.Empty(result.Document.WithdrawnSessions);
        Assert.Contains(result.Document.Sessions, session => session.Id == LegacyId);
        Assert.Contains(result.Document.Sessions, session => session.Id == TalkCanonicalId);
        Assert.Equal([LegacyId], (await ReadCacheAsync()).LegacyActiveSessionIds);
    }

    [Fact]
    public async Task AmbiguousMicrosoftCandidatesAreNotMerged()
    {
        var feed = WithTalk(SizedMicrosoft(70));
        feed = feed with { Sessions = [.. feed.Sessions, feed.Sessions.Single(session => session.Id == TalkId) with { Id = "BRK071" }] };
        var community = LegacySource([LegacyTalk()], speakers: [LegacySpeaker()]);
        await SeedLegacyAsync(feed, community);
        var result = await Repository().LoadAsync();
        Assert.Empty(result.Document.SessionAliases);
        Assert.Empty(result.Document.WithdrawnSessions);
        Assert.Contains(result.Document.Sessions, session => session.Id == LegacyId);
    }

    [Fact]
    public async Task AmbiguousLegacyRecordsAreNotMergedIntoOneMicrosoftTalk()
    {
        var community = LegacySource([LegacyTalk(), LegacyTalk("1235")], speakers: [LegacySpeaker()]);
        await SeedLegacyAsync(WithTalk(SizedMicrosoft(70)), community);
        var result = await Repository().LoadAsync();
        Assert.Empty(result.Document.SessionAliases);
        Assert.Empty(result.Document.WithdrawnSessions);
        Assert.Contains(result.Document.Sessions, session => session.Id == LegacyId);
        Assert.Contains(result.Document.Sessions, session => session.Id == "1235");
    }

    [Fact]
    public async Task LegacyRecordsWithoutSpeakersAreNeverMatched()
    {
        var legacy = LegacyTalk() with { SpeakerIds = [] };
        var feed = WithTalk(SizedMicrosoft(70));
        feed = feed with { Sessions = [.. feed.Sessions.Where(session => session.Id != TalkId), feed.Sessions.Single(s => s.Id == TalkId) with { SpeakerIds = [] }] };
        await SeedLegacyAsync(feed, LegacySource([legacy]));
        var result = await Repository().LoadAsync();
        Assert.Empty(result.Document.SessionAliases);
        Assert.Empty(result.Document.WithdrawnSessions);
        Assert.Contains(result.Document.Sessions, session => session.Id == LegacyId);
    }

    [Fact]
    public async Task LegacyRecordMatchesAnArchivedMicrosoftTalkAndDuplicateArchiveCollapses()
    {
        var repository = Repository();
        Serve(WithTalk(SizedMicrosoft(70), "BRK071"));
        await repository.RefreshAsync();
        clock.Advance();
        Serve(SizedMicrosoft(70), "\"v2\"");
        var withdrawn = await repository.RefreshAsync();
        Assert.Equal("20261071", Assert.Single(withdrawn.Document.WithdrawnSessions).Id);
        var cache = await ReadCacheAsync();
        await WriteCacheAsync(cache with
        {
            SchemaVersion = 5,
            Community = LegacySource([LegacyTalk()], speakers: [LegacySpeaker()])
        });
        var result = await Repository().LoadAsync();
        Assert.Equal("20261071", result.Document.SessionAliases[LegacyId]);
        Assert.Equal("20261071", Assert.Single(result.Document.WithdrawnSessions).Id);
        Assert.DoesNotContain(result.Document.WithdrawnSessions, session => session.Id == LegacyId);
    }

    [Fact]
    public async Task SessionizeOnlyLegacyCacheStaysArchivedOfflineAndAliasesOnTheFirstLaterMicrosoftFetch()
    {
        await Repository().RefreshAsync();
        var current = await ReadCacheAsync();
        var empty = new CachedAgenda { Sessions = [], Speakers = [], PayloadHash = "" };
        var community = LegacySource([LegacyTalk()], speakers: [LegacySpeaker()]);
        await WriteCacheAsync(current with { SchemaVersion = 5, Microsoft = empty, Community = community });
        handler.Requests.Clear();
        handler.Respond = (_, _) => throw new HttpRequestException("Offline");

        var repository = Repository();
        var loaded = await repository.LoadAsync();
        Assert.Empty(loaded.Issues);
        Assert.Empty(loaded.Document.Sessions);
        Assert.Equal(LegacyId, Assert.Single(loaded.Document.WithdrawnSessions).Id);
        Assert.Null(loaded.LastCheckedAt);
        Assert.Empty(handler.Requests);
        var offline = await repository.RefreshAsync();
        Assert.Equal(ConferenceRepository.MicrosoftSource, Assert.Single(offline.Issues).Source);
        Assert.Empty(offline.Document.Sessions);
        Assert.Equal(LegacyId, Assert.Single(offline.Document.WithdrawnSessions).Id);
        var offlineCache = await ReadCacheAsync();
        Assert.Equal(community.PayloadHash, offlineCache.LegacyPayloadHash);
        Assert.Empty(offlineCache.LegacyActiveSessionIds);

        Serve(WithTalk(SizedMicrosoft(70)));
        var online = await Repository().RefreshAsync();
        Assert.Empty(online.Issues);
        Assert.Equal(TalkCanonicalId, online.Document.SessionAliases[LegacyId]);
        Assert.Empty(online.Document.WithdrawnSessions);
        Assert.Equal(TalkCanonicalId, (await Repository().LoadAsync()).Document.SessionAliases[LegacyId]);
        AssertOnlyMicrosoftRequests();
    }

    [Fact]
    public async Task ValidationFailureAfterMigrationKeepsExactLastGoodBytes()
    {
        await SeedLegacyAsync(SizedMicrosoft(70), LegacySource([LegacyTalk()], speakers: [LegacySpeaker()]));
        var repository = Repository();
        await repository.LoadAsync();
        var bytes = await File.ReadAllBytesAsync(CachePath);
        handler.Respond = (_, _) => Task.FromResult(Ok("{}"));
        var result = await repository.RefreshAsync();
        Assert.Single(result.Issues);
        Assert.Contains(result.Document.Sessions, session => session.Id == LegacyId);
        Assert.Empty(result.Document.WithdrawnSessions);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(CachePath));
    }

    [Fact]
    public async Task RetiredSourceIdentityCollidingWithMicrosoftIsRejectedWithoutLosingTheCache()
    {
        var community = LegacySource([LegacyTalk("20261001")], speakers: [LegacySpeaker()]);
        var legacy = await SeedLegacyAsync(SizedMicrosoft(70), community);
        var bytes = await File.ReadAllBytesAsync(CachePath);
        var result = await Repository().LoadAsync();
        Assert.Equal(ConferenceRepository.CacheSource, Assert.Single(result.Issues).Source);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(CachePath));
        Assert.Equal(5, legacy.SchemaVersion);
    }

    [Fact]
    public async Task FailedLegacyMigrationPersistenceStillShowsTheMigratedAgendaAndReportsTheCacheFailure()
    {
        var community = LegacySource([LegacyTalk()], speakers: [LegacySpeaker()]);
        await SeedLegacyAsync(SizedMicrosoft(70), community);
        var store = new MemoryCacheStore { Content = await File.ReadAllBytesAsync(CachePath), WriteException = new IOException("read only") };
        var result = await new ConferenceRepository(metadata, client, store, clock).LoadAsync();
        Assert.Equal(ConferenceRepository.CacheSource, Assert.Single(result.Issues).Source);
        Assert.Equal(71, result.Document.Sessions.Count);
        Assert.Contains(result.Document.Sessions, session => session.Id == LegacyId);
        Assert.Empty(result.Document.WithdrawnSessions);
    }

    [Fact]
    public async Task RetiredSourceIsNeverRequestedAcrossMigrationRefreshAndRestart()
    {
        var community = LegacySource([LegacyTalk()], speakers: [LegacySpeaker()]);
        await SeedLegacyAsync(WithTalk(SizedMicrosoft(70)), community);
        await Repository().LoadAsync();
        clock.Advance();
        Serve(WithTalk(SizedMicrosoft(70)), "\"v2\"");
        var repository = Repository();
        await repository.RefreshAsync();
        await Repository().LoadAsync();
        AssertOnlyMicrosoftRequests();
        Assert.Single(handler.Requests);
        Assert.Null((await ReadCacheAsync()).Community);
    }

    [Fact]
    public async Task CachedProfilesAreCurrentOnlyWhileTheirActiveFallbackTalkIsRetained()
    {
        var community = LegacySource([LegacyTalk()], speakers: [LegacySpeaker()]);
        await SeedLegacyAsync(SizedMicrosoft(70), community);
        var repository = Repository();
        var cached = await repository.LoadAsync();
        Assert.Single(cached.Document.Speakers, speaker => speaker.Name == "Community Speaker");
        Assert.All(cached.Document.Sessions.SelectMany(session => session.SpeakerIds),
            id => Assert.Contains(cached.Document.Speakers, speaker => speaker.Id == id));

        clock.Advance();
        Serve(SizedMicrosoft(70), "\"v2\"");
        var refreshed = await repository.RefreshAsync();
        Assert.Empty(refreshed.Issues);
        Assert.DoesNotContain(refreshed.Document.Speakers, speaker => speaker.Name == "Community Speaker");
        Assert.Contains(refreshed.Document.WithdrawnSpeakers, speaker => speaker.Id == "legacy-speaker");
        Assert.NotEmpty(refreshed.Document.Speakers);
        Assert.All(refreshed.Document.Sessions.SelectMany(session => session.SpeakerIds),
            id => Assert.Contains(refreshed.Document.Speakers, speaker => speaker.Id == id));
    }

    [Fact]
    public async Task SuccessfulMicrosoftRefreshRetiresUnmatchedFallbackIntoGenuineWithdrawalsAndPersists()
    {
        await SeedLegacyAsync(SizedMicrosoft(70), LegacySource([LegacyTalk()], speakers: [LegacySpeaker()]));
        var repository = Repository();
        await repository.LoadAsync();
        clock.Advance();
        Serve(SizedMicrosoft(70), "\"v2\"");
        var result = await repository.RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(70, result.Document.Sessions.Count);
        Assert.Equal(LegacyId, Assert.Single(result.Document.WithdrawnSessions).Id);
        Assert.Equal("Synthetic legacy abstract", result.Document.WithdrawnSessions[0].Description);
        var cache = await ReadCacheAsync();
        Assert.Empty(cache.LegacyActiveSessionIds);
        Assert.Equal([LegacyId], cache.LegacySessionIds);
        Assert.Contains(cache.Microsoft.WithdrawnSessions, session => session.Id == LegacyId);
        var restart = await Repository().LoadAsync();
        Assert.Equal(70, restart.Document.Sessions.Count);
        Assert.Equal(LegacyId, Assert.Single(restart.Document.WithdrawnSessions).Id);
        AssertOnlyMicrosoftRequests();
    }

    [Fact]
    public async Task MigrationForcesAFullDownloadSoAnOldNotModifiedCannotBypassIt()
    {
        await SeedLegacyAsync(SizedMicrosoft(70), LegacySource([LegacyTalk()], speakers: [LegacySpeaker()]));
        var validators = new List<bool>();
        handler.Respond = (request, _) =>
        {
            validators.Add(request.Headers.IfNoneMatch.Count != 0 || request.Headers.IfModifiedSince is not null);
            return Task.FromResult(validators.Count == 1 ? Ok(Json(SizedMicrosoft(70)), "\"v1\"") : NotModified());
        };
        var repository = Repository();
        await repository.LoadAsync();
        Assert.Empty((await repository.RefreshAsync()).Issues);
        Assert.Equal([false], validators);
        Assert.Empty((await ReadCacheAsync()).LegacyActiveSessionIds);
    }

    [Fact]
    public async Task FailedRefreshesKeepTheActiveFallbackAndExactCacheBytes()
    {
        await SeedLegacyAsync(SizedMicrosoft(70), LegacySource([LegacyTalk()], speakers: [LegacySpeaker()]));
        var repository = Repository();
        await repository.LoadAsync();
        var bytes = await File.ReadAllBytesAsync(CachePath);
        foreach (var failure in new Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[]
        {
            (request, _) => throw new HttpRequestException("Offline"),
            (request, _) => Task.FromResult(Ok("{}")),
            (request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
        })
        {
            handler.Respond = failure;
            var result = await repository.RefreshAsync();
            Assert.Single(result.Issues);
            Assert.Contains(result.Document.Sessions, session => session.Id == LegacyId);
            Assert.Empty(result.Document.WithdrawnSessions);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(CachePath));
        }
        var restart = await Repository().LoadAsync();
        Assert.Contains(restart.Document.Sessions, session => session.Id == LegacyId);
    }

    [Fact]
    public async Task PendingContractionDoesNotRetireTheFallbackUntilConfirmedThenItDoes()
    {
        await SeedLegacyAsync(SizedMicrosoft(70), LegacySource([LegacyTalk()], speakers: [LegacySpeaker()]));
        var repository = Repository();
        await repository.LoadAsync();
        clock.Advance();
        Serve(SizedMicrosoft(40), "\"small\"");
        var pending = await repository.RefreshAsync();
        Assert.Contains(pending.Document.Sessions, session => session.Id == LegacyId);
        Assert.Empty(pending.Document.WithdrawnSessions);
        var cache = await ReadCacheAsync();
        Assert.Equal([LegacyId], cache.LegacyActiveSessionIds);
        Assert.NotNull(cache.Microsoft.PendingReductionHash);
        Assert.Contains(LegacyId, Ids((await Repository().LoadAsync()).Document.Sessions));

        clock.Advance();
        var confirmed = await repository.RefreshAsync();
        Assert.Empty(confirmed.Issues);
        Assert.Equal(40, confirmed.Document.Sessions.Count);
        Assert.Contains(LegacyId, Ids(confirmed.Document.WithdrawnSessions));
        Assert.Empty((await ReadCacheAsync()).LegacyActiveSessionIds);
    }

    [Fact]
    public async Task CancellationWhenTheClockIsReadCancelsTheCallerWithoutSilentSuccessOrRetirement()
    {
        await SeedLegacyAsync(SizedMicrosoft(70), LegacySource([LegacyTalk()], speakers: [LegacySpeaker()]));
        var repository = Repository();
        await repository.LoadAsync();
        var bytes = await File.ReadAllBytesAsync(CachePath);
        Serve(SizedMicrosoft(70), "\"v2\"");
        using var cancellation = new CancellationTokenSource();
        clock.OnRead = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.RefreshAsync(cancellation.Token));
        clock.OnRead = null;
        Assert.Equal(bytes, await File.ReadAllBytesAsync(CachePath));
        var snapshot = await repository.LoadAsync();
        Assert.Contains(snapshot.Document.Sessions, session => session.Id == LegacyId);
        Assert.Empty(snapshot.Document.WithdrawnSessions);

        var later = await repository.RefreshAsync();
        Assert.Empty(later.Issues);
        Assert.Equal(LegacyId, Assert.Single(later.Document.WithdrawnSessions).Id);
    }

    [Fact]
    public async Task MatchedFallbackTalksAreRemovedFromTheFallbackWhileUnmatchedOnesStayUntilSuccess()
    {
        var community = LegacySource(
            [LegacyTalk(), LegacyTalk("5678", "Unmatched talk", speaker: "other-speaker")],
            speakers: [LegacySpeaker(), LegacySpeaker("other-speaker", "Other Speaker")]);
        await SeedLegacyAsync(WithTalk(SizedMicrosoft(70)), community);
        var result = await Repository().LoadAsync();
        Assert.Equal(TalkCanonicalId, result.Document.SessionAliases[LegacyId]);
        Assert.DoesNotContain(LegacyId, Ids(result.Document.Sessions));
        Assert.Contains("5678", Ids(result.Document.Sessions));
        Assert.Empty(result.Document.WithdrawnSessions);
        var cache = await ReadCacheAsync();
        Assert.Equal(["5678"], cache.LegacyActiveSessionIds);
        Assert.Equal(["5678"], cache.LegacySessionIds);
    }

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = DateTimeOffset.Parse("2026-10-29T12:00:00Z");
        public Action? OnRead { get; set; }
        public override DateTimeOffset GetUtcNow()
        {
            OnRead?.Invoke();
            return Now;
        }
        public void Advance() => Now = Now.AddHours(1);
        public void Rewind() => Now = Now.AddHours(-1);
    }

    private sealed class MetadataSource : IConferenceDataSource
    {
        public ConferenceDocument Document { get; set; } = Bundle with { Sessions = [], Speakers = [] };
        public int Calls { get; private set; }
        public Task<ConferenceDocument> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(Document);
        }
    }

    private sealed class Handler : HttpMessageHandler
    {
        public ConcurrentQueue<string> Requests { get; } = new();
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
            (request, _) => Task.FromResult(DefaultResponse(request));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(request.RequestUri!.AbsoluteUri);
            return Respond(request, cancellationToken);
        }
    }

    private sealed class MemoryCacheStore : IConferenceCacheStore
    {
        public byte[]? Content { get; set; }
        public int Writes { get; private set; }
        public IOException? WriteException { get; set; }

        public Task<byte[]?> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Content?.ToArray());
        }

        public Task WriteAsync(byte[] content, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (WriteException is not null)
                throw WriteException;
            Content = content.ToArray();
            Writes++;
            return Task.CompletedTask;
        }
    }

    private sealed class SnapshotProgress(Action<ConferenceSnapshot> report) : IProgress<ConferenceSnapshot>
    {
        public void Report(ConferenceSnapshot value) => report(value);
    }
}
