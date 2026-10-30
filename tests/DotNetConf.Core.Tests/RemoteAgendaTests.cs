using System.Text;
using System.Text.Json;
using DotNetConf.Core;

namespace DotNetConf.Core.Tests;

public sealed class RemoteAgendaTests
{
    internal const string GeraldId = "7871d1fd-13e8-5c11-be64-fc52a0091913";
    internal static readonly ConferenceDocument Bundle = new()
    {
        SchemaVersion = 1, Name = ".NET Conf", Description = "Conference", WebsiteUrl = "https://dotnetconf.net",
        Speakers =
        [
            new() { Id = GeraldId, Name = "Gerald Versluis", Bio = "Rich bundled bio", Tagline = "Bundled tagline",
                ProfilePicture = "https://example.com/gerald.jpg" },
            new() { Id = "community-speaker", Name = "Community Speaker", Bio = "Community bundled bio",
                ProfilePicture = "https://example.com/community.jpg" }
        ],
        Sessions =
        [
            new() { Id = "20261006", Title = "MAUI", Description = "Bundled abstract",
                StartsAt = DateTimeOffset.Parse("2026-11-10T11:30:00-08:00"),
                EndsAt = DateTimeOffset.Parse("2026-11-10T12:00:00-08:00"), SpeakerIds = [GeraldId], Topics = [".NET MAUI"] },
            new() { Id = "1234", Title = "Community", Description = "Community abstract",
                StartsAt = DateTimeOffset.Parse("2026-11-12T08:00:00-08:00"),
                EndsAt = DateTimeOffset.Parse("2026-11-12T08:30:00-08:00"), SpeakerIds = ["community-speaker"] }
        ]
    };

    internal static MicrosoftAgenda Microsoft => new()
    {
        Sessions =
        [
            new() { Id = "BRK006", Title = "MAUI", Day = 1, StartTime = "11:30:00", EndTime = "12:00:00",
                Timezone = "PST", SpeakerIds = ["speaker-gerald-versluis"], Topics = [".NET MAUI"], Abstract = "Bundled abstract" }
        ],
        Speakers = [new() { Id = "speaker-gerald-versluis", Name = "Gerald Versluis", PrimaryBio = "TODO" }]
    };

    internal static SessionizeDocument Community => new()
    {
        Sessions =
        [
            new() { Id = "1234", Title = "Community", Description = "Community abstract", StartsAt = "2026-11-12T08:00:00", EndsAt = "2026-11-12T08:30:00",
                Speakers = ["community-speaker"], CategoryItems = [] }
        ],
        Speakers = [new() { Id = "community-speaker", FullName = "Community Speaker",
            Bio = "Community bundled bio", ProfilePicture = "https://example.com/community.jpg" }],
        Categories = [], Rooms = []
    };

    internal static string Json(MicrosoftAgenda data) =>
        JsonSerializer.Serialize(data, RemoteAgendaJsonContext.Default.MicrosoftAgenda);
    internal static string Json(SessionizeDocument data) =>
        JsonSerializer.Serialize(data, SessionizeJsonContext.Default.SessionizeDocument);

    private static async Task<ConferenceDocument> ReadAsync(MicrosoftAgenda data, ConferenceDocument? previous = null) =>
        await ReadJsonAsync(Json(data), previous);

    private static async Task<ConferenceDocument> ReadJsonAsync(string json, ConferenceDocument? previous = null)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await MicrosoftAgendaJson.ReadAsync(stream, previous ?? Bundle);
    }

    [Fact]
    public async Task ActualMcpSessionIgnoresUnansweredCoSpeakerButKeepsStephenHalterIdentity()
    {
        var result = await ReadJsonAsync("""
            {"sessions":[{"id":"BRK021","title":"What's new in the MCP C# SDK",
            "day":2,"startTime":"11:00:00","endTime":"11:30:00","timezone":"PST",
            "speakerIds":["speaker-stephen-halter","speaker-no-response"],
            "abstract":"The Model Context Protocol is evolving, and the MCP C# SDK is evolving with it.",
            "topics":["C#","ASP.NET Core"],"youtubeLink":"*No response*","sessionLinks":"*No response*"}],
            "speakers":[{"id":"speaker-no-response","name":"*No response*","avatar":"",
            "primaryBio":"","additionalBio":"*No response*"},
            {"id":"speaker-stephen-halter","name":"Stephen Halter","avatar":"",
            "primaryBio":"ASP.NET Core Engineer","additionalBio":""}]}
            """);
        var session = Assert.Single(result.Sessions);
        Assert.Equal("20261021", session.Id);
        Assert.Equal(["afb89ca3-235b-5f5e-ba59-fb09fe16459d"], session.SpeakerIds);
        Assert.Equal("Stephen Halter", Assert.Single(result.Speakers).Name);
        Assert.Equal(new DateOnly(2026, 11, 11), DateOnly.FromDateTime(session.StartsAt.Date));
    }

    [Fact]
    public async Task ActualMicrosoftWireFieldsMapAndPlaceholderRowsAreIgnored()
    {
        var result = await ReadJsonAsync("""
            {"sessions":[{"id":"BRK006","title":"What’s New in .NET MAUI in .NET 11",
            "day":1,"startTime":"11:30:00","endTime":"12:00:00","timezone":"PST",
            "speakerIds":["speaker-gerald-versluis"],"abstract":"New features",
            "topics":[".NET MAUI",".NET AI"],"youtubeLink":"","sessionLinks":""}],
            "speakers":[{"id":"speaker-no-response","name":"*No response*","avatar":"",
            "primaryBio":"","additionalBio":"*No response*"},
            {"id":"speaker-gerald-versluis","name":"Gerald Versluis",
            "avatar":"https://github.com/user-attachments/assets/390e288c-b2e5-4958-ab04-7c5b006777ec",
            "primaryBio":"Published biography","additionalBio":"","primarySocials":"","additionalSocials":""}]}
            """);
        var session = Assert.Single(result.Sessions);
        var speaker = Assert.Single(result.Speakers);
        Assert.Equal("20261006", session.Id);
        Assert.Equal(GeraldId, speaker.Id);
        Assert.Equal([GeraldId], session.SpeakerIds);
        Assert.Equal(DateTimeOffset.Parse("2026-11-10T19:30:00Z"), session.StartsAt);
        Assert.Equal(TimeSpan.FromHours(-8), session.StartsAt.Offset);
        Assert.Equal("Published biography", speaker.Bio);
        Assert.Equal("https://example.com/gerald.jpg", speaker.ProfilePicture);
        Assert.Equal([".NET MAUI", ".NET AI"], session.Topics);
    }

    [Theory]
    [InlineData("KEY001", "20260001")]
    [InlineData("KEY002", "20260002")]
    [InlineData("BRK001", "20261001")]
    [InlineData("BRK032", "20261032")]
    public async Task NativeSessionIdentitiesStayStableOnDisplayEdits(string sourceId, string expected)
    {
        var data = Microsoft;
        var result = await ReadAsync(data with
        {
            Sessions = [data.Sessions[0] with { Id = sourceId, Title = "Renamed title" }],
            Speakers = [data.Speakers[0] with { Name = "Renamed presenter" }]
        });
        Assert.Equal(expected, Assert.Single(result.Sessions).Id);
        Assert.Equal(GeraldId, Assert.Single(result.Speakers).Id);
    }

    [Fact]
    public async Task NewSpeakersUseStableSlugsAndDayTwoUsesPacific()
    {
        var data = Microsoft;
        var result = await ReadAsync(data with
        {
            Sessions = [data.Sessions[0] with { Day = 2, SpeakerIds = ["speaker-new"] }],
            Speakers = [data.Speakers[0] with { Id = "speaker-new", Name = "New speaker" }]
        });
        Assert.Equal("speaker-new", Assert.Single(result.Speakers).Id);
        Assert.Equal(new DateOnly(2026, 11, 11), DateOnly.FromDateTime(Assert.Single(result.Sessions).StartsAt.Date));
    }

    [Theory]
    [InlineData("TODO")]
    [InlineData("  todo  ")]
    [InlineData("TBD")]
    [InlineData("*No response*")]
    [InlineData("Chet is")]
    [InlineData("")]
    [InlineData(null)]
    public async Task PlaceholderBioAndAbstractRetainLastGoodValues(string? value)
    {
        var data = Microsoft;
        var result = await ReadAsync(data with
        {
            Sessions = [data.Sessions[0] with { Abstract = value }],
            Speakers = [data.Speakers[0] with { PrimaryBio = value, AdditionalBio = "\"Get copilot to write me one\" -Damian, October 2 2026" }]
        });
        Assert.Equal("Rich bundled bio", Assert.Single(result.Speakers).Bio);
        Assert.Equal("Bundled abstract", Assert.Single(result.Sessions).Description);
    }

    [Fact]
    public async Task UsefulSourceContentWinsIncludingPhotoAndAdditionalBiography()
    {
        var data = Microsoft;
        var result = await ReadAsync(data with
        {
            Speakers = [data.Speakers[0] with
            {
                PrimaryBio = "New primary", AdditionalBio = "New additional", Avatar = "https://example.com/new.jpg"
            }]
        });
        var speaker = Assert.Single(result.Speakers);
        Assert.Equal("New primary\n\nNew additional", speaker.Bio);
        Assert.Equal("https://example.com/new.jpg", speaker.ProfilePicture);
        Assert.Equal("Bundled tagline", speaker.Tagline);
    }

    [Fact]
    public async Task GithubHttpProfilePhotoIsNotRetainedFromEitherFeedOrPreviousProfile()
    {
        var data = Microsoft;
        var result = await ReadAsync(data with
        {
            Speakers = [data.Speakers[0] with { Avatar = "http://github.com/elbruno.png" }]
        }, Bundle with
        {
            Speakers = [Bundle.Speakers[0] with { ProfilePicture = "https://avatars.githubusercontent.com/u/123" }]
        });
        Assert.Empty(Assert.Single(result.Speakers).ProfilePicture);
    }

    [Theory]
    [InlineData("http://example.com/photo.jpg")]
    [InlineData("not a url")]
    [InlineData("https://github.com/user-attachments/assets/private")]
    public async Task UnusablePhotosRetainPreviousPortrait(string avatar)
    {
        var data = Microsoft;
        var result = await ReadAsync(data with { Speakers = [data.Speakers[0] with { Avatar = avatar }] });
        Assert.Equal("https://example.com/gerald.jpg", Assert.Single(result.Speakers).ProfilePicture);
    }

    [Theory]
    [InlineData("20261006")]
    [InlineData("KEY000")]
    [InlineData("KEYabc")]
    [InlineData("ABC001")]
    [InlineData("BRK1")]
    [InlineData(null)]
    public async Task InvalidMicrosoftIdentitiesAreRejected(string? id)
    {
        var data = Microsoft;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ReadAsync(data with { Sessions = [data.Sessions[0] with { Id = id! }] }));
    }

    [Theory]
    [InlineData(0, "PST", "08:00:00", "09:00:00")]
    [InlineData(3, "PST", "08:00:00", "09:00:00")]
    [InlineData(1, "UTC", "08:00:00", "09:00:00")]
    [InlineData(1, "PDT", "08:00:00", "09:00:00")]
    [InlineData(1, "PST", "25:00:00", "09:00:00")]
    [InlineData(1, "PST", "8am", "09:00:00")]
    [InlineData(1, "PST", "09:00:00", "08:00:00")]
    [InlineData(1, "PST", "08:00:00", "08:00:00")]
    public async Task InvalidDatesZonesAndTimesAreRejected(int day, string timezone, string start, string end)
    {
        var data = Microsoft;
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(data with
        {
            Sessions = [data.Sessions[0] with { Day = day, Timezone = timezone, StartTime = start, EndTime = end }]
        }));
    }

    [Fact]
    public async Task InvalidNullDuplicateAndMissingReferencesAreRejected()
    {
        var data = Microsoft;
        foreach (var session in new[]
        {
            data.Sessions[0] with { SpeakerIds = ["missing"] },
            data.Sessions[0] with { SpeakerIds = [null!] },
            data.Sessions[0] with { SpeakerIds = ["speaker-gerald-versluis", "speaker-gerald-versluis"] },
            data.Sessions[0] with { SpeakerIds = null! },
            data.Sessions[0] with { Topics = null! },
            data.Sessions[0] with { Topics = [""] },
            data.Sessions[0] with { Title = null! },
            null
        })
            await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(data with { Sessions = [session!] }));
        foreach (var speakers in new List<MicrosoftSpeaker>[]
        {
            [null!], [data.Speakers[0], data.Speakers[0]],
            [data.Speakers[0] with { Id = null! }], [data.Speakers[0] with { Name = "*No response*" }]
        })
            await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(data with { Speakers = speakers }));
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(data with { Sessions = [data.Sessions[0], data.Sessions[0]] }));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{"sessions":null,"speakers":[]}""")]
    [InlineData("""{"sessions":[],"speakers":[]}""")]
    [InlineData("""{"sessions":[],"speakers":null}""")]
    public async Task NullAndEmptyFeedsAreRejected(string json) =>
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadJsonAsync(json));

    [Theory]
    [InlineData("{}")]
    [InlineData("invalid")]
    [InlineData("""{"sessions":[]}""")]
    public async Task MissingAndMalformedJsonAreRejected(string json) =>
        await Assert.ThrowsAsync<JsonException>(() => ReadJsonAsync(json));

    [Fact]
    public async Task AdapterPreservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Json(Microsoft)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MicrosoftAgendaJson.ReadAsync(stream, Bundle, cancellation.Token));
    }
}
