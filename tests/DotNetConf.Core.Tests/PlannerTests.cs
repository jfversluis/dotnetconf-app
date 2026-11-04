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
        SpeakerIds = [speaker], Topics = [speaker == "sam" ? "C#" : "Mobile"]
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
    public void EmptyDuplicateAndOverconstrainedProposalsCannotBeApplied()
    {
        Assert.Equal("Select at least one session before saving.",
            Assert.Single(Review(new([], [], [])).Errors));
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
            TopicFilter = new(["C#"], false, false, true)
        };
        Assert.Same(all, SchedulePlanner.Arrange(Conference, all, TimeZoneInfo.Utc, []));
        Assert.Contains(Review(all).Errors, e => e.StartsWith("Overlapping proposals:"));
        Assert.Contains(Review(all with { SessionIds = ["language"] }).Errors,
            e => e.StartsWith("You requested all sessions matching"));
        Assert.Contains(Review(all with { TopicFilter = new(["unknown topic"], false, false, true) }).Errors,
            e => e.StartsWith("No sessions match"));
    }

    [Fact]
    public void AllSpeakerRequestsPreserveOverlapsButOnlyRequireTalksWithinAvailability()
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
        Assert.True(Review(unavailable).CanApply);
    }

    [Fact]
    public void SavedSessionsSatisfySpeakerAndAllMatchingRequirements()
    {
        var proposal = new ScheduleProposal(["adjacent"], ["Alex"], [])
        {
            AllSessionsByRequiredSpeakers = true,
            TopicFilter = new(["Mobile"], false, true, true)
        };
        Assert.True(SchedulePlanner.Review(Conference, proposal, TimeZoneInfo.Utc, ["mobile"]).CanApply);
        var requiredSaved = new ScheduleProposal(["language"], ["Alex"], []);
        Assert.True(SchedulePlanner.Review(Conference, requiredSaved, TimeZoneInfo.Utc, ["mobile"]).CanApply);
        Assert.Equal(["language"], SchedulePlanner.Recommend(Conference, requiredSaved, ["language"],
            TimeZoneInfo.Utc, ["mobile"]));
        var scoped = requiredSaved with { TopicFilter = new(["C#"], false, true, false) };
        Assert.Contains(SchedulePlanner.Review(Conference, scoped, TimeZoneInfo.Utc, ["mobile"]).Issues,
            issue => issue.Code == ProposalIssueCode.MissingSpeakerSession);
    }

    [Fact]
    public void MixedAllSpeakerAndOptionalTopicsNeverForceOptionalConflicts()
    {
        var preferences = new ScheduleProposal([], ["Alex"], [])
        {
            AllSessionsByRequiredSpeakers = true,
            TopicFilter = new(["C#"], false, false, false)
        };
        var ids = SchedulePlanner.SelectSuggestions(Conference, preferences,
            Conference.Sessions.Select(session => session.Id), TimeZoneInfo.Utc, []);
        Assert.Contains("mobile", ids);
        Assert.Contains("adjacent", ids);
        Assert.DoesNotContain("overlap", ids);
        Assert.Contains("language", ids);
        Assert.True(Review(preferences with { SessionIds = ids }).CanApply);
        var allTopics = preferences with
        {
            RequiredSpeakers = ["Sam"], AllSessionsByRequiredSpeakers = false,
            TopicFilter = new(["Mobile"], false, false, true)
        };
        var topicIds = SchedulePlanner.SelectSuggestions(Conference, allTopics,
            Conference.Sessions.Select(session => session.Id), TimeZoneInfo.Utc, []);
        Assert.Contains("mobile", topicIds);
        Assert.Contains("adjacent", topicIds);
        Assert.Contains("language", topicIds);
        Assert.DoesNotContain("overlap", topicIds);
        Assert.True(Review(allTopics with { SessionIds = topicIds }).CanApply);
    }

    [Fact]
    public void InherentlyConflictingMandatorySessionsRemainExplicit()
    {
        var preferences = new ScheduleProposal([], ["Sam"], []) { AllSessionsByRequiredSpeakers = true };
        var ids = SchedulePlanner.SelectSuggestions(Conference, preferences,
            Conference.Sessions.Select(session => session.Id), TimeZoneInfo.Utc, []);
        Assert.Equal(["language", "midnight", "overlap"], ids.Order().ToArray());
        Assert.Contains(Review(preferences with { SessionIds = ids }).Issues,
            issue => issue.Code == ProposalIssueCode.OverlappingProposals);
    }

    [Fact]
    public void ArrangementHandlesHundredsOfAlternativesWithoutCappingTheSavedSelection()
    {
        var sessions = Enumerable.Range(0, 250).SelectMany(index =>
        {
            var start = DateTimeOffset.Parse("2026-11-10T00:00Z").AddHours(index);
            return new[]
            {
                Talk($"a-{index}", start.ToString("O"), start.AddMinutes(30).ToString("O"), "alex"),
                Talk($"s-{index}", start.ToString("O"), start.AddMinutes(30).ToString("O"), "sam")
            };
        }).ToList();
        var conference = Conference with { Sessions = sessions };
        var candidates = new ScheduleProposal(sessions.Select(session => session.Id).ToArray(),
            ["Alex", "Sam"], []);
        var arranged = SchedulePlanner.Arrange(conference, candidates, TimeZoneInfo.Utc, []);
        Assert.Equal(250, arranged.SessionIds.Length);
        Assert.True(SchedulePlanner.Review(conference, arranged, TimeZoneInfo.Utc, []).CanApply);
    }

    [Fact]
    public void RecommendationTargetIsSeparateFromValidationAndCanBeOverridden()
    {
        var start = DateTimeOffset.Parse("2026-11-10T00:00Z");
        var conference = Conference with
        {
            Sessions = Enumerable.Range(0, 20).Select(index => Talk($"synthetic-{index}",
                start.AddHours(index).ToString("O"), start.AddHours(index).AddMinutes(30).ToString("O"),
                "alex")).ToList()
        };
        var preferences = new ScheduleProposal([], [], []);
        var ids = conference.Sessions.Select(session => session.Id).ToArray();
        Assert.Equal(SchedulePlanner.DefaultSuggestionCount,
            SchedulePlanner.Recommend(conference, preferences, ids, TimeZoneInfo.Utc, []).Length);
        var selected = SchedulePlanner.Recommend(conference, preferences, ids, TimeZoneInfo.Utc, [], count: 20);
        Assert.Equal(20, selected.Length);
        Assert.True(SchedulePlanner.Review(conference, preferences with { SessionIds = selected },
            TimeZoneInfo.Utc, []).CanApply);
    }

    [Fact]
    public void ArrangementNeverRelaxesAnImpossibleRequiredSpeakerCombination()
    {
        var candidates = new ScheduleProposal(["mobile", "overlap"], ["Alex", "Sam"], []);
        Assert.Same(candidates, SchedulePlanner.Arrange(Conference, candidates, TimeZoneInfo.Utc, []));
    }

    [Fact]
    public async Task AllRequestsAboveTwelveSessionsCanBeSavedWithoutDroppingExistingLikes()
    {
        var start = DateTimeOffset.Parse("2026-11-10T00:00Z");
        var conference = Conference with
        {
            Sessions = Enumerable.Range(0, 50).Select(index => Talk($"synthetic-{index}",
                start.AddHours(index).ToString("O"), start.AddHours(index).AddMinutes(30).ToString("O"),
                "alex")).ToList()
        };
        var proposal = new ScheduleProposal(conference.Sessions.Select(session => session.Id).ToArray(),
            ["Alex"], [])
        {
            AllSessionsByRequiredSpeakers = true,
            TopicFilter = new(["Mobile"], false, false, true)
        };
        var schedule = new ScheduleService(new MemoryStore());
        var review = await schedule.AddProposalAsync(conference, proposal, TimeZoneInfo.Utc);
        Assert.True(review!.CanApply);
        Assert.Equal(51, schedule.SavedCount);
        Assert.Contains("previous-like", schedule.SavedIds);
        Assert.All(proposal.SessionIds, id => Assert.True(schedule.IsSaved(id)));
        Assert.Same(proposal, SchedulePlanner.Arrange(conference, proposal, TimeZoneInfo.Utc, []));
        Assert.Equal(50, SchedulePlanner.SelectSuggestions(conference, proposal, proposal.SessionIds,
            TimeZoneInfo.Utc, []).Length);
    }

    [Fact]
    public void IntervalRecommendationsMatchExhaustiveReferenceWithAndWithoutATarget()
    {
        var random = new Random(27);
        var start = DateTimeOffset.Parse("2026-11-10T00:00Z");
        for (var sample = 0; sample < 8; sample++)
        {
            var conference = Conference with
            {
                Sessions = Enumerable.Range(0, 9).Select(index =>
                {
                    var instant = start.AddMinutes(random.Next(18));
                    return Talk($"synthetic-{index}", instant.ToString("O"),
                        instant.AddMinutes(random.Next(1, 7)).ToString("O"), index % 2 == 0 ? "alex" : "sam");
                }).ToList()
            };
            foreach (var target in new[] { 2, 4, 9 })
            {
                var preferences = new ScheduleProposal([], ["Alex", "Sam"], []);
                var expected = Enumerable.Range(1, (1 << conference.Sessions.Count) - 1).Select(mask =>
                    conference.Sessions.Where((_, index) => (mask & (1 << index)) != 0)
                        .Select(session => session.Id).ToArray())
                    .Where(ids => ids.Length <= target && SchedulePlanner.Review(conference,
                        preferences with { SessionIds = ids }, TimeZoneInfo.Utc, []).CanApply)
                    .Select(ids => ids.Length).DefaultIfEmpty().Max();
                var selected = SchedulePlanner.Recommend(conference, preferences,
                    conference.Sessions.Select(session => session.Id), TimeZoneInfo.Utc, [], target);
                Assert.Equal(expected, selected.Length);
                if (expected > 0)
                    Assert.True(SchedulePlanner.Review(conference, preferences with { SessionIds = selected },
                        TimeZoneInfo.Utc, []).CanApply);
            }
        }
    }

    private sealed class MemoryStore : IScheduleStore
    {
        private string[] ids = ["previous-like"];
        public Task<IReadOnlyCollection<string>> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<string>>(ids);
        public Task WriteAsync(IReadOnlyCollection<string> sessionIds, CancellationToken cancellationToken = default)
        {
            ids = sessionIds.ToArray();
            return Task.CompletedTask;
        }
    }

    private static ProposalReview Review(ScheduleProposal proposal) =>
        SchedulePlanner.Review(Conference, proposal, TimeZoneInfo.Utc, []);

    private static AvailabilityWindow Window(string text)
    {
        var parts = text.Split('/');
        return new(parts[0], parts.Length == 2 ? parts[1] : "", "local");
    }
}
