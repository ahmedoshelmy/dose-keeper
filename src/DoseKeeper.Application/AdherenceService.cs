using DoseKeeper.Domain;

namespace DoseKeeper.Application;

/// <summary>Adherence per schedule and week; the weekly report fans out over schedules with <see cref="ForScheduleAsync"/>.</summary>
public sealed class AdherenceService(
    IMedicationRepository medications,
    IScheduleRepository schedules,
    IDoseEventRepository events,
    IAdherenceReportStore reports,
    TimeProvider clock)
{
    /// <summary>Weeks start on Saturday, as the work week does in Egypt.</summary>
    public static DateOnly WeekStartOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7));

    public DateOnly CurrentWeekStart() => WeekStartOf(DoseSchedule.LocalToday(clock.GetUtcNow()));

    public async Task<IReadOnlyList<string>> ScheduleIdsAsync(CancellationToken ct) =>
        [.. (await schedules.ListAsync(ct)).Select(s => s.Id)];

    public async Task<ScheduleAdherence?> ForScheduleAsync(string scheduleId, DateOnly weekStart, CancellationToken ct)
    {
        var schedule = await schedules.GetAsync(scheduleId, ct);
        if (schedule is null)
        {
            return null;
        }

        var medication = await medications.GetAsync(schedule.MedicationId, ct);
        var adherence = await CalculateAsync(schedule, weekStart, ct);
        return new ScheduleAdherence(scheduleId, medication?.Name ?? schedule.MedicationId, adherence);
    }

    public async Task<WeeklyAdherenceReport> SaveReportAsync(DateOnly weekStart, IReadOnlyList<ScheduleAdherence> parts, CancellationToken ct)
    {
        var overall = Adherence.Combine(parts.Select(p => p.Adherence));
        var report = new WeeklyAdherenceReport(weekStart, parts, overall.Percent, clock.GetUtcNow());
        await reports.SaveAsync(report, ct);
        return report;
    }

    public Task<WeeklyAdherenceReport?> LatestReportAsync(CancellationToken ct) => reports.LatestAsync(ct);

    /// <summary>Overall adherence for the last <paramref name="weeks"/> weeks, oldest first, for the chart.</summary>
    public async Task<IReadOnlyList<WeekPoint>> HistoryAsync(int weeks, CancellationToken ct)
    {
        var all = await schedules.ListAsync(ct);
        var current = CurrentWeekStart();
        var points = new List<WeekPoint>();
        for (var i = Math.Clamp(weeks, 1, 26) - 1; i >= 0; i--)
        {
            var weekStart = current.AddDays(-7 * i);
            var parts = new List<Adherence>();
            foreach (var schedule in all)
            {
                parts.Add(await CalculateAsync(schedule, weekStart, ct));
            }

            var total = Adherence.Combine(parts);
            points.Add(new WeekPoint(weekStart, total.Percent, total.Due, total.Taken));
        }

        return points;
    }

    private async Task<Adherence> CalculateAsync(DoseSchedule schedule, DateOnly weekStart, CancellationToken ct)
    {
        var from = schedule.ToUtc(weekStart, TimeOnly.MinValue);
        var to = schedule.ToUtc(weekStart.AddDays(7), TimeOnly.MinValue);
        var recorded = await events.ListAsync(schedule.Id, from, to, ct);
        return Adherence.Calculate(schedule, recorded, from, to, clock.GetUtcNow());
    }
}
