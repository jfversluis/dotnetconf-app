using System.Text;
using System.Text.Json;
using DotNetConf.Core;

namespace DotNetConf.Core.Tests;

public sealed class ConferenceTests
{
    private static async Task<ConferenceDocument> LoadAsync()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        return await ConferenceJson.ReadAsync(stream);
    }

    [Fact]
    public async Task BundledMockIsValidAndClearlyMarked()
    {
        var document = await LoadAsync();
        Assert.True(document.IsMock);
        Assert.Equal(6, document.Sessions.Count);
        Assert.Equal(4, document.Speakers.Count);
        Assert.Equal(24, document.CommunityEvents.Count);
        Assert.Contains(document.Sessions, session => session.SpeakerIds.Count > 1);
        Assert.Contains(document.Sessions, session => session.StartsAt.Date != session.EndsAt.Date);
    }

    [Fact]
    public async Task CommunityEventsHaveLocationsWebsitesAndBothKinds()
    {
        var document = await LoadAsync();
        Assert.Equal(5, document.CommunityEvents.Count(e => e.City == "Amsterdam"));
        Assert.Contains(document.CommunityEvents, e => e.Kind == "watch-party");
        Assert.Contains(document.CommunityEvents, e => e.Kind == "community-conference");
        Assert.All(document.CommunityEvents, e =>
        {
            Assert.Contains(e.City, e.LocationLabel);
            Assert.Equal("example.com", new Uri(e.WebsiteUrl).Host);
        });
    }

    [Fact]
    public async Task CommunityExamplesIncludeDenseMixedHubsAndIsolatedEvents()
    {
        var document = await LoadAsync();
        var cities = document.CommunityEvents.GroupBy(e => e.City).ToArray();
        Assert.Equal(4, cities.Count(city => city.Count() >= 4));
        Assert.True(cities.Count(city => city.Count() == 1) >= 5);
        foreach (var city in cities.Where(city => city.Count() >= 4))
        {
            Assert.Contains(city, e => e.Kind == "watch-party");
            Assert.Contains(city, e => e.Kind == "community-conference");
            Assert.True(city.Max(e => e.Latitude) - city.Min(e => e.Latitude) < 0.05);
            Assert.True(city.Max(e => e.Longitude) - city.Min(e => e.Longitude) < 0.1);
        }
        Assert.All(document.CommunityEvents, e => Assert.False(string.IsNullOrWhiteSpace(e.KindDescription)));
    }

    [Fact]
    public async Task InvalidCommunityEventsAreRejected()
    {
        var document = await LoadAsync();
        var example = document.CommunityEvents[0];
        Assert.Throws<InvalidDataException>(() => ConferenceJson.Validate(document with { CommunityEvents = null! }));
        Assert.Throws<InvalidDataException>(() => ConferenceJson.Validate(document with { CommunityEvents = [example, example] }));
        foreach (var invalid in new[]
        {
            example with { Id = "" }, example with { Name = "" }, example with { Venue = "" },
            example with { City = "" }, example with { Country = "" }, example with { Kind = "unknown" },
            example with { Latitude = 91 }, example with { Latitude = double.NaN },
            example with { Longitude = -181 }, example with { Longitude = double.PositiveInfinity },
            example with { WebsiteUrl = "http://example.com" }, example with { WebsiteUrl = "/relative" }
        })
            Assert.Throws<InvalidDataException>(() => ConferenceJson.Validate(document with { CommunityEvents = [invalid] }));
    }

    [Fact]
    public async Task MissingCommunityCoordinatesAreNotInterpretedAsZero()
    {
        var document = await LoadAsync();
        var json = JsonSerializer.Serialize(document, ConferenceJsonContext.Default.ConferenceDocument);
        var missingLatitude = json.Replace("\"latitude\":52.3702,", "", StringComparison.Ordinal);
        Assert.NotEqual(json, missingLatitude);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(missingLatitude));
        await Assert.ThrowsAsync<JsonException>(() => ConferenceJson.ReadAsync(stream));
    }

    [Fact]
    public async Task OlderDocumentsWithoutCommunityEventsRemainValid()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            """{"schemaVersion":1,"name":"Test","description":"Test","websiteUrl":"https://example.com","speakers":[],"sessions":[]}"""));
        var document = await ConferenceJson.ReadAsync(stream);
        Assert.Empty(document.CommunityEvents);
    }

    [Fact]
    public async Task SearchMatchesSpeakersTopicsAndAllTerms()
    {
        var document = await LoadAsync();
        Assert.Single(Agenda.Search(document, "Sam languages", null, null, TimeZoneInfo.Utc));
        Assert.Single(Agenda.Search(document, "AVALONIA", null, null, TimeZoneInfo.Utc));
        Assert.Empty(Agenda.Search(document, "Avalonia nonexistent", null, null, TimeZoneInfo.Utc));
        Assert.Equal(2, Agenda.Search(document, "", null, [".NET MAUI"], TimeZoneInfo.Utc).Count());
        var sessions = Agenda.Search(document, "  ", null, null, TimeZoneInfo.Utc).ToArray();
        Assert.Equal(sessions.OrderBy(session => session.StartsAt), sessions);
    }

    [Fact]
    public async Task DayFilterUsesLocalDateNotUtcDate()
    {
        var document = await LoadAsync();
        var zone = TimeZoneInfo.CreateCustomTimeZone("UTC+02", TimeSpan.FromHours(2), "UTC+02", "UTC+02");
        var sessions = Agenda.Search(document, "community", new DateOnly(2026, 11, 12), null, zone);
        Assert.Single(sessions);
        Assert.Empty(Agenda.Search(document, "community", new DateOnly(2026, 11, 11), null, zone));
    }

    [Fact]
    public async Task SearchDayAndTopicFiltersCombineAndClearIndependently()
    {
        var document = await LoadAsync();
        var day = new DateOnly(2026, 11, 10);
        Assert.Single(Agenda.Search(document, "Alex", day, [".NET MAUI"], TimeZoneInfo.Utc));
        Assert.Empty(Agenda.Search(document, "Alex", day, ["AI"], TimeZoneInfo.Utc));
        Assert.Equal(3, Agenda.Search(document, "Alex", null, null, TimeZoneInfo.Utc).Count());
        Assert.Equal(2, Agenda.Search(document, "", null, [".NET MAUI"], TimeZoneInfo.Utc).Count());
        Assert.Equal(6, Agenda.Search(document, "", null, null, TimeZoneInfo.Utc).Count());
    }

    [Fact]
    public async Task MultipleTopicsMatchAnyWithoutDuplicatingSessions()
    {
        var document = await LoadAsync();
        var topics = new[] { ".NET MAUI", "UI", ".net maui" };
        var results = Agenda.Search(document, "", null, topics, TimeZoneInfo.Utc).ToArray();
        var expected = document.Sessions.Where(session =>
            session.Topics.Contains(".NET MAUI") || session.Topics.Contains("UI"));
        Assert.Equal(expected.Select(session => session.Id).Order(),
            results.Select(session => session.Id).Order());
        Assert.Equal(results.Length, results.Select(session => session.Id).Distinct().Count());
        Assert.Equal(document.Sessions.Count, Agenda.Search(document, "", null, [], TimeZoneInfo.Utc).Count());
    }

    [Fact]
    public async Task TopicSelectionAppliesToEachLocalDayAndAllDays()
    {
        var document = await LoadAsync();
        var zone = TimeZoneInfo.CreateCustomTimeZone("UTC+02", TimeSpan.FromHours(2), "UTC+02", "UTC+02");
        var topics = new[] { ".NET MAUI", "AI" };
        var allDays = Agenda.Search(document, "", null, topics, zone).ToArray();
        Assert.NotEmpty(allDays);
        var days = document.Sessions.Select(session => Agenda.LocalDay(session.StartsAt, zone)).Distinct();
        foreach (var day in days)
        {
            var results = Agenda.Search(document, "", day, topics, zone).ToArray();
            Assert.Equal(allDays.Where(session => Agenda.LocalDay(session.StartsAt, zone) == day), results);
            Assert.Equal(Agenda.Search(document, "Alex", day, null, zone),
                Agenda.Search(document, "Alex", day, [], zone));
        }
        Assert.Single(Agenda.Search(document, "Alex", new DateOnly(2026, 11, 10), topics, zone));
    }

    [Fact]
    public void ConversionUsesTheOffsetAtTheSessionInstant()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        Assert.Equal(new DateOnly(2026, 11, 1), Agenda.LocalDay(DateTimeOffset.Parse("2026-11-02T04:30:00Z"), zone));
        Assert.Equal(new DateOnly(2026, 7, 2), Agenda.LocalDay(DateTimeOffset.Parse("2026-07-02T04:30:00Z"), zone));
    }

    [Fact]
    public async Task ConflictsExcludeSelfAndAdjacentSessions()
    {
        var document = await LoadAsync();
        var language = document.Sessions.Single(session => session.Id == "language-features");
        var avalonia = document.Sessions.Single(session => session.Id == "avalonia-ui");
        Assert.True(Agenda.Overlaps(language, avalonia));
        Assert.False(Agenda.Overlaps(language, language));
        Assert.False(Agenda.Overlaps(language, avalonia with { StartsAt = language.EndsAt }));
    }

    [Fact]
    public async Task InvalidContractsAreRejected()
    {
        var document = await LoadAsync();
        Assert.Throws<InvalidDataException>(() => ConferenceJson.Validate(document with { SchemaVersion = 2 }));
        Assert.Throws<InvalidDataException>(() => ConferenceJson.Validate(document with { WebsiteUrl = "http://example.com" }));
        Assert.Throws<InvalidDataException>(() => ConferenceJson.Validate(document with { Speakers = [document.Speakers[0], document.Speakers[0]] }));
        Assert.Throws<InvalidDataException>(() => ConferenceJson.Validate(document with { Sessions = [document.Sessions[0], document.Sessions[0]] }));
        Assert.Throws<InvalidDataException>(() => ConferenceJson.Validate(document with { Sessions = [document.Sessions[0] with { SpeakerIds = ["missing"] }] }));
        Assert.Throws<InvalidDataException>(() => ConferenceJson.Validate(document with { Sessions = [document.Sessions[0] with { EndsAt = document.Sessions[0].StartsAt }] }));
        Assert.Null(document.Sessions.Find(session => session.Id == "missing"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1,\"name\":\"Test\",\"description\":\"Test\",\"websiteUrl\":\"https://example.com\",\"speakers\":null}")]
    public async Task EmptyOrMalformedDataIsNotSuccess(string json)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await Assert.ThrowsAsync<InvalidDataException>(() => ConferenceJson.ReadAsync(stream));
    }

    [Fact]
    public async Task CorruptJsonIsNotSuccess()
    {
        await using var stream = new MemoryStream("not-json"u8.ToArray());
        await Assert.ThrowsAsync<JsonException>(() => ConferenceJson.ReadAsync(stream));
    }
}
