using DoseKeeper.Application;
using DoseKeeper.Domain;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;

namespace DoseKeeper.Functions.Orchestration;

/// <summary>
/// Eternal orchestration, one instance per schedule ("reminders-{scheduleId}").
/// Each turn: durable timer until the next dose, remind, wait for the user's response (human-interaction pattern);
/// no response by half the late window: remind again; none by the end of it: mark missed and alert the caregiver.
/// "Snooze N minutes" restarts that clock after a durable timer (at most <see cref="MaxSnoozes"/> times).
/// Then ContinueAsNew so the history stays small forever.
/// </summary>
public static class DoseReminderOrchestrator
{
    public const int MaxSnoozes = 3;
    private const int MaxResponsesPerWait = 10;

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
        var start = next.DueUtc.UtcDateTime;
        for (var snoozes = 0; snoozes <= MaxSnoozes; snoozes++)
        {
            var response = await RemindAndWaitAsync(context, next, start);
            switch (response)
            {
                case { Status: DoseStatus.Taken or DoseStatus.Skipped }:
                    return $"answered {response.Status}";
                case { SnoozeMinutes: > 0 } when snoozes < MaxSnoozes:
                    start = context.CurrentUtcDateTime.AddMinutes(response.SnoozeMinutes.Value);
                    context.SetCustomStatus($"snoozed until {start:O} {next.DueUtc:O}");
                    // The user may still take or skip the dose while it is snoozed.
                    if (await WaitForResponseAsync(context, next.DueUtc, start) is { Status: DoseStatus.Taken or DoseStatus.Skipped } early)
                    {
                        return $"answered {early.Status}";
                    }

                    continue;
                case { Status: DoseStatus.Pending, SnoozeMinutes: null }:
                    // Activities found the dose already answered (e.g. taken before the reminder fired).
                    return "answered";
                default:
                    // Timed out, or one snooze too many: the dose counts as missed.
                    var marked = await context.CallActivityAsync<bool>(nameof(ReminderActivities.MarkMissed), new DoseRef(next.ScheduleId, next.DueUtc));
                    return marked ? "missed" : "answered";
            }
        }

        return "answered";
    }

    /// <summary>
    /// Reminder, wait until half the window, second reminder, wait until the end. Returns the user's response, a
    /// "Pending, no snooze" marker when the dose was already answered, or null on timeout.
    /// </summary>
    private static async Task<DoseResponse?> RemindAndWaitAsync(TaskOrchestrationContext context, NextDose next, DateTime start)
    {
        var answeredAlready = new DoseResponse(next.DueUtc, DoseStatus.Pending, null);
        if (!await context.CallActivityAsync<bool>(nameof(ReminderActivities.SendReminder), new ReminderRequest(next.ScheduleId, next.DueUtc, false)))
        {
            return answeredAlready;
        }

        if (await WaitForResponseAsync(context, next.DueUtc, start.AddSeconds(next.EscalateAfterSeconds)) is { } first)
        {
            return first;
        }

        if (!await context.CallActivityAsync<bool>(nameof(ReminderActivities.SendReminder), new ReminderRequest(next.ScheduleId, next.DueUtc, true)))
        {
            return answeredAlready;
        }

        return await WaitForResponseAsync(context, next.DueUtc, start.AddSeconds(next.LateWindowSeconds));
    }

    /// <summary>Waits for a response to this dose until the deadline; responses about other doses (stale or early) are ignored.</summary>
    internal static async Task<DoseResponse?> WaitForResponseAsync(TaskOrchestrationContext context, DateTimeOffset dueUtc, DateTime deadline)
    {
        using var cts = new CancellationTokenSource();
        var timer = context.CreateTimer(deadline, cts.Token);
        for (var responses = 0; responses < MaxResponsesPerWait; responses++)
        {
            var response = context.WaitForExternalEvent<DoseResponse>(Names.DoseResponseEvent, cts.Token);
            var winner = await Task.WhenAny(response, timer);
            if (winner == timer)
            {
                return null;
            }

            var received = await response;
            if (received.DueUtc == dueUtc)
            {
                CancelOnOrchestrationThread(cts);
                return received;
            }
        }

        // Pathological stream of unrelated responses: treat as unanswered rather than loop forever.
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
