using System.Text.Json;
using Azure.Storage.Queues;
using DoseKeeper.Application;

namespace DoseKeeper.Infrastructure;

/// <summary>Sends notifications to the storage queue that the queue-triggered function drains.</summary>
public sealed class StorageQueueNotificationOutbox(QueueServiceClient service) : INotificationOutbox
{
    private readonly QueueClient queue = service.GetQueueClient(StorageNames.NotificationsQueue);

    public async Task EnqueueAsync(NotificationMessage message, CancellationToken ct)
    {
        await queue.SendMessageAsync(JsonSerializer.Serialize(message, Json.Options), ct);
    }
}
