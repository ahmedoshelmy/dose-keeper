using DoseKeeper.Domain;
using Microsoft.Extensions.Time.Testing;

namespace DoseKeeper.Application.UnitTests;

public sealed class ServiceTests
{
    // Saturday 2026-01-10 07:00 UTC = 09:00 in Cairo.
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 1, 10, 7, 0, 0, TimeSpan.Zero));
    private readonly InMemoryStore store = new();
    private readonly CancellationToken ct = CancellationToken.None;

    private MedicationService Medications => new(store, store, clock);
    private ScheduleService Schedules => new(store, store, clock);
    private DoseService Doses => new(store, store, store, clock);
    private ReminderService Reminders => new(store, store, store, store, clock);
    private AdherenceService Adherence => new(store, store, store, store, clock);

    private async Task<DoseSchedule> SeedAsync(int pills = 20, string? caregiver = "caregiver@example.com")
    {
        var med = await Medications.CreateAsync(new NewMedication("Metformin", "500 mg", pills, 7), ct);
        return await Schedules.CreateAsync(new NewSchedule(med.Id, ["08:00", "20:00"], null, null, null, null, 1, 30, caregiver), ct);
    }

    private static readonly DateTimeOffset Morning = new(2026, 1, 10, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task New_schedule_starts_today_in_the_users_time_zone()
    {
        var schedule = await SeedAsync();

        schedule.StartDate.ShouldBe(new DateOnly(2026, 1, 10));
        schedule.TimeZone.Id.ShouldBe("Africa/Cairo");
    }

    [Fact]
    public async Task Schedule_for_an_unknown_medication_is_not_found()
    {
        await Should.ThrowAsync<NotFoundException>(() =>
            Schedules.CreateAsync(new NewSchedule("med-missing", ["08:00"], null, null, null, null, 1, 30, null), ct));
    }

    [Fact]
    public async Task Bad_time_of_day_is_a_domain_error()
    {
        var med = await Medications.CreateAsync(new NewMedication("Metformin", "500 mg", 5, 7), ct);

        await Should.ThrowAsync<DomainException>(() =>
            Schedules.CreateAsync(new NewSchedule(med.Id, ["8 pm"], null, null, null, null, 1, 30, null), ct));
    }

    [Fact]
    public async Task Today_lists_doses_in_local_time_with_their_status()
    {
        var schedule = await SeedAsync();
        await Doses.AnswerAsync(schedule.Id, Morning, DoseStatus.Taken, ct);

        var today = await Doses.TodayAsync(ct);

        today.Select(d => (d.LocalTime, d.Status)).ShouldBe([("08:00", DoseStatus.Taken), ("20:00", DoseStatus.Pending)]);
        today[0].MedicationName.ShouldBe("Metformin 500 mg");
    }

    [Fact]
    public async Task Taking_a_dose_draws_stock_once_even_if_answered_twice()
    {
        var schedule = await SeedAsync(pills: 20);

        await Doses.AnswerAsync(schedule.Id, Morning, DoseStatus.Taken, ct);
        await Doses.AnswerAsync(schedule.Id, Morning, DoseStatus.Taken, ct);

        store.Medication(schedule.MedicationId).PillsOnHand.ShouldBe(19);
    }

    [Fact]
    public async Task Answering_a_time_that_is_not_scheduled_is_rejected()
    {
        var schedule = await SeedAsync();

        await Should.ThrowAsync<DomainException>(() => Doses.AnswerAsync(schedule.Id, Morning.AddMinutes(7), DoseStatus.Taken, ct));
    }

    [Fact]
    public async Task Missed_cannot_be_answered_with_missed_by_the_user()
    {
        var schedule = await SeedAsync();

        await Should.ThrowAsync<DomainException>(() => Doses.AnswerAsync(schedule.Id, Morning, DoseStatus.Missed, ct));
    }

    [Fact]
    public async Task Next_dose_carries_the_escalation_timings()
    {
        var schedule = await SeedAsync();

        var next = await Reminders.NextDoseAsync(schedule.Id, clock.GetUtcNow(), ct);

        next.ShouldBe(new NextDose(schedule.Id, new DateTimeOffset(2026, 1, 10, 18, 0, 0, TimeSpan.Zero), 900, 1800));
    }

    [Fact]
    public async Task Reminder_is_queued_for_an_open_dose_and_suppressed_for_an_answered_one()
    {
        var schedule = await SeedAsync();

        (await Reminders.RemindAsync(schedule.Id, Morning, escalation: false, ct)).ShouldBeTrue();
        await Doses.AnswerAsync(schedule.Id, Morning, DoseStatus.Skipped, ct);
        (await Reminders.RemindAsync(schedule.Id, Morning, escalation: true, ct)).ShouldBeFalse();

        store.Sent.ShouldHaveSingleItem().Kind.ShouldBe(NotificationKind.Reminder);
        store.Sent[0].Text.ShouldContain("08:00");
    }

    [Fact]
    public async Task Missed_dose_is_recorded_and_the_caregiver_is_alerted()
    {
        var schedule = await SeedAsync();

        (await Reminders.MarkMissedAsync(schedule.Id, Morning, ct)).ShouldBeTrue();

        (await store.GetAsync(schedule.Id, Morning, ct))!.Status.ShouldBe(DoseStatus.Missed);
        store.Sent.Select(s => (s.Kind, s.Recipient)).ShouldBe([(NotificationKind.Missed, "user"), (NotificationKind.CaregiverAlert, "caregiver@example.com")]);
    }

    [Fact]
    public async Task Missed_never_overrides_a_dose_the_user_already_took()
    {
        var schedule = await SeedAsync();
        await Doses.AnswerAsync(schedule.Id, Morning, DoseStatus.Taken, ct);

        (await Reminders.MarkMissedAsync(schedule.Id, Morning, ct)).ShouldBeFalse();
        store.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Refill_check_queues_a_notice_only_for_low_stock()
    {
        await SeedAsync(pills: 10);
        var plenty = await Medications.CreateAsync(new NewMedication("Vitamin D", "1000 IU", 500, 7), ct);
        await Schedules.CreateAsync(new NewSchedule(plenty.Id, ["09:00"], null, null, null, null, 1, 30, null), ct);

        var queued = await new RefillCheckService(Medications, Reminders, store).RunAsync(ct);

        queued.ShouldBe(1);
        store.Sent.ShouldHaveSingleItem().Text.ShouldStartWith("Refill Metformin: 10 pill(s) left, about 5 day(s)");
    }

    [Fact]
    public async Task Weekly_report_combines_schedule_parts()
    {
        // Taken at 07:00 UTC for the 06:00 dose: an hour late against a 30-minute window.
        var schedule = await SeedAsync();
        await Doses.AnswerAsync(schedule.Id, Morning, DoseStatus.Taken, ct);
        clock.Advance(TimeSpan.FromDays(1));
        var weekStart = AdherenceService.WeekStartOf(new DateOnly(2026, 1, 10));

        var part = await Adherence.ForScheduleAsync(schedule.Id, weekStart, ct);
        var report = await Adherence.SaveReportAsync(weekStart, [part!], ct);

        part!.Adherence.ShouldBe(new Adherence(Due: 3, Taken: 1, Late: 1, Skipped: 0, Missed: 2));
        report.OverallPercent.ShouldBe(33.3m);
        (await Adherence.LatestReportAsync(ct)).ShouldBe(report);
    }

    [Theory]
    [InlineData("2026-01-10", "2026-01-10")]
    [InlineData("2026-01-16", "2026-01-10")]
    [InlineData("2026-01-17", "2026-01-17")]
    public void Weeks_start_on_saturday(string date, string expected)
    {
        AdherenceService.WeekStartOf(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }
}
