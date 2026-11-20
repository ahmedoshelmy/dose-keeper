using DoseKeeper.Application;
using DoseKeeper.Functions.Orchestration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Configuration;

namespace DoseKeeper.Functions.Http;

/// <summary>Loads synthetic demo data. Returns 404 unless DoseKeeper__EnableDemoSeed=true (compose sets it).</summary>
public sealed class DemoFunctions(DemoSeeder seeder, IConfiguration configuration, TimeProvider clock)
{
    [Function("SeedDemo")]
    public Task<IActionResult> Seed(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "demo/seed")] HttpRequest req,
        [DurableClient] DurableTaskClient durable,
        CancellationToken ct) =>
        Api.Run(async () =>
        {
            if (!configuration.GetValue<bool>("DoseKeeper:EnableDemoSeed"))
            {
                return new NotFoundResult();
            }

            var schedules = await seeder.SeedAsync(ct);
            foreach (var schedule in schedules)
            {
                var instanceId = Names.ReminderInstanceId(schedule.Id);
                if (await durable.GetInstanceAsync(instanceId, ct) is null)
                {
                    await durable.ScheduleNewOrchestrationInstanceAsync(
                        nameof(DoseReminderOrchestrator), new ReminderLoopInput(schedule.Id, clock.GetUtcNow()), new StartOrchestrationOptions(instanceId), ct);
                }
            }

            return Api.Ok(new { Schedules = schedules.Count });
        });
}
