using System.Text;
using System.Text.Json;
using DotNetConf.Core;

namespace DotNetConf.Core.Tests;

public sealed class SessionizeTests
{
    private static readonly TimeZoneInfo ConferenceZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
    private static readonly ConferenceDocument Metadata = new()
    {
        SchemaVersion = 1, Name = "Test", Description = "Test conference", WebsiteUrl = "https://example.com"
    };

    private static SessionizeDocument Example => new()
    {
        Sessions =
        [
            new()
            {
                Id = "14022", Title = "Example session", Description = null,
                StartsAt = "2026-11-11T08:00:00", EndsAt = "2026-11-11T09:00:00",
                Speakers = ["speaker-1", "speaker-2"], CategoryItems = [4373], RoomId = 215
            }
        ],
        Speakers =
        [
            new()
            {
                Id = "speaker-1", FullName = "First Speaker", Bio = "Published bio",
                TagLine = "Published tagline", ProfilePicture = "https://example.com/avatar.png"
            },
            new() { Id = "speaker-2", FirstName = "Second", LastName = "Speaker" }
        ],
        Categories = [new() { Id = 2124, Title = "Topic", Items = [new() { Id = 4373, Name = ".NET MAUI" }] }],
        Rooms = [new() { Id = 215, Name = "Main room" }]
    };

    private static async Task<ConferenceDocument> ReadAsync(
        SessionizeDocument data, ConferenceDocument? conference = null)
    {
        var json = JsonSerializer.Serialize(data, SessionizeJsonContext.Default.SessionizeDocument);
        return await ReadJsonAsync(json, conference);
    }

    private static async Task<ConferenceDocument> ReadJsonAsync(
        string json, ConferenceDocument? conference = null)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await SessionizeJson.ReadAsync(stream, conference ?? Metadata, ConferenceZone);
    }

    private static async Task<ConferenceDocument> LoadSnapshotAsync()
    {
        await using var metadata = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.json"));
        var conference = await ConferenceJson.ReadAsync(metadata);
        await using var agenda = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "sessionize.json"));
        return await SessionizeJson.ReadAsync(agenda, conference, ConferenceZone);
    }

    [Fact]
    public async Task AppMetadataContainsNoConferenceAgenda()
    {
        await using var metadata = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.json"));
        var document = await ConferenceJson.ReadAsync(metadata);
        Assert.Empty(document.Sessions);
        Assert.Empty(document.Speakers);
        Assert.Empty(document.WithdrawnSessions);
        Assert.False(document.IsMock);
        Assert.False(document.CommunityEventsAreMock);
        Assert.Empty(document.CommunityEvents);
    }

    [Fact]
    public async Task PublicTestFixtureContainsSessionsAndSpeakersWithoutLocalCommunityExamples()
    {
        var conference = await LoadSnapshotAsync();
        Assert.False(conference.IsMock);
        Assert.False(conference.CommunityEventsAreMock);
        Assert.Contains("no agenda is bundled", conference.AgendaNotice);
        Assert.Contains("https://www.dotnetconf.net/agenda", conference.AgendaNotice);
        Assert.Equal(70, conference.Sessions.Count);
        Assert.Equal(70, conference.Speakers.Count);
        Assert.Empty(conference.CommunityEvents);
        Assert.Equal(66, conference.Speakers.Count(speaker => !string.IsNullOrWhiteSpace(speaker.Bio)));
        Assert.Equal(36, conference.Speakers.Count(speaker => !string.IsNullOrWhiteSpace(speaker.ProfilePicture)));
        Assert.Equal(
            new[] { "Matt Soucoup", "Paul Yuknewicz", "Scott Hanselman", "Tanner Gooding" },
            conference.Speakers.Where(speaker => string.IsNullOrWhiteSpace(speaker.Bio))
                .Select(speaker => speaker.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.All(conference.Speakers, speaker =>
        {
            Assert.DoesNotContain("No response", speaker.Name, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(conference.Sessions, session => session.SpeakerIds.Contains(speaker.Id));
            if (!string.IsNullOrEmpty(speaker.ProfilePicture))
            {
                Assert.StartsWith("https://", speaker.ProfilePicture, StringComparison.Ordinal);
                Assert.NotEmpty(SpeakerPhotoUrl.Normalize(speaker.ProfilePicture));
            }
        });
        Assert.Equal(18, conference.Sessions.SelectMany(session => session.Topics).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(28, conference.Sessions.Count(session => session.Topics.Count > 0));
        Assert.All(conference.Sessions, session =>
        {
            Assert.NotEmpty(session.SpeakerIds);
            Assert.DoesNotContain("fictional", session.Description, StringComparison.OrdinalIgnoreCase);
        });
        var days = conference.Sessions.GroupBy(session => Agenda.LocalDay(session.StartsAt, ConferenceZone)).ToArray();
        Assert.Equal(new[] { new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 13) },
            days.Select(day => day.Key));
        Assert.All(days, day =>
        {
            var sessions = day.OrderBy(session => session.StartsAt).ToArray();
            var hasKeynote = day.Key.Day is 10 or 11;
            Assert.Equal(hasKeynote ? 17 : 18, sessions.Length);
            Assert.Equal(TimeSpan.FromHours(8), sessions[0].StartsAt.TimeOfDay);
            Assert.Equal(TimeSpan.FromHours(17), sessions[^1].EndsAt.TimeOfDay);
            Assert.Equal(TimeSpan.FromMinutes(hasKeynote ? 60 : 30), sessions[0].EndsAt - sessions[0].StartsAt);
            Assert.All(sessions.Skip(1), session => Assert.Equal(TimeSpan.FromMinutes(30), session.EndsAt - session.StartsAt));
            Assert.All(sessions, session => Assert.Equal(TimeSpan.FromHours(-8), session.StartsAt.Offset));
            for (var i = 1; i < sessions.Length; i++)
                Assert.Equal(sessions[i - 1].EndsAt, sessions[i].StartsAt);
        });
    }

    [Theory]
    [InlineData("20260001", "Keynote", "Scott Hanselman")]
    [InlineData("20261001", "What's new in C#", "Mads Torgersen, Dustin Campbell")]
    [InlineData("20261006", "What\u2019s New in .NET MAUI in .NET 11", "Gerald Versluis")]
    [InlineData("20261009", "What's New in Aspire for .NET Developers", "Maddy Montaquila, Damian Edwards")]
    [InlineData("20261024", "CsWinRT 3.0: Modernizing the Windows Projections for .NET", "Manodasan Wignarajah, Sergio Pedri")]
    [InlineData("20261026", "Beyond Code Generation: Give AI Eyes and Hands for .NET MAUI", "Gerald Versluis")]
    [InlineData("20261021", "What's new in the MCP C# SDK", "Stephen Halter")]
    [InlineData("20261028", "Azure's new lineup for .NET developers (Part 1)", "Matt Soucoup, Paul Yuknewicz")]
    [InlineData("20261029", "Azure's new lineup for .NET developers (Part 2)", "Matt Soucoup, Paul Yuknewicz")]
    [InlineData("20261032", "EF Core 11: Vector Search, Smarter LINQ, and a Friendlier dotnet ef", "Jiri Cincura")]
    public async Task PublishedTitlesAndSpeakerNamesArePreserved(string id, string title, string speakerNames)
    {
        var conference = await LoadSnapshotAsync();
        var session = Assert.Single(conference.Sessions, session => session.Id == id);
        Assert.Equal(title, session.Title);
        Assert.Equal(speakerNames, string.Join(", ", session.SpeakerIds.Select(
            speakerId => conference.Speakers.Single(speaker => speaker.Id == speakerId).Name)));
    }

    [Fact]
    public async Task PublishedAgendaSupportsSearchLocalDatesAndSpeakerNavigation()
    {
        var conference = await LoadSnapshotAsync();
        Assert.Equal(2, Agenda.Search(conference, "Gerald", null, null, TimeZoneInfo.Utc).Count());
        Assert.Single(Agenda.Search(conference, "Gerald MAUI", new DateOnly(2026, 11, 11), null, ConferenceZone));
        var finalTalk = Assert.Single(Agenda.Search(conference, "Jiri vector", null, null, TimeZoneInfo.Utc));
        Assert.Equal(new DateOnly(2026, 11, 12), Agenda.LocalDay(finalTalk.StartsAt, TimeZoneInfo.Utc));
        Assert.Equal(new DateOnly(2026, 11, 11), Agenda.LocalDay(finalTalk.StartsAt, ConferenceZone));
    }

    [Theory]
    [InlineData("Bruno Capuano")]
    [InlineData("Gerald Versluis")]
    [InlineData("Harshada Hole")]
    [InlineData("Jonathan Peppers")]
    [InlineData("Mads Torgersen")]
    [InlineData("Manodasan Wignarajah")]
    [InlineData("Morten Nielsen")]
    [InlineData("Sergio Pedri")]
    [InlineData("Stephen Halter")]
    [InlineData("Tanner Gooding")]
    public async Task MicrosoftProfilesDoNotRetainGithubOrOrganizerOnlyPortraits(string name)
    {
        var conference = await LoadSnapshotAsync();
        var speaker = Assert.Single(conference.Speakers, speaker => speaker.Name == name);
        Assert.Empty(speaker.ProfilePicture);
    }

    [Fact]
    public async Task MissingPortraitPlaceholderRemainsBundled()
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "speaker_placeholder.png"));
        Assert.True(bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
    }

    [Fact]
    public async Task RefreshedProfilesUseSuppliedBiosRatherThanUnfinishedVariants()
    {
        var conference = await LoadSnapshotAsync();
        Assert.Empty(Assert.Single(conference.Speakers, speaker => speaker.Name == "Tanner Gooding").Bio);
        Assert.StartsWith("Synthetic test biography",
            Assert.Single(conference.Speakers, speaker => speaker.Name == "Chet Husk").Bio);
        Assert.StartsWith("Synthetic test biography",
            Assert.Single(conference.Speakers, speaker => speaker.Name == "Damian Edwards").Bio);
        Assert.StartsWith("Synthetic test description",
            Assert.Single(conference.Sessions, session => session.Id == "20261021").Description);
        Assert.All(conference.Sessions.Where(session => session.Id is "20261028" or "20261029"),
            session => Assert.DoesNotContain("placeholder", session.Description, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HostedFeedSessionsRetainNativeIdsProfilesAndPacificTimes()
    {
        var conference = await LoadSnapshotAsync();
        var session = Assert.Single(conference.Sessions, session => session.Id == "1349627");
        var speaker = Assert.Single(conference.Speakers, speaker => speaker.Id == "fc942537-8236-4e04-8987-b7c15ed7346f");
        Assert.Equal("Building Oracle AI Apps with .NET and EF Core 11", session.Title);
        Assert.Equal(new[] { speaker.Id }, session.SpeakerIds);
        Assert.Equal("Alex Keh", speaker.Name);
        Assert.Equal("Synthetic test speaker tagline.", speaker.Tagline);
        Assert.NotEmpty(speaker.Bio);
        Assert.StartsWith("https://", speaker.ProfilePicture, StringComparison.Ordinal);
        Assert.Equal(new DateOnly(2026, 11, 13), Agenda.LocalDay(session.StartsAt, ConferenceZone));
        Assert.Equal(TimeSpan.FromHours(-8), session.StartsAt.Offset);
        Assert.Equal(36, conference.Sessions.Count(session => Agenda.LocalDay(session.StartsAt, ConferenceZone).Day >= 12));
    }

    [Theory]
    [InlineData("https://github.com/person.png")]
    [InlineData("https://avatars.githubusercontent.com/u/123")]
    [InlineData("https://person.github.io/photo.png")]
    public async Task GithubPortraitIsOmittedWithoutLosingPublicSessionizeBiographyOrSessions(string url)
    {
        var example = Example;
        var conference = await ReadAsync(example with
        {
            Speakers = [example.Speakers[0] with { ProfilePicture = url }, example.Speakers[1]]
        });
        Assert.Single(conference.Sessions);
        Assert.Equal("Published bio", conference.Speakers[0].Bio);
        Assert.Empty(conference.Speakers[0].ProfilePicture);
    }

    [Fact]
    public async Task SessionizeFieldsMapWithoutDependingOnAppSpecificFields()
    {
        var conference = await ReadAsync(Example);
        var session = Assert.Single(conference.Sessions);
        Assert.Equal("14022", session.Id);
        Assert.Equal(new[] { "speaker-1", "speaker-2" }, session.SpeakerIds);
        Assert.Equal(new[] { ".NET MAUI" }, session.Topics);
        Assert.Empty(session.Description);
        Assert.Equal("First Speaker", conference.Speakers[0].Name);
        Assert.Equal("Published bio", conference.Speakers[0].Bio);
        Assert.Equal("Published tagline", conference.Speakers[0].Tagline);
        Assert.Equal("https://example.com/avatar.png", conference.Speakers[0].ProfilePicture);
        Assert.Equal("Second Speaker", conference.Speakers[1].Name);
        Assert.Empty(conference.Speakers[1].Bio);
        Assert.Empty(conference.Speakers[1].ProfilePicture);
    }

    [Fact]
    public async Task LegacyLocalAssetMappingsAreIgnoredBySessionizeImport()
    {
        var json = JsonSerializer.Serialize(Metadata, ConferenceJsonContext.Default.ConferenceDocument).Insert(1,
            "\"speakerProfilePictureAssets\":{\"speaker-2\":\"speaker_obsolete.jpg\",\"missing-speaker\":\"speaker_missing.jpg\"},");
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var metadata = await ConferenceJson.ReadAsync(stream);
        var conference = await ReadAsync(Example, metadata);
        Assert.Equal(2, conference.Speakers.Count);
        Assert.Equal("https://example.com/avatar.png", conference.Speakers[0].ProfilePicture);
        Assert.Empty(conference.Speakers[1].ProfilePicture);
        Assert.DoesNotContain("pictureAsset",
            JsonSerializer.Serialize(conference, ConferenceJsonContext.Default.ConferenceDocument),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SessionizeAllWireShapeUsesTagLineAndIdReferences()
    {
        var conference = await ReadJsonAsync("""
            {
              "sessions": [{
                "id": "14022", "title": "Aiden's Keynote", "description": null,
                "startsAt": "2023-09-16T09:00:00Z", "endsAt": "2023-09-16T10:00:00Z",
                "isServiceSession": false, "isPlenumSession": true,
                "speakers": ["00000000-0000-0000-0000-000000000004"],
                "categoryItems": [4373], "questionAnswers": [{"questionId": 148, "answerValue": "67"}],
                "roomId": 215, "liveUrl": null, "recordingUrl": null,
                "status": "Accepted", "isInformed": false, "isConfirmed": false
              }],
              "speakers": [{
                "id": "00000000-0000-0000-0000-000000000004",
                "firstName": "Aiden", "lastName": "Test", "fullName": "Aiden Test",
                "bio": "Demo biography", "tagLine": "Professional public speaker",
                "profilePicture": null, "isTopSpeaker": true,
                "links": [{"title": "Website", "url": "https://sessionize.com", "linkType": "Website"}],
                "sessions": [14022], "categoryItems": [], "questionAnswers": []
              }],
              "questions": [{"id": 148, "question": "Demo", "questionType": "Percentage", "sort": 5}],
              "categories": [{
                "id": 2124, "title": "Session format", "items": [{"id": 4373, "name": "Session", "sort": 2}],
                "sort": 0, "type": "session"
              }],
              "rooms": [{"id": 215, "name": "Green Room", "sort": 0}]
            }
            """);
        var session = Assert.Single(conference.Sessions);
        var speaker = Assert.Single(conference.Speakers);
        Assert.Equal("14022", session.Id);
        Assert.Equal(new[] { speaker.Id }, session.SpeakerIds);
        Assert.Equal("Aiden Test", speaker.Name);
        Assert.Equal("Professional public speaker", speaker.Tagline);
        Assert.Equal("Demo biography", speaker.Bio);
        Assert.Empty(speaker.ProfilePicture);
        Assert.Equal(new[] { "Session" }, session.Topics);
    }

    [Theory]
    [InlineData("http://example.com/avatar.png")]
    [InlineData("file:///tmp/avatar.png")]
    public async Task ProfilePicturesMustUseHttps(string profilePicture)
    {
        var example = Example;
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(example with
        {
            Speakers = [example.Speakers[0] with { ProfilePicture = profilePicture }, example.Speakers[1]]
        }));
    }

    [Fact]
    public async Task AdditionalSessionizeFieldsAreAccepted()
    {
        var json = JsonSerializer.Serialize(Example, SessionizeJsonContext.Default.SessionizeDocument);
        json = json.Replace("\"sessions\":", "\"questions\":[],\"futureField\":true,\"sessions\":", StringComparison.Ordinal);
        Assert.Single((await ReadJsonAsync(json)).Sessions);
    }

    [Theory]
    [InlineData("2026-11-11T08:00:00", "2026-11-11T09:00:00")]
    [InlineData("2026-11-11T08:00:00-08:00", "2026-11-11T09:00:00-08:00")]
    [InlineData("2026-11-11T16:00:00Z", "2026-11-11T17:00:00Z")]
    [InlineData("2026-11-11T16:00:00z", "2026-11-11T17:00:00z")]
    [InlineData(" 2026-11-11T16:00:00Z ", "2026-11-11T17:00:00Z ")]
    public async Task TimesUseExplicitOffsetsOrConferenceZoneNeverDeviceZone(string start, string end)
    {
        var example = Example;
        var conference = await ReadAsync(example with
        {
            Sessions = [example.Sessions[0] with { StartsAt = start, EndsAt = end }]
        });
        Assert.Equal(DateTimeOffset.Parse("2026-11-11T16:00:00Z"), conference.Sessions[0].StartsAt);
    }

    [Fact]
    public async Task UnscheduledTalksAreNotGivenInventedTimes()
    {
        var example = Example;
        var conference = await ReadAsync(example with
        {
            Sessions = [example.Sessions[0] with { StartsAt = null, EndsAt = null, RoomId = null }]
        });
        Assert.Empty(conference.Sessions);
        Assert.Equal(2, conference.Speakers.Count);
    }

    [Fact]
    public async Task ScheduledSpeakerlessServiceSessionsAreSupported()
    {
        var example = Example;
        var conference = await ReadAsync(example with
        {
            Sessions = [example.Sessions[0] with { Speakers = [], CategoryItems = [], RoomId = null }]
        });
        Assert.Empty(Assert.Single(conference.Sessions).SpeakerIds);
    }

    [Theory]
    [InlineData(null, "2026-11-11T09:00:00")]
    [InlineData("", "2026-11-11T09:00:00")]
    [InlineData("bad-time", "2026-11-11T09:00:00")]
    [InlineData("2026-11-11T08:00:00", "2026-11-11T07:00:00")]
    [InlineData("2026-03-08T02:30:00", "2026-03-08T04:00:00")]
    [InlineData("2026-11-01T01:30:00", "2026-11-01T03:00:00")]
    public async Task IncompleteInvalidOrAmbiguousTimesAreRejected(string? start, string? end)
    {
        var example = Example;
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(example with
        {
            Sessions = [example.Sessions[0] with { StartsAt = start, EndsAt = end }]
        }));
    }

    [Fact]
    public async Task InvalidReferencesAndDuplicateIdsAreRejected()
    {
        var example = Example;
        foreach (var session in new[]
        {
            example.Sessions[0] with { Speakers = ["missing"] },
            example.Sessions[0] with { Speakers = ["speaker-1", "speaker-1"] },
            example.Sessions[0] with { CategoryItems = [999] },
            example.Sessions[0] with { RoomId = 999 },
            example.Sessions[0] with { Id = "" },
            example.Sessions[0] with { Title = "" }
        })
            await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(example with { Sessions = [session] }));
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(example with
        {
            Sessions = [example.Sessions[0], example.Sessions[0]]
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(example with
        {
            Speakers = [example.Speakers[0], example.Speakers[0], example.Speakers[1]]
        }));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{"sessions":null,"speakers":[],"categories":[],"rooms":[]}""")]
    [InlineData("""{"sessions":[],"speakers":null,"categories":[],"rooms":[]}""")]
    public async Task NullDataIsNotSuccess(string json) =>
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadJsonAsync(json));

    [Theory]
    [InlineData("{}")]
    [InlineData("not-json")]
    [InlineData("""{"sessions":[],"speakers":[]}""")]
    public async Task MissingOrMalformedContractIsNotSuccess(string json) =>
        await Assert.ThrowsAsync<JsonException>(() => ReadJsonAsync(json));

    [Fact]
    public async Task CancellationIsPreserved()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var stream = new MemoryStream("{}"u8.ToArray());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SessionizeJson.ReadAsync(stream, Metadata, ConferenceZone, cancellation.Token));
    }
}
