namespace DotNetConf.Core.Tests;

public sealed class ScheduleDiscoveryTests
{
    private static ConferenceDocument Catalog() => new()
    {
        Speakers = Enumerable.Range(0, 7).Select(index => new Speaker
        {
            Id = $"speaker-{index}", Name = $"Synthetic Speaker {index}"
        }).ToList(),
        Sessions = Enumerable.Range(0, 60).Select(index => new Session
        {
            Id = $"talk-{index}", Title = $"Synthetic discovery talk {index}",
            Topics = [$"Topic {index % 4}"], SpeakerIds = [$"speaker-{index % 7}"],
            StartsAt = DateTimeOffset.Parse("2026-11-10T00:00Z").AddDays(index / 20).AddMinutes(index % 20 * 30),
            EndsAt = DateTimeOffset.Parse("2026-11-10T00:00Z").AddDays(index / 20).AddMinutes(index % 20 * 30 + 25)
        }).ToList()
    };

    [Fact]
    public void SeededDiscoveryIsRepeatableAndIndependentOfAgendaOrdering()
    {
        var conference = Catalog();
        var first = SchedulePlanner.Discover(conference, TimeZoneInfo.Utc, [], random: new Random(42));
        var second = SchedulePlanner.Discover(conference with
        {
            Sessions = conference.Sessions.AsEnumerable().Reverse().ToList()
        }, TimeZoneInfo.Utc, [], random: new Random(42));
        Assert.Equal(first.SessionIds, second.SessionIds);
        Assert.Equal(first.SessionIds, first.MatchedSessionIds);
        Assert.Null(first.GapAnchorIds);
        Assert.Equal(6, first.SessionIds.Length);
    }

    [Fact]
    public void DifferentSeedsProvideDifferentRealCompatiblePreviews()
    {
        var conference = Catalog();
        var previews = Enumerable.Range(0, 12).Select(seed =>
            SchedulePlanner.Discover(conference, TimeZoneInfo.Utc, [], random: new Random(seed))).ToArray();
        Assert.True(previews.Select(proposal => string.Join(",", proposal.SessionIds)).Distinct().Count() > 1);
        foreach (var preview in previews)
        {
            Assert.Equal(6, preview.SessionIds.Distinct().Count());
            Assert.True(SchedulePlanner.Review(conference, preview, TimeZoneInfo.Utc, []).CanApply);
            Assert.All(preview.SessionIds, id => Assert.Contains(conference.Sessions, session => session.Id == id));
        }
    }

    [Fact]
    public void DiscoveryVariesTopicsSpeakersAndDaysWhenTheyAreCompatible()
    {
        var conference = Catalog();
        var proposal = SchedulePlanner.Discover(conference, TimeZoneInfo.Utc, [], random: new Random(17));
        var selected = conference.Sessions.Where(session => proposal.SessionIds.Contains(session.Id)).ToArray();
        Assert.Equal(4, selected.SelectMany(session => session.Topics).Distinct().Count());
        Assert.Equal(6, selected.SelectMany(session => session.SpeakerIds).Distinct().Count());
        Assert.Equal(3, selected.Select(session => Agenda.LocalDay(session.StartsAt, TimeZoneInfo.Utc)).Distinct().Count());
    }

    [Fact]
    public void SavedSessionsAndTheirOverlappingAlternativesAreNeverDiscovered()
    {
        var conference = Catalog();
        var saved = conference.Sessions[0];
        conference.Sessions.Add(saved with { Id = "synthetic-conflict", Title = "Synthetic conflicting talk" });
        var proposal = SchedulePlanner.Discover(conference, TimeZoneInfo.Utc, [saved.Id], random: new Random(4));
        Assert.DoesNotContain(saved.Id, proposal.SessionIds);
        Assert.DoesNotContain("synthetic-conflict", proposal.SessionIds);
        Assert.True(SchedulePlanner.Review(conference, proposal, TimeZoneInfo.Utc, [saved.Id]).CanApply);
    }

    [Fact]
    public void DiscoveryHonorsWholeSessionAvailability()
    {
        var conference = Catalog();
        AvailabilityWindow[] windows = [new("2026-11-10T02:00", "2026-11-10T05:00", "UTC")];
        var proposal = SchedulePlanner.Discover(conference, TimeZoneInfo.Utc, [], random: new Random(8),
            availability: windows);
        Assert.Equal(6, proposal.SessionIds.Length);
        Assert.Equal(windows, proposal.Availability);
        Assert.True(SchedulePlanner.Review(conference, proposal, TimeZoneInfo.Utc, []).CanApply);
        Assert.All(proposal.SessionIds, id =>
        {
            var session = conference.Sessions.Single(session => session.Id == id);
            Assert.InRange(session.StartsAt.Hour, 2, 4);
            Assert.True(session.EndsAt <= DateTimeOffset.Parse("2026-11-10T05:00Z"));
        });
    }

    [Fact]
    public void GapDiscoveryKeepsAnchorsAndOnlyUsesFreeTimeOnSavedDays()
    {
        var conference = Catalog();
        var proposal = SchedulePlanner.Discover(conference, TimeZoneInfo.Utc, ["talk-9"],
            fillGaps: true, random: new Random(12));
        Assert.Equal(["talk-9"], proposal.GapAnchorIds!);
        Assert.DoesNotContain("talk-9", proposal.SessionIds);
        Assert.True(SchedulePlanner.Review(conference, proposal, TimeZoneInfo.Utc, ["talk-9"]).CanApply);
        Assert.All(proposal.SessionIds, id => Assert.Equal(10,
            conference.Sessions.Single(session => session.Id == id).StartsAt.Day));
        Assert.False(SchedulePlanner.Review(conference, proposal, TimeZoneInfo.Utc, ["talk-9", "talk-10"]).CanApply);
    }

    [Fact]
    public void DiscoveryReturnsHonestEmptyOrSmallPreviewsWithoutInventingSessions()
    {
        Assert.Empty(SchedulePlanner.Discover(new(), TimeZoneInfo.Utc, [], random: new Random(1)).SessionIds);
        var conference = Catalog();
        var small = conference with { Sessions = conference.Sessions.Take(2).ToList() };
        Assert.Equal(2, SchedulePlanner.Discover(small, TimeZoneInfo.Utc, [], random: new Random(1)).SessionIds.Length);
        Assert.Empty(SchedulePlanner.Discover(small, TimeZoneInfo.Utc, small.Sessions.Select(session => session.Id).ToArray(),
            random: new Random(1)).SessionIds);
    }

    [Fact]
    public void GapDiscoveryRequiresKnownSavedCommitments()
    {
        var conference = Catalog();
        Assert.Throws<InvalidDataException>(() =>
            SchedulePlanner.Discover(conference, TimeZoneInfo.Utc, [], fillGaps: true));
        Assert.Throws<InvalidDataException>(() =>
            SchedulePlanner.Discover(conference, TimeZoneInfo.Utc, ["unknown"], fillGaps: true));
    }

    [Fact]
    public void DiscoveryReturnsASmallerCompatiblePreviewWhenTheTargetIsImpossible()
    {
        var conference = Catalog();
        var first = conference.Sessions[0];
        conference = conference with
        {
            Sessions = conference.Sessions.Take(8).Select(session => session with
            {
                StartsAt = first.StartsAt, EndsAt = first.EndsAt
            }).ToList()
        };
        var proposal = SchedulePlanner.Discover(conference, TimeZoneInfo.Utc, [], random: new Random(42));
        Assert.Single(proposal.SessionIds);
        Assert.True(SchedulePlanner.Review(conference, proposal, TimeZoneInfo.Utc, []).CanApply);
    }

    [Fact]
    public void DiscoveryCanHonorALargerExplicitTargetWithoutASaveLimit()
    {
        var conference = Catalog();
        var proposal = SchedulePlanner.Discover(conference, TimeZoneInfo.Utc, [], random: new Random(42), count: 20);
        Assert.Equal(20, proposal.SessionIds.Length);
        Assert.True(SchedulePlanner.Review(conference, proposal, TimeZoneInfo.Utc, []).CanApply);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DiscoveryRejectsInvalidTargets(int count) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SchedulePlanner.Discover(Catalog(), TimeZoneInfo.Utc, [], count: count));
}
