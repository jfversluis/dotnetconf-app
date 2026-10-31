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
    private const string TalkId = "BRK050";
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
        JsonSerializer.Deserialize(await File.ReadAllTextAsync(CachePath), ConferenceCacheJsonContext.Default.ConferenceCache)!;

    private async Task WriteCacheAsync(ConferenceCache cache)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(CachePath, JsonSerializer.Serialize(cache, ConferenceCacheJsonContext.Default.ConferenceCache));
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

    // Replaces or adds one synthetic talk with a published speaker.
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

    private static string[] Ids(IEnumerable<Session> sessions) => [.. sessions.Select(session => session.Id)];

    [Fact]
    public async Task FirstRefreshUsesOnlyMicrosoftAndPersistsAllSeventyTalks()
    {
        var result = await Repository().RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(70, result.Document.Sessions.Count);
        Assert.Empty(result.Document.WithdrawnSessions);
        Assert.Equal(clock.Now, result.LastCheckedAt);
        Assert.Equal(clock.Now, result.LastUpdatedAt);
        Assert.Single(handler.Requests);
        AssertOnlyMicrosoftRequests();
        var cache = await ReadCacheAsync();
        Assert.Equal(8, cache.SchemaVersion);
        Assert.Equal(70, cache.Microsoft.Sessions.Count);
        Assert.Equal(64, cache.Microsoft.PayloadHash.Length);
        Assert.Equal(["schemaVersion", "metadata", "microsoft"],
            JsonNode.Parse(await File.ReadAllTextAsync(CachePath))!.AsObject().Select(property => property.Key));
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
        Assert.Equal(new DateOnly(2026, 11, 15), DateOnly.FromDateTime(updated.Document.Sessions.Single(s => s.Id == "BRK101").StartsAt.Date));
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
        metadata.Document = Previous;
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
        Assert.True(cache.Microsoft.RequiresRevalidation);
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
        Assert.False(cache.Microsoft.RequiresRevalidation);

        clock.Advance();
        await repository.RefreshAsync();
        Assert.Equal([false, true], validators);
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
        var restarted = await Repository().LoadAsync();
        Assert.Equal(ConferenceRepository.MetadataSource, Assert.Single(restarted.Issues).Source);
        Assert.Equal(first.Document.StartDate, restarted.Document.StartDate);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(CachePath));

        CorrectStartDate(new DateOnly(2026, 11, 11));
        var corrected = await Repository().LoadAsync();
        Assert.Empty(corrected.Issues);
        Assert.Equal(new DateOnly(2026, 11, 11), FirstDate(corrected));
    }

    [Fact]
    public async Task CorrectedStartDateRebasesMicrosoftWithdrawalsWithoutChangingSavedIds()
    {
        var repository = Repository();
        Serve(WithTalk(SizedMicrosoft(70), "BRK071"));
        await repository.RefreshAsync();
        Serve(SizedMicrosoft(70), "\"withdrawn\"");
        var before = await repository.RefreshAsync();
        var withdrawn = Assert.Single(before.Document.WithdrawnSessions);
        var schedule = new ScheduleService(new JsonScheduleStore(Path.Combine(directory, "schedule.json")));
        await schedule.SetSavedAsync(withdrawn.Id, true);
        var scheduleBytes = await File.ReadAllBytesAsync(Path.Combine(directory, "schedule.json"));
        handler.Requests.Clear();
        CorrectStartDate(new DateOnly(2026, 11, 11));

        var corrected = await Repository().LoadAsync();

        Assert.Empty(corrected.Issues);
        Assert.Empty(handler.Requests);
        var shifted = Assert.Single(corrected.Document.WithdrawnSessions);
        Assert.Equal(withdrawn.Id, shifted.Id);
        Assert.Equal(withdrawn.StartsAt.AddDays(1), shifted.StartsAt);
        Assert.Equal(withdrawn.EndsAt.AddDays(1), shifted.EndsAt);
        Assert.Equal(Ids(before.Document.Sessions), Ids(corrected.Document.Sessions));
        Assert.True(schedule.IsSaved(shifted.Id));
        Assert.Equal(scheduleBytes, await File.ReadAllBytesAsync(Path.Combine(directory, "schedule.json")));
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
            bytes, ConferenceCacheJsonContext.Default.ConferenceCache)!.Microsoft.LastCheckedAt);
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
        Assert.Equal("Renamed synthetic talk", result.Document.Sessions.Single(session => session.Id == "BRK001").Title);
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
        Assert.Equal("BRK071", Assert.Single(withdrawn.Document.WithdrawnSessions).Id);
        Assert.DoesNotContain(withdrawn.Document.Speakers, speaker => speaker.Name == "Archived Presenter");
        Assert.Contains(withdrawn.Document.WithdrawnSpeakers, speaker => speaker.Name == "Archived Presenter");
        Assert.DoesNotContain(withdrawn.Document.Sessions, session => session.Id == "BRK071");

        var restart = await Repository().LoadAsync();
        Assert.Equal("BRK071", Assert.Single(restart.Document.WithdrawnSessions).Id);
        Assert.DoesNotContain(restart.Document.Speakers, speaker => speaker.Name == "Archived Presenter");
        clock.Advance();
        var unchanged = await Repository().RefreshAsync();
        Assert.Single(unchanged.Document.WithdrawnSessions);

        clock.Advance();
        Serve(baseline, "\"v3\"");
        var returned = await Repository().RefreshAsync();
        Assert.Empty(returned.Document.WithdrawnSessions);
        Assert.Empty(returned.Document.WithdrawnSpeakers);
        Assert.Contains(returned.Document.Sessions, session => session.Id == "BRK071");
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
        Assert.Equal(8, (await ReadCacheAsync()).SchemaVersion);
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
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(9)]
    public async Task UnsupportedCacheIsRejectedUntilMicrosoftRefreshSucceedsWithoutChangingBookmarks(int schema)
    {
        var original = await Repository().RefreshAsync();
        var cache = await ReadCacheAsync();
        await WriteCacheAsync(cache with { SchemaVersion = schema });
        var bytes = await File.ReadAllBytesAsync(CachePath);
        var schedulePath = Path.Combine(directory, "schedule.json");
        var schedule = new JsonScheduleStore(schedulePath);
        await schedule.WriteAsync([original.Document.Sessions[0].Id, "unavailable-id"]);
        var scheduleBytes = await File.ReadAllBytesAsync(schedulePath);
        handler.Requests.Clear();
        handler.Respond = (_, _) => throw new HttpRequestException("Offline");
        var repository = Repository();

        var loaded = await repository.LoadAsync();

        Assert.Equal(ConferenceRepository.CacheSource, Assert.Single(loaded.Issues).Source);
        Assert.False(loaded.IsCached);
        Assert.Empty(loaded.Document.Sessions);
        Assert.Empty(loaded.Document.Speakers);
        Assert.Empty(loaded.Document.WithdrawnSessions);
        Assert.Empty(loaded.Document.WithdrawnSpeakers);
        Assert.Empty(handler.Requests);
        var offline = await repository.RefreshAsync();
        Assert.Equal(2, offline.Issues.Count);
        Assert.Empty(offline.Document.Sessions);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(CachePath));
        Assert.Equal(scheduleBytes, await File.ReadAllBytesAsync(schedulePath));

        handler.Respond = (request, _) =>
        {
            Assert.Empty(request.Headers.IfNoneMatch);
            Assert.Null(request.Headers.IfModifiedSince);
            return Task.FromResult(Ok(Json(SizedMicrosoft(70))));
        };
        var refreshed = await repository.RefreshAsync();
        Assert.Empty(refreshed.Issues);
        Assert.Equal(70, refreshed.Document.Sessions.Count);
        Assert.Empty(refreshed.Document.WithdrawnSessions);
        Assert.Equal(8, (await ReadCacheAsync()).SchemaVersion);
        Assert.Equal(scheduleBytes, await File.ReadAllBytesAsync(schedulePath));
        AssertOnlyMicrosoftRequests();
        var restarted = await Repository().LoadAsync();
        Assert.Empty(restarted.Issues);
        Assert.Equal(Ids(refreshed.Document.Sessions), Ids(restarted.Document.Sessions));
    }

    [Theory]
    [InlineData("community")]
    [InlineData("sessionAliases")]
    [InlineData("legacySessionIds")]
    [InlineData("legacyActiveSessionIds")]
    [InlineData("legacyPayloadHash")]
    public async Task UnrecognizedCacheFieldsCannotIntroduceAnotherAgendaSource(string field)
    {
        await Repository().RefreshAsync();
        var json = JsonNode.Parse(await File.ReadAllTextAsync(CachePath))!.AsObject();
        json[field] = json["microsoft"]!.DeepClone();
        await File.WriteAllTextAsync(CachePath, json.ToJsonString());
        var bytes = await File.ReadAllBytesAsync(CachePath);
        handler.Requests.Clear();

        var result = await Repository().LoadAsync();

        Assert.Equal(ConferenceRepository.CacheSource, Assert.Single(result.Issues).Source);
        Assert.Empty(result.Document.Sessions);
        Assert.Empty(result.Document.Speakers);
        Assert.Empty(result.Document.WithdrawnSessions);
        Assert.Empty(result.Document.WithdrawnSpeakers);
        Assert.Empty(handler.Requests);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(CachePath));
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("microsoft")]
    [InlineData("session")]
    [InlineData("speaker")]
    public async Task UnrecognizedNestedCacheFieldsAreRejected(string target)
    {
        await Repository().RefreshAsync();
        var json = JsonNode.Parse(await File.ReadAllTextAsync(CachePath))!;
        var node = target switch
        {
            "metadata" => json["metadata"],
            "microsoft" => json["microsoft"],
            "session" => json["microsoft"]!["sessions"]![0],
            "speaker" => json["microsoft"]!["speakers"]![0],
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        node!.AsObject()["unexpected"] = true;
        await File.WriteAllTextAsync(CachePath, json.ToJsonString());
        var bytes = await File.ReadAllBytesAsync(CachePath);
        handler.Requests.Clear();

        var result = await Repository().LoadAsync();

        Assert.Equal(ConferenceRepository.CacheSource, Assert.Single(result.Issues).Source);
        Assert.Empty(result.Document.Sessions);
        Assert.Empty(result.Document.Speakers);
        Assert.Empty(handler.Requests);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(CachePath));
    }

    [Fact]
    public async Task CancellationWhenTheClockIsReadKeepsLastGoodCacheAndAllowsRetry()
    {
        var repository = Repository();
        var original = await repository.RefreshAsync();
        var bytes = await File.ReadAllBytesAsync(CachePath);
        Serve(SizedMicrosoft(70), "\"v2\"");
        using var cancellation = new CancellationTokenSource();
        clock.OnRead = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.RefreshAsync(cancellation.Token));
        clock.OnRead = null;
        Assert.Equal(bytes, await File.ReadAllBytesAsync(CachePath));
        var snapshot = await repository.LoadAsync();
        Assert.Equal(Ids(original.Document.Sessions), Ids(snapshot.Document.Sessions));
        Assert.Empty(snapshot.Document.WithdrawnSessions);

        var later = await repository.RefreshAsync();
        Assert.Empty(later.Issues);
        Assert.Equal(Ids(original.Document.Sessions), Ids(later.Document.Sessions));
        Assert.Empty(later.Document.WithdrawnSessions);
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
        public ConferenceDocument Document { get; set; } = Previous with { Sessions = [], Speakers = [] };
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
