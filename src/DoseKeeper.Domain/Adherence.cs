namespace DoseKeeper.Domain;

/// <summary>Share of due doses that were taken in a period. Doses still inside their late window are left out.</summary>
public sealed record Adherence(int Due, int Taken, int Late, int Skipped, int Missed)
{
    public decimal Percent => Due == 0 ? 100m : Math.Round(100m * Taken / Due, 1);

    public static Adherence Calculate(
        DoseSchedule schedule,
        IEnumerable<DoseEvent> events,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        DateTimeOffset nowUtc)
    {
        var byDue = events.Where(e => e.ScheduleId == schedule.Id).ToDictionary(e => e.DueUtc);
        int due = 0, taken = 0, late = 0, skipped = 0, missed = 0;
        foreach (var occurrence in schedule.OccurrencesBetween(fromUtc, toUtc))
        {
            byDue.TryGetValue(occurrence, out var recorded);
            var settled = recorded is { Status: not DoseStatus.Pending } || occurrence + schedule.LateWindow <= nowUtc;
            if (!settled)
            {
                continue;
            }

            due++;
            switch (recorded?.Status)
            {
                case DoseStatus.Taken:
                    taken++;
                    late += recorded.IsLate(schedule.LateWindow) ? 1 : 0;
                    break;
                case DoseStatus.Skipped:
                    skipped++;
                    break;
                default:
                    missed++;
                    break;
            }
        }

        return new Adherence(due, taken, late, skipped, missed);
    }

    public static Adherence Combine(IEnumerable<Adherence> parts) => parts.Aggregate(
        new Adherence(0, 0, 0, 0, 0),
        (sum, a) => new Adherence(sum.Due + a.Due, sum.Taken + a.Taken, sum.Late + a.Late, sum.Skipped + a.Skipped, sum.Missed + a.Missed));
}
