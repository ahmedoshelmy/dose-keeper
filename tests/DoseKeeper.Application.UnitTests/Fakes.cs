using DoseKeeper.Domain;

namespace DoseKeeper.Application.UnitTests;

internal sealed class InMemoryStore : IMedicationRepository, IScheduleRepository, IDoseEventRepository, INotificationOutbox, IAdherenceReportStore
{
    private readonly Dictionary<string, Medication> medications = [];
    private readonly Dictionary<string, DoseSchedule> schedules = [];
    private readonly Dictionary<(string, DateTimeOffset), DoseEvent> events = [];

    public List<NotificationMessage> Sent { get; } = [];
    public List<WeeklyAdherenceReport> Reports { get; } = [];

    Task<Medication?> IMedicationRepository.GetAsync(string id, CancellationToken ct) => Task.FromResult(medications.GetValueOrDefault(id));
    Task<IReadOnlyList<Medication>> IMedicationRepository.ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Medication>>([.. medications.Values]);
    public Task SaveAsync(Medication medication, CancellationToken ct) => Task.FromResult(medications[medication.Id] = medication);

    Task<DoseSchedule?> IScheduleRepository.GetAsync(string id, CancellationToken ct) => Task.FromResult(schedules.GetValueOrDefault(id));
    Task<IReadOnlyList<DoseSchedule>> IScheduleRepository.ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<DoseSchedule>>([.. schedules.Values]);
    public Task SaveAsync(DoseSchedule schedule, CancellationToken ct) => Task.FromResult(schedules[schedule.Id] = schedule);

    public Task<DoseEvent?> GetAsync(string scheduleId, DateTimeOffset dueUtc, CancellationToken ct) =>
        Task.FromResult(events.GetValueOrDefault((scheduleId, dueUtc)));

    public Task<IReadOnlyList<DoseEvent>> ListAsync(string scheduleId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DoseEvent>>([.. events.Values.Where(e => e.ScheduleId == scheduleId && e.DueUtc >= fromUtc && e.DueUtc < toUtc)]);

    public Task SaveAsync(DoseEvent doseEvent, CancellationToken ct) => Task.FromResult(events[(doseEvent.ScheduleId, doseEvent.DueUtc)] = doseEvent);

    public Task EnqueueAsync(NotificationMessage message, CancellationToken ct)
    {
        Sent.Add(message);
        return Task.CompletedTask;
    }

    public Task SaveAsync(WeeklyAdherenceReport report, CancellationToken ct)
    {
        Reports.Add(report);
        return Task.CompletedTask;
    }

    public Task<WeeklyAdherenceReport?> LatestAsync(CancellationToken ct) => Task.FromResult(Reports.LastOrDefault());

    public Medication Medication(string id) => medications[id];
}
