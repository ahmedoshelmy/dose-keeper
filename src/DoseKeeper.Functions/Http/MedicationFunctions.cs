using DoseKeeper.Application;
using DoseKeeper.Functions.Orchestration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;

namespace DoseKeeper.Functions.Http;

/// <summary>
/// HTTP API (ASP.NET Core integration). Anonymous auth level: the app is a single-user local demo; in Azure put
/// Easy Auth or APIM in front (see docs/adr/0004).
/// </summary>
public sealed class MedicationFunctions(MedicationService medications, ScheduleService schedules, TimeProvider clock)
{
    [Function("CreateMedication")]
    public Task<IActionResult> CreateMedication(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "medications")] HttpRequest req, CancellationToken ct) =>
        Api.Run(async () => Api.Created(MedicationDto.From(await medications.CreateAsync(await Api.BodyAsync<NewMedication>(req, ct), ct))));

    [Function("ListMedications")]
    public Task<IActionResult> ListMedications(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "medications")] HttpRequest req, CancellationToken ct) =>
        Api.Run(async () => Api.Ok((await medications.ListAsync(ct)).Select(MedicationDto.From)));

    [Function("RefillMedication")]
    public Task<IActionResult> RefillMedication(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "medications/{id}/refill")] HttpRequest req, string id, CancellationToken ct) =>
        Api.Run(async () => Api.Ok(MedicationDto.From(await medications.RefillAsync(id, (await Api.BodyAsync<RefillRequest>(req, ct)).Pills, ct))));

    [Function("RefillForecasts")]
    public Task<IActionResult> RefillForecasts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "refills")] HttpRequest req, CancellationToken ct) =>
        Api.Run(async () => Api.Ok(await medications.ForecastAsync(ct)));

    /// <summary>Saves the schedule, then starts its eternal reminder orchestration (idempotent instance id).</summary>
    [Function("CreateSchedule")]
    public Task<IActionResult> CreateSchedule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "schedules")] HttpRequest req,
        [DurableClient] DurableTaskClient durable,
        CancellationToken ct) =>
        Api.Run(async () =>
        {
            var schedule = await schedules.CreateAsync(await Api.BodyAsync<NewSchedule>(req, ct), ct);
            await durable.ScheduleNewOrchestrationInstanceAsync(
                nameof(DoseReminderOrchestrator),
                new ReminderLoopInput(schedule.Id, clock.GetUtcNow()),
                new StartOrchestrationOptions(Names.ReminderInstanceId(schedule.Id)),
                ct);
            return Api.Created(ScheduleDto.From(schedule));
        });

    [Function("ListSchedules")]
    public Task<IActionResult> ListSchedules(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "schedules")] HttpRequest req, CancellationToken ct) =>
        Api.Run(async () => Api.Ok((await schedules.ListAsync(ct)).Select(ScheduleDto.From)));

    /// <summary>The reminder loop's runtime and custom status, straight from the Durable Task client.</summary>
    [Function("ReminderStatus")]
    public static Task<IActionResult> ReminderStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "schedules/{id}/reminders")] HttpRequest req,
        string id,
        [DurableClient] DurableTaskClient durable,
        CancellationToken ct) =>
        Api.Run(async () =>
        {
            var instance = await durable.GetInstanceAsync(Names.ReminderInstanceId(id), getInputsAndOutputs: true, ct)
                ?? throw new NotFoundException($"No reminder loop for schedule {id}.");
            return Api.Ok(new
            {
                instance.InstanceId,
                RuntimeStatus = instance.RuntimeStatus.ToString(),
                CustomStatus = instance.ReadCustomStatusAs<string>(),
                instance.LastUpdatedAt,
            });
        });
}
