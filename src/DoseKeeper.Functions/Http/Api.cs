using DoseKeeper.Application;
using DoseKeeper.Domain;
using DoseKeeper.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DoseKeeper.Functions.Http;

/// <summary>Maps domain/application errors to RFC 7807 problem responses.</summary>
internal static class Api
{
    public static async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (DomainException ex)
        {
            return Problem(StatusCodes.Status400BadRequest, "Invalid request", ex.Message);
        }
        catch (NotFoundException ex)
        {
            return Problem(StatusCodes.Status404NotFound, "Not found", ex.Message);
        }
        catch (System.Text.Json.JsonException ex)
        {
            return Problem(StatusCodes.Status400BadRequest, "Malformed JSON", ex.Message);
        }
    }

    public static async Task<T> BodyAsync<T>(HttpRequest request, CancellationToken ct) =>
        await request.ReadFromJsonAsync<T>(Json.Options, ct) ?? throw new DomainException("A JSON body is required.");

    public static IActionResult Ok(object? value) => new JsonResult(value, Json.Options);

    public static IActionResult Created(object value) => new JsonResult(value, Json.Options) { StatusCode = StatusCodes.Status201Created };

    private static ObjectResult Problem(int status, string title, string detail) => new(new ProblemDetails { Status = status, Title = title, Detail = detail })
    {
        StatusCode = status,
        ContentTypes = { "application/problem+json" },
    };
}
