using DoseKeeper.Application;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;

namespace DoseKeeper.Functions.Orchestration;

/// <summary>
/// Eternal orchestration, one instance per schedule ("reminders-{scheduleId}").
/// Each turn: durable timer until the next dose, remind, wait for the user's answer (human-interaction pattern);
/// no answer by half the late window: remind again; none by the end of it: mark missed and alert the caregiver.
/// Then ContinueAsNew so the history stays small forever.
/// </summary>
public static class DoseReminderOrchestrator
{
    private const int MaxAnswersPerWait = 10;

    [Function(nameof(DoseReminderOrchestrator))]
    public static async Task<string> RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<ReminderLoopInput>() ?? throw new InvalidOperationException("Missing orchestration input.");
        var next = await context.CallActivityAsync<NextDose?>(nameof(ReminderActivities.GetNextDose), new NextDoseRequest(input.ScheduleId, input.AfterUtc));
        if (next is null)
        {
            context.SetCustomStatus("schedule ended");
            return "schedule ended";
        }

        context.SetCustomStatus($"next dose {next.DueUtc:O}");
        await context.CreateTimer(next.DueUtc.UtcDateTime, CancellationToken.None);

        var outcome = await HandleDoseAsync(context, next);
        context.SetCustomStatus($"{outcome} {next.DueUtc:O}");
        context.ContinueAsNew(input with { AfterUtc = next.DueUtc }, preserveUnprocessedEvents: false);
        return outcome;
    }

    private static async Task<string> HandleDoseAsync(TaskOrchestrationContext context, NextDose next)
    {
        var dose = new DoseRef(next.ScheduleId, next.DueUtc);
        if (!await context.CallActivityAsync<bool>(nameof(ReminderActivities.SendReminder), new ReminderRequest(next.ScheduleId, next.DueUtc, false)))
        {
            return "answered";
        }

        var escalateAfter = TimeSpan.FromSeconds(next.EscalateAfterSeconds);
        if (await WaitForAnswerAsync(context, next.DueUtc, escalateAfter) is { } first)
        {
            return $"answered {first.Status}";
        }

        if (!await context.CallActivityAsync<bool>(nameof(ReminderActivities.SendReminder), new ReminderRequest(next.ScheduleId, next.DueUtc, true)))
        {
            return "answered";
        }

        var rest = TimeSpan.FromSeconds(next.LateWindowSeconds) - escalateAfter;
        if (await WaitForAnswerAsync(context, next.DueUtc, rest) is { } second)
        {
            return $"answered {second.Status}";
        }

        var marked = await context.CallActivityAsync<bool>(nameof(ReminderActivities.MarkMissed), dose);
        return marked ? "missed" : "answered";
    }

    /// <summary>Waits for an answer to this dose until the deadline; answers to other doses (stale or early) are ignored.</summary>
    internal static async Task<DoseAnswered?> WaitForAnswerAsync(TaskOrchestrationContext context, DateTimeOffset dueUtc, TimeSpan timeout)
    {
        var deadline = context.CurrentUtcDateTime + timeout;
        using var cts = new CancellationTokenSource();
        var timer = context.CreateTimer(deadline, cts.Token);
        for (var answers = 0; answers < MaxAnswersPerWait; answers++)
        {
            var answer = context.WaitForExternalEvent<DoseAnswered>(Names.DoseAnsweredEvent, cts.Token);
            var winner = await Task.WhenAny(answer, timer);
            if (winner == timer)
            {
                return null;
            }

            var received = await answer;
            if (received.DueUtc == dueUtc)
            {
                CancelOnOrchestrationThread(cts);
                return received;
            }
        }

        // Pathological stream of unrelated answers: treat as unanswered rather than loop forever.
        CancelOnOrchestrationThread(cts);
        return null;
    }

    /// <summary>
    /// Cancels the pending durable timer synchronously. CancelAsync would run the callbacks on the thread pool and
    /// resume the orchestrator off its single-threaded scheduler, which Durable Functions does not allow.
    /// </summary>
#pragma warning disable CA1849 // Synchronous Cancel is required inside orchestrator code.
    private static void CancelOnOrchestrationThread(CancellationTokenSource cts) => cts.Cancel();
#pragma warning restore CA1849
}
