using static DoseKeeper.Domain.UnitTests.Samples;

namespace DoseKeeper.Domain.UnitTests;

public sealed class AdherenceTests
{
    private static readonly DateTimeOffset From = Utc(2026, 1, 10, 0);
    private static readonly DateTimeOffset To = Utc(2026, 1, 12, 0);

    [Fact]
    public void Counts_taken_skipped_and_unanswered_doses()
    {
        var schedule = Schedule("08:00,20:00");
        DoseEvent[] events =
        [
            new("sch-a", Utc(2026, 1, 10, 6), DoseStatus.Taken, Utc(2026, 1, 10, 6, 5)),
            new("sch-a", Utc(2026, 1, 10, 18), DoseStatus.Taken, Utc(2026, 1, 10, 19)),
            new("sch-a", Utc(2026, 1, 11, 6), DoseStatus.Skipped, Utc(2026, 1, 11, 6)),
        ];

        var result = Adherence.Calculate(schedule, events, From, To, Utc(2026, 1, 12, 0));

        result.ShouldBe(new Adherence(Due: 4, Taken: 2, Late: 1, Skipped: 1, Missed: 1));
        result.Percent.ShouldBe(50m);
    }

    [Fact]
    public void Doses_still_inside_their_late_window_do_not_count_yet()
    {
        var schedule = Schedule("08:00,20:00", lateMinutes: 60);

        var result = Adherence.Calculate(schedule, [], From, To, Utc(2026, 1, 11, 6, 30));

        // Due so far: 10th 06:00, 10th 18:00 (settled), 11th 06:00 still in its window.
        result.Due.ShouldBe(2);
        result.Missed.ShouldBe(2);
    }

    [Fact]
    public void An_answered_dose_counts_even_inside_its_window()
    {
        var schedule = Schedule("08:00", lateMinutes: 60);
        DoseEvent[] events = [new("sch-a", Utc(2026, 1, 11, 6), DoseStatus.Taken, Utc(2026, 1, 11, 6))];

        var result = Adherence.Calculate(schedule, events, Utc(2026, 1, 11, 0), To, Utc(2026, 1, 11, 6, 1));

        result.ShouldBe(new Adherence(1, 1, 0, 0, 0));
    }

    [Fact]
    public void No_due_doses_means_full_adherence()
    {
        Adherence.Calculate(Schedule("08:00"), [], From, From, To).Percent.ShouldBe(100m);
    }

    [Fact]
    public void Combine_adds_the_parts()
    {
        var total = Adherence.Combine([new Adherence(4, 3, 1, 0, 1), new Adherence(2, 2, 0, 0, 0)]);

        total.ShouldBe(new Adherence(6, 5, 1, 0, 1));
        total.Percent.ShouldBe(83.3m);
    }
}
