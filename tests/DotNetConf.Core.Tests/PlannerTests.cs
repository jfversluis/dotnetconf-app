using DotNetConf.Core;

namespace DotNetConf.Core.Tests;

public sealed class PlannerTests
{
    private static readonly ConferenceDocument Conference = new()
    {
        Speakers = [new() { Id = "alex", Name = "Alex Rivera" }, new() { Id = "sam", Name = "Sam Taylor" }],
        Sessions =
        [
            Talk("mobile", "2026-11-10T16:00Z", "2026-11-10T16:45Z", "alex"),
            Talk("language", "2026-11-10T17:00Z", "2026-11-10T17:45Z", "sam"),
            Talk("overlap", "2026-11-10T16:30Z", "2026-11-10T17:15Z", "sam"),
            Talk("adjacent", "2026-11-10T16:45Z", "2026-11-10T17:00Z", "alex"),
            Talk("midnight", "2026-11-11T23:30Z", "2026-11-12T00:15Z", "sam")
        ]
    };

    private static Session Talk(string id, string start, string end, string speaker) => new()
    {
        Id = id, Title = id, StartsAt = DateTimeOffset.Parse(start), EndsAt = DateTimeOffset.Parse(end),
        SpeakerIds = [speaker]
    };

    [Fact]
    public void AcceptsRealIdsFullDurationAndRequiredSpeakerInDeviceZone()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Demo+2", TimeSpan.FromHours(2), "Demo+2", "Demo+2");
        var review = SchedulePlanner.Review(Conference,
            new(["mobile", "adjacent"], ["alex rivera"], [Window("2026-11-10T18:00/2026-11-10T19:00")]), zone, []);
        Assert.True(review.CanApply);
        Assert.Empty(review.Errors);
        Assert.Equal(["mobile", "adjacent"], review.Sessions.Select(s => s.Id));
    }

    [Fact]
    public void ExplicitUtcAvailabilityDoesNotRequireModelTimeZoneArithmetic()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Demo+2", TimeSpan.FromHours(2), "Demo+2", "Demo+2");
        Assert.True(SchedulePlanner.Review(Conference, new(["mobile"], [],
            [new("2026-11-10T16:00", "2026-11-10T16:45", "UTC")]), zone, []).CanApply);
        Assert.False(SchedulePlanner.Review(Conference, new(["mobile"], [],
            [new("2026-11-10T16:00", "2026-11-10T16:45", "Not/AZone")]), zone, []).CanApply);
    }

    [Fact]
    public void RejectsHallucinatedAndDuplicateIds()
    {
        var review = Review(new(["not-real", "mobile", "mobile"], [], []));
        Assert.False(review.CanApply);
        Assert.Contains(review.Errors, e => e.StartsWith("Unknown session ID:"));
        Assert.Contains(review.Errors, e => e.StartsWith("Duplicate session:"));
    }

    [Theory]
    [InlineData("2026-11-10T16:01/2026-11-10T18:00")]
    [InlineData("2026-11-10T16:00/2026-11-10T16:44")]
    public void RequiresTheEntireSessionInsideAvailability(string window)
    {
        var review = Review(new(["mobile"], [], [Window(window)]));
        Assert.False(review.CanApply);
        Assert.Contains(review.Errors, e => e.StartsWith("Outside your availability:"));
    }

    [Theory]
    [InlineData("sometime Tuesday")]
    [InlineData("2026-11-10T18:00/2026-11-10T16:00")]
    [InlineData("2026-11-10T16:00Z/2026-11-10T18:00Z")]
    [InlineData("2026-11-10T16:00+02:00/2026-11-10T18:00+02:00")]
    [InlineData("2026-11-10T16:00/2026-11-10T16:00")]
    public void RejectsMalformedReversedOrOffsetBearingLocalWindows(string window)
    {
        var review = Review(new(["mobile"], [], [Window(window)]));
        Assert.False(review.CanApply);
        Assert.Contains(review.Errors, e => e.StartsWith("Invalid or ambiguous availability:"));
    }

    [Theory]
    [InlineData("2026-03-08T02:15/2026-03-08T03:15")]
    [InlineData("2026-11-01T01:15/2026-11-01T02:15")]
    public void RejectsInvalidAndAmbiguousDaylightSavingTimes(string window)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var review = SchedulePlanner.Review(Conference, new(["mobile"], [], [Window(window)]), zone, []);
        Assert.False(review.CanApply);
        Assert.Contains(review.Errors, e => e.StartsWith("Invalid or ambiguous availability:"));
    }

    [Fact]
    public void AllowsMidnightSpanningSessionsAndMultipleAvailabilityWindows()
    {
        var review = Review(new(["mobile", "midnight"], ["Alex Rivera", "Sam Taylor"],
            [Window("2026-11-10T16:00/2026-11-10T16:45"), Window("2026-11-11T23:00/2026-11-12T01:00")]));
        Assert.True(review.CanApply);
    }

    [Fact]
    public void ReportsUnknownAndUnfulfilledRequiredSpeakers()
    {
        var review = Review(new(["mobile"], ["Not A Speaker", "Sam Taylor"], []));
        Assert.False(review.CanApply);
        Assert.Contains(review.Errors, e => e.StartsWith("Speaker not found"));
        Assert.Contains(review.Errors, e => e.StartsWith("Required speaker has no proposed session:"));
    }

    [Fact]
    public void RejectsOverlappingProposalsAndConflictsWithExistingLikes()
    {
        Assert.False(Review(new(["mobile", "overlap"], [], [])).CanApply);
        var review = SchedulePlanner.Review(Conference, new(["mobile"], [], []), TimeZoneInfo.Utc, ["overlap"]);
        Assert.False(review.CanApply);
        Assert.Contains(review.Errors, e => e.StartsWith("Conflicts with a liked session:"));
    }

    [Fact]
    public void AlreadyLikedSessionIsIdempotentAndUnknownSavedIdsAreNotErrors()
    {
        var review = SchedulePlanner.Review(Conference, new(["mobile"], [], []), TimeZoneInfo.Utc,
            ["mobile", "no-longer-in-agenda"]);
        Assert.True(review.CanApply);
    }

    [Fact]
    public void EmptyAndOversizedProposalsCannotBeApplied()
    {
        Assert.False(Review(new([], [], [])).CanApply);
        Assert.False(Review(new(Enumerable.Repeat("mobile", 13).ToArray(), [], [])).CanApply);
        Assert.False(Review(new(["mobile"], Enumerable.Repeat("Alex Rivera", 9).ToArray(), [])).CanApply);
    }

    [Fact]
    public void ArrangementKeepsTheLargestCompatibleSubsetAndRequiredSpeakers()
    {
        var arranged = SchedulePlanner.Arrange(Conference,
            new(["mobile", "overlap", "adjacent", "language"], ["Alex", "Sam"], []), TimeZoneInfo.Utc, []);
        Assert.Equal(["mobile", "adjacent", "language"], arranged.SessionIds);
        Assert.True(Review(arranged).CanApply);

        var requiredSam = SchedulePlanner.Arrange(Conference,
            new(["mobile", "overlap"], ["Sam"], []), TimeZoneInfo.Utc, []);
        Assert.Equal(["overlap"], requiredSam.SessionIds);
        Assert.True(Review(requiredSam).CanApply);
    }

    [Fact]
    public void ArrangementDoesNotHideUnknownIdsMissingSpeakersOrLikedConflicts()
    {
        foreach (var proposal in new ScheduleProposal[]
        {
            new(["mobile", "not-real"], [], []),
            new(["mobile", "overlap"], ["Not A Speaker"], []),
            new(["mobile", "overlap"], [], [Window("2026-11-10T17:00/2026-11-10T18:00")])
        })
            Assert.Same(proposal, SchedulePlanner.Arrange(Conference, proposal, TimeZoneInfo.Utc, []));
        var conflict = new ScheduleProposal(["mobile", "language"], [], []);
        Assert.Same(conflict, SchedulePlanner.Arrange(Conference, conflict, TimeZoneInfo.Utc, ["overlap"]));
    }

    [Fact]
    public void AllMatchingRequestsMustNotDropOverlapsOrPretendUnknownQueriesSucceeded()
    {
        var all = new ScheduleProposal(["language", "overlap", "midnight"], [], [])
        {
            AllSessionsMatchingQuery = "Sam"
        };
        Assert.Same(all, SchedulePlanner.Arrange(Conference, all, TimeZoneInfo.Utc, []));
        Assert.Contains(Review(all).Errors, e => e.StartsWith("Overlapping proposals:"));
        Assert.Contains(Review(all with { SessionIds = ["language"] }).Errors,
            e => e.StartsWith("You requested all sessions matching"));
        Assert.Contains(Review(all with { AllSessionsMatchingQuery = "unknown topic" }).Errors,
            e => e.StartsWith("No sessions match"));
    }

    [Fact]
    public void AllSpeakerRequestsMustNotDropOverlapsOrUnavailableTalks()
    {
        var all = new ScheduleProposal(["language", "overlap", "midnight"], ["Sam"], [])
        {
            AllSessionsByRequiredSpeakers = true
        };
        Assert.Same(all, SchedulePlanner.Arrange(Conference, all, TimeZoneInfo.Utc, []));
        Assert.False(Review(all).CanApply);
        var unavailable = all with
        {
            SessionIds = ["language"],
            Availability = [Window("2026-11-10T17:00/2026-11-10T18:00")]
        };
        Assert.Contains(Review(unavailable).Errors, e => e.StartsWith("You requested all sessions"));
    }

    private static ProposalReview Review(ScheduleProposal proposal) =>
        SchedulePlanner.Review(Conference, proposal, TimeZoneInfo.Utc, []);

    private static AvailabilityWindow Window(string text)
    {
        var parts = text.Split('/');
        return new(parts[0], parts.Length == 2 ? parts[1] : "", "local");
    }
}
