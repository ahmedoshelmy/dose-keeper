using System.Globalization;
using static DoseKeeper.Domain.UnitTests.Samples;

namespace DoseKeeper.Domain.UnitTests;

public sealed class DoseScheduleTests
{
    [Fact]
    public void Winter_doses_in_Cairo_are_two_hours_ahead_of_utc()
    {
        var schedule = Schedule("08:00,20:00");

        var due = schedule.OccurrencesBetween(Utc(2026, 1, 10, 0), Utc(2026, 1, 11, 0)).ToList();

        due.ShouldBe([Utc(2026, 1, 10, 6), Utc(2026, 1, 10, 18)]);
    }

    [Fact]
    public void Summer_doses_follow_egypt_daylight_saving_time()
    {
        // Egypt brought back DST in 2023 (UTC+3 from late April to late October): 08:00 local is 05:00 UTC in July.
        var schedule = Schedule("08:00");

        schedule.OccurrencesBetween(Utc(2026, 7, 1, 0), Utc(2026, 7, 2, 0)).ShouldBe([Utc(2026, 7, 1, 5)]);
    }

    [Fact]
    public void A_time_skipped_by_the_spring_forward_gap_moves_forward_instead_of_disappearing()
    {
        // Clocks jump 00:00 -> 01:00 local on the last Friday of April 2026 (24 April).
        var schedule = Schedule("00:30");

        var due = schedule.ToUtc(new DateOnly(2026, 4, 24), new TimeOnly(0, 30));

        due.ShouldBe(Utc(2026, 4, 23, 22, 30));
    }

    [Fact]
    public void An_ambiguous_time_on_the_fall_back_night_is_reminded_once_at_its_first_occurrence()
    {
        // Clocks fall back 24:00 -> 23:00 local on the last Thursday of October 2026 (29 October).
        var schedule = Schedule("23:30");

        var due = schedule.OccurrencesBetween(Utc(2026, 10, 29, 12), Utc(2026, 10, 30, 12)).ToList();

        due.ShouldBe([Utc(2026, 10, 29, 20, 30)]);
    }

    [Fact]
    public void Only_chosen_weekdays_between_start_and_end_produce_doses()
    {
        var schedule = Schedule("09:00", days: [DayOfWeek.Monday, DayOfWeek.Thursday], start: new DateOnly(2026, 3, 3), end: new DateOnly(2026, 3, 12));

        var days = schedule.OccurrencesBetween(Utc(2026, 3, 1, 0), Utc(2026, 3, 20, 0)).Select(d => d.Day).ToList();

        // 2026-03-02 is a Monday but before the start; 2026-03-16 is after the end.
        days.ShouldBe([5, 9, 12]);
    }

    [Fact]
    public void Next_occurrence_is_strictly_after_the_given_instant()
    {
        var schedule = Schedule("08:00,20:00");

        schedule.NextOccurrenceAfter(Utc(2026, 1, 10, 6)).ShouldBe(Utc(2026, 1, 10, 18));
        schedule.NextOccurrenceAfter(Utc(2026, 1, 10, 18)).ShouldBe(Utc(2026, 1, 11, 6));
    }

    [Fact]
    public void Next_occurrence_is_null_once_the_schedule_has_ended()
    {
        var schedule = Schedule("08:00", end: new DateOnly(2026, 1, 10));

        schedule.NextOccurrenceAfter(Utc(2026, 1, 10, 7)).ShouldBeNull();
    }

    [Fact]
    public void Next_occurrence_skips_to_the_next_active_weekday()
    {
        var schedule = Schedule("08:00", days: [DayOfWeek.Friday]);

        // Saturday 2026-01-10 -> next Friday 2026-01-16.
        schedule.NextOccurrenceAfter(Utc(2026, 1, 10, 12)).ShouldBe(Utc(2026, 1, 16, 6));
    }

    [Fact]
    public void Daily_use_averages_weekday_schedules_over_the_week()
    {
        Schedule("08:00,20:00", pills: 2).DailyUse.ShouldBe(4m);
        Schedule("08:00", days: [DayOfWeek.Saturday], pills: 7).DailyUse.ShouldBe(1m);
    }

    [Fact]
    public void Local_today_rolls_over_at_cairo_midnight_not_utc_midnight()
    {
        // 22:30 UTC on 9 January is 00:30 on 10 January in Cairo.
        DoseSchedule.LocalToday(Utc(2026, 1, 9, 22, 30)).ShouldBe(new DateOnly(2026, 1, 10));
    }

    [Theory]
    [InlineData("", "times")]
    [InlineData("08:00", "zone")]
    [InlineData("08:00", "late")]
    [InlineData("08:00", "pills")]
    [InlineData("08:00", "end")]
    public void Invalid_schedules_are_rejected(string times, string broken)
    {
        Should.Throw<DomainException>(() => DoseSchedule.Create(
            "sch-x",
            "med-a",
            times.Length == 0 ? [] : [TimeOnly.Parse(times, CultureInfo.InvariantCulture)],
            null,
            new DateOnly(2026, 1, 10),
            broken == "end" ? new DateOnly(2026, 1, 1) : null,
            broken == "zone" ? "Mars/Olympus_Mons" : "Africa/Cairo",
            broken == "pills" ? 0 : 1,
            TimeSpan.FromMinutes(broken == "late" ? 0 : 30),
            null));
    }
}
