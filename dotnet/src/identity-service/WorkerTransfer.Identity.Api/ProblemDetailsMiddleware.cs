using System.Text.Json;

namespace WorkerTransfer.Identity.Api;

/// <summary>
/// Turns an unhandled failure into an RFC 9457 problem document.
/// </summary>
/// <remarks>
/// Stands in for Girder's <c>UseExceptionHandling()</c>, which writes an
/// envelope of a different shape. The field names here are the contract the
/// React app reads — it takes the message out of <c>detail</c> — and a
/// migration that changes them changes a contract.
/// <para>
/// Carries the correlation id and nothing else: a body is a place a value ends
/// up in a log or a screenshot.
/// </para>
/// </remarks>
public sealed class ProblemDetailsMiddleware(
    RequestDelegate next,
    ILogger<ProblemDetailsMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled request exception");

            if (context.Response.HasStarted)
            {
                throw;
            }

            await Schreibe(context, StatusCodes.Status500InternalServerError,
                "Internal server error", "An unexpected error occurred.");
        }
    }

    /// <summary>Writes one problem document.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="status">The status code.</param>
    /// <param name="title">The kind of failure.</param>
    /// <param name="detail">What the caller is told.</param>
    public static async Task Schreibe(
        HttpContext context,
        int status,
        string title,
        string detail)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var body = new Dictionary<string, object?>
        {
            ["type"] = $"https://workertransfer.dev/problems/{status}",
            ["title"] = title,
            ["status"] = status,
            ["detail"] = detail
        };

        if (context.Items["CorrelationId"]?.ToString() is { Length: > 0 } correlationId)
        {
            body["correlationId"] = correlationId;
        }

        await context.Response.WriteAsync(JsonSerializer.Serialize(body));
    }
}
