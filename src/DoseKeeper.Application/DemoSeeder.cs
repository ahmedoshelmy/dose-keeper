using System.Globalization;
using DoseKeeper.Domain;

namespace DoseKeeper.Application;

/// <summary>
/// Synthetic demo data: three generic medications with six weeks of history so the chart and refill warning have
/// something to show. Deterministic (no randomness), and only enabled by configuration.
/// </summary>
public sealed class DemoSeeder(
    IMedicationRepository medications,
    IScheduleRepository schedules,
    IDoseEventRepository events,
    TimeProvider clock)
{
    private static readonly (string Name, string Strength, int Pills, string[] Times, int SkipEvery, int MissEvery)[] Plan =
    [
        ("Vitamin D", "1000 IU", 40, ["09:00"], 0, 9),
        ("Metformin", "500 mg", 9, ["08:00", "20:00"], 11, 5),
        ("Atorvastatin", "20 mg", 60, ["22:00"], 0, 4),
    ];

    public async Task<IReadOnlyList<DoseSchedule>> SeedAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var created = new List<DoseSchedule>();
        var index = 0;
        foreach (var (name, strength, pills, times, skipEvery, missEvery) in Plan)
        {
            index++;
            var medication = Medication.Create($"med-demo{index}", name, strength, pills, 7);
            var zone = DoseSchedule.FindZone(null);
            var start = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime).AddDays(-42);
            var schedule = DoseSchedule.Create(
                $"sch-demo{index}", medication.Id, times.Select(t => TimeOnly.Parse(t, CultureInfo.InvariantCulture)), null, start, null, null, 1, TimeSpan.FromMinutes(30), "caregiver@example.com");
            await medications.SaveAsync(medication, ct);
            await schedules.SaveAsync(schedule, ct);
            await SeedHistoryAsync(schedule, now, skipEvery, missEvery, ct);
            created.Add(schedule);
        }

        return created;
    }

    private async Task SeedHistoryAsync(DoseSchedule schedule, DateTimeOffset now, int skipEvery, int missEvery, CancellationToken ct)
    {
        var from = schedule.ToUtc(schedule.StartDate, TimeOnly.MinValue);
        var n = 0;
        foreach (var due in schedule.OccurrencesBetween(from, now - schedule.LateWindow + TimeSpan.FromTicks(1)))
        {
            n++;
            // Later weeks get better: misses thin out as n grows, which makes the chart trend upwards.
            var effectiveMiss = missEvery + (n / 20);
            if (skipEvery > 0 && n % skipEvery == 0)
            {
                await events.SaveAsync(new DoseEvent(schedule.Id, due, DoseStatus.Skipped, due.AddMinutes(5)), ct);
            }
            else if (n % effectiveMiss == 0)
            {
                await events.SaveAsync(new DoseEvent(schedule.Id, due, DoseStatus.Missed, due + schedule.LateWindow), ct);
            }
            else
            {
                var delay = n % 7 == 0 ? 50 : n % 3;
                await events.SaveAsync(new DoseEvent(schedule.Id, due, DoseStatus.Taken, due.AddMinutes(delay)), ct);
            }
        }
    }
}
