using Azure.Data.Tables;
using Azure.Storage.Queues;
using DoseKeeper.Application;
using Microsoft.Extensions.DependencyInjection;

namespace DoseKeeper.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers Table + Queue adapters on one storage account (Azurite locally).</summary>
    public static IServiceCollection AddDoseKeeperStorage(this IServiceCollection services, string connectionString)
    {
        // Base64 keeps the payload compatible with the Functions queue trigger's default encoding.
        services.AddSingleton(new QueueServiceClient(connectionString, new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 }));
        services.AddSingleton(new TableServiceClient(connectionString));
        services.AddSingleton<IMedicationRepository, TableMedicationRepository>();
        services.AddSingleton<IScheduleRepository, TableScheduleRepository>();
        services.AddSingleton<IDoseEventRepository, TableDoseEventRepository>();
        services.AddSingleton<INotificationLog, TableNotificationLog>();
        services.AddSingleton<IAdherenceReportStore, TableAdherenceReportStore>();
        services.AddSingleton<INotificationOutbox, StorageQueueNotificationOutbox>();
        return services;
    }

    /// <summary>Creates tables and queues if missing; idempotent, run once at start-up.</summary>
    public static async Task EnsureStorageAsync(TableServiceClient tables, QueueServiceClient queues, CancellationToken ct)
    {
        foreach (var name in new[]
                 {
                     StorageNames.MedicationsTable, StorageNames.SchedulesTable, StorageNames.DoseEventsTable,
                     StorageNames.NotificationsTable, StorageNames.ReportsTable,
                 })
        {
            await tables.CreateTableIfNotExistsAsync(name, ct);
        }

        await queues.GetQueueClient(StorageNames.NotificationsQueue).CreateIfNotExistsAsync(cancellationToken: ct);
    }
}
