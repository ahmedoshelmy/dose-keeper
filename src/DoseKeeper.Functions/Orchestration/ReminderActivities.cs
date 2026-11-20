using DoseKeeper.Application;
using Microsoft.Azure.Functions.Worker;

namespace DoseKeeper.Functions.Orchestration;

/// <summary>Activities are thin: all decisions live in the application services so they are unit-testable.</summary>
public sealed class ReminderActivities(ReminderService reminders)
{
    [Function(nameof(GetNextDose))]
    public Task<NextDose?> GetNextDose([ActivityTrigger] NextDoseRequest request, CancellationToken ct) =>
        reminders.NextDoseAsync(request.ScheduleId, request.AfterUtc, ct);

    [Function(nameof(SendReminder))]
    public Task<bool> SendReminder([ActivityTrigger] ReminderRequest request, CancellationToken ct) =>
        reminders.RemindAsync(request.ScheduleId, request.DueUtc, request.Escalation, ct);

    [Function(nameof(MarkMissed))]
    public Task<bool> MarkMissed([ActivityTrigger] DoseRef dose, CancellationToken ct) =>
        reminders.MarkMissedAsync(dose.ScheduleId, dose.DueUtc, ct);
}
