using Azure.Data.Tables;
using DoseKeeper.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace DoseKeeper.Functions.Http;

public sealed class HealthFunction(TableServiceClient tables)
{
    /// <summary>Ready when the storage account answers (Table query on a known table).</summary>
    [Function("Health")]
    public async Task<IActionResult> Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest req, CancellationToken ct)
    {
        try
        {
            _ = await tables.GetTableClient(StorageNames.MedicationsTable)
                .QueryAsync<TableEntity>(maxPerPage: 1, cancellationToken: ct)
                .AsPages(pageSizeHint: 1)
                .FirstOrDefaultAsync(ct);

            return new OkObjectResult(new { Status = "ok" });
        }
        catch (Azure.RequestFailedException ex)
        {
            return new ObjectResult(new { Status = "storage unavailable", ex.Message }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
        }
    }
}
