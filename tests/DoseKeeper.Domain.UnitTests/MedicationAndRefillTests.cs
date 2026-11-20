using static DoseKeeper.Domain.UnitTests.Samples;

namespace DoseKeeper.Domain.UnitTests;

public sealed class MedicationAndRefillTests
{
    private static readonly DateOnly Today = new(2026, 1, 10);

    [Fact]
    public void Consuming_never_drops_stock_below_zero()
    {
        var med = Medication.Create("med-a", "Metformin", "500 mg", 1, 5);

        med.Consume(2);

        med.PillsOnHand.ShouldBe(0);
    }

    [Fact]
    public void Refill_adds_to_stock()
    {
        var med = Medication.Create("med-a", "Metformin", "500 mg", 3, 5);

        med.Refill(30);

        med.PillsOnHand.ShouldBe(33);
    }

    [Theory]
    [InlineData("", 1, 5)]
    [InlineData("Metformin", -1, 5)]
    [InlineData("Metformin", 1, 91)]
    public void Invalid_medications_are_rejected(string name, int pills, int threshold)
    {
        Should.Throw<DomainException>(() => Medication.Create("med-a", name, "500 mg", pills, threshold));
    }

    [Fact]
    public void Forecast_divides_stock_by_daily_use_and_flags_low_stock()
    {
        var med = Medication.Create("med-a", "Metformin", "500 mg", 20, 7);

        var forecast = RefillForecast.For(med, [Schedule("08:00,20:00", pills: 1)], Today);

        forecast.DailyUse.ShouldBe(2m);
        forecast.DaysLeft.ShouldBe(10);
        forecast.RunOutDate.ShouldBe(new DateOnly(2026, 1, 20));
        forecast.NeedsRefill.ShouldBeFalse();
    }

    [Fact]
    public void Forecast_needs_refill_at_or_below_the_threshold()
    {
        var med = Medication.Create("med-a", "Metformin", "500 mg", 14, 7);

        RefillForecast.For(med, [Schedule("08:00,20:00")], Today).NeedsRefill.ShouldBeTrue();
    }

    [Fact]
    public void Forecast_sums_several_schedules_and_ignores_ended_ones()
    {
        var med = Medication.Create("med-a", "Metformin", "500 mg", 30, 3);
        var schedules = new[]
        {
            Schedule("08:00", id: "sch-1"),
            Schedule("20:00", id: "sch-2", pills: 2),
            Schedule("12:00", id: "sch-3", end: new DateOnly(2026, 1, 1)),
        };

        RefillForecast.For(med, schedules, Today).DaysLeft.ShouldBe(10);
    }

    [Fact]
    public void Medication_without_a_schedule_has_no_forecast()
    {
        var med = Medication.Create("med-a", "Vitamin D", "1000 IU", 5, 7);

        var forecast = RefillForecast.For(med, [], Today);

        forecast.DaysLeft.ShouldBeNull();
        forecast.NeedsRefill.ShouldBeFalse();
    }
}
