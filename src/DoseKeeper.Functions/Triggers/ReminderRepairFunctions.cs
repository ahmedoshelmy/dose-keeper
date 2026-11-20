using DoseKeeper.Application;
using DoseKeeper.Functions.Http;
using DoseKeeper.Functions.Orchestration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace DoseKeeper.Functions.Triggers;

/// <summary>
/// Starts the reminder loop of every live schedule that has none (or a failed/terminated one): on a timer
/// (<c>ReminderRepairSchedule</c>, default every 15 minutes) and on demand.
/// </summary>
public sealed class ReminderRepairFunctions(ReminderLoopRepairService repair, ILogger<ReminderRepairFunctions> logger)
{
    [Function(nameof(RepairReminderLoops))]
    public async Task RepairReminderLoops(
        [TimerTrigger("%ReminderRepairSchedule%")] TimerInfo timer, [DurableClient] DurableTaskClient durable, CancellationToken ct) =>
        await RunAsync(durable, ct);

    [Function("RepairReminderLoopsNow")]
    public Task<IActionResult> RepairNow(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "reminders/repair")] HttpRequest req,
        [DurableClient] DurableTaskClient durable,
        CancellationToken ct) =>
        Api.Run(async () => Api.Ok(await RunAsync(durable, ct)));

    private async Task<ReminderRepairResult> RunAsync(DurableTaskClient durable, CancellationToken ct)
    {
        var result = await repair.RunAsync(new DurableReminderLoops(durable), ct);
        if (result.Restarted.Count > 0 || result.Failed.Count > 0)
        {
            logger.LogWarning(
                "Reminder repair: {Checked} schedule(s), restarted {Restarted}, failed {Failed}",
                result.Checked, result.Restarted, result.Failed);
        }

        return result;
    }
}
