using System.Globalization;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using DoseKeeper.Application;
using DoseKeeper.Domain;

namespace DoseKeeper.Infrastructure;

/// <summary>Azure Table Storage adapters. Entities are flat property bags; the domain never sees them.</summary>
public sealed class TableMedicationRepository(TableServiceClient service) : IMedicationRepository
{
    private const string Partition = "med";
    private readonly TableClient table = service.GetTableClient(StorageNames.MedicationsTable);

    public async Task<Medication?> GetAsync(string id, CancellationToken ct)
    {
        var response = await table.GetEntityIfExistsAsync<TableEntity>(Partition, id, cancellationToken: ct);
        return response.HasValue ? Map(response.Value!) : null;
    }

    public async Task<IReadOnlyList<Medication>> ListAsync(CancellationToken ct)
    {
        var list = new List<Medication>();
        await foreach (var entity in table.QueryAsync<TableEntity>(e => e.PartitionKey == Partition, cancellationToken: ct))
        {
            list.Add(Map(entity));
        }

        return [.. list.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public Task SaveAsync(Medication medication, CancellationToken ct) => table.UpsertEntityAsync(
        new TableEntity(Partition, medication.Id)
        {
            ["Name"] = medication.Name,
            ["Strength"] = medication.Strength,
            ["PillsOnHand"] = medication.PillsOnHand,
            ["RefillThresholdDays"] = medication.RefillThresholdDays,
        },
        TableUpdateMode.Replace,
        ct);

    private static Medication Map(TableEntity e) => Medication.Create(
        e.RowKey, e.GetString("Name"), e.GetString("Strength") ?? string.Empty, e.GetInt32("PillsOnHand") ?? 0, e.GetInt32("RefillThresholdDays") ?? 0);
}

public sealed class TableScheduleRepository(TableServiceClient service) : IScheduleRepository
{
    private const string Partition = "sch";
    private readonly TableClient table = service.GetTableClient(StorageNames.SchedulesTable);

    public async Task<DoseSchedule?> GetAsync(string id, CancellationToken ct)
    {
        var response = await table.GetEntityIfExistsAsync<TableEntity>(Partition, id, cancellationToken: ct);
        return response.HasValue ? Map(response.Value!) : null;
    }

    public async Task<IReadOnlyList<DoseSchedule>> ListAsync(CancellationToken ct)
    {
        var list = new List<DoseSchedule>();
        await foreach (var entity in table.QueryAsync<TableEntity>(e => e.PartitionKey == Partition, cancellationToken: ct))
        {
            list.Add(Map(entity));
        }

        return list;
    }

    public Task SaveAsync(DoseSchedule schedule, CancellationToken ct) => table.UpsertEntityAsync(
        new TableEntity(Partition, schedule.Id)
        {
            ["MedicationId"] = schedule.MedicationId,
            ["TimesOfDay"] = string.Join(',', schedule.TimesOfDay.Select(t => t.ToString("HH:mm", CultureInfo.InvariantCulture))),
            ["DaysOfWeek"] = string.Join(',', schedule.DaysOfWeek.Order().Select(d => (int)d)),
            ["StartDate"] = schedule.StartDate.ToString("O", CultureInfo.InvariantCulture),
            ["EndDate"] = schedule.EndDate?.ToString("O", CultureInfo.InvariantCulture),
            ["TimeZone"] = schedule.TimeZone.Id,
            ["DosePerIntake"] = schedule.DosePerIntake,
            ["LateWindowMinutes"] = (int)schedule.LateWindow.TotalMinutes,
            ["CaregiverContact"] = schedule.CaregiverContact,
        },
        TableUpdateMode.Replace,
        ct);

    private static DoseSchedule Map(TableEntity e) => DoseSchedule.Create(
        e.RowKey,
        e.GetString("MedicationId"),
        e.GetString("TimesOfDay").Split(',').Select(t => TimeOnly.ParseExact(t, "HH:mm", CultureInfo.InvariantCulture)),
        e.GetString("DaysOfWeek").Split(',').Select(d => (DayOfWeek)int.Parse(d, CultureInfo.InvariantCulture)),
        DateOnly.ParseExact(e.GetString("StartDate"), "O", CultureInfo.InvariantCulture),
        e.GetString("EndDate") is { Length: > 0 } end ? DateOnly.ParseExact(end, "O", CultureInfo.InvariantCulture) : null,
        e.GetString("TimeZone"),
        e.GetInt32("DosePerIntake") ?? 1,
        TimeSpan.FromMinutes(e.GetInt32("LateWindowMinutes") ?? 30),
        e.GetString("CaregiverContact"));
}

/// <summary>Partition = schedule, row = due instant as sortable UTC text, so a schedule's week is one range query.</summary>
public sealed class TableDoseEventRepository(TableServiceClient service) : IDoseEventRepository
{
    private readonly TableClient table = service.GetTableClient(StorageNames.DoseEventsTable);

    public static string RowKey(DateTimeOffset dueUtc) => dueUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    public async Task<DoseEvent?> GetAsync(string scheduleId, DateTimeOffset dueUtc, CancellationToken ct)
    {
        var response = await table.GetEntityIfExistsAsync<TableEntity>(scheduleId, RowKey(dueUtc), cancellationToken: ct);
        return response.HasValue ? Map(response.Value!) : null;
    }

    public async Task<IReadOnlyList<DoseEvent>> ListAsync(string scheduleId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var from = RowKey(fromUtc);
        var to = RowKey(toUtc);
        var list = new List<DoseEvent>();
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {scheduleId} and RowKey ge {from} and RowKey lt {to}");
        await foreach (var entity in table.QueryAsync<TableEntity>(filter, cancellationToken: ct))
        {
            list.Add(Map(entity));
        }

        return list;
    }

    public Task SaveAsync(DoseEvent doseEvent, CancellationToken ct) => table.UpsertEntityAsync(
        new TableEntity(doseEvent.ScheduleId, RowKey(doseEvent.DueUtc))
        {
            ["DueUtc"] = doseEvent.DueUtc.UtcDateTime,
            ["Status"] = doseEvent.Status.ToString(),
            ["RecordedUtc"] = doseEvent.RecordedUtc.UtcDateTime,
        },
        TableUpdateMode.Replace,
        ct);

    private static DoseEvent Map(TableEntity e) => new(
        e.PartitionKey,
        e.GetDateTimeOffset("DueUtc") ?? DateTimeOffset.MinValue,
        Enum.Parse<DoseStatus>(e.GetString("Status")),
        e.GetDateTimeOffset("RecordedUtc") ?? DateTimeOffset.MinValue);
}

/// <summary>Newest first: the row key is the inverted timestamp, so a plain query returns the latest rows.</summary>
public sealed class TableNotificationLog(TableServiceClient service) : INotificationLog
{
    private const string Partition = "ntf";
    private readonly TableClient table = service.GetTableClient(StorageNames.NotificationsTable);

    public async Task AddAsync(NotificationMessage message, CancellationToken ct)
    {
        var rowKey = $"{DateTimeOffset.MaxValue.UtcTicks - message.CreatedUtc.UtcTicks:D19}-{message.Id}";
        try
        {
            await table.AddEntityAsync(new TableEntity(Partition, rowKey) { ["Json"] = JsonSerializer.Serialize(message, Json.Options) }, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // Already delivered: queue messages are at-least-once, the log stays exactly-once.
        }
    }

    public async Task<IReadOnlyList<NotificationMessage>> RecentAsync(int take, CancellationToken ct)
    {
        var list = new List<NotificationMessage>();
        await foreach (var entity in table.QueryAsync<TableEntity>(e => e.PartitionKey == Partition, maxPerPage: take, cancellationToken: ct))
        {
            list.Add(JsonSerializer.Deserialize<NotificationMessage>(entity.GetString("Json"), Json.Options)!);
            if (list.Count >= take)
            {
                break;
            }
        }

        return list;
    }
}

public sealed class TableAdherenceReportStore(TableServiceClient service) : IAdherenceReportStore
{
    private const string Partition = "report";
    private readonly TableClient table = service.GetTableClient(StorageNames.ReportsTable);

    public Task SaveAsync(WeeklyAdherenceReport report, CancellationToken ct) => table.UpsertEntityAsync(
        new TableEntity(Partition, $"{DateTimeOffset.MaxValue.UtcTicks - report.CreatedUtc.UtcTicks:D19}")
        {
            ["Json"] = JsonSerializer.Serialize(report, Json.Options),
        },
        TableUpdateMode.Replace,
        ct);

    public async Task<WeeklyAdherenceReport?> LatestAsync(CancellationToken ct)
    {
        await foreach (var entity in table.QueryAsync<TableEntity>(e => e.PartitionKey == Partition, maxPerPage: 1, cancellationToken: ct))
        {
            return JsonSerializer.Deserialize<WeeklyAdherenceReport>(entity.GetString("Json"), Json.Options);
        }

        return null;
    }
}
