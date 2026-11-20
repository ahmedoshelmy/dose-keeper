using Microsoft.Extensions.Time.Testing;

namespace DoseKeeper.Application.UnitTests;

public sealed class ReminderLoopRepairTests
{
    // Saturday 2026-01-10 07:00 UTC = 09:00 in Cairo.
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 1, 10, 7, 0, 0, TimeSpan.Zero));
    private readonly InMemoryStore store = new();
    private readonly FakeLoops loops = new();
    private readonly CancellationToken ct = CancellationToken.None;

    private ReminderLoopRepairService Repair => new(store, clock);

    private async Task<string> ScheduleAsync(DateOnly? endDate = null)
    {
        var med = await new MedicationService(store, store, clock).CreateAsync(new NewMedication("Vitamin D", "1000 IU", 30, 7), ct);
        var schedule = await new ScheduleService(store, store, clock).CreateAsync(
            new NewSchedule(med.Id, ["08:00"], null, new DateOnly(2026, 1, 1), endDate, null, 1, 30, null), ct);
        return schedule.Id;
    }

    [Fact]
    public async Task Starts_missing_and_broken_loops_from_now_and_leaves_live_ones_alone()
    {
        var missing = await ScheduleAsync();
        var failed = await ScheduleAsync();
        var running = await ScheduleAsync();
        var completed = await ScheduleAsync();
        loops.States[failed] = ReminderLoopState.Broken;
        loops.States[running] = ReminderLoopState.Running;
        loops.States[completed] = ReminderLoopState.Completed;

        var result = await Repair.RunAsync(loops, ct);

        result.Checked.ShouldBe(4);
        result.Restarted.ShouldBe([missing, failed], ignoreOrder: true);
        result.Failed.ShouldBeEmpty();
        loops.Started.ShouldBe([(missing, clock.GetUtcNow()), (failed, clock.GetUtcNow())], ignoreOrder: true);
    }

    [Fact]
    public async Task Is_idempotent_a_second_run_starts_nothing()
    {
        await ScheduleAsync();

        (await Repair.RunAsync(loops, ct)).Restarted.Count.ShouldBe(1);
        (await Repair.RunAsync(loops, ct)).Restarted.ShouldBeEmpty();

        loops.Started.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Skips_schedules_that_have_ended()
    {
        await ScheduleAsync(endDate: new DateOnly(2026, 1, 5));

        var result = await Repair.RunAsync(loops, ct);

        result.Restarted.ShouldBeEmpty();
        loops.Started.ShouldBeEmpty();
    }

    [Fact]
    public async Task One_failing_start_does_not_stop_the_others()
    {
        var bad = await ScheduleAsync();
        var good = await ScheduleAsync();
        loops.FailFor.Add(bad);

        var result = await Repair.RunAsync(loops, ct);

        result.Restarted.ShouldBe([good]);
        result.Failed.ShouldBe([bad]);
    }

    private sealed class FakeLoops : IReminderLoops
    {
        public Dictionary<string, ReminderLoopState> States { get; } = [];
        public List<(string ScheduleId, DateTimeOffset AfterUtc)> Started { get; } = [];
        public HashSet<string> FailFor { get; } = [];

        public Task<ReminderLoopState> GetStateAsync(string scheduleId, CancellationToken ct) =>
            Task.FromResult(States.GetValueOrDefault(scheduleId, ReminderLoopState.Missing));

        public Task StartAsync(string scheduleId, DateTimeOffset afterUtc, CancellationToken ct)
        {
            if (FailFor.Contains(scheduleId))
            {
                throw new InvalidOperationException("task hub unavailable");
            }

            Started.Add((scheduleId, afterUtc));
            States[scheduleId] = ReminderLoopState.Running;
            return Task.CompletedTask;
        }
    }
}
