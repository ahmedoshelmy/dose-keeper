using System.Globalization;
using DoseKeeper.Domain;

namespace DoseKeeper.Application;

/// <summary>Creates schedules; the caller (the Functions adapter) then starts the schedule's reminder loop.</summary>
public sealed class ScheduleService(
    IMedicationRepository medications,
    IScheduleRepository schedules,
    TimeProvider clock)
{
    public async Task<DoseSchedule> CreateAsync(NewSchedule request, CancellationToken ct)
    {
        _ = await medications.GetAsync(request.MedicationId, ct) ?? throw new NotFoundException($"Medication {request.MedicationId} not found.");
        var times = request.TimesOfDay.Select(ParseTime).ToList();
        var start = request.StartDate ?? DoseSchedule.LocalToday(clock.GetUtcNow(), request.TimeZone);
        var schedule = DoseSchedule.Create(
            Ids.New("sch"),
            request.MedicationId,
            times,
            request.DaysOfWeek,
            start,
            request.EndDate,
            request.TimeZone,
            request.DosePerIntake,
            TimeSpan.FromMinutes(request.LateWindowMinutes),
            request.CaregiverContact);
        await schedules.SaveAsync(schedule, ct);
        return schedule;
    }

    public Task<IReadOnlyList<DoseSchedule>> ListAsync(CancellationToken ct) => schedules.ListAsync(ct);

    private static TimeOnly ParseTime(string value) =>
        TimeOnly.TryParseExact(value, ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : throw new DomainException($"'{value}' is not a time of day (expected HH:mm).");
}
