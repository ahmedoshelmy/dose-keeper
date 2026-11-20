namespace DoseKeeper.Application;

/// <summary>Nightly job: queue a refill notice for every medication that runs out within its threshold.</summary>
public sealed class RefillCheckService(MedicationService medications, ReminderService reminders, INotificationOutbox queue)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var meds = (await medications.ListAsync(ct)).ToDictionary(m => m.Id);
        var sent = 0;
        foreach (var forecast in (await medications.ForecastAsync(ct)).Where(f => f.NeedsRefill))
        {
            var name = meds[forecast.MedicationId].Name;
            var text = $"Refill {name}: {forecast.PillsOnHand} pill(s) left, about {forecast.DaysLeft} day(s) (runs out {forecast.RunOutDate:yyyy-MM-dd}).";
            await queue.EnqueueAsync(reminders.Message(NotificationKind.Refill, ReminderService.UserRecipient, text, null, null), ct);
            sent++;
        }

        return sent;
    }
}
