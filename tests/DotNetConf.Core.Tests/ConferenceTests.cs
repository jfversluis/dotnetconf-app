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
        Assert.Contains(document.Sessions, session => session.SpeakerIds.Count > 1);
        Assert.Contains(document.Sessions, session => session.StartsAt.Date != session.EndsAt.Date);
    }

    [Fact]
    public async Task SearchMatchesSpeakersTopicsAndAllTerms()
    {
        var document = await LoadAsync();
        Assert.Single(Agenda.Search(document, "Sam languages", null, null, TimeZoneInfo.Utc));
        Assert.Single(Agenda.Search(document, "AVALONIA", null, null, TimeZoneInfo.Utc));
        Assert.Empty(Agenda.Search(document, "Avalonia nonexistent", null, null, TimeZoneInfo.Utc));
        Assert.Equal(2, Agenda.Search(document, "", null, ".NET MAUI", TimeZoneInfo.Utc).Count());
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
