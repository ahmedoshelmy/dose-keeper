using DoseKeeper.Application;
using DoseKeeper.Functions.Orchestration;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using NSubstitute;

namespace DoseKeeper.Functions.UnitTests;

public sealed class DurableReminderLoopsTests
{
    [Theory]
    [InlineData(null, ReminderLoopState.Missing)]
    [InlineData(OrchestrationRuntimeStatus.Pending, ReminderLoopState.Running)]
    [InlineData(OrchestrationRuntimeStatus.Running, ReminderLoopState.Running)]
    [InlineData(OrchestrationRuntimeStatus.Suspended, ReminderLoopState.Running)]
    [InlineData(OrchestrationRuntimeStatus.Completed, ReminderLoopState.Completed)]
    [InlineData(OrchestrationRuntimeStatus.Failed, ReminderLoopState.Broken)]
    [InlineData(OrchestrationRuntimeStatus.Terminated, ReminderLoopState.Broken)]
    public void Maps_the_runtime_status(OrchestrationRuntimeStatus? status, ReminderLoopState expected) =>
        DurableReminderLoops.StateOf(status).ShouldBe(expected);

    [Fact]
    public async Task Starts_the_loop_under_the_schedules_fixed_instance_id()
    {
        var client = Substitute.For<DurableTaskClient>("test");
        var after = new DateTimeOffset(2026, 1, 10, 7, 0, 0, TimeSpan.Zero);

        await new DurableReminderLoops(client).StartAsync("sch-1", after, CancellationToken.None);

        await client.Received(1).ScheduleNewOrchestrationInstanceAsync(
            new TaskName(nameof(DoseReminderOrchestrator)),
            Arg.Is<object?>(o => o is ReminderLoopInput && ((ReminderLoopInput)o).ScheduleId == "sch-1" && ((ReminderLoopInput)o).AfterUtc == after),
            Arg.Is<StartOrchestrationOptions?>(o => o != null && o.InstanceId == "reminders-sch-1"),
            CancellationToken.None);
    }
}
