using System.Globalization;
using DoseKeeper.Domain;

namespace DoseKeeper.Functions.Http;

public sealed record MedicationDto(string Id, string Name, string Strength, int PillsOnHand, int RefillThresholdDays)
{
    public static MedicationDto From(Medication m) => new(m.Id, m.Name, m.Strength, m.PillsOnHand, m.RefillThresholdDays);
}

public sealed record ScheduleDto(
    string Id,
    string MedicationId,
    IReadOnlyList<string> TimesOfDay,
    IReadOnlyList<DayOfWeek> DaysOfWeek,
    DateOnly StartDate,
    DateOnly? EndDate,
    string TimeZone,
    int DosePerIntake,
    int LateWindowMinutes,
    string? CaregiverContact)
{
    public static ScheduleDto From(DoseSchedule s) => new(
        s.Id,
        s.MedicationId,
        [.. s.TimesOfDay.Select(t => t.ToString("HH:mm", CultureInfo.InvariantCulture))],
        [.. s.DaysOfWeek.Order()],
        s.StartDate,
        s.EndDate,
        s.TimeZone.Id,
        s.DosePerIntake,
        (int)s.LateWindow.TotalMinutes,
        s.CaregiverContact);
}

public sealed record RefillRequest(int Pills);

public sealed record AnswerRequest(string ScheduleId, DateTimeOffset DueUtc);

public sealed record SnoozeRequest(string ScheduleId, DateTimeOffset DueUtc, int Minutes);
