using System.Globalization;
using DoseKeeper.Domain;

namespace DoseKeeper.Application;

/// <summary>The steps the reminder loop calls as activities: find the next dose, remind, escalate, give up.</summary>
public sealed class ReminderService(
    IMedicationRepository medications,
    IScheduleRepository schedules,
    IDoseEventRepository events,
    INotificationOutbox queue,
    TimeProvider clock)
{
    public const string UserRecipient = "user";

    public async Task<NextDose?> NextDoseAsync(string scheduleId, DateTimeOffset afterUtc, CancellationToken ct)
    {
        var schedule = await schedules.GetAsync(scheduleId, ct);
        var due = schedule?.NextOccurrenceAfter(afterUtc);
        return due is null || schedule is null
            ? null
            : new NextDose(scheduleId, due.Value, (int)schedule.EscalateAfter.TotalSeconds, (int)schedule.LateWindow.TotalSeconds);
    }

    /// <summary>Sends a reminder unless the dose was already answered. Returns false when there is nothing to remind.</summary>
    public async Task<bool> RemindAsync(string scheduleId, DateTimeOffset dueUtc, bool escalation, CancellationToken ct)
    {
        var (schedule, name) = await LoadAsync(scheduleId, ct);
        if (schedule is null || await IsAnsweredAsync(scheduleId, dueUtc, ct))
        {
            return false;
        }

        var at = LocalTime(schedule, dueUtc);
        var text = escalation
            ? $"Second reminder: {name} ({schedule.DosePerIntake} pill(s)) was due at {at}."
            : $"Time for {name}: {schedule.DosePerIntake} pill(s) at {at}.";
        await queue.EnqueueAsync(Message(escalation ? NotificationKind.Escalation : NotificationKind.Reminder, UserRecipient, text, scheduleId, dueUtc), ct);
        return true;
    }

    /// <summary>Marks an unanswered dose missed and alerts the caregiver, if any. Never overrides the user's own answer.</summary>
    public async Task<bool> MarkMissedAsync(string scheduleId, DateTimeOffset dueUtc, CancellationToken ct)
    {
        var (schedule, name) = await LoadAsync(scheduleId, ct);
        var existing = await events.GetAsync(scheduleId, dueUtc, ct);
        if (schedule is null || !DoseEvent.CanReplace(existing, DoseStatus.Missed))
        {
            return false;
        }

        await events.SaveAsync(new DoseEvent(scheduleId, dueUtc, DoseStatus.Missed, clock.GetUtcNow()), ct);
        var at = LocalTime(schedule, dueUtc);
        await queue.EnqueueAsync(Message(NotificationKind.Missed, UserRecipient, $"Missed: {name} due at {at}.", scheduleId, dueUtc), ct);
        if (!string.IsNullOrWhiteSpace(schedule.CaregiverContact))
        {
            await queue.EnqueueAsync(
                Message(NotificationKind.CaregiverAlert, schedule.CaregiverContact, $"A dose of {name} due at {at} was not confirmed.", scheduleId, dueUtc),
                ct);
        }

        return true;
    }

    public NotificationMessage Message(NotificationKind kind, string recipient, string text, string? scheduleId, DateTimeOffset? dueUtc) =>
        new(Ids.New("ntf"), kind, recipient, text, scheduleId, dueUtc, clock.GetUtcNow());

    private async Task<bool> IsAnsweredAsync(string scheduleId, DateTimeOffset dueUtc, CancellationToken ct) =>
        await events.GetAsync(scheduleId, dueUtc, ct) is { Status: DoseStatus.Taken or DoseStatus.Skipped };

    private async Task<(DoseSchedule? Schedule, string Name)> LoadAsync(string scheduleId, CancellationToken ct)
    {
        var schedule = await schedules.GetAsync(scheduleId, ct);
        if (schedule is null)
        {
            return (null, scheduleId);
        }

        var medication = await medications.GetAsync(schedule.MedicationId, ct);
        return (schedule, medication is null ? schedule.MedicationId : $"{medication.Name} {medication.Strength}".Trim());
    }

    private static string LocalTime(DoseSchedule schedule, DateTimeOffset dueUtc) =>
        TimeZoneInfo.ConvertTime(dueUtc, schedule.TimeZone).ToString("HH:mm", CultureInfo.InvariantCulture);
}
