using System.Globalization;
using DotNetConf.App.Services;
using DotNetConf.App.ViewModels;

namespace DotNetConf.Core.Tests;

public sealed class PlannerPresentationTests
{
    [Theory]
    [InlineData("2026-11-10T16:30:00Z", 30, "18:30", "30 min")]
    [InlineData("2026-11-10T21:30:00Z", 90, "23:30", "1 h 30 min")]
    [InlineData("2026-11-10T22:30:00Z", 60, "00:30", "1 h")]
    public void ShortlistShowsLocalStartAndElapsedDurationWithoutRepeatingTheEnd(
        string startsAt, int minutes, string clock, string duration)
    {
        var start = DateTimeOffset.Parse(startsAt, CultureInfo.InvariantCulture);
        var zone = TimeZoneInfo.CreateCustomTimeZone("Synthetic test zone", TimeSpan.FromHours(2), "", "");
        var session = new Session
        {
            Id = "synthetic-presentation-test",
            Title = "Synthetic presentation test",
            StartsAt = start,
            EndsAt = start.AddMinutes(minutes)
        };
        var row = new ProposedSessionViewModel(session, "Synthetic speaker", zone, false, "", () => { });
        var local = TimeZoneInfo.ConvertTime(start, zone);

        Assert.Equal($"{local:ddd}, {Agenda.MonthDay(DateOnly.FromDateTime(local.DateTime))} \u00b7 {clock} \u00b7 {duration}", row.Time);
        Assert.Contains($"Ends {TimeZoneInfo.ConvertTime(session.EndsAt, zone):ddd, MMM d HH:mm}.", row.SelectionDescription);
        Assert.Contains(row.Time, row.SelectionDescription);
    }

    [Theory]
    [InlineData("none", "23:00", "local", "Assumed daily start: 07:00.")]
    [InlineData("18:00", "none", "local", "Assumed daily finish: 23:00.")]
    [InlineData("23:30", "none", "local", "Assumed daily finish: midnight.")]
    [InlineData("09:00", "17:00", "local", "")]
    [InlineData("none", "23:00", "UTC", "Assumed daily start: 07:00 (UTC).")]
    [InlineData("18:00", "none", "America/New_York", "Assumed daily finish: 23:00 (America/New_York).")]
    public void AssumedBoundaryIsShortAndExplicit(string start, string end, string zone, string expected)
    {
        var availability = new PlannerDailyAvailability(start, end, zone).Complete(PlannerWatchingHours.Default);

        Assert.Equal(expected, availability.Assumption);
    }
}
