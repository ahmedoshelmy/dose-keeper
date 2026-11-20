using DoseKeeper.Domain;

namespace DoseKeeper.Functions.Orchestration;

/// <summary>State carried across ContinueAsNew: the schedule and the last dose already handled.</summary>
public sealed record ReminderLoopInput(string ScheduleId, DateTimeOffset AfterUtc);

public sealed record NextDoseRequest(string ScheduleId, DateTimeOffset AfterUtc);

public sealed record ReminderRequest(string ScheduleId, DateTimeOffset DueUtc, bool Escalation);

public sealed record DoseRef(string ScheduleId, DateTimeOffset DueUtc);

/// <summary>
/// Payload of the external event the HTTP API raises when the user responds to a dose: Taken/Skipped, or a snooze
/// (Status Pending with SnoozeMinutes set).
/// </summary>
public sealed record DoseResponse(DateTimeOffset DueUtc, DoseStatus Status, int? SnoozeMinutes = null);

public sealed record WeeklyReportInput(DateOnly WeekStart);

public sealed record ScheduleWeek(string ScheduleId, DateOnly WeekStart);

public static class Names
{
    public const string DoseResponseEvent = "DoseResponse";

    public static string ReminderInstanceId(string scheduleId) => $"reminders-{scheduleId}";
}
