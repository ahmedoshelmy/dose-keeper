using System.Globalization;

namespace DoseKeeper.Domain.UnitTests;

internal static class Samples
{
    public static DoseSchedule Schedule(
        string times = "08:00,20:00",
        DayOfWeek[]? days = null,
        DateOnly? start = null,
        DateOnly? end = null,
        int pills = 1,
        int lateMinutes = 30,
        string id = "sch-a") =>
        DoseSchedule.Create(
            id,
            "med-a",
            times.Split(',').Select(t => TimeOnly.Parse(t, CultureInfo.InvariantCulture)),
            days,
            start ?? new DateOnly(2026, 1, 1),
            end,
            "Africa/Cairo",
            pills,
            TimeSpan.FromMinutes(lateMinutes),
            "caregiver@example.com");

    public static DateTimeOffset Utc(int year, int month, int day, int hour, int minute = 0) => new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}
