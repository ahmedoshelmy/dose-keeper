using Azure.Data.Tables;
using Azure.Storage.Queues;
using DoseKeeper.Infrastructure;
using Testcontainers.Azurite;

namespace DoseKeeper.IntegrationTests;

/// <summary>One Azurite container (Blob/Queue/Table emulator) shared by the tests in the collection.</summary>
public sealed class AzuriteFixture : IAsyncLifetime
{
    private readonly AzuriteContainer container = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.38.0")
        .WithCommand("--skipApiVersionCheck", "--loose")
        .Build();

    public TableServiceClient Tables { get; private set; } = null!;
    public QueueServiceClient Queues { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        var connection = container.GetConnectionString();
        Tables = new TableServiceClient(connection);
        Queues = new QueueServiceClient(connection, new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
        await DependencyInjection.EnsureStorageAsync(Tables, Queues, CancellationToken.None);
    }

    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class AzuriteGroup : ICollectionFixture<AzuriteFixture>
{
    public const string Name = "azurite";
}
