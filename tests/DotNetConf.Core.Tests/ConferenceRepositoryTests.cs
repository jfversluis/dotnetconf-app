using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotNetConf.Core;
using static DotNetConf.Core.Tests.RemoteAgendaTests;

namespace DotNetConf.Core.Tests;

public sealed class ConferenceRepositoryTests : IDisposable
{
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

    private static HttpResponseMessage DefaultResponse(HttpRequestMessage request) =>
        Ok(request.RequestUri!.Host == "sessionize.com" ? Json(Community) : Json(RemoteAgendaTests.Microsoft));

    private async Task<ConferenceCache> ReadCacheAsync() =>
        JsonSerializer.Deserialize(await File.ReadAllTextAsync(CachePath), RemoteAgendaJsonContext.Default.ConferenceCache)!;

    private async Task WriteCacheAsync(ConferenceCache cache)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(CachePath, JsonSerializer.Serialize(cache, RemoteAgendaJsonContext.Default.ConferenceCache));
    }

    private static MicrosoftAgenda SizedMicrosoft(int count)
    {
        var data = RemoteAgendaTests.Microsoft;
        return data with
        {
            Sessions = Enumerable.Range(1, count).Select(number => data.Sessions[0] with
            {
                Id = $"BRK{number:D3}", Title = $"Microsoft session {number}"
            }).ToList()
        };
    }

    private static SessionizeDocument SizedCommunity(int count)
    {
        var data = Community;
        return data with
        {
            Sessions = Enumerable.Range(1, count).Select(number => data.Sessions[0] with
            {
                Id = (30000 + number).ToString(), Title = $"Community session {number}"
            }).ToList()
        };
    }

    [Theory]
    [InlineData(true, 34, 1, false)]
    [InlineData(true, 34, 25, false)]
    [InlineData(true, 34, 26, true)]
    [InlineData(true, 34, 33, true)]
    [InlineData(false, 36, 1, false)]
    [InlineData(false, 36, 26, false)]
    [InlineData(false, 36, 27, true)]
    [InlineData(false, 36, 35, true)]
    public async Task ContractionGuardRejectsMoreThanOneQuarterButAllowsBoundaryAndSmallWithdrawals(
        bool microsoft, int previousCount, int nextCount, bool accepted)
    {
        var repository = Repository();
        handler.Respond = (request, _) => Task.FromResult(Ok(request.RequestUri!.Host == "sessionize.com"
            ? Json(SizedCommunity(36)) : Json(SizedMicrosoft(34))));
        await repository.RefreshAsync();
        clock.Advance();
        handler.Respond = (request, _) => Task.FromResult(
            (request.RequestUri!.Host == "sessionize.com") == !microsoft
                ? Ok(microsoft ? Json(SizedMicrosoft(nextCount)) : Json(SizedCommunity(nextCount)), "\"v2\"")
                : new HttpResponseMessage(HttpStatusCode.NotModified));
        var result = await repository.RefreshAsync();
        var source = microsoft ? ConferenceRepository.MicrosoftSource : ConferenceRepository.CommunitySource;
        if (accepted)
        {
            Assert.Empty(result.Issues);
            Assert.Equal(previousCount - nextCount, result.Document.WithdrawnSessions.Count);
            Assert.Equal(clock.Now, result.LastCheckedAt);
        }
        else
        {
            var issue = Assert.Single(result.Issues);
            Assert.Equal(source, issue.Source);
            Assert.IsType<InvalidDataException>(issue.Exception);
            Assert.Contains("greater than 25%", issue.Exception.Message);
            Assert.Empty(result.Document.WithdrawnSessions);
        }
        var cache = await ReadCacheAsync();
        var changed = microsoft ? cache.Microsoft : cache.Community;
        Assert.Equal(accepted ? nextCount : previousCount, changed.Sessions.Count);
        Assert.Equal(accepted ? "\"v2\"" : "\"v1\"", changed.ETag);
        var restart = await Repository().LoadAsync();
        Assert.Equal(result.Document.Sessions.Count, restart.Document.Sessions.Count);
        Assert.Equal(result.Document.WithdrawnSessions.Count, restart.Document.WithdrawnSessions.Count);
    }

    [Fact]
    public async Task FourDayMicrosoftUpdatePersistsWithoutChangingExistingIdsOrWithdrawingSessions()
    {
        var repository = Repository();
        var original = await repository.RefreshAsync();
        var oldMicrosoft = Assert.Single((await ReadCacheAsync()).Microsoft.Sessions);
        clock.Advance();
        var microsoft = RemoteAgendaTests.Microsoft;
        handler.Respond = (request, _) => Task.FromResult(request.RequestUri!.Host == "sessionize.com"
            ? new HttpResponseMessage(HttpStatusCode.NotModified)
            : Ok(Json(microsoft with
            {
                Sessions =
                [
                    microsoft.Sessions[0],
                    microsoft.Sessions[0] with { Id = "BRK032", Day = 2, Title = "Synthetic day two" },
                    microsoft.Sessions[0] with { Id = "BRK100", Day = 3, Title = "Synthetic day three" },
                    microsoft.Sessions[0] with { Id = "BRK135", Day = 4, Title = "Synthetic day four" }
                ]
            }), "\"four-days\""));

        var updated = await repository.RefreshAsync();
        Assert.Empty(updated.Issues);
        Assert.Empty(updated.Document.WithdrawnSessions);
        Assert.Equal(original.Document.Sessions.Count + 3, updated.Document.Sessions.Count);
        Assert.Equivalent(oldMicrosoft, updated.Document.Sessions.Single(session => session.Id == oldMicrosoft.Id));
        Assert.Equal(clock.Now, updated.LastCheckedAt);
        var cache = await ReadCacheAsync();
        Assert.Equal("\"four-days\"", cache.Microsoft.ETag);
        Assert.Equal(4, cache.Microsoft.Sessions.Count);
        Assert.Equal(4, cache.Microsoft.Sessions.Select(session => session.StartsAt.Date).Distinct().Count());
        var restarted = await Repository().LoadAsync();
        Assert.Empty(restarted.Issues);
        Assert.Equivalent(updated.Document, restarted.Document);
    }

    [Fact]
    public async Task FirstRefreshAcceptsValidRemoteDataWithoutAnInventedBundledCount()
    {
        handler.Respond = (request, _) => Task.FromResult(Ok(request.RequestUri!.Host == "sessionize.com"
            ? Json(SizedCommunity(36)) : Json(SizedMicrosoft(1))));
        var result = await Repository().RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(37, result.Document.Sessions.Count);
        Assert.Empty(result.Document.WithdrawnSessions);
        Assert.Equal(clock.Now, result.LastCheckedAt);
        var cache = await ReadCacheAsync();
        Assert.Single(cache.Microsoft.Sessions);
        Assert.Equal(clock.Now, cache.Microsoft.LastCheckedAt);
        Assert.NotEmpty(cache.Microsoft.PayloadHash);
        Assert.Equal(clock.Now, cache.Community.LastCheckedAt);
        Assert.Equal(37, (await Repository().LoadAsync()).Document.Sessions.Count);
    }

    [Fact]
    public async Task TruncatedSourceCannotWithdrawDataButOtherChangedSourcePublishesAndPersists()
    {
        var repository = Repository();
        handler.Respond = (request, _) => Task.FromResult(Ok(request.RequestUri!.Host == "sessionize.com"
            ? Json(SizedCommunity(36)) : Json(SizedMicrosoft(34))));
        await repository.RefreshAsync();
        var oldCheckedAt = clock.Now;
        var oldMicrosoft = (await ReadCacheAsync()).Microsoft;
        clock.Advance();
        var community = SizedCommunity(36);
        handler.Respond = (request, _) => Task.FromResult(Ok(request.RequestUri!.Host == "sessionize.com"
            ? Json(community with
            {
                Sessions = [community.Sessions[0] with { Title = "Valid community update" }, .. community.Sessions.Skip(1)]
            })
            : Json(SizedMicrosoft(1)), "\"v2\""));
        var result = await repository.RefreshAsync();
        Assert.Equal(ConferenceRepository.MicrosoftSource, Assert.Single(result.Issues).Source);
        Assert.Equal(oldCheckedAt, result.LastCheckedAt);
        Assert.Equal(clock.Now, result.LastUpdatedAt);
        Assert.Empty(result.Document.WithdrawnSessions);
        Assert.Contains(result.Document.Sessions, session => session.Title == "Valid community update");
        var cache = await ReadCacheAsync();
        Assert.Equal(oldMicrosoft.PayloadHash, cache.Microsoft.PayloadHash);
        Assert.Equal(oldMicrosoft.LastCheckedAt, cache.Microsoft.LastCheckedAt);
        Assert.Equal(oldMicrosoft.ETag, cache.Microsoft.ETag);
        Assert.Equal(34, cache.Microsoft.Sessions.Count);
        Assert.Equal("\"v2\"", cache.Community.ETag);
        var restart = await Repository().LoadAsync();
        Assert.Equal(70, restart.Document.Sessions.Count);
        Assert.Contains(restart.Document.Sessions, session => session.Title == "Valid community update");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IdenticalCompleteReductionIsConfirmedAcrossRestartsWithoutTrustingItsFirstValidators(bool microsoft)
    {
        handler.Respond = (request, _) => Task.FromResult(Ok(request.RequestUri!.Host == "sessionize.com"
            ? Json(SizedCommunity(36)) : Json(SizedMicrosoft(34))));
        await Repository().RefreshAsync();
        clock.Advance();
        handler.Respond = (request, _) =>
        {
            Assert.Equal("\"v1\"", Assert.Single(request.Headers.IfNoneMatch).ToString());
            return Task.FromResult((request.RequestUri!.Host == "sessionize.com") == !microsoft
                ? Ok(microsoft ? Json(SizedMicrosoft(1)) : Json(SizedCommunity(1)), "\"reduced\"")
                : new HttpResponseMessage(HttpStatusCode.NotModified));
        };
        var first = await Repository().RefreshAsync();
        Assert.Single(first.Issues);
        Assert.Equal(70, first.Document.Sessions.Count);
        var pendingCache = await ReadCacheAsync();
        var pending = microsoft ? pendingCache.Microsoft : pendingCache.Community;
        Assert.NotNull(pending.PendingReductionHash);
        Assert.Equal("\"v1\"", pending.ETag);
        Assert.Equal(clock.Now.AddHours(-1), pending.LastCheckedAt);
        clock.Advance();
        var confirmed = await Repository().RefreshAsync();
        Assert.Empty(confirmed.Issues);
        Assert.Equal(microsoft ? 37 : 35, confirmed.Document.Sessions.Count);
        Assert.Equal(microsoft ? 33 : 35, confirmed.Document.WithdrawnSessions.Count);
        Assert.Equal(clock.Now, confirmed.LastUpdatedAt);
        var confirmedCache = await ReadCacheAsync();
        var current = microsoft ? confirmedCache.Microsoft : confirmedCache.Community;
        Assert.Null(current.PendingReductionHash);
        Assert.Equal(pending.PendingReductionHash, current.PayloadHash);
        Assert.Equal("\"reduced\"", current.ETag);
        var restarted = await Repository().LoadAsync();
        Assert.Equivalent(confirmed.Document.Sessions, restarted.Document.Sessions);
        Assert.Equivalent(confirmed.Document.WithdrawnSessions, restarted.Document.WithdrawnSessions);
    }

    [Fact]
    public async Task DifferentReductionsEachRequireTheirOwnIdenticalConfirmation()
    {
        handler.Respond = (request, _) => Task.FromResult(Ok(request.RequestUri!.Host == "sessionize.com"
            ? Json(SizedCommunity(36)) : Json(SizedMicrosoft(34))));
        await Repository().RefreshAsync();
        foreach (var count in new[] { 1, 2, 1 })
        {
            handler.Respond = (request, _) => Task.FromResult(request.RequestUri!.Host == "sessionize.com"
                ? new HttpResponseMessage(HttpStatusCode.NotModified) : Ok(Json(SizedMicrosoft(count)), "\"reduced\""));
            Assert.Single((await Repository().RefreshAsync()).Issues);
            Assert.Equal(34, (await ReadCacheAsync()).Microsoft.Sessions.Count);
        }
        Assert.Empty((await Repository().RefreshAsync()).Issues);
        Assert.Single((await ReadCacheAsync()).Microsoft.Sessions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidOrNotModifiedResponseClearsPendingReductionConfirmation(bool malformed)
    {
        handler.Respond = (request, _) => Task.FromResult(Ok(request.RequestUri!.Host == "sessionize.com"
            ? Json(SizedCommunity(36)) : Json(SizedMicrosoft(34))));
        await Repository().RefreshAsync();
        Task<HttpResponseMessage> Reduced(HttpRequestMessage request, CancellationToken _) =>
            Task.FromResult(request.RequestUri!.Host == "sessionize.com"
                ? new HttpResponseMessage(HttpStatusCode.NotModified) : Ok(Json(SizedMicrosoft(1)), "\"reduced\""));
        handler.Respond = Reduced;
        Assert.Single((await Repository().RefreshAsync()).Issues);
        handler.Respond = (request, _) => Task.FromResult(request.RequestUri!.Host == "sessionize.com"
            ? new HttpResponseMessage(HttpStatusCode.NotModified)
            : malformed ? Ok("{}") : new HttpResponseMessage(HttpStatusCode.NotModified));
        await Repository().RefreshAsync();
        Assert.Null((await ReadCacheAsync()).Microsoft.PendingReductionHash);
        handler.Respond = Reduced;
        Assert.Single((await Repository().RefreshAsync()).Issues);
        Assert.Equal(34, (await ReadCacheAsync()).Microsoft.Sessions.Count);
    }

    [Fact]
    public async Task RepeatedEmptyResponsesNeverConfirmTotalAgendaRemoval()
    {
        await Repository().RefreshAsync();
        var old = await File.ReadAllBytesAsync(CachePath);
        handler.Respond = (_, _) => Task.FromResult(Ok("{}"));
        for (var attempt = 0; attempt < 2; attempt++)
            Assert.Equal(2, (await Repository().RefreshAsync()).Issues.Count);
        Assert.Equal(old, await File.ReadAllBytesAsync(CachePath));
    }

    [Fact]
    public async Task PublicFixtureSessionAndLegacySpeakerIdentitiesSurviveBothRemoteAdapters()
    {
        await using var metadataStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.json"));
        var metadata = await ConferenceJson.ReadAsync(metadataStream);
        var agendaBytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "sessionize.json"));
        var wire = JsonSerializer.Deserialize(agendaBytes, SessionizeJsonContext.Default.SessionizeDocument)!;
        using var agendaStream = new MemoryStream(agendaBytes);
        var document = await SessionizeJson.ReadAsync(agendaStream, metadata,
            TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"));
        this.metadata.Document = document with { Sessions = [], Speakers = [] };
        var microsoftSessions = document.Sessions.Where(session => session.Id.StartsWith("2026", StringComparison.Ordinal)).ToList();
        var speakerIds = microsoftSessions.SelectMany(session => session.SpeakerIds).ToHashSet(StringComparer.Ordinal);
        var slugs = document.Speakers.Where(speaker => speakerIds.Contains(speaker.Id)).ToDictionary(
            speaker => speaker.Id,
            speaker => "speaker-" + speaker.Name.ToLowerInvariant().Replace('ö', 'o').Replace(' ', '-'));
        var microsoft = new MicrosoftAgenda
        {
            Sessions = microsoftSessions.Select(session => new MicrosoftSession
            {
                Id = session.Id.StartsWith("202600", StringComparison.Ordinal)
                    ? $"KEY{int.Parse(session.Id[6..]):D3}" : $"BRK{int.Parse(session.Id[5..]):D3}",
                Title = session.Title, Day = session.StartsAt.Day - 9,
                StartTime = session.StartsAt.ToString("HH:mm:ss"), EndTime = session.EndsAt.ToString("HH:mm:ss"),
                Timezone = "PST", SpeakerIds = session.SpeakerIds.Select(id => slugs[id]).ToList(),
                Abstract = session.Description, Topics = session.Topics
            }).ToList(),
            Speakers = document.Speakers.Where(speaker => speakerIds.Contains(speaker.Id)).Select(speaker => new MicrosoftSpeaker
            {
                Id = slugs[speaker.Id], Name = "Renamed " + speaker.Name, PrimaryBio = speaker.Bio, Avatar = speaker.ProfilePicture
            }).ToList()
        };
        var community = wire with
        {
            Sessions = wire.Sessions.Where(session => !session.Id.StartsWith("2026", StringComparison.Ordinal)).ToList(),
            Speakers = wire.Speakers.Where(speaker => !speakerIds.Contains(speaker.Id)).ToList()
        };
        handler.Respond = (request, _) => Task.FromResult(Ok(request.RequestUri!.Host == "sessionize.com"
            ? Json(community) : Json(microsoft)));
        var result = await Repository().RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(70, result.Document.Sessions.Count);
        Assert.Equal(70, result.Document.Speakers.Count);
        Assert.Equal(34, microsoft.Sessions.Count);
        Assert.Equal(36, community.Sessions.Count);
        Assert.Equal(document.Sessions.Select(session => session.Id).Order(), result.Document.Sessions.Select(session => session.Id).Order());
        Assert.Equal(document.Speakers.Select(speaker => speaker.Id).Order(), result.Document.Speakers.Select(speaker => speaker.Id).Order());
        Assert.All(result.Document.Sessions, session =>
        {
            var old = Assert.Single(document.Sessions, prior => prior.Id == session.Id);
            Assert.Equal(old.SpeakerIds, session.SpeakerIds);
            Assert.Equal(old.StartsAt, session.StartsAt);
            Assert.Equal(old.EndsAt, session.EndsAt);
        });
    }

    [Fact]
    public async Task ChangedSourcesUseOneCheckTimestampWhenClockAdvancesBetweenEveryRead()
    {
        var advancingClock = new AdvancingClock();
        var microsoft = RemoteAgendaTests.Microsoft;
        var community = Community;
        handler.Respond = (request, _) => Task.FromResult(Ok(request.RequestUri!.Host == "sessionize.com"
            ? Json(community with { Sessions = [community.Sessions[0] with { Title = "Changed community" }] })
            : Json(microsoft with { Sessions = [microsoft.Sessions[0] with { Title = "Changed Microsoft" }] })));
        var repository = new ConferenceRepository(metadata, client, CachePath, advancingClock);
        var result = await repository.RefreshAsync();
        Assert.Empty(result.Issues);
        var cache = await ReadCacheAsync();
        foreach (var source in new[] { cache.Microsoft, cache.Community })
        {
            Assert.NotNull(source.LastCheckedAt);
            Assert.Equal(source.LastCheckedAt, source.LastUpdatedAt);
            Assert.NotEmpty(source.PayloadHash);
        }
        Assert.Equal(2, advancingClock.Reads);
        var restart = await Repository().LoadAsync();
        Assert.True(restart.IsCached);
        Assert.Empty(restart.Issues);
        Assert.Equal(result.LastUpdatedAt, restart.LastUpdatedAt);
    }

    [Fact]
    public async Task CachedCommunityAndMicrosoftSeedAcceptChangedMicrosoftWithAnAdvancingClock()
    {
        handler.Respond = (request, _) => Task.FromResult(request.RequestUri!.Host == "sessionize.com"
            ? Ok(Json(SizedCommunity(36)), null)
            : new HttpResponseMessage(HttpStatusCode.BadRequest));
        var partial = await Repository().RefreshAsync();
        Assert.Equal(ConferenceRepository.MicrosoftSource, Assert.Single(partial.Issues).Source);
        var initialCache = await ReadCacheAsync();
        Assert.Empty(initialCache.Microsoft.PayloadHash);
        Assert.Null(initialCache.Microsoft.LastCheckedAt);
        Assert.Null(initialCache.Microsoft.LastUpdatedAt);
        Assert.NotEmpty(initialCache.Community.PayloadHash);
        Assert.NotNull(initialCache.Community.LastCheckedAt);

        var microsoft = SizedMicrosoft(34);
        handler.Respond = (request, _) =>
        {
            if (request.RequestUri!.Host == "sessionize.com")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            Assert.True(request.Headers.CacheControl!.NoCache);
            Assert.Empty(request.Headers.IfNoneMatch);
            var response = Ok(Json(microsoft with
            {
                Sessions = [microsoft.Sessions[0] with { Title = "Updated Microsoft session" }, .. microsoft.Sessions.Skip(1)]
            }), "\"0x8DF355A00F5D9BA\"");
            response.Content.Headers.LastModified = DateTimeOffset.Parse("2026-10-29T01:14:38Z");
            return Task.FromResult(response);
        };
        var repository = new ConferenceRepository(metadata, client, CachePath, new AdvancingClock());
        Assert.True((await repository.LoadAsync()).IsCached);
        var refreshed = await repository.RefreshAsync();
        Assert.Empty(refreshed.Issues);
        Assert.Equal(70, refreshed.Document.Sessions.Count);
        var persisted = await ReadCacheAsync();
        Assert.NotEmpty(persisted.Microsoft.PayloadHash);
        Assert.Equal("\"0x8DF355A00F5D9BA\"", persisted.Microsoft.ETag);
        Assert.NotNull(persisted.Microsoft.LastCheckedAt);
        Assert.Equal(persisted.Microsoft.LastCheckedAt, persisted.Microsoft.LastUpdatedAt);
        Assert.Equal(initialCache.Community.PayloadHash, persisted.Community.PayloadHash);
        var restart = await Repository().LoadAsync();
        Assert.True(restart.IsCached);
        Assert.Empty(restart.Issues);
        Assert.Contains(restart.Document.Sessions, session => session.Title == "Updated Microsoft session");
    }

    [Fact]
    public async Task ChangedResponseAfterClockRegressionUsesItsNewObservedInstant()
    {
        var repository = Repository();
        var microsoft = RemoteAgendaTests.Microsoft;
        var community = Community;
        handler.Respond = (request, _) => Task.FromResult(Ok(request.RequestUri!.Host == "sessionize.com"
            ? Json(community with { Sessions = [community.Sessions[0] with { Title = "First community update" }] })
            : Json(microsoft with { Sessions = [microsoft.Sessions[0] with { Title = "First Microsoft update" }] })));
        await repository.RefreshAsync();
        clock.Rewind();
        handler.Respond = (request, _) => Task.FromResult(Ok(request.RequestUri!.Host == "sessionize.com"
            ? Json(community with { Sessions = [community.Sessions[0] with { Title = "Second community update" }] })
            : Json(microsoft with { Sessions = [microsoft.Sessions[0] with { Title = "Second Microsoft update" }] })));
        var result = await repository.RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(clock.Now, result.LastCheckedAt);
        Assert.Equal(clock.Now, result.LastUpdatedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnchangedResponsesClampCheckTimeToRetainedUpdateAfterClockRegression(bool notModified)
    {
        var microsoft = RemoteAgendaTests.Microsoft;
        var community = Community;
        var microsoftJson = Json(microsoft with { Sessions = [microsoft.Sessions[0] with { Title = "Updated Microsoft" }] });
        var communityJson = Json(community with { Sessions = [community.Sessions[0] with { Title = "Updated community" }] });
        handler.Respond = (request, _) => Task.FromResult(Ok(request.RequestUri!.Host == "sessionize.com"
            ? communityJson : microsoftJson));
        var repository = Repository();
        var updated = await repository.RefreshAsync();
        Assert.Empty(updated.Issues);
        var updatedAt = clock.Now;
        Assert.Equal(updatedAt, updated.LastUpdatedAt);
        clock.Rewind();
        handler.Respond = (request, _) => Task.FromResult(notModified
            ? new HttpResponseMessage(HttpStatusCode.NotModified)
            : Ok(request.RequestUri!.Host == "sessionize.com" ? communityJson : microsoftJson));
        var result = await repository.RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(updatedAt, result.LastCheckedAt);
        Assert.Equal(updatedAt, result.LastUpdatedAt);
        Assert.True(result.LastCheckedAt > clock.Now);
        var cache = await ReadCacheAsync();
        foreach (var source in new[] { cache.Microsoft, cache.Community })
        {
            Assert.Equal(updatedAt, source.LastCheckedAt);
            Assert.Equal(updatedAt, source.LastUpdatedAt);
        }
        var restart = await Repository().LoadAsync();
        Assert.Empty(restart.Issues);
        Assert.True(restart.IsCached);
        Assert.Equal(updatedAt, restart.LastCheckedAt);
        Assert.Equal(updatedAt, restart.LastUpdatedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnchangedResponsesKeepFirstDownloadTimeWhenClockRegresses(bool notModified)
    {
        var repository = Repository();
        var first = await repository.RefreshAsync();
        clock.Rewind();
        handler.Respond = (request, _) => Task.FromResult(notModified
            ? new HttpResponseMessage(HttpStatusCode.NotModified) : DefaultResponse(request));
        var result = await repository.RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(first.LastUpdatedAt, result.LastCheckedAt);
        Assert.Equal(first.LastUpdatedAt, result.LastUpdatedAt);
    }

    [Fact]
    public async Task FirstLoadContainsOnlyMetadataWithoutNetworkOrCacheWrite()
    {
        var result = await Repository().LoadAsync();
        Assert.Empty(result.Document.Sessions);
        Assert.Empty(result.Document.Speakers);
        Assert.Empty(result.Issues);
        Assert.False(result.IsCached);
        Assert.Null(result.LastCheckedAt);
        Assert.Null(result.LastUpdatedAt);
        Assert.Empty(handler.Requests);
        Assert.Equal(1, metadata.Calls);
        Assert.False(File.Exists(CachePath));
    }

    [Fact]
    public async Task FreshOfflineRefreshNeverInventsSessionsOrProfiles()
    {
        handler.Respond = (_, _) => throw new HttpRequestException("Offline");
        var result = await Repository().RefreshAsync();
        Assert.Empty(result.Document.Sessions);
        Assert.Empty(result.Document.Speakers);
        Assert.Equal(2, result.Issues.Count);
        Assert.Null(result.LastCheckedAt);
        Assert.False(File.Exists(CachePath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SchemaTwoMigrationKeepsOnlyDownloadedSourcesAndNeverMetadataAgenda(bool microsoftDownloaded)
    {
        await Repository().RefreshAsync();
        var prior = await ReadCacheAsync();
        static CachedAgenda Seeded(CachedAgenda source) => source with
        {
            PayloadHash = "", ETag = null, LastModified = null, LastCheckedAt = null, LastUpdatedAt = null
        };
        await WriteCacheAsync(prior with
        {
            SchemaVersion = 2,
            Metadata = Bundle,
            Microsoft = microsoftDownloaded ? prior.Microsoft : Seeded(prior.Microsoft),
            Community = microsoftDownloaded ? Seeded(prior.Community) : prior.Community
        });
        var requests = handler.Requests.Count;
        var result = await Repository().LoadAsync();
        Assert.Empty(result.Issues);
        Assert.True(result.IsCached);
        Assert.Equal(microsoftDownloaded ? "20261006" : "1234", Assert.Single(result.Document.Sessions).Id);
        Assert.Single(result.Document.Speakers);
        Assert.Null(result.LastCheckedAt);
        Assert.Equal(requests, handler.Requests.Count);
        var migrated = await ReadCacheAsync();
        Assert.Equal(5, migrated.SchemaVersion);
        Assert.Empty(migrated.Metadata.Speakers);
        Assert.Empty(migrated.Metadata.Sessions);
        Assert.Empty((microsoftDownloaded ? migrated.Community : migrated.Microsoft).Sessions);
        Assert.Equal(result.Document.Sessions.Select(session => (session.Id, session.Title, session.Description)),
            (await Repository().LoadAsync()).Document.Sessions.Select(session => (session.Id, session.Title, session.Description)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SchemaTwoMigrationPreservesDownloadedOfflineDataAndRejectsUndownloadedSeeds(bool downloaded)
    {
        await Repository().RefreshAsync();
        var old = await ReadCacheAsync();
        await WriteCacheAsync(old with
        {
            SchemaVersion = 2, Metadata = Bundle,
            Microsoft = downloaded ? old.Microsoft : old.Microsoft with
            {
                PayloadHash = "", LastCheckedAt = null, LastUpdatedAt = null, ETag = null, LastModified = null
            },
            Community = downloaded ? old.Community : old.Community with
            {
                PayloadHash = "", LastCheckedAt = null, LastUpdatedAt = null, ETag = null, LastModified = null
            }
        });
        var requests = handler.Requests.Count;
        var result = await Repository().LoadAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(downloaded ? 2 : 0, result.Document.Sessions.Count);
        Assert.Equal(downloaded ? old.Microsoft.Speakers.Count + old.Community.Speakers.Count : 0,
            result.Document.Speakers.Count);
        Assert.Equal(requests, handler.Requests.Count);
        Assert.Empty((await ReadCacheAsync()).Metadata.Sessions);
        Assert.Equal(result.Document.Sessions.Select(session => (session.Id, session.Title, session.Description)),
            (await Repository().LoadAsync()).Document.Sessions.Select(session => (session.Id, session.Title, session.Description)));
    }

    [Fact]
    public async Task MetadataCannotSeedSessionsOrSpeakerProfiles()
    {
        metadata.Document = Bundle;
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Repository().LoadAsync());
        Assert.Contains("must not contain", error.Message);
        Assert.Empty(handler.Requests);
        Assert.False(File.Exists(CachePath));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task UpgradeRemovesLegacyCommunityExamplesWithoutLosingDownloadedAgenda(int schema)
    {
        await Repository().RefreshAsync();
        var old = await ReadCacheAsync();
        await using var fixture = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var examples = await ConferenceJson.ReadAsync(fixture);
        metadata.Document = metadata.Document with { CommunityEventsAreMock = false };
        await WriteCacheAsync(old with
        {
            SchemaVersion = schema,
            Metadata = old.Metadata with { CommunityEvents = examples.CommunityEvents, CommunityEventsAreMock = true }
        });
        var requests = handler.Requests.Count;
        var upgraded = await Repository().LoadAsync();
        Assert.Empty(upgraded.Issues);
        Assert.Equal(2, upgraded.Document.Sessions.Count);
        Assert.Equal(old.Microsoft.PayloadHash, (await ReadCacheAsync()).Microsoft.PayloadHash);
        Assert.Equal(old.Community.PayloadHash, (await ReadCacheAsync()).Community.PayloadHash);
        Assert.Empty(upgraded.Document.CommunityEvents);
        Assert.False(upgraded.Document.CommunityEventsAreMock);
        Assert.Empty((await ReadCacheAsync()).Metadata.CommunityEvents);
        Assert.Equal(requests, handler.Requests.Count);
        Assert.Empty((await Repository().LoadAsync()).Document.CommunityEvents);
    }

    [Fact]
    public async Task MetadataCannotReintroduceLocalCommunityExamples()
    {
        await using var fixture = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var examples = await ConferenceJson.ReadAsync(fixture);
        metadata.Document = metadata.Document with { CommunityEvents = examples.CommunityEvents };
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Repository().LoadAsync());
        Assert.Contains("community events", error.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RefreshPersistsVersionedSourceGeneratedCacheAndRestartDoesNotLoadMetadataOrNetwork()
    {
        var result = await Repository().RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(clock.Now, result.LastCheckedAt);
        Assert.Equal(clock.Now, result.LastUpdatedAt);
        var cache = await ReadCacheAsync();
        Assert.Equal(5, cache.SchemaVersion);
        Assert.Empty(cache.Metadata.Sessions);
        Assert.Empty(cache.Metadata.Speakers);
        Assert.Equal(64, cache.Microsoft.PayloadHash.Length);
        Assert.Equal(64, cache.Community.PayloadHash.Length);
        Assert.Equal("\"v1\"", cache.Microsoft.ETag);
        Assert.NotNull(cache.Community.LastModified);
        var requests = handler.Requests.Count;
        var calls = metadata.Calls;
        var restart = await Repository().LoadAsync();
        Assert.True(restart.IsCached);
        Assert.Equal(result.Document, restart.Document with
        {
            Speakers = result.Document.Speakers, Sessions = result.Document.Sessions,
            WithdrawnSessions = result.Document.WithdrawnSessions,
            WithdrawnSpeakers = result.Document.WithdrawnSpeakers,
            CommunityEvents = result.Document.CommunityEvents
        });
        Assert.Equal(requests, handler.Requests.Count);
        Assert.Equal(calls, metadata.Calls);
    }

    [Fact]
    public async Task LegacyPortraitAssetFieldsDoNotBreakCachedAgendaAndAreDroppedOnRefresh()
    {
        var original = await Repository().RefreshAsync();
        var json = JsonNode.Parse(await File.ReadAllTextAsync(CachePath))!;
        json["metadata"]!["speakerProfilePictureAssets"] = new JsonObject
        {
            [GeraldId] = "speaker_obsolete.jpg"
        };
        foreach (var source in new[] { "metadata", "microsoft", "community" })
            foreach (var speaker in json[source]!["speakers"]!.AsArray())
                speaker!["profilePictureAsset"] = "speaker_obsolete.jpg";
        await File.WriteAllTextAsync(CachePath, json.ToJsonString());

        var requests = handler.Requests.Count;
        var calls = metadata.Calls;
        var repository = Repository();
        var restart = await repository.LoadAsync();
        Assert.True(restart.IsCached);
        Assert.Empty(restart.Issues);
        Assert.Equal(original.Document.Speakers, restart.Document.Speakers);
        Assert.Equal(original.Document.Sessions.Select(session => session.Id),
            restart.Document.Sessions.Select(session => session.Id));
        Assert.Equal(requests, handler.Requests.Count);
        Assert.Equal(calls, metadata.Calls);

        Assert.Empty((await repository.RefreshAsync()).Issues);
        Assert.DoesNotContain("pictureAsset", await File.ReadAllTextAsync(CachePath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LegacyEnrichmentIsReplacedOfflineWithoutChangingSavedIdsOrTrustingValidators()
    {
        await Repository().RefreshAsync();
        var old = await ReadCacheAsync();
        const string privateMarker = "Organizer-only legacy enrichment";
        static Speaker PrivateProfile(Speaker speaker) => speaker with
        {
            Bio = privateMarker, Tagline = privateMarker,
            ProfilePicture = "https://github.com/user-attachments/assets/legacy"
        };
        var legacy = old with
        {
            SchemaVersion = 1,
            Metadata = old.Metadata with { Speakers = old.Metadata.Speakers.Select(PrivateProfile).ToList() },
            Microsoft = old.Microsoft with
            {
                Speakers = old.Microsoft.Speakers.Select(PrivateProfile).ToList(),
                Sessions = old.Microsoft.Sessions.Select(session => session with
                {
                    Description = privateMarker, Topics = [privateMarker]
                }).ToList(),
                WithdrawnSessions = [old.Microsoft.Sessions[0] with { Id = "withdrawn", Description = privateMarker }]
            },
            Community = old.Community with { Speakers = old.Community.Speakers.Select(PrivateProfile).ToList() }
        };
        await WriteCacheAsync(legacy);
        var schedulePath = Path.Combine(directory, "schedule.json");
        var schedule = new JsonScheduleStore(schedulePath);
        await schedule.WriteAsync(["20261006", "1234", "withdrawn"]);
        var savedBytes = await File.ReadAllBytesAsync(schedulePath);
        var requests = handler.Requests.Count;

        var repository = Repository();
        var migrated = await repository.LoadAsync();
        Assert.Empty(migrated.Issues);
        Assert.False(migrated.IsCached);
        Assert.Equal(requests, handler.Requests.Count);
        Assert.Empty(migrated.Document.Speakers);
        Assert.Empty(migrated.Document.Sessions);
        Assert.Empty(migrated.Document.WithdrawnSessions);
        Assert.Null(migrated.LastCheckedAt);
        Assert.Null(migrated.LastUpdatedAt);
        Assert.Equal(savedBytes, await File.ReadAllBytesAsync(schedulePath));
        var persisted = await File.ReadAllTextAsync(CachePath);
        Assert.DoesNotContain(privateMarker, persisted);
        Assert.DoesNotContain("github.com", persisted);
        Assert.Equal(5, (await ReadCacheAsync()).SchemaVersion);
        Assert.Empty((await ReadCacheAsync()).Microsoft.PayloadHash);
        Assert.Null((await ReadCacheAsync()).Microsoft.ETag);

        var warm = await Repository().LoadAsync();
        Assert.True(warm.IsCached);
        Assert.Empty(warm.Document.Speakers);
        handler.Respond = (request, _) =>
        {
            Assert.Empty(request.Headers.IfNoneMatch);
            Assert.Null(request.Headers.IfModifiedSince);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
        };
        var rejected304 = await repository.RefreshAsync();
        Assert.Equal(2, rejected304.Issues.Count);
        Assert.Empty(rejected304.Document.Speakers);
        handler.Respond = (request, _) => Task.FromResult(DefaultResponse(request));
        Assert.Empty((await repository.RefreshAsync()).Issues);
        Assert.DoesNotContain(privateMarker, await File.ReadAllTextAsync(CachePath));
        Assert.Equal(savedBytes, await File.ReadAllBytesAsync(schedulePath));
    }

    [Fact]
    public async Task FailedLegacyMigrationPersistenceNeverDisplaysUntraceableData()
    {
        if (OperatingSystem.IsWindows())
            return;
        await Repository().RefreshAsync();
        var old = await ReadCacheAsync();
        await WriteCacheAsync(old with
        {
            SchemaVersion = 1,
            Microsoft = old.Microsoft with
            {
                Speakers = [old.Microsoft.Speakers[0] with { Bio = "Private legacy biography" }]
            }
        });
        var permissions = File.GetUnixFileMode(directory);
        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            var result = await Repository().LoadAsync();
            Assert.Equal(ConferenceRepository.CacheSource, Assert.Single(result.Issues).Source);
            Assert.Empty(result.Document.Speakers);
            Assert.Empty(result.Document.Sessions);
            Assert.Equal(1, (await ReadCacheAsync()).SchemaVersion);
        }
        finally { File.SetUnixFileMode(directory, permissions); }
    }

    [Fact]
    public async Task RevalidationIgnoresLongMaxAgeAndSendsValidators()
    {
        var repository = Repository();
        var first = await repository.RefreshAsync();
        clock.Advance();
        handler.Respond = (request, _) =>
        {
            Assert.True(request.Headers.CacheControl!.NoCache);
            Assert.Equal("\"v1\"", Assert.Single(request.Headers.IfNoneMatch).Tag);
            Assert.Equal(DateTimeOffset.Parse("2026-10-28T12:00:00Z"), request.Headers.IfModifiedSince);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
        };
        var result = await repository.RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(clock.Now, result.LastCheckedAt);
        Assert.Equal(first.LastUpdatedAt, result.LastUpdatedAt);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task NotModifiedResponseCanUpdateValidatorWithoutChangingContentTimestamp()
    {
        var repository = Repository();
        var first = await repository.RefreshAsync();
        clock.Advance();
        handler.Respond = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.NotModified);
            response.Headers.ETag = EntityTagHeaderValue.Parse("\"updated-validator\"");
            return Task.FromResult(response);
        };
        var result = await repository.RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(first.LastUpdatedAt, result.LastUpdatedAt);
        Assert.Equal("\"updated-validator\"", (await ReadCacheAsync()).Microsoft.ETag);
        Assert.Equal("\"updated-validator\"", (await ReadCacheAsync()).Community.ETag);
    }

    [Fact]
    public async Task NotModifiedWithoutValidatedRemoteDataIsAnError()
    {
        handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
        var result = await Repository().RefreshAsync();
        Assert.Equal(2, result.Issues.Count);
        Assert.All(result.Issues, issue => Assert.IsType<InvalidDataException>(issue.Exception));
        Assert.Empty(result.Document.Sessions);
        Assert.False(File.Exists(CachePath));
    }

    [Fact]
    public async Task HashComparisonWorksWithoutEtagsAndWhitespaceDoesNotChangeUpdatedTime()
    {
        handler.Respond = (request, _) => Task.FromResult(Ok(
            request.RequestUri!.Host == "sessionize.com" ? Json(Community) : Json(RemoteAgendaTests.Microsoft), null));
        var repository = Repository();
        var first = await repository.RefreshAsync();
        var originalHash = (await ReadCacheAsync()).Community.PayloadHash;
        clock.Advance();
        await repository.RefreshAsync();
        Assert.Equal(originalHash, (await ReadCacheAsync()).Community.PayloadHash);
        handler.Respond = (request, _) => Task.FromResult(Ok(
            "  " + (request.RequestUri!.Host == "sessionize.com" ? Json(Community) : Json(RemoteAgendaTests.Microsoft)), null));
        clock.Advance();
        var result = await repository.RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.NotEqual(originalHash, (await ReadCacheAsync()).Community.PayloadHash);
        Assert.Equal(first.LastUpdatedAt, result.LastUpdatedAt);
        Assert.Equal(clock.Now, result.LastCheckedAt);
    }

    [Fact]
    public async Task ChangedContentWinsWithStableIdentitiesAndOnlyUpdatesItsSourceTimestamp()
    {
        var repository = Repository();
        await repository.RefreshAsync();
        clock.Advance();
        var data = RemoteAgendaTests.Microsoft;
        handler.Respond = (request, _) => Task.FromResult(request.RequestUri!.Host == "sessionize.com"
            ? new HttpResponseMessage(HttpStatusCode.NotModified)
            : Ok(Json(data with
            {
                Sessions = [data.Sessions[0] with { Title = "New MAUI title", Abstract = "New abstract" }],
                Speakers = [data.Speakers[0] with { Name = "New display name", PrimaryBio = "New biography", Avatar = "https://example.com/new.jpg" }]
            }), "\"v2\""));
        var result = await repository.RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(clock.Now, result.LastUpdatedAt);
        var session = Assert.Single(result.Document.Sessions, session => session.Id == "20261006");
        Assert.Equal("New MAUI title", session.Title);
        Assert.Equal([GeraldId], session.SpeakerIds);
        var speaker = Assert.Single(result.Document.Speakers, speaker => speaker.Id == GeraldId);
        Assert.Equal("New biography", speaker.Bio);
        Assert.Equal("https://example.com/new.jpg", speaker.ProfilePicture);
        Assert.Equal(clock.Now.AddHours(-1), (await ReadCacheAsync()).Community.LastUpdatedAt);
        Assert.Equal("\"v2\"", (await ReadCacheAsync()).Microsoft.ETag);
    }

    [Fact]
    public async Task PartialFailurePublishesSuccessAndDoesNotClaimFailedFeedWasChecked()
    {
        var repository = Repository();
        await repository.RefreshAsync();
        var checkedAt = clock.Now;
        clock.Advance();
        var community = Community;
        handler.Respond = (request, _) => request.RequestUri!.Host == "sessionize.com"
            ? Task.FromResult(Ok(Json(community with { Sessions = [community.Sessions[0] with { Title = "Updated community" }] })))
            : throw new HttpRequestException("offline");
        var result = await repository.RefreshAsync();
        var issue = Assert.Single(result.Issues);
        Assert.Equal(ConferenceRepository.MicrosoftSource, issue.Source);
        Assert.Equal(checkedAt, result.LastCheckedAt);
        Assert.Equal(clock.Now, result.LastUpdatedAt);
        Assert.Contains(result.Document.Sessions, session => session.Title == "Updated community");
        Assert.Contains(result.Document.Sessions, session => session.Title == "MAUI");
        var cache = await ReadCacheAsync();
        Assert.Equal(checkedAt, cache.Microsoft.LastCheckedAt);
        Assert.Equal(clock.Now, cache.Community.LastCheckedAt);
        var restart = await Repository().LoadAsync();
        Assert.Contains(restart.Document.Sessions, session => session.Title == "Updated community");
    }

    [Fact]
    public async Task FirstPartialRefreshLeavesOverallCheckedTimeUnknown()
    {
        handler.Respond = (request, _) => Task.FromResult(request.RequestUri!.Host == "sessionize.com"
            ? new HttpResponseMessage(HttpStatusCode.BadRequest) : DefaultResponse(request));
        var result = await Repository().RefreshAsync();
        Assert.Single(result.Issues);
        Assert.Single(result.Document.Sessions);
        Assert.Single(result.Document.Speakers);
        Assert.Equal("20261006", result.Document.Sessions[0].Id);
        Assert.Null(result.LastCheckedAt);
        Assert.NotNull((await ReadCacheAsync()).Microsoft.LastCheckedAt);
        Assert.Null((await ReadCacheAsync()).Community.LastCheckedAt);
        Assert.Single((await Repository().LoadAsync()).Document.Sessions);
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
        Assert.Equal(2, result.Issues.Count);
        Assert.True(result.IsCached);
        Assert.Same(loaded.Document, result.Document);
        Assert.Equal(loaded.LastCheckedAt, result.LastCheckedAt);
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
        Assert.Equal(2, result.Issues.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(CachePath));
    }

    [Theory]
    [InlineData(204)]
    [InlineData(206)]
    public async Task IncompleteSuccessStatusesAreNotAccepted(int status)
    {
        handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(Json(RemoteAgendaTests.Microsoft))
        });
        var result = await Repository().RefreshAsync();
        Assert.Equal(2, result.Issues.Count);
        Assert.Equal(2, handler.Requests.Count);
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
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(2, result.Issues.Count);
        Assert.All(result.Issues, issue => Assert.Equal((HttpStatusCode)status, Assert.IsType<HttpRequestException>(issue.Exception).StatusCode));
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
        Assert.Equal(6, handler.Requests.Count);
        Assert.Equal(2, result.Issues.Count);
    }

    [Fact]
    public async Task TransientFailureCanRecoverWithinRetryBudget()
    {
        var attempts = new ConcurrentDictionary<string, int>();
        handler.Respond = (request, _) => Task.FromResult(
            attempts.AddOrUpdate(request.RequestUri!.Host, 1, (_, value) => value + 1) == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : DefaultResponse(request));
        var result = await Repository().RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(4, handler.Requests.Count);
        Assert.True(File.Exists(CachePath));
    }

    [Fact]
    public async Task HttpClientTimeoutIsReportedNotMistakenForUserCancellation()
    {
        handler.Respond = (_, _) => throw new TaskCanceledException("HTTP timeout");
        var result = await Repository().RefreshAsync();
        Assert.Equal(2, result.Issues.Count);
        Assert.All(result.Issues, issue => Assert.IsType<TimeoutException>(issue.Exception));
        Assert.Equal(6, handler.Requests.Count);
    }

    [Fact]
    public async Task SlowRefreshDoesNotBlockCachedLoadAndConcurrentRefreshIsSingleFlight()
    {
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
        Assert.Empty(loaded.Document.Sessions);
        Assert.False(first.IsCompleted);
        release.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.Same(results[0], results[1]);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompletedSourcePublishesAndPersistsBeforeSlowSiblingAndSurvivesCallerCancellation(bool microsoft)
    {
        var store = new MemoryCacheStore();
        var slowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Respond = async (request, token) =>
        {
            if ((request.RequestUri!.Host == "sessionize.com") == !microsoft)
                return DefaultResponse(request);
            slowStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        };
        var repository = new ConferenceRepository(metadata, client, store, clock);
        using var cancellation = new CancellationTokenSource();
        var refresh = repository.RefreshAsync(cancellation.Token);
        await slowStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await store.Written.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(refresh.IsCompleted);
        var partial = await repository.LoadAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(microsoft ? "20261006" : "1234", Assert.Single(partial.Document.Sessions).Id);
        Assert.Null(partial.LastCheckedAt);
        Assert.Equal(clock.Now, partial.LastUpdatedAt);
        Assert.Empty(partial.Issues);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.Equal(partial.Document.Sessions, (await repository.LoadAsync()).Document.Sessions);
        var restart = await new ConferenceRepository(metadata, client, store, clock).LoadAsync();
        Assert.Equivalent(partial.Document.Sessions, restart.Document.Sessions);
        Assert.Equal(2, handler.Requests.Count);
        handler.Respond = (request, _) => Task.FromResult(DefaultResponse(request));
        var recovered = await repository.RefreshAsync();
        Assert.Empty(recovered.Issues);
        Assert.Equal(2, recovered.Document.Sessions.Count);
    }

    [Fact]
    public async Task SuccessfulSourceIsRetainedWhenCancellationArrivesDuringItsPersistence()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new MemoryCacheStore { BeforeWrite = () => cancellation.Cancel() };
        handler.Respond = async (request, token) =>
        {
            if (request.RequestUri!.Host != "sessionize.com")
                return DefaultResponse(request);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        };
        var repository = new ConferenceRepository(metadata, client, store, clock);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.RefreshAsync(cancellation.Token));
        Assert.Equal("20261006", Assert.Single((await repository.LoadAsync()).Document.Sessions).Id);
        Assert.Equal("20261006", Assert.Single(
            (await new ConferenceRepository(metadata, client, store, clock).LoadAsync()).Document.Sessions).Id);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task ProgressReportsCompletedSourceAndSaveIssuesBeforeSiblingCompletes(bool microsoft, bool saveFailure)
    {
        var store = new MemoryCacheStore
        {
            WriteException = saveFailure ? new IOException("Storage unavailable") : null
        };
        var reported = new TaskCompletionSource<ConferenceSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new SnapshotProgress(value => reported.TrySetResult(value));
        handler.Respond = async (request, token) =>
        {
            if ((request.RequestUri!.Host == "sessionize.com") == !microsoft)
                return DefaultResponse(request);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        };
        IConferenceRepository repository = new ConferenceRepository(metadata, client, store, clock);
        using var cancellation = new CancellationTokenSource();
        var refresh = repository.RefreshAsync(cancellation.Token, progress);
        var partial = await reported.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(refresh.IsCompleted);
        Assert.Equal(microsoft ? "20261006" : "1234", Assert.Single(partial.Document.Sessions).Id);
        Assert.Same(partial, await repository.LoadAsync());
        if (saveFailure)
            Assert.Equal(ConferenceRepository.CacheSource, Assert.Single(partial.Issues).Source);
        else
            Assert.Empty(partial.Issues);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
    }

    [Fact]
    public async Task ProgressReportsEachAcceptedSourceAndEndsWithReturnedSnapshot()
    {
        var reported = new List<ConferenceSnapshot>();
        var progress = new SnapshotProgress(reported.Add);
        var result = await Repository().RefreshAsync(progress: progress);
        Assert.Equal(2, reported.Count);
        Assert.Single(reported[0].Document.Sessions);
        Assert.Equal(2, reported[1].Document.Sessions.Count);
        Assert.Same(result, reported[1]);
    }

    [Fact]
    public async Task CacheStoreSeamRoundTripsWithoutFileSystemAndReportsStorageFailures()
    {
        var store = new MemoryCacheStore();
        var first = await new ConferenceRepository(metadata, client, store, clock).RefreshAsync();
        Assert.Empty(first.Issues);
        Assert.Equal(2, store.Writes);
        var restarted = await new ConferenceRepository(metadata, client, store, clock).LoadAsync();
        Assert.True(restarted.IsCached);
        Assert.Equivalent(first.Document.Sessions, restarted.Document.Sessions);
        store.WriteException = new IOException("Storage unavailable");
        var failed = await new ConferenceRepository(metadata, client, store, clock).RefreshAsync();
        Assert.Contains(failed.Issues, issue => issue.Source == ConferenceRepository.CacheSource);
        Assert.Equal(2, failed.Document.Sessions.Count);
    }

    [Fact]
    public async Task OversizedCacheFromCustomStoreIsRejectedBeforeDeserialization()
    {
        var store = new MemoryCacheStore { Content = new byte[16 * 1024 * 1024 + 1] };
        var loaded = await new ConferenceRepository(metadata, client, store, clock).LoadAsync();
        Assert.Empty(loaded.Document.Sessions);
        Assert.IsType<InvalidDataException>(Assert.Single(loaded.Issues).Exception);
    }

    [Fact]
    public async Task FileCacheStorePreCancelledWritePreservesLastGoodAndLeavesNoPendingFiles()
    {
        var store = new FileConferenceCacheStore(CachePath);
        await store.WriteAsync([1, 2, 3]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.WriteAsync([4], cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ReadAsync(cancellation.Token));
        Assert.Equal(new byte[] { 1, 2, 3 }, await store.ReadAsync());
        Assert.Empty(Directory.GetFiles(directory, "*.pending"));
    }

    [Fact]
    public async Task CallerCancellationIsNotSwallowedAndDoesNotWriteOrPoisonLaterRefresh()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Respond = async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        };
        var repository = Repository();
        using var cancellation = new CancellationTokenSource();
        var pending = repository.RefreshAsync(cancellation.Token);
        await started.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(File.Exists(CachePath));
        Assert.Empty((await repository.LoadAsync()).Issues);
        handler.Respond = (request, _) => Task.FromResult(DefaultResponse(request));
        var result = await repository.RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.True(File.Exists(CachePath));
    }

    [Fact]
    public async Task CancellingAWaitingCallerDoesNotCancelSharedRefresh()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Respond = async (request, token) =>
        {
            await release.Task.WaitAsync(token);
            return DefaultResponse(request);
        };
        var repository = Repository();
        var first = repository.RefreshAsync();
        using var cancellation = new CancellationTokenSource();
        var second = repository.RefreshAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        release.SetResult();
        Assert.Empty((await first).Issues);
        Assert.Equal(2, handler.Requests.Count);
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

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("corrupt bytes")]
    [InlineData("""{"schemaVersion":999}""")]
    public async Task CorruptCacheFallsBackWithVisibleIssueAndIsReplacedOnlyOnSuccessfulRefresh(string json)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(CachePath, json);
        var repository = Repository();
        var result = await repository.LoadAsync();
        Assert.Equal(ConferenceRepository.CacheSource, Assert.Single(result.Issues).Source);
        Assert.False(result.IsCached);
        Assert.Empty(result.Document.Sessions);
        Assert.Empty(handler.Requests);
        Assert.Equal(json, await File.ReadAllTextAsync(CachePath));
        var updated = await repository.RefreshAsync();
        Assert.Empty(updated.Issues);
        Assert.Equal(5, (await ReadCacheAsync()).SchemaVersion);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("duplicates")]
    [InlineData("references")]
    [InlineData("validator")]
    [InlineData("hash")]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("timestamps")]
    [InlineData("updated-without-check")]
    [InlineData("validators-without-hash")]
    [InlineData("forbidden-photo")]
    [InlineData("null-withdrawn-session")]
    [InlineData("null-withdrawn-speaker-ids")]
    public async Task SchemaInvalidCacheIsNeverTrusted(string invalid)
    {
        await Repository().RefreshAsync();
        var cache = await ReadCacheAsync();
        cache = invalid switch
        {
            "version" => cache with { SchemaVersion = 999 },
            "duplicates" => cache with { Microsoft = cache.Microsoft with { Sessions = [.. cache.Microsoft.Sessions, .. cache.Microsoft.Sessions] } },
            "references" => cache with { Microsoft = cache.Microsoft with { Speakers = [] } },
            "validator" => cache with { Microsoft = cache.Microsoft with { ETag = "not valid" } },
            "hash" => cache with { Microsoft = cache.Microsoft with { PayloadHash = "bad" } },
            "null" => cache with { Community = null! },
            "empty" => cache with { Microsoft = cache.Microsoft with { Sessions = [] } },
            "null-withdrawn-session" => cache with { Microsoft = cache.Microsoft with { WithdrawnSessions = [null!] } },
            "null-withdrawn-speaker-ids" => cache with { Microsoft = cache.Microsoft with
            {
                WithdrawnSessions = [cache.Microsoft.Sessions[0] with { Id = "withdrawn", SpeakerIds = null! }]
            } },
            "updated-without-check" => cache with { Microsoft = cache.Microsoft with { LastCheckedAt = null, LastUpdatedAt = clock.Now } },
            "validators-without-hash" => cache with { Microsoft = cache.Microsoft with { PayloadHash = "" } },
            "forbidden-photo" => cache with
            {
                Microsoft = cache.Microsoft with
                {
                    Speakers = [cache.Microsoft.Speakers[0] with { ProfilePicture = "https://github.com/person.png" }]
                }
            },
            _ => cache with { Microsoft = cache.Microsoft with { LastUpdatedAt = clock.Now.AddDays(1) } }
        };
        await WriteCacheAsync(cache);
        var requests = handler.Requests.Count;
        var result = await Repository().LoadAsync();
        Assert.False(result.IsCached);
        var issue = Assert.Single(result.Issues);
        var diagnostic = invalid switch
        {
            "validator" => "ETag",
            "hash" => "PayloadHash",
            "empty" => "active sessions",
            "timestamps" => "LastUpdatedAt",
            "updated-without-check" => "check time",
            "validators-without-hash" => "without a validated remote payload",
            _ => null
        };
        if (diagnostic is not null)
        {
            Assert.Contains(ConferenceRepository.MicrosoftSource, issue.Exception.Message);
            Assert.Contains(diagnostic, issue.Exception.Message);
        }
        Assert.Equal(requests, handler.Requests.Count);
    }

    [Fact]
    public async Task MissingRequiredCachePayloadHashesAreReportedAsCorruption()
    {
        await Repository().RefreshAsync();
        var json = (await File.ReadAllTextAsync(CachePath)).Replace("\"payloadHash\":", "\"unknownProperty\":", StringComparison.Ordinal);
        await File.WriteAllTextAsync(CachePath, json);
        var result = await Repository().LoadAsync();
        Assert.False(result.IsCached);
        Assert.Equal(ConferenceRepository.CacheSource, Assert.Single(result.Issues).Source);
    }

    [Fact]
    public async Task OversizedCacheIsRejectedAndOriginalFileIsNotDeleted()
    {
        Directory.CreateDirectory(directory);
        var oversized = new string(' ', 16 * 1024 * 1024 + 1);
        await File.WriteAllTextAsync(CachePath, oversized);
        var result = await Repository().LoadAsync();
        Assert.Equal(ConferenceRepository.CacheSource, Assert.Single(result.Issues).Source);
        Assert.Empty(result.Document.Sessions);
        Assert.Equal(oversized.Length, new FileInfo(CachePath).Length);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task WithdrawnSessionsKeepSpeakerProfilesUntilTheyReturn()
    {
        var repository = Repository();
        await repository.RefreshAsync();
        var community = Community;
        handler.Respond = (request, _) => Task.FromResult(request.RequestUri!.Host == "sessionize.com"
            ? Ok(Json(community with
            {
                Sessions = [community.Sessions[0] with { Id = "5678", Speakers = ["new-community"] }],
                Speakers = [new() { Id = "new-community", FullName = "New community speaker" }]
            })) : DefaultResponse(request));
        var result = await repository.RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.DoesNotContain(result.Document.Sessions, session => session.Id == "1234");
        Assert.DoesNotContain(result.Document.Speakers, speaker => speaker.Id == "community-speaker");
        Assert.Contains(result.Document.WithdrawnSpeakers, speaker => speaker.Id == "community-speaker");
        var withdrawnCache = await ReadCacheAsync();
        Assert.DoesNotContain(withdrawnCache.Community.Speakers, speaker => speaker.Id == "community-speaker");
        var archivedProfile = Assert.Single(withdrawnCache.Community.WithdrawnSpeakers);
        Assert.Equal("community-speaker", archivedProfile.Id);
        Assert.Equal("Community bundled bio", archivedProfile.Bio);
        Assert.Equal("1234", Assert.Single(result.Document.WithdrawnSessions).Id);
        var withdrawnRestart = await Repository().LoadAsync();
        Assert.Equal("1234", Assert.Single(withdrawnRestart.Document.WithdrawnSessions).Id);
        Assert.DoesNotContain(withdrawnRestart.Document.Speakers, speaker => speaker.Id == "community-speaker");
        Assert.Contains(withdrawnRestart.Document.WithdrawnSpeakers, speaker => speaker.Id == "community-speaker");
        Assert.Contains(result.Document.Sessions, session => session.Id == "5678");
        handler.Respond = (request, _) => Task.FromResult(DefaultResponse(request));
        var returned = await Repository().RefreshAsync();
        var speaker = Assert.Single(returned.Document.Speakers, speaker => speaker.Id == "community-speaker");
        Assert.Equal("Community bundled bio", speaker.Bio);
        Assert.Equal("https://example.com/community.jpg", speaker.ProfilePicture);
        Assert.DoesNotContain(returned.Document.WithdrawnSessions, session => session.Id == "1234");
        var returnedCache = await ReadCacheAsync();
        Assert.Contains(returnedCache.Community.Speakers, profile => profile.Id == "community-speaker");
        Assert.DoesNotContain(returnedCache.Community.WithdrawnSpeakers, profile => profile.Id == "community-speaker");
    }

    [Fact]
    public async Task WithdrawalsAreDeduplicatedAcrossRestartsAndUnchangedRefreshes()
    {
        var repository = Repository();
        await repository.RefreshAsync();
        var data = Community;
        handler.Respond = (request, _) => Task.FromResult(request.RequestUri!.Host == "sessionize.com"
            ? Ok(Json(data with
            {
                Sessions = [data.Sessions[0] with { Id = "replacement", Speakers = [] }], Speakers = []
            })) : DefaultResponse(request));
        var withdrawn = await repository.RefreshAsync();
        var changedAt = withdrawn.LastUpdatedAt;
        Assert.Equal("1234", Assert.Single(withdrawn.Document.WithdrawnSessions).Id);
        clock.Advance();
        var unchanged = await Repository().RefreshAsync();
        Assert.Empty(unchanged.Issues);
        Assert.Equal(changedAt, unchanged.LastUpdatedAt);
        var archived = Assert.Single(unchanged.Document.WithdrawnSessions);
        Assert.Equal("Community abstract", archived.Description);
        Assert.DoesNotContain(unchanged.Document.Speakers, speaker => speaker.Id == "community-speaker");
        Assert.Contains(unchanged.Document.WithdrawnSpeakers, speaker => speaker.Id == "community-speaker");
        Assert.DoesNotContain(Agenda.Search(unchanged.Document, "", null, null, TimeZoneInfo.Utc),
            session => session.Id == "1234");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PublishedUnscheduledSpeakersRemainCurrentEvenWhenTheirLastScheduledTalkIsWithdrawn(bool microsoft)
    {
        await Repository().RefreshAsync();
        var community = Community;
        var official = RemoteAgendaTests.Microsoft;
        handler.Respond = (request, _) => Task.FromResult(request.RequestUri!.Host == "sessionize.com"
            ? Ok(Json(community with
            {
                Sessions = [community.Sessions[0] with { Id = "replacement", Speakers = [] }],
                Speakers = [.. community.Speakers, new() { Id = "unscheduled-community", FullName = "Unscheduled speaker" }]
            }))
            : Ok(Json(official with
            {
                Sessions = [official.Sessions[0] with { Id = "BRK007", SpeakerIds = [] }],
                Speakers = [.. official.Speakers, new() { Id = "unscheduled-official", Name = "Unscheduled official speaker" }]
            })));
        var result = await Repository().RefreshAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(4, result.Document.Speakers.Count);
        Assert.Empty(result.Document.WithdrawnSpeakers);
        Assert.Equal(2, result.Document.WithdrawnSessions.Count);
        var cache = await ReadCacheAsync();
        Assert.Equal(2, (microsoft ? cache.Microsoft : cache.Community).Speakers.Count);
        Assert.Empty((microsoft ? cache.Microsoft : cache.Community).WithdrawnSpeakers);
        Assert.Equivalent(result.Document, (await Repository().LoadAsync()).Document);
    }

    [Fact]
    public async Task SchemaFourMigrationSeparatesMixedArchivedProfilesWithoutLosingUnscheduledProfilesOrBookmarks()
    {
        await Repository().RefreshAsync();
        var old = await ReadCacheAsync();
        var withdrawn = old.Community.Sessions[0];
        await WriteCacheAsync(old with
        {
            SchemaVersion = 4,
            Community = old.Community with
            {
                Sessions = [withdrawn with { Id = "replacement", SpeakerIds = [] }],
                WithdrawnSessions = [withdrawn],
                Speakers = [.. old.Community.Speakers, new() { Id = "unscheduled", Name = "Published unscheduled speaker" }]
            }
        });
        var schedulePath = Path.Combine(directory, "bookmarks.json");
        var schedule = new JsonScheduleStore(schedulePath);
        await schedule.WriteAsync([withdrawn.Id]);
        var bookmarks = await File.ReadAllBytesAsync(schedulePath);
        var requests = handler.Requests.Count;
        var result = await Repository().LoadAsync();
        Assert.Empty(result.Issues);
        Assert.Equal(requests, handler.Requests.Count);
        Assert.Contains(result.Document.Speakers, speaker => speaker.Id == "unscheduled");
        Assert.DoesNotContain(result.Document.Speakers, speaker => speaker.Id == "community-speaker");
        Assert.Equal("community-speaker", Assert.Single(result.Document.WithdrawnSpeakers).Id);
        Assert.Equal(withdrawn.Description, Assert.Single(result.Document.WithdrawnSessions).Description);
        var migrated = await ReadCacheAsync();
        Assert.Equal(5, migrated.SchemaVersion);
        Assert.Equal(old.Community.PayloadHash, migrated.Community.PayloadHash);
        Assert.Equal(old.Community.ETag, migrated.Community.ETag);
        Assert.Equal(bookmarks, await File.ReadAllBytesAsync(schedulePath));
        Assert.Equivalent(result.Document, (await Repository().LoadAsync()).Document);
    }

    [Fact]
    public async Task MigratedAmbiguousProfilesRequireFullParseEvenWhenPayloadHashIsUnchanged()
    {
        await Repository().RefreshAsync();
        var community = Community;
        handler.Respond = (request, _) => Task.FromResult(request.RequestUri!.Host == "sessionize.com"
            ? Ok(Json(community with { Sessions = [community.Sessions[0] with { Id = "replacement", Speakers = [] }] }))
            : DefaultResponse(request));
        var upstream = await Repository().RefreshAsync();
        Assert.Contains(upstream.Document.Speakers, speaker => speaker.Id == "community-speaker");
        var cache = await ReadCacheAsync();
        await WriteCacheAsync(cache with { SchemaVersion = 4 });
        var repository = Repository();
        var migrated = await repository.LoadAsync();
        Assert.Contains(migrated.Document.WithdrawnSpeakers, speaker => speaker.Id == "community-speaker");
        Assert.True((await ReadCacheAsync()).Community.RequiresProfileRefresh);
        handler.Respond = (request, _) =>
        {
            if (request.RequestUri!.Host != "sessionize.com")
                return Task.FromResult(DefaultResponse(request));
            Assert.Empty(request.Headers.IfNoneMatch);
            Assert.Null(request.Headers.IfModifiedSince);
            return Task.FromResult(Ok(Json(community with
            {
                Sessions = [community.Sessions[0] with { Id = "replacement", Speakers = [] }]
            })));
        };
        var corrected = await repository.RefreshAsync();
        Assert.Empty(corrected.Issues);
        Assert.Contains(corrected.Document.Speakers, speaker => speaker.Id == "community-speaker");
        Assert.Empty(corrected.Document.WithdrawnSpeakers);
        Assert.False((await ReadCacheAsync()).Community.RequiresProfileRefresh);
        Assert.Equal(cache.Community.PayloadHash, (await ReadCacheAsync()).Community.PayloadHash);
    }

    [Fact]
    public async Task ActiveTalkCannotReferenceOnlyWithdrawnProfileAndArchivesCannotDuplicateCurrentProfiles()
    {
        var snapshot = await Repository().RefreshAsync();
        var document = snapshot.Document;
        var speaker = document.Speakers[0];
        Assert.Throws<InvalidDataException>(() => ConferenceJson.Validate(document with
        {
            Speakers = document.Speakers.Skip(1).ToList(), WithdrawnSpeakers = [speaker]
        }));
        Assert.Throws<InvalidDataException>(() => ConferenceJson.Validate(document with { WithdrawnSpeakers = [speaker] }));
        ConferenceJson.Validate(document with
        {
            Sessions = [], WithdrawnSessions = document.Sessions,
            Speakers = [], WithdrawnSpeakers = document.Speakers
        });
    }

    [Fact]
    public async Task SourceCannotCollideWithAnArchivedIdentity()
    {
        var repository = Repository();
        await repository.RefreshAsync();
        var community = Community;
        handler.Respond = (request, _) => Task.FromResult(request.RequestUri!.Host == "sessionize.com"
            ? Ok(Json(community with { Sessions = [community.Sessions[0] with { Id = "replacement" }] }))
            : DefaultResponse(request));
        await repository.RefreshAsync();
        var microsoft = RemoteAgendaTests.Microsoft;
        handler.Respond = (request, _) => Task.FromResult(request.RequestUri!.Host == "sessionize.com"
            ? Ok(Json(community with { Sessions = [community.Sessions[0] with { Id = "replacement" }] }))
            : Ok(Json(microsoft with
            {
                Speakers = [microsoft.Speakers[0] with { Id = "community-speaker" }],
                Sessions = [microsoft.Sessions[0] with { SpeakerIds = ["community-speaker"] }]
            })));
        var result = await repository.RefreshAsync();
        Assert.Contains(result.Issues, issue => issue.Source == ConferenceRepository.MicrosoftSource);
        Assert.Equal("1234", Assert.Single(result.Document.WithdrawnSessions).Id);
    }

    [Fact]
    public async Task DuplicateAcrossFeedsRejectsOnlyBadSourceAndRetainsLastGood()
    {
        var repository = Repository();
        await repository.RefreshAsync();
        var data = Community;
        handler.Respond = (request, _) => Task.FromResult(request.RequestUri!.Host == "sessionize.com"
            ? Ok(Json(data with { Sessions = [data.Sessions[0] with { Id = "20261006" }] }))
            : DefaultResponse(request));
        var result = await repository.RefreshAsync();
        Assert.Equal(ConferenceRepository.CommunitySource, Assert.Single(result.Issues).Source);
        Assert.Equal(2, result.Document.Sessions.Count);
        Assert.Contains(result.Document.Sessions, session => session.Id == "1234");
        Assert.NotNull((await ReadCacheAsync()).Microsoft.LastCheckedAt);
        Assert.Equal(clock.Now, (await ReadCacheAsync()).Community.LastCheckedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OversizedResponsesAreRejectedWithoutClearingReadableData(bool contentLength)
    {
        var repository = Repository();
        await repository.RefreshAsync();
        var bytesBefore = await File.ReadAllBytesAsync(CachePath);
        var bytes = Encoding.UTF8.GetBytes(new string(' ', 4 * 1024 * 1024 + 1));
        handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = contentLength ? new ByteArrayContent(bytes) :
                new StreamContent(new SpeakerImageCacheTests.NonSeekableReadStream(bytes))
        });
        var result = await repository.RefreshAsync();
        Assert.Equal(2, result.Document.Sessions.Count);
        Assert.Equal(2, result.Issues.Count);
        Assert.All(result.Issues, issue => Assert.IsType<InvalidDataException>(issue.Exception));
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(CachePath));
    }

    [Fact]
    public async Task UnwritableCachePathStillDisplaysRemoteDataAndReportsPersistenceFailure()
    {
        Directory.CreateDirectory(directory);
        var parentFile = Path.Combine(directory, "file-not-directory");
        await File.WriteAllTextAsync(parentFile, "Original bytes");
        var data = RemoteAgendaTests.Microsoft;
        handler.Respond = (request, _) => Task.FromResult(request.RequestUri!.Host == "sessionize.com"
            ? DefaultResponse(request) : Ok(Json(data with { Sessions = [data.Sessions[0] with { Title = "Downloaded session" }] })));
        var result = await Repository(Path.Combine(parentFile, "agenda.json")).RefreshAsync();
        Assert.Contains(result.Issues, issue => issue.Source == ConferenceRepository.CacheSource);
        Assert.Contains(result.Document.Sessions, session => session.Title == "Downloaded session");
        Assert.Equal(clock.Now, result.LastCheckedAt);
        Assert.Equal("Original bytes", await File.ReadAllTextAsync(parentFile));
        Assert.Empty(Directory.GetFiles(directory, "*.pending", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task AtomicWriteFailureKeepsOriginalCacheBytes()
    {
        if (OperatingSystem.IsWindows())
            return;
        var repository = Repository();
        await repository.RefreshAsync();
        var original = await File.ReadAllBytesAsync(CachePath);
        var data = RemoteAgendaTests.Microsoft;
        handler.Respond = (request, _) => Task.FromResult(request.RequestUri!.Host == "sessionize.com"
            ? DefaultResponse(request)
            : Ok(Json(data with { Sessions = [data.Sessions[0] with { Title = "Memory-only update" }] })));
        var permissions = File.GetUnixFileMode(directory);
        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            var result = await repository.RefreshAsync();
            Assert.Contains(result.Issues, issue => issue.Source == ConferenceRepository.CacheSource);
            Assert.Contains(result.Document.Sessions, session => session.Title == "Memory-only update");
            Assert.Equal(original, await File.ReadAllBytesAsync(CachePath));
            Assert.DoesNotContain((await Repository().LoadAsync()).Document.Sessions,
                session => session.Title == "Memory-only update");
        }
        finally { File.SetUnixFileMode(directory, permissions); }
        Assert.Empty(Directory.GetFiles(directory, "*.pending"));
    }

    [Fact]
    public async Task InvalidMetadataFailsLoudlyRatherThanInventingEmptySuccess()
    {
        metadata.Document = metadata.Document with { SchemaVersion = 999 };
        await Assert.ThrowsAsync<InvalidDataException>(() => Repository().LoadAsync());
        Assert.Empty(handler.Requests);
    }

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = DateTimeOffset.Parse("2026-10-29T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance() => Now = Now.AddHours(1);
        public void Rewind() => Now = Now.AddHours(-1);
    }

    private sealed class AdvancingClock : TimeProvider
    {
        private int reads;
        public int Reads => reads;
        public override DateTimeOffset GetUtcNow() =>
            DateTimeOffset.Parse("2026-10-29T23:00:00Z").AddTicks(Interlocked.Increment(ref reads));
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
        public Action? BeforeWrite { get; set; }
        public TaskCompletionSource Written { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<byte[]?> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Content?.ToArray());
        }

        public Task WriteAsync(byte[] content, CancellationToken cancellationToken = default)
        {
            BeforeWrite?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (WriteException is not null)
                throw WriteException;
            Content = content.ToArray();
            Writes++;
            Written.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class SnapshotProgress(Action<ConferenceSnapshot> report) : IProgress<ConferenceSnapshot>
    {
        public void Report(ConferenceSnapshot value) => report(value);
    }
}
