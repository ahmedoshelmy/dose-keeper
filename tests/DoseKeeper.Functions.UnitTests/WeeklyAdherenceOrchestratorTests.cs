using DoseKeeper.Application;
using DoseKeeper.Domain;
using DoseKeeper.Functions.Orchestration;
using Microsoft.DurableTask;
using NSubstitute;

namespace DoseKeeper.Functions.UnitTests;

public sealed class WeeklyAdherenceOrchestratorTests
{
    [Fact]
    public async Task Fans_out_one_activity_per_schedule_and_fans_in_to_one_report()
    {
        var week = new DateOnly(2026, 1, 10);
        var context = Substitute.For<TaskOrchestrationContext>();
        context.GetInput<WeeklyReportInput>().Returns(new WeeklyReportInput(week));
        context.CallActivityAsync<List<string>>(nameof(AdherenceActivities.ListScheduleIds), Arg.Any<object?>(), Arg.Any<TaskOptions?>())
            .Returns(["sch-a", "sch-b", "sch-gone"]);
        context.CallActivityAsync<ScheduleAdherence?>(nameof(AdherenceActivities.ComputeScheduleAdherence), Arg.Any<object?>(), Arg.Any<TaskOptions?>())
            .Returns(
                call => ((ScheduleWeek)call[1]!).ScheduleId == "sch-gone"
                    ? null
                    : new ScheduleAdherence(((ScheduleWeek)call[1]!).ScheduleId, "Metformin", new Adherence(2, 1, 0, 0, 1)));
        WeeklyReportParts? saved = null;
        context.CallActivityAsync<WeeklyAdherenceReport>(nameof(AdherenceActivities.SaveAdherenceReport), Arg.Do<object?>(p => saved = (WeeklyReportParts)p!), Arg.Any<TaskOptions?>())
            .Returns(new WeeklyAdherenceReport(week, [], 50m, DateTimeOffset.UnixEpoch));

        var report = await WeeklyAdherenceOrchestrator.RunAsync(context);

        report.OverallPercent.ShouldBe(50m);
        await context.Received(3).CallActivityAsync<ScheduleAdherence?>(nameof(AdherenceActivities.ComputeScheduleAdherence), Arg.Any<object?>(), Arg.Any<TaskOptions?>());
        saved.ShouldNotBeNull();
        saved.Parts.Select(p => p.ScheduleId).ShouldBe(["sch-a", "sch-b"]);
    }
}
