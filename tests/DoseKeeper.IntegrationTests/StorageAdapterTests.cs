using System.Text.Json;
using DoseKeeper.Application;
using DoseKeeper.Domain;
using DoseKeeper.Infrastructure;

namespace DoseKeeper.IntegrationTests;

[Collection(AzuriteGroup.Name)]
public sealed class StorageAdapterTests(AzuriteFixture azurite)
{
    private readonly CancellationToken ct = CancellationToken.None;
    private static readonly DateTimeOffset Due = new(2026, 7, 1, 5, 0, 0, TimeSpan.Zero);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 11)];

    [Fact]
    public async Task Medication_and_schedule_round_trip_through_table_storage()
    {
        var medications = new TableMedicationRepository(azurite.Tables);
        var schedules = new TableScheduleRepository(azurite.Tables);
        var med = Medication.Create(Unique("med"), "Metformin", "500 mg", 60, 7);
        var schedule = DoseSchedule.Create(
            Unique("sch"), med.Id, [new TimeOnly(8, 0), new TimeOnly(20, 0)], [DayOfWeek.Sunday, DayOfWeek.Wednesday],
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "Africa/Cairo", 2, TimeSpan.FromMinutes(45), "caregiver@example.com");

        await medications.SaveAsync(med, ct);
        await schedules.SaveAsync(schedule, ct);
        var loadedMed = await medications.GetAsync(med.Id, ct);
        var loaded = await schedules.GetAsync(schedule.Id, ct);

        loadedMed.ShouldNotBeNull().PillsOnHand.ShouldBe(60);
        loaded.ShouldNotBeNull();
        loaded.TimesOfDay.ShouldBe(schedule.TimesOfDay);
        loaded.DaysOfWeek.Order().ShouldBe([DayOfWeek.Sunday, DayOfWeek.Wednesday]);
        loaded.EndDate.ShouldBe(new DateOnly(2026, 12, 31));
        loaded.LateWindow.ShouldBe(TimeSpan.FromMinutes(45));
        loaded.OccurrencesBetween(Due.AddDays(-7), Due.AddDays(7)).ShouldBe(schedule.OccurrencesBetween(Due.AddDays(-7), Due.AddDays(7)));
        (await medications.GetAsync("med-missing", ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Dose_events_are_idempotent_per_due_time_and_range_queried_per_schedule()
    {
        var events = new TableDoseEventRepository(azurite.Tables);
        var scheduleId = Unique("sch");
        await events.SaveAsync(new DoseEvent(scheduleId, Due, DoseStatus.Missed, Due.AddMinutes(30)), ct);
        await events.SaveAsync(new DoseEvent(scheduleId, Due, DoseStatus.Taken, Due.AddMinutes(40)), ct);
        await events.SaveAsync(new DoseEvent(scheduleId, Due.AddHours(12), DoseStatus.Skipped, Due.AddHours(12)), ct);
        await events.SaveAsync(new DoseEvent(scheduleId, Due.AddDays(3), DoseStatus.Taken, Due.AddDays(3)), ct);

        var window = await events.ListAsync(scheduleId, Due, Due.AddDays(1), ct);

        window.Select(e => e.Status).ShouldBe([DoseStatus.Taken, DoseStatus.Skipped]);
        window[0].RecordedUtc.ShouldBe(Due.AddMinutes(40));
        (await events.GetAsync(scheduleId, Due.AddDays(3), ct)).ShouldNotBeNull().Status.ShouldBe(DoseStatus.Taken);
    }

    [Fact]
    public async Task Notification_goes_through_the_storage_queue_and_lands_once_in_the_log()
    {
        var queue = azurite.Queues.GetQueueClient(StorageNames.NotificationsQueue);
        await queue.ClearMessagesAsync(ct);
        var outbox = new StorageQueueNotificationOutbox(azurite.Queues);
        var log = new TableNotificationLog(azurite.Tables);
        var message = new NotificationMessage(Unique("ntf"), NotificationKind.CaregiverAlert, "caregiver@example.com", "A dose was not confirmed.", "sch-a", Due, DateTimeOffset.UtcNow);

        await outbox.EnqueueAsync(message, ct);
        var received = (await queue.ReceiveMessageAsync(cancellationToken: ct)).Value;
        var parsed = JsonSerializer.Deserialize<NotificationMessage>(received.Body.ToString(), Json.Options);
        await log.AddAsync(parsed!, ct);
        await log.AddAsync(parsed!, ct); // at-least-once delivery: the duplicate is absorbed

        parsed.ShouldBe(message);
        var recent = await log.RecentAsync(50, ct);
        recent.Count(n => n.Id == message.Id).ShouldBe(1);
    }

    [Fact]
    public async Task Notification_log_returns_newest_first()
    {
        var log = new TableNotificationLog(azurite.Tables);
        var older = new NotificationMessage(Unique("ntf"), NotificationKind.Reminder, "user", "older", null, null, DateTimeOffset.UtcNow.AddMinutes(-5));
        var newer = older with { Id = Unique("ntf"), Text = "newer", CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(5) };

        await log.AddAsync(older, ct);
        await log.AddAsync(newer, ct);

        (await log.RecentAsync(1, ct)).ShouldHaveSingleItem().Text.ShouldBe("newer");
    }

    [Fact]
    public async Task Missed_dose_flow_runs_against_real_storage()
    {
        var medications = new TableMedicationRepository(azurite.Tables);
        var schedules = new TableScheduleRepository(azurite.Tables);
        var events = new TableDoseEventRepository(azurite.Tables);
        var queue = azurite.Queues.GetQueueClient(StorageNames.NotificationsQueue);
        await queue.ClearMessagesAsync(ct);
        var med = Medication.Create(Unique("med"), "Vitamin D", "1000 IU", 30, 5);
        var schedule = DoseSchedule.Create(Unique("sch"), med.Id, [new TimeOnly(8, 0)], null, new DateOnly(2026, 1, 1), null, null, 1, TimeSpan.FromMinutes(30), "caregiver@example.com");
        await medications.SaveAsync(med, ct);
        await schedules.SaveAsync(schedule, ct);
        var reminders = new ReminderService(medications, schedules, events, new StorageQueueNotificationOutbox(azurite.Queues), TimeProvider.System);

        (await reminders.MarkMissedAsync(schedule.Id, Due, ct)).ShouldBeTrue();
        (await reminders.MarkMissedAsync(schedule.Id, Due, ct)).ShouldBeFalse();

        (await events.GetAsync(schedule.Id, Due, ct)).ShouldNotBeNull().Status.ShouldBe(DoseStatus.Missed);
        var messages = (await queue.ReceiveMessagesAsync(maxMessages: 10, cancellationToken: ct)).Value;
        messages.Select(m => JsonSerializer.Deserialize<NotificationMessage>(m.Body.ToString(), Json.Options)!.Kind)
            .ShouldBe([NotificationKind.Missed, NotificationKind.CaregiverAlert], ignoreOrder: true);
    }

    [Fact]
    public async Task Adherence_report_store_returns_the_latest_report()
    {
        var store = new TableAdherenceReportStore(azurite.Tables);
        var week = new DateOnly(2026, 1, 10);
        var part = new ScheduleAdherence("sch-a", "Metformin", new Adherence(4, 3, 1, 0, 1));

        await store.SaveAsync(new WeeklyAdherenceReport(week, [part], 75m, DateTimeOffset.UtcNow.AddDays(-7)), ct);
        await store.SaveAsync(new WeeklyAdherenceReport(week.AddDays(7), [part], 80m, DateTimeOffset.UtcNow), ct);

        var latest = await store.LatestAsync(ct);
        latest.ShouldNotBeNull().OverallPercent.ShouldBe(80m);
        latest.Schedules.ShouldHaveSingleItem().Adherence.ShouldBe(part.Adherence);
    }
}
