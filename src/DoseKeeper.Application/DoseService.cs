using System.Globalization;
using DoseKeeper.Domain;

namespace DoseKeeper.Application;

/// <summary>Today's doses and the user's answers (taken / skipped) to them.</summary>
public sealed class DoseService(
    IMedicationRepository medications,
    IScheduleRepository schedules,
    IDoseEventRepository events,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<DoseView>> TodayAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var meds = (await medications.ListAsync(ct)).ToDictionary(m => m.Id);
        var result = new List<DoseView>();
        foreach (var schedule in await schedules.ListAsync(ct))
        {
            var today = schedule.LocalDate(now);
            var from = schedule.ToUtc(today, TimeOnly.MinValue);
            var to = schedule.ToUtc(today.AddDays(1), TimeOnly.MinValue);
            var recorded = (await events.ListAsync(schedule.Id, from, to, ct)).ToDictionary(e => e.DueUtc);
            var name = meds.TryGetValue(schedule.MedicationId, out var med) ? $"{med.Name} {med.Strength}".Trim() : schedule.MedicationId;
            foreach (var due in schedule.OccurrencesBetween(from, to))
            {
                recorded.TryGetValue(due, out var dose);
                var local = TimeZoneInfo.ConvertTime(due, schedule.TimeZone);
                result.Add(new DoseView(
                    schedule.Id,
                    schedule.MedicationId,
                    name,
                    due,
                    local.ToString("HH:mm", CultureInfo.InvariantCulture),
                    schedule.DosePerIntake,
                    dose?.Status ?? DoseStatus.Pending,
                    dose?.IsLate(schedule.LateWindow) ?? false));
            }
        }

        return [.. result.OrderBy(d => d.DueUtc)];
    }

    /// <summary>
    /// Records the user's answer and draws pills from stock once. Returns the stored outcome; the caller forwards it to the
    /// reminder loop so it stops nagging.
    /// </summary>
    public async Task<DoseEvent> AnswerAsync(string scheduleId, DateTimeOffset dueUtc, DoseStatus status, CancellationToken ct)
    {
        if (status is not (DoseStatus.Taken or DoseStatus.Skipped))
        {
            throw new DomainException("A dose can only be marked taken or skipped.");
        }

        var schedule = await schedules.GetAsync(scheduleId, ct) ?? throw new NotFoundException($"Schedule {scheduleId} not found.");
        dueUtc = dueUtc.ToUniversalTime();
        if (!schedule.OccurrencesBetween(dueUtc, dueUtc.AddTicks(1)).Any())
        {
            throw new DomainException("That time is not a scheduled dose.");
        }

        var existing = await events.GetAsync(scheduleId, dueUtc, ct);
        if (!DoseEvent.CanReplace(existing, status))
        {
            return existing!;
        }

        var doseEvent = new DoseEvent(scheduleId, dueUtc, status, clock.GetUtcNow());
        await events.SaveAsync(doseEvent, ct);
        if (status == DoseStatus.Taken && existing?.Status != DoseStatus.Taken)
        {
            var medication = await medications.GetAsync(schedule.MedicationId, ct);
            if (medication is not null)
            {
                medication.Consume(schedule.DosePerIntake);
                await medications.SaveAsync(medication, ct);
            }
        }

        return doseEvent;
    }
}
