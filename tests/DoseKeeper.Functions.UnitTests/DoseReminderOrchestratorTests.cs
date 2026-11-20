using DoseKeeper.Application;
using DoseKeeper.Domain;
using DoseKeeper.Functions.Orchestration;
using Microsoft.DurableTask;
using NSubstitute;

namespace DoseKeeper.Functions.UnitTests;

/// <summary>
/// Drives the orchestrator with a substituted TaskOrchestrationContext: timers and external events are plain tasks the
/// test completes (or never completes), which is exactly what the Durable replay engine does deterministically.
/// </summary>
public sealed class DoseReminderOrchestratorTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 10, 5, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Due = new(2026, 1, 10, 6, 0, 0, TimeSpan.Zero);
    private static readonly NextDose Next = new("sch-a", Due, EscalateAfterSeconds: 900, LateWindowSeconds: 1800);

    private readonly TaskOrchestrationContext context = Substitute.For<TaskOrchestrationContext>();

    public DoseReminderOrchestratorTests()
    {
        context.GetInput<ReminderLoopInput>().Returns(new ReminderLoopInput("sch-a", Start));
        // Replay clock: at the due time for the first wait, 15 minutes later for the escalation wait.
        context.CurrentUtcDateTime.Returns(Due.UtcDateTime, Due.UtcDateTime.AddMinutes(15));
        context.CallActivityAsync<NextDose?>(nameof(ReminderActivities.GetNextDose), Arg.Any<object?>(), Arg.Any<TaskOptions?>()).Returns(Next);
        context.CallActivityAsync<bool>(nameof(ReminderActivities.SendReminder), Arg.Any<object?>(), Arg.Any<TaskOptions?>()).Returns(true);
        context.CallActivityAsync<bool>(nameof(ReminderActivities.MarkMissed), Arg.Any<object?>(), Arg.Any<TaskOptions?>()).Returns(true);
        // The sleep until the dose is due always elapses.
        context.CreateTimer(Due.UtcDateTime, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    private void AnswerTimersElapse() =>
        context.CreateTimer(Arg.Is<DateTime>(d => d > Due.UtcDateTime), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

    private void AnswerTimersNeverElapse() =>
        context.CreateTimer(Arg.Is<DateTime>(d => d > Due.UtcDateTime), Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource().Task);

    private void UserAnswers(params DoseResponse[] answers)
    {
        var tasks = answers.Select(Task.FromResult).ToArray();
        context.WaitForExternalEvent<DoseResponse>(Names.DoseResponseEvent, Arg.Any<CancellationToken>()).Returns(tasks[0], tasks[1..]);
    }

    private void UserNeverAnswers() =>
        context.WaitForExternalEvent<DoseResponse>(Names.DoseResponseEvent, Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<DoseResponse>().Task);

    [Fact]
    public async Task Answer_after_first_reminder_stops_escalation_and_continues_as_new()
    {
        AnswerTimersNeverElapse();
        UserAnswers(new DoseResponse(Due, DoseStatus.Taken));

        var outcome = await DoseReminderOrchestrator.RunAsync(context);

        outcome.ShouldBe("answered Taken");
        await context.Received(1).CreateTimer(Due.UtcDateTime, Arg.Any<CancellationToken>());
        await context.Received(1).CallActivityAsync<bool>(nameof(ReminderActivities.SendReminder), Arg.Any<object?>(), Arg.Any<TaskOptions?>());
        await context.DidNotReceive().CallActivityAsync<bool>(nameof(ReminderActivities.MarkMissed), Arg.Any<object?>(), Arg.Any<TaskOptions?>());
        context.Received(1).ContinueAsNew(new ReminderLoopInput("sch-a", Due), false);
    }

    [Fact]
    public async Task No_answer_escalates_then_marks_missed_then_continues_as_new()
    {
        AnswerTimersElapse();
        UserNeverAnswers();

        var outcome = await DoseReminderOrchestrator.RunAsync(context);

        outcome.ShouldBe("missed");
        await context.Received(1).CallActivityAsync<bool>(
            nameof(ReminderActivities.SendReminder), new ReminderRequest("sch-a", Due, false), Arg.Any<TaskOptions?>());
        await context.Received(1).CallActivityAsync<bool>(
            nameof(ReminderActivities.SendReminder), new ReminderRequest("sch-a", Due, true), Arg.Any<TaskOptions?>());
        await context.Received(1).CallActivityAsync<bool>(nameof(ReminderActivities.MarkMissed), new DoseRef("sch-a", Due), Arg.Any<TaskOptions?>());
        // Escalation after half the window, give-up at the end of it.
        await context.Received(1).CreateTimer(Due.UtcDateTime.AddMinutes(15), Arg.Any<CancellationToken>());
        await context.Received(1).CreateTimer(Due.UtcDateTime.AddMinutes(30), Arg.Any<CancellationToken>());
        context.Received(1).ContinueAsNew(new ReminderLoopInput("sch-a", Due), false);
    }

    [Fact]
    public async Task Answer_after_the_escalation_is_accepted_and_nothing_is_marked_missed()
    {
        // First wait times out (escalation), the second gets the answer before its timer.
        context.CreateTimer(Due.UtcDateTime.AddMinutes(15), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        context.CreateTimer(Due.UtcDateTime.AddMinutes(30), Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource().Task);
        context.WaitForExternalEvent<DoseResponse>(Names.DoseResponseEvent, Arg.Any<CancellationToken>())
            .Returns(new TaskCompletionSource<DoseResponse>().Task, Task.FromResult(new DoseResponse(Due, DoseStatus.Taken)));

        var outcome = await DoseReminderOrchestrator.RunAsync(context);

        outcome.ShouldBe("answered Taken");
        await context.Received(1).CallActivityAsync<bool>(
            nameof(ReminderActivities.SendReminder), new ReminderRequest("sch-a", Due, true), Arg.Any<TaskOptions?>());
        await context.DidNotReceive().CallActivityAsync<bool>(nameof(ReminderActivities.MarkMissed), Arg.Any<object?>(), Arg.Any<TaskOptions?>());
    }

    [Fact]
    public async Task Snooze_restarts_the_reminder_clock_after_a_durable_timer()
    {
        AnswerTimersElapse();
        context.WaitForExternalEvent<DoseResponse>(Names.DoseResponseEvent, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DoseResponse(Due, DoseStatus.Pending, 10)), new TaskCompletionSource<DoseResponse>().Task);

        var outcome = await DoseReminderOrchestrator.RunAsync(context);

        outcome.ShouldBe("missed");
        await context.Received(1).CreateTimer(Due.UtcDateTime.AddMinutes(10), Arg.Any<CancellationToken>());
        // After the snooze: remind again, escalate 15 minutes later, give up 30 minutes later.
        await context.Received(1).CreateTimer(Due.UtcDateTime.AddMinutes(25), Arg.Any<CancellationToken>());
        await context.Received(1).CreateTimer(Due.UtcDateTime.AddMinutes(40), Arg.Any<CancellationToken>());
        await context.Received(2).CallActivityAsync<bool>(
            nameof(ReminderActivities.SendReminder), new ReminderRequest("sch-a", Due, false), Arg.Any<TaskOptions?>());
    }

    [Fact]
    public async Task Taking_the_dose_while_snoozed_ends_the_turn_without_another_reminder()
    {
        AnswerTimersNeverElapse();
        context.WaitForExternalEvent<DoseResponse>(Names.DoseResponseEvent, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DoseResponse(Due, DoseStatus.Pending, 10)), Task.FromResult(new DoseResponse(Due, DoseStatus.Taken)));

        var outcome = await DoseReminderOrchestrator.RunAsync(context);

        outcome.ShouldBe("answered Taken");
        await context.Received(1).CallActivityAsync<bool>(nameof(ReminderActivities.SendReminder), Arg.Any<object?>(), Arg.Any<TaskOptions?>());
    }

    [Fact]
    public async Task Snoozing_more_than_the_limit_counts_as_missed()
    {
        AnswerTimersElapse();
        context.WaitForExternalEvent<DoseResponse>(Names.DoseResponseEvent, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DoseResponse(Due, DoseStatus.Pending, 5)));

        var outcome = await DoseReminderOrchestrator.RunAsync(context);

        outcome.ShouldBe("missed");
        await context.Received(DoseReminderOrchestrator.MaxSnoozes + 1).CallActivityAsync<bool>(
            nameof(ReminderActivities.SendReminder), new ReminderRequest("sch-a", Due, false), Arg.Any<TaskOptions?>());
        await context.Received(1).CallActivityAsync<bool>(nameof(ReminderActivities.MarkMissed), new DoseRef("sch-a", Due), Arg.Any<TaskOptions?>());
    }

    [Fact]
    public async Task Dose_answered_before_the_reminder_skips_waiting()
    {
        context.CallActivityAsync<bool>(nameof(ReminderActivities.SendReminder), Arg.Any<object?>(), Arg.Any<TaskOptions?>()).Returns(false);

        var outcome = await DoseReminderOrchestrator.RunAsync(context);

        outcome.ShouldBe("answered");
        _ = context.DidNotReceive().WaitForExternalEvent<DoseResponse>(Arg.Any<string>(), Arg.Any<CancellationToken>());
        context.Received(1).ContinueAsNew(Arg.Any<object?>(), false);
    }

    [Fact]
    public async Task A_stale_answer_for_another_dose_is_ignored()
    {
        AnswerTimersNeverElapse();
        UserAnswers(new DoseResponse(Due.AddDays(-1), DoseStatus.Taken), new DoseResponse(Due, DoseStatus.Skipped));

        var outcome = await DoseReminderOrchestrator.RunAsync(context);

        outcome.ShouldBe("answered Skipped");
    }

    [Fact]
    public async Task Ended_schedule_completes_the_orchestration()
    {
        context.CallActivityAsync<NextDose?>(nameof(ReminderActivities.GetNextDose), Arg.Any<object?>(), Arg.Any<TaskOptions?>()).Returns((NextDose?)null);

        var outcome = await DoseReminderOrchestrator.RunAsync(context);

        outcome.ShouldBe("schedule ended");
        context.DidNotReceive().ContinueAsNew(Arg.Any<object?>(), Arg.Any<bool>());
    }
}
