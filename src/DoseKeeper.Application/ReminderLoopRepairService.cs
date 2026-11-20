using DoseKeeper.Domain;

namespace DoseKeeper.Application;

/// <summary>What the orchestration runtime says about a schedule's reminder loop.</summary>
public enum ReminderLoopState
{
    /// <summary>No instance: the start after saving the schedule failed, or the task hub was reset.</summary>
    Missing,

    /// <summary>Pending, running or suspended: leave it alone.</summary>
    Running,

    /// <summary>Finished on purpose (the schedule ended).</summary>
    Completed,

    /// <summary>Failed or terminated: the loop is dead and reminders stopped.</summary>
    Broken,
}

/// <summary>The reminder loops (one Durable orchestration per schedule), as the repair job needs them.</summary>
public interface IReminderLoops
{
    Task<ReminderLoopState> GetStateAsync(string scheduleId, CancellationToken ct);

    /// <summary>Starts the schedule's loop under its fixed instance id, looking for doses after <paramref name="afterUtc"/>.</summary>
    Task StartAsync(string scheduleId, DateTimeOffset afterUtc, CancellationToken ct);
}

public sealed record ReminderRepairResult(int Checked, IReadOnlyList<string> Restarted, IReadOnlyList<string> Failed);

/// <summary>
/// Safety net run by a timer: every schedule that has not ended must have a live reminder loop. A missing or broken
/// loop is started again from "now" (doses due while it was down are not reminded, they show as pending/missed).
/// Idempotent: a running loop is never touched, and the fixed instance id means two repairs start one loop.
/// </summary>
public sealed class ReminderLoopRepairService(IScheduleRepository schedules, TimeProvider clock)
{
    public async Task<ReminderRepairResult> RunAsync(IReminderLoops loops, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var all = await schedules.ListAsync(ct);
        List<string> restarted = [];
        List<string> failed = [];
        foreach (var schedule in all.Where(s => !HasEnded(s, now)))
        {
            var state = await loops.GetStateAsync(schedule.Id, ct);
            if (state is not (ReminderLoopState.Missing or ReminderLoopState.Broken))
            {
                continue;
            }

            try
            {
                await loops.StartAsync(schedule.Id, now, ct);
                restarted.Add(schedule.Id);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // One bad schedule must not stop the others; the next run tries again.
                failed.Add(schedule.Id);
            }
        }

        return new ReminderRepairResult(all.Count, restarted, failed);
    }

    private static bool HasEnded(DoseSchedule schedule, DateTimeOffset now) => schedule.NextOccurrenceAfter(now) is null;
}
