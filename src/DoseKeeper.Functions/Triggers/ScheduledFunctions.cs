using DoseKeeper.Application;
using DoseKeeper.Functions.Orchestration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace DoseKeeper.Functions.Triggers;

/// <summary>Timer triggers. CRON comes from app settings so a test can make them fire every minute.</summary>
public sealed class ScheduledFunctions(RefillCheckService refillCheck, AdherenceService adherence, ILogger<ScheduledFunctions> logger)
{
    [Function(nameof(NightlyRefillCheck))]
    public async Task NightlyRefillCheck([TimerTrigger("%RefillCheckSchedule%")] TimerInfo timer, CancellationToken ct)
    {
        var queued = await refillCheck.RunAsync(ct);
        logger.LogInformation("Refill check queued {Count} notice(s)", queued);
    }

    [Function(nameof(WeeklyReportTimer))]
    public async Task WeeklyReportTimer(
        [TimerTrigger("%WeeklyReportSchedule%")] TimerInfo timer, [DurableClient] DurableTaskClient durable, CancellationToken ct)
    {
        var id = await durable.ScheduleNewOrchestrationInstanceAsync(
            nameof(WeeklyAdherenceOrchestrator), new WeeklyReportInput(adherence.CurrentWeekStart().AddDays(-7)), ct);
        logger.LogInformation("Started weekly adherence report {InstanceId}", id);
    }
}
