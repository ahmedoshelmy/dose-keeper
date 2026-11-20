namespace DoseKeeper.Domain;

/// <summary>
/// When a medication should be taken: wall-clock times in the user's time zone, on chosen weekdays, between two dates.
/// Expansion converts each local time to UTC with the zone's rules for that date, so daylight-saving changes are honoured.
/// </summary>
public sealed class DoseSchedule
{
    public const string DefaultTimeZone = "Africa/Cairo";

    private DoseSchedule(
        string id,
        string medicationId,
        IReadOnlyList<TimeOnly> timesOfDay,
        IReadOnlySet<DayOfWeek> daysOfWeek,
        DateOnly startDate,
        DateOnly? endDate,
        TimeZoneInfo timeZone,
        int dosePerIntake,
        TimeSpan lateWindow,
        string? caregiverContact)
    {
        Id = id;
        MedicationId = medicationId;
        TimesOfDay = timesOfDay;
        DaysOfWeek = daysOfWeek;
        StartDate = startDate;
        EndDate = endDate;
        TimeZone = timeZone;
        DosePerIntake = dosePerIntake;
        LateWindow = lateWindow;
        CaregiverContact = caregiverContact;
    }

    public string Id { get; }
    public string MedicationId { get; }
    public IReadOnlyList<TimeOnly> TimesOfDay { get; }
    public IReadOnlySet<DayOfWeek> DaysOfWeek { get; }
    public DateOnly StartDate { get; }
    public DateOnly? EndDate { get; }
    public TimeZoneInfo TimeZone { get; }
    public int DosePerIntake { get; }

    /// <summary>How long after the due time a dose still counts as on time; after it the dose is missed.</summary>
    public TimeSpan LateWindow { get; }

    /// <summary>Optional person to notify when a dose is missed (synthetic address in samples).</summary>
    public string? CaregiverContact { get; }

    /// <summary>Halfway through the late window the reminder is repeated (escalation step one).</summary>
    public TimeSpan EscalateAfter => LateWindow / 2;

    public static DoseSchedule Create(
        string id,
        string medicationId,
        IEnumerable<TimeOnly> timesOfDay,
        IEnumerable<DayOfWeek>? daysOfWeek,
        DateOnly startDate,
        DateOnly? endDate,
        string? timeZoneId,
        int dosePerIntake,
        TimeSpan lateWindow,
        string? caregiverContact)
    {
        var times = timesOfDay.Distinct().Order().ToList();
        if (times.Count == 0)
        {
            throw new DomainException("A schedule needs at least one time of day.");
        }

        var days = (daysOfWeek ?? Enum.GetValues<DayOfWeek>()).ToHashSet();
        if (days.Count == 0)
        {
            throw new DomainException("A schedule needs at least one weekday.");
        }

        if (endDate is { } end && end < startDate)
        {
            throw new DomainException("The end date cannot be before the start date.");
        }

        if (dosePerIntake is < 1 or > 20)
        {
            throw new DomainException("Dose per intake must be between 1 and 20.");
        }

        if (lateWindow < TimeSpan.FromMinutes(1) || lateWindow > TimeSpan.FromHours(12))
        {
            throw new DomainException("The late window must be between 1 minute and 12 hours.");
        }

        return new DoseSchedule(id, medicationId, times, days, startDate, endDate, FindZone(timeZoneId), dosePerIntake, lateWindow, caregiverContact);
    }

    /// <summary>Average pills used per day, used by the refill forecast.</summary>
    public decimal DailyUse => TimesOfDay.Count * DosePerIntake * DaysOfWeek.Count / 7m;

    public bool IsActiveOn(DateOnly localDate) =>
        localDate >= StartDate && (EndDate is null || localDate <= EndDate) && DaysOfWeek.Contains(localDate.DayOfWeek);

    /// <summary>All due instants (UTC) in [fromUtc, toUtc), in order.</summary>
    public IEnumerable<DateTimeOffset> OccurrencesBetween(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        var firstDay = LocalDate(fromUtc).AddDays(-1);
        var lastDay = LocalDate(toUtc).AddDays(1);
        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            if (!IsActiveOn(day))
            {
                continue;
            }

            foreach (var time in TimesOfDay)
            {
                var due = ToUtc(day, time);
                if (due >= fromUtc && due < toUtc)
                {
                    yield return due;
                }
            }
        }
    }

    /// <summary>The first due instant strictly after <paramref name="afterUtc"/>, or null once the schedule has ended.</summary>
    public DateTimeOffset? NextOccurrenceAfter(DateTimeOffset afterUtc)
    {
        var start = LocalDate(afterUtc);
        if (EndDate is { } end && start > end)
        {
            return null;
        }

        // Looking 8 days ahead always reaches the next active weekday.
        return OccurrencesBetween(afterUtc.AddTicks(1), afterUtc.AddDays(8)).Cast<DateTimeOffset?>().FirstOrDefault();
    }

    public DateOnly LocalDate(DateTimeOffset utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, TimeZone).DateTime);

    /// <summary>
    /// Converts a wall-clock time to UTC. A time that does not exist (clocks jump forward) moves forward by the gap;
    /// an ambiguous time (clocks fall back) uses its first occurrence, so the dose is never reminded twice.
    /// </summary>
    public DateTimeOffset ToUtc(DateOnly localDate, TimeOnly localTime)
    {
        var local = localDate.ToDateTime(localTime, DateTimeKind.Unspecified);
        if (TimeZone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        var offset = TimeZone.IsAmbiguousTime(local)
            ? TimeZone.GetAmbiguousTimeOffsets(local).Max()
            : TimeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    public static TimeZoneInfo FindZone(string? timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(timeZoneId) ? DefaultTimeZone : timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new DomainException($"Unknown time zone '{timeZoneId}'.");
        }
    }
}
