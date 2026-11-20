using DoseKeeper.Domain;

namespace DoseKeeper.Application;

public enum NotificationKind
{
    Reminder,
    Escalation,
    Missed,
    CaregiverAlert,
    Refill,
}

public sealed record NotificationMessage(
    string Id,
    NotificationKind Kind,
    string Recipient,
    string Text,
    string? ScheduleId,
    DateTimeOffset? DueUtc,
    DateTimeOffset CreatedUtc);

public sealed record NewMedication(string Name, string Strength, int PillsOnHand, int RefillThresholdDays);

public sealed record NewSchedule(
    string MedicationId,
    IReadOnlyList<string> TimesOfDay,
    IReadOnlyList<DayOfWeek>? DaysOfWeek,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? TimeZone,
    int DosePerIntake,
    int LateWindowMinutes,
    string? CaregiverContact);

public sealed record DoseView(
    string ScheduleId,
    string MedicationId,
    string MedicationName,
    DateTimeOffset DueUtc,
    string LocalTime,
    int Pills,
    DoseStatus Status,
    bool Late);

/// <summary>What the reminder loop needs for its next turn.</summary>
public sealed record NextDose(string ScheduleId, DateTimeOffset DueUtc, int EscalateAfterSeconds, int LateWindowSeconds);

public sealed record ScheduleAdherence(string ScheduleId, string MedicationName, Adherence Adherence);

public sealed record WeeklyAdherenceReport(DateOnly WeekStart, IReadOnlyList<ScheduleAdherence> Schedules, decimal OverallPercent, DateTimeOffset CreatedUtc);

public sealed record WeekPoint(DateOnly WeekStart, decimal Percent, int Due, int Taken);
