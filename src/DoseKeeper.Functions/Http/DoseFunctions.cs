using DoseKeeper.Application;
using DoseKeeper.Domain;
using DoseKeeper.Functions.Orchestration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace DoseKeeper.Functions.Http;

public sealed class DoseFunctions(DoseService doses, INotificationLog notifications, ILogger<DoseFunctions> logger)
{
    [Function("TodayDoses")]
    public Task<IActionResult> Today(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "doses/today")] HttpRequest req, CancellationToken ct) =>
        Api.Run(async () => Api.Ok(await doses.TodayAsync(ct)));

    [Function("TakeDose")]
    public Task<IActionResult> Take(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "doses/take")] HttpRequest req,
        [DurableClient] DurableTaskClient durable,
        CancellationToken ct) => Answer(req, durable, DoseStatus.Taken, ct);

    [Function("SkipDose")]
    public Task<IActionResult> Skip(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "doses/skip")] HttpRequest req,
        [DurableClient] DurableTaskClient durable,
        CancellationToken ct) => Answer(req, durable, DoseStatus.Skipped, ct);

    [Function("RecentNotifications")]
    public Task<IActionResult> Notifications(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "notifications")] HttpRequest req, CancellationToken ct) =>
        Api.Run(async () => Api.Ok(await notifications.RecentAsync(30, ct)));

    /// <summary>Stores the answer, then raises the DoseAnswered external event so the reminder loop stops escalating.</summary>
    private Task<IActionResult> Answer(HttpRequest req, DurableTaskClient durable, DoseStatus status, CancellationToken ct) => Api.Run(async () =>
    {
        var body = await Api.BodyAsync<AnswerRequest>(req, ct);
        var recorded = await doses.AnswerAsync(body.ScheduleId, body.DueUtc, status, ct);
        try
        {
            await durable.RaiseEventAsync(
                Names.ReminderInstanceId(body.ScheduleId), Names.DoseAnsweredEvent, new DoseAnswered(recorded.DueUtc, recorded.Status), ct);
        }
        catch (InvalidOperationException ex)
        {
            // The loop may have ended (schedule over); the answer is already stored, so this is not an error for the user.
            logger.LogWarning(ex, "Could not notify reminder loop for {ScheduleId}", body.ScheduleId);
        }

        return Api.Ok(recorded);
    });
}
