using DoseKeeper.Application;
using DoseKeeper.Functions.Orchestration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;

namespace DoseKeeper.Functions.Http;

public sealed class ReportFunctions(AdherenceService adherence, RefillCheckService refillCheck)
{
    [Function("AdherenceHistory")]
    public Task<IActionResult> History(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "adherence")] HttpRequest req, CancellationToken ct) =>
        Api.Run(async () => Api.Ok(await adherence.HistoryAsync(int.TryParse(req.Query["weeks"], out var w) ? w : 6, ct)));

    /// <summary>Starts the fan-out/fan-in report for the current week; 202 with the instance id to poll.</summary>
    [Function("StartWeeklyReport")]
    public Task<IActionResult> StartWeekly(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "reports/weekly")] HttpRequest req,
        [DurableClient] DurableTaskClient durable,
        CancellationToken ct) =>
        Api.Run(async () =>
        {
            var id = await durable.ScheduleNewOrchestrationInstanceAsync(
                nameof(WeeklyAdherenceOrchestrator), new WeeklyReportInput(adherence.CurrentWeekStart()), ct);
            return new AcceptedResult($"/api/reports/weekly/{id}", new { InstanceId = id });
        });

    [Function("WeeklyReportStatus")]
    public static Task<IActionResult> WeeklyStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/weekly/{id}")] HttpRequest req,
        string id,
        [DurableClient] DurableTaskClient durable,
        CancellationToken ct) =>
        Api.Run(async () =>
        {
            var instance = await durable.GetInstanceAsync(id, getInputsAndOutputs: true, ct)
                ?? throw new NotFoundException($"No report run {id}.");
            return Api.Ok(new
            {
                RuntimeStatus = instance.RuntimeStatus.ToString(),
                Report = instance.RuntimeStatus == OrchestrationRuntimeStatus.Completed ? instance.ReadOutputAs<WeeklyAdherenceReport>() : null,
            });
        });

    [Function("LatestWeeklyReport")]
    public Task<IActionResult> Latest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/latest")] HttpRequest req, CancellationToken ct) =>
        Api.Run(async () => await adherence.LatestReportAsync(ct) is { } report ? Api.Ok(report) : new NoContentResult());

    /// <summary>Runs the nightly refill check on demand (same code as the timer trigger).</summary>
    [Function("RunRefillCheck")]
    public Task<IActionResult> RunRefillCheck(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "refills/check")] HttpRequest req, CancellationToken ct) =>
        Api.Run(async () => Api.Ok(new { Queued = await refillCheck.RunAsync(ct) }));
}
