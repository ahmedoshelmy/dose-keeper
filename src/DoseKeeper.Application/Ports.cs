using DoseKeeper.Domain;

namespace DoseKeeper.Application;

public interface IMedicationRepository
{
    Task<Medication?> GetAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<Medication>> ListAsync(CancellationToken ct);
    Task SaveAsync(Medication medication, CancellationToken ct);
}

public interface IScheduleRepository
{
    Task<DoseSchedule?> GetAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<DoseSchedule>> ListAsync(CancellationToken ct);
    Task SaveAsync(DoseSchedule schedule, CancellationToken ct);
}

public interface IDoseEventRepository
{
    Task<DoseEvent?> GetAsync(string scheduleId, DateTimeOffset dueUtc, CancellationToken ct);
    Task<IReadOnlyList<DoseEvent>> ListAsync(string scheduleId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);
    Task SaveAsync(DoseEvent doseEvent, CancellationToken ct);
}

/// <summary>Hands a notification to the delivery pipeline (a storage queue in the Functions app).</summary>
public interface INotificationOutbox
{
    Task EnqueueAsync(NotificationMessage message, CancellationToken ct);
}

/// <summary>Delivered notifications, readable by the web page (the "inbox" a push/SMS provider would replace).</summary>
public interface INotificationLog
{
    Task AddAsync(NotificationMessage message, CancellationToken ct);
    Task<IReadOnlyList<NotificationMessage>> RecentAsync(int take, CancellationToken ct);
}

public interface IAdherenceReportStore
{
    Task SaveAsync(WeeklyAdherenceReport report, CancellationToken ct);
    Task<WeeklyAdherenceReport?> LatestAsync(CancellationToken ct);
}
