namespace DoseKeeper.Domain;

/// <summary>How long the current stock lasts at the scheduled rate, and whether it is time to refill.</summary>
public sealed record RefillForecast(string MedicationId, int PillsOnHand, decimal DailyUse, int? DaysLeft, DateOnly? RunOutDate, bool NeedsRefill)
{
    public static RefillForecast For(Medication medication, IEnumerable<DoseSchedule> schedules, DateOnly today)
    {
        var dailyUse = schedules.Where(s => s.MedicationId == medication.Id && (s.EndDate is null || s.EndDate >= today)).Sum(s => s.DailyUse);
        if (dailyUse == 0)
        {
            return new RefillForecast(medication.Id, medication.PillsOnHand, 0, null, null, false);
        }

        var daysLeft = (int)Math.Floor(medication.PillsOnHand / dailyUse);
        return new RefillForecast(
            medication.Id,
            medication.PillsOnHand,
            Math.Round(dailyUse, 2),
            daysLeft,
            today.AddDays(daysLeft),
            daysLeft <= medication.RefillThresholdDays);
    }
}
