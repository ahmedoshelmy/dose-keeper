using DoseKeeper.Application;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;

namespace DoseKeeper.Functions.Orchestration;

/// <summary>The application's view of reminder loops, backed by the Durable Task client of one function call.</summary>
public sealed class DurableReminderLoops(DurableTaskClient durable) : IReminderLoops
{
    public async Task<ReminderLoopState> GetStateAsync(string scheduleId, CancellationToken ct)
    {
        var instance = await durable.GetInstanceAsync(Names.ReminderInstanceId(scheduleId), getInputsAndOutputs: false, ct);
        return StateOf(instance?.RuntimeStatus);
    }

    /// <remarks>The Durable extension replaces a finished instance with the same id; a running one is never replaced.</remarks>
    public Task StartAsync(string scheduleId, DateTimeOffset afterUtc, CancellationToken ct) =>
        durable.ScheduleNewOrchestrationInstanceAsync(
            nameof(DoseReminderOrchestrator),
            new ReminderLoopInput(scheduleId, afterUtc),
            new StartOrchestrationOptions(Names.ReminderInstanceId(scheduleId)),
            ct);

    public static ReminderLoopState StateOf(OrchestrationRuntimeStatus? status) => status switch
    {
        null => ReminderLoopState.Missing,
        OrchestrationRuntimeStatus.Completed => ReminderLoopState.Completed,
        OrchestrationRuntimeStatus.Failed or OrchestrationRuntimeStatus.Terminated => ReminderLoopState.Broken,
        _ => ReminderLoopState.Running,
    };
}
