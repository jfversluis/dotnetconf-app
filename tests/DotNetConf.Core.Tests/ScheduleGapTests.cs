namespace DotNetConf.Core.Tests;

public sealed class ScheduleGapTests
{
    private static Session Talk(string id, int start, int end, int day = 10) => new()
    {
        Id = id, Title = id, StartsAt = new(2026, 11, day, start, 0, 0, TimeSpan.Zero),
        EndsAt = new(2026, 11, day, end, 0, 0, TimeSpan.Zero), SpeakerIds = ["speaker"], Topics = ["AI"]
    };

    private static ConferenceDocument Agenda(params Session[] sessions) => new()
    {
        Sessions = sessions.ToList(), Speakers = [new() { Id = "speaker", Name = "Real Speaker" }]
    };

    [Fact]
    public void GapsUseActualAgendaBoundsAndOnlySavedDays()
    {
        var agenda = Agenda(Talk("before", 7, 8), Talk("anchor", 9, 10), Talk("after", 11, 13),
            Talk("another-day", 7, 9, 11));
        var gaps = ScheduleGaps.Find(agenda, ["anchor"], TimeZoneInfo.Utc);
        Assert.Equal(2, gaps.Count);
        Assert.Equal(agenda.Sessions[0].StartsAt, gaps[0].StartsAt);
        Assert.Equal(agenda.Sessions[1].StartsAt, gaps[0].EndsAt);
        Assert.Equal(agenda.Sessions[1].EndsAt, gaps[1].StartsAt);
        Assert.Equal(agenda.Sessions[2].EndsAt, gaps[1].EndsAt);
        Assert.False(ScheduleGaps.Fits(agenda.Sessions[3], gaps));
    }

    [Theory]
    [InlineData(8, 10)]
    [InlineData(9, 11)]
    [InlineData(10, 11)]
    public void MergesOverlappingAndAdjacentSavedIntervals(int start, int end)
    {
        var agenda = Agenda(Talk("bounds", 7, 14), Talk("a", 8, 10), Talk("b", start, end));
        var gaps = ScheduleGaps.Find(agenda, ["a", "b"], TimeZoneInfo.Utc);
        Assert.Equal(2, gaps.Count);
        Assert.Equal(8, gaps[0].EndsAt.Hour);
        Assert.Equal(Math.Max(10, end), gaps[1].StartsAt.Hour);
    }

    [Fact]
    public void WholeDaySavedHasNoGapsAndMissingOrEmptyScheduleIsExplicit()
    {
        var agenda = Agenda(Talk("whole-day", 7, 14));
        Assert.Empty(ScheduleGaps.Find(agenda, ["whole-day"], TimeZoneInfo.Utc));
        Assert.Throws<InvalidDataException>(() => ScheduleGaps.Find(agenda, [], TimeZoneInfo.Utc));
        Assert.Throws<InvalidDataException>(() => ScheduleGaps.Find(agenda, ["withdrawn"], TimeZoneInfo.Utc));
    }

    [Fact]
    public void GapReviewRejectsCrossingBoundariesAlreadySavedAndChangedSchedule()
    {
        var agenda = Agenda(Talk("early", 7, 8), Talk("saved", 9, 10), Talk("crossing", 8, 11), Talk("fits", 10, 11));
        ScheduleProposal Proposal(string id) => new([id], [], []) { GapAnchorIds = ["saved"] };
        Assert.True(SchedulePlanner.Review(agenda, Proposal("fits"), TimeZoneInfo.Utc, ["saved"]).CanApply);
        foreach (var id in new[] { "saved", "crossing" })
            Assert.Contains(SchedulePlanner.Review(agenda, Proposal(id), TimeZoneInfo.Utc, ["saved"]).Issues,
                issue => issue.Code == ProposalIssueCode.OutsideScheduleGap);
        Assert.Contains(SchedulePlanner.Review(agenda, Proposal("fits"), TimeZoneInfo.Utc, ["saved", "early"]).Issues,
            issue => issue.Code == ProposalIssueCode.InvalidGapSchedule);
    }

    [Fact]
    public void RetainedWithdrawnBookmarksRemainFixedButAreNeverSuggested()
    {
        var agenda = Agenda(Talk("before", 7, 8), Talk("crossing", 8, 11), Talk("after", 11, 12)) with
        {
            WithdrawnSessions = [Talk("withdrawn", 9, 10)]
        };
        var gaps = ScheduleGaps.Find(agenda, ["withdrawn"], TimeZoneInfo.Utc);
        Assert.True(ScheduleGaps.Fits(agenda.Sessions[0], gaps));
        Assert.True(ScheduleGaps.Fits(agenda.Sessions[2], gaps));
        Assert.False(ScheduleGaps.Fits(agenda.Sessions[1], gaps));
        var proposal = new ScheduleProposal(["after"], [], []) { GapAnchorIds = ["withdrawn"] };
        Assert.True(SchedulePlanner.Review(agenda, proposal, TimeZoneInfo.Utc, ["withdrawn"]).CanApply);
        Assert.False(SchedulePlanner.Review(agenda, proposal with { SessionIds = ["withdrawn"] },
            TimeZoneInfo.Utc, ["withdrawn"]).CanApply);
        Assert.Contains(SchedulePlanner.Review(agenda, new(["crossing"], [], []), TimeZoneInfo.Utc, ["withdrawn"]).Issues,
            issue => issue.Code == ProposalIssueCode.SavedConflict);
    }

    [Theory]
    [InlineData(6, 7)]
    [InlineData(14, 15)]
    public void WithdrawnBookmarksOutsideUpdatedDayBoundsDoNotHideThatDayOrExtendItsAgenda(int start, int end)
    {
        var active = Talk("active", 8, 12);
        var agenda = Agenda(active) with { WithdrawnSessions = [Talk("withdrawn", start, end)] };
        var gap = Assert.Single(ScheduleGaps.Find(agenda, ["withdrawn"], TimeZoneInfo.Utc));
        Assert.Equal(active.StartsAt, gap.StartsAt);
        Assert.Equal(active.EndsAt, gap.EndsAt);
    }

    [Fact]
    public void GapRecommendationsRetainTopicSpeakerAndAvailabilityConstraints()
    {
        var agenda = Agenda(Talk("saved", 9, 10), Talk("fits", 11, 12), Talk("late", 13, 14));
        var preferences = new ScheduleProposal([], ["Real Speaker"],
            [new("2026-11-10T10:00", "2026-11-10T12:00", "UTC")])
        {
            GapAnchorIds = ["saved"], TopicFilter = new(["AI"], false, true, false)
        };
        var ids = SchedulePlanner.Recommend(agenda, preferences, ["saved", "fits", "late"], TimeZoneInfo.Utc, ["saved"]);
        Assert.Equal(["fits"], ids);
        Assert.True(SchedulePlanner.Review(agenda, preferences with { SessionIds = ids }, TimeZoneInfo.Utc, ["saved"]).CanApply);
        Assert.False(SchedulePlanner.Review(agenda, preferences with { SessionIds = [] }, TimeZoneInfo.Utc, ["saved"]).CanApply);
    }

    [Fact]
    public void InstantBasedGapsRespectLocalDaysAcrossDst()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var saved = Talk("saved", 6, 7, 1);
        var after = Talk("after", 7, 8, 1);
        var before = Talk("before", 5, 6, 1);
        var agenda = Agenda(before, saved, after);
        var gaps = ScheduleGaps.Find(agenda, ["saved"], zone);
        Assert.True(ScheduleGaps.Fits(before, gaps));
        Assert.True(ScheduleGaps.Fits(after, gaps));
        Assert.False(ScheduleGaps.Fits(saved, gaps));
    }

    [Fact]
    public async Task BulkAddsPreserveExternalLikesAndValidatedSaveRejectsStaleGapAnchors()
    {
        var store = new MemoryStore { Ids = ["saved"] };
        var service = new ScheduleService(store);
        await service.InitializeAsync();
        store.Ids = ["saved", "external"];
        await service.AddAsync(["fits"]);
        Assert.Equal(["external", "fits", "saved"], store.Ids.Order().ToArray());
        store.Ids = ["saved", "early"];
        var agenda = Agenda(Talk("early", 7, 8), Talk("saved", 9, 10), Talk("fits", 11, 12));
        var review = await service.AddProposalAsync(agenda,
            new(["fits"], [], []) { GapAnchorIds = ["saved"] }, TimeZoneInfo.Utc);
        Assert.False(review!.CanApply);
        Assert.Equal(["saved", "early"], store.Ids);
    }

    private sealed class MemoryStore : IScheduleStore
    {
        public string[] Ids { get; set; } = [];
        public Task<IReadOnlyCollection<string>> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<string>>(Ids);
        public Task WriteAsync(IReadOnlyCollection<string> sessionIds, CancellationToken cancellationToken = default)
        {
            Ids = sessionIds.ToArray();
            return Task.CompletedTask;
        }
    }
}
