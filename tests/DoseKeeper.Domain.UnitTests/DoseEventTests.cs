using static DoseKeeper.Domain.UnitTests.Samples;

namespace DoseKeeper.Domain.UnitTests;

public sealed class DoseEventTests
{
    private static readonly DateTimeOffset Due = Utc(2026, 1, 10, 6);

    [Fact]
    public void Taken_inside_the_late_window_is_on_time()
    {
        new DoseEvent("sch-a", Due, DoseStatus.Taken, Due.AddMinutes(30)).IsLate(TimeSpan.FromMinutes(30)).ShouldBeFalse();
    }

    [Fact]
    public void Taken_after_the_late_window_is_late()
    {
        new DoseEvent("sch-a", Due, DoseStatus.Taken, Due.AddMinutes(31)).IsLate(TimeSpan.FromMinutes(30)).ShouldBeTrue();
    }

    [Fact]
    public void Skipped_is_never_late()
    {
        new DoseEvent("sch-a", Due, DoseStatus.Skipped, Due.AddHours(5)).IsLate(TimeSpan.FromMinutes(30)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, DoseStatus.Missed, true)]
    [InlineData(DoseStatus.Missed, DoseStatus.Taken, true)]
    [InlineData(DoseStatus.Missed, DoseStatus.Skipped, true)]
    [InlineData(DoseStatus.Taken, DoseStatus.Missed, false)]
    [InlineData(DoseStatus.Skipped, DoseStatus.Missed, false)]
    [InlineData(DoseStatus.Taken, DoseStatus.Skipped, true)]
    [InlineData(DoseStatus.Missed, DoseStatus.Missed, false)]
    public void The_automatic_missed_outcome_never_overrides_the_users_answer(DoseStatus? existing, DoseStatus incoming, bool allowed)
    {
        var stored = existing is null ? null : new DoseEvent("sch-a", Due, existing.Value, Due);

        DoseEvent.CanReplace(stored, incoming).ShouldBe(allowed);
    }
}
