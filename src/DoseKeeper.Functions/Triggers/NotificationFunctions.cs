using System.Text.Json;
using Azure.Data.Tables;
using DoseKeeper.Application;
using DoseKeeper.Infrastructure;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DoseKeeper.Functions.Triggers;

/// <summary>
/// Drains the notifications queue. Delivery is a stub that writes to the notifications table the web page reads; a real
/// push/SMS/email sender would plug in here. A message that fails 3 times (host.json maxDequeueCount) is moved by the
/// runtime to "notifications-poison", where <see cref="RecordPoisonNotification"/> parks it for a human.
/// </summary>
public sealed class NotificationFunctions(INotificationLog log, TableServiceClient tables, ILogger<NotificationFunctions> logger)
{
    public const string DeadLettersTable = "deadletters";

    [Function(nameof(DeliverNotification))]
    public async Task DeliverNotification([QueueTrigger(StorageNames.NotificationsQueue)] string body, CancellationToken ct)
    {
        var message = Parse(body);
        logger.LogInformation("Delivering {Kind} to {Recipient}: {Text}", message.Kind, message.Recipient, message.Text);
        await log.AddAsync(message, ct);
    }

    [Function(nameof(RecordPoisonNotification))]
    public async Task RecordPoisonNotification(
        [QueueTrigger(StorageNames.NotificationsQueue + StorageNames.PoisonSuffix)] string body, CancellationToken ct)
    {
        logger.LogError("Notification moved to poison queue after repeated failures: {Body}", body);
        var table = tables.GetTableClient(DeadLettersTable);
        await table.CreateIfNotExistsAsync(ct);
        await table.AddEntityAsync(new TableEntity("poison", Guid.NewGuid().ToString("N")) { ["Body"] = body }, ct);
    }

    /// <summary>Throws on anything that is not a complete notification, so the runtime retries and then poisons it.</summary>
    internal static NotificationMessage Parse(string body)
    {
        var message = JsonSerializer.Deserialize<NotificationMessage>(body, Json.Options);
        return message is { Id.Length: > 0, Recipient.Length: > 0, Text.Length: > 0 }
            ? message
            : throw new InvalidOperationException("Notification payload is incomplete.");
    }
}
