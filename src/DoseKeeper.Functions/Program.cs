using Azure.Data.Tables;
using Azure.Storage.Queues;
using DoseKeeper.Application;
using DoseKeeper.Infrastructure;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

var storage = builder.Configuration["AzureWebJobsStorage"]
    ?? throw new InvalidOperationException("AzureWebJobsStorage is not configured.");
builder.Services.AddDoseKeeperApplication().AddDoseKeeperStorage(storage);

var host = builder.Build();
await DoseKeeper.Infrastructure.DependencyInjection.EnsureStorageAsync(
    host.Services.GetRequiredService<TableServiceClient>(),
    host.Services.GetRequiredService<QueueServiceClient>(),
    CancellationToken.None);
await host.RunAsync();
