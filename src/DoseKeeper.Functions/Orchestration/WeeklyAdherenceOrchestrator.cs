using DoseKeeper.Application;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;

namespace DoseKeeper.Functions.Orchestration;

/// <summary>Fan-out/fan-in: one activity per schedule computes its week in parallel, then one activity saves the report.</summary>
public static class WeeklyAdherenceOrchestrator
{
    [Function(nameof(WeeklyAdherenceOrchestrator))]
    public static async Task<WeeklyAdherenceReport> RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<WeeklyReportInput>() ?? throw new InvalidOperationException("Missing orchestration input.");
        var scheduleIds = await context.CallActivityAsync<List<string>>(nameof(AdherenceActivities.ListScheduleIds), input);
        var parts = await Task.WhenAll(scheduleIds.Select(id =>
            context.CallActivityAsync<ScheduleAdherence?>(nameof(AdherenceActivities.ComputeScheduleAdherence), new ScheduleWeek(id, input.WeekStart))));
        var report = new WeeklyReportParts(input.WeekStart, [.. parts.OfType<ScheduleAdherence>()]);
        return await context.CallActivityAsync<WeeklyAdherenceReport>(nameof(AdherenceActivities.SaveAdherenceReport), report);
    }
}

public sealed record WeeklyReportParts(DateOnly WeekStart, List<ScheduleAdherence> Parts);

public sealed class AdherenceActivities(AdherenceService adherence)
{
    [Function(nameof(ListScheduleIds))]
    public async Task<List<string>> ListScheduleIds([ActivityTrigger] WeeklyReportInput input, CancellationToken ct) =>
        [.. await adherence.ScheduleIdsAsync(ct)];

    [Function(nameof(ComputeScheduleAdherence))]
    public Task<ScheduleAdherence?> ComputeScheduleAdherence([ActivityTrigger] ScheduleWeek request, CancellationToken ct) =>
        adherence.ForScheduleAsync(request.ScheduleId, request.WeekStart, ct);

    [Function(nameof(SaveAdherenceReport))]
    public Task<WeeklyAdherenceReport> SaveAdherenceReport([ActivityTrigger] WeeklyReportParts report, CancellationToken ct) =>
        adherence.SaveReportAsync(report.WeekStart, report.Parts, ct);
}
