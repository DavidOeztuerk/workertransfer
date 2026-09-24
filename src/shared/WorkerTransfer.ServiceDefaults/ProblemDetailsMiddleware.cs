using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>
/// Turns an unhandled failure into an RFC 9457 problem document.
/// </summary>
/// <remarks>
/// Stands in for Noelia's <c>UseExceptionHandling()</c>, which writes an
/// envelope of a different shape. One shape across every service, because a
/// caller that has to know which service answered in order to read the error is
/// a caller that will read the wrong field.
/// <para>
/// Carries the correlation id and nothing else. A body is a place a value ends
/// up in a log or a screenshot, so what a person typed never reaches it.
/// </para>
/// </remarks>
public sealed class ProblemDetailsMiddleware(
    RequestDelegate next,
    ILogger<ProblemDetailsMiddleware> logger)
{
    /// <summary>Runs the rest of the pipeline and catches what falls out.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context);
        }
        catch (FluentValidation.ValidationException invalid)
        {
            // THE STAGE IS ARMED, ONLY EMPTY.
            //
            // `AddCQRS` already hangs Noelia's `ValidationBehavior` into every
            // pipeline and already calls `AddValidatorsFromAssemblies`. It bows
            // out immediately as long as it finds no validator — and we had
            // none. Whoever writes the first one would, without this branch,
            // get a 500 for an input the caller got wrong.
            //
            // 422 and not 400: the body was readable, its content is not.
            // Exactly the distinction the hand-written checks at the endpoints
            // already make.
            //
            // ONLY THE FIELD NAMES ARE NAMED, never the values. `ErrorMessage`
            // is deliberately not passed on: FluentValidation puts placeholders
            // into its default messages, and `{PropertyValue}` would be a
            // person's value in an error document — that ends up in screenshots
            // and bug reports. A field name is shape, not content.
            var fields = string.Join(
                ", ",
                invalid.Errors.Select(error => error.PropertyName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.Ordinal));

            logger.LogWarning("Validation failed for {Fields}", fields);

            if (context.Response.HasStarted)
            {
                throw;
            }

            await Write(context, StatusCodes.Status422UnprocessableEntity,
                "Request failed",
                fields.Length > 0 ? $"invalid: {fields}" : "validation failed");
        }
        catch (BadHttpRequestException badRequest)
        {
            // A BROKEN BODY IS NOT A SERVER ERROR.
            //
            // Unreadable JSON — an open brace, a wrong type — makes ASP.NET's
            // model binding throw, and without this branch that came back as a
            // 500: at EVERY endpoint, with a stack trace in the log. Measured
            // at D3, found with a truncated JSON body.
            //
            // The status code comes out of the exception itself (400) and is
            // not invented here — it knows better what it means.
            //
            // The message is NOT passed on. It names the parameter and the byte
            // position, and that is already a statement about what somebody
            // sent. `detail` says the shape, never the content.
            logger.LogWarning(
                "Malformed request body ({Status})", badRequest.StatusCode);

            if (context.Response.HasStarted)
            {
                throw;
            }

            await Write(context, badRequest.StatusCode,
                "Request failed", "malformed request body");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            /*
             * THE CALLER HUNG UP — that is not a server error.
             *
             * Measured on an E2E run: 853 of the 866 error lines in the whole
             * stack were exactly that. The browser aborts requests as soon as a
             * page changes or a fetch supersedes an older one
             * (`AbortController`), and every single one became an "Unhandled
             * request exception" together with a 500. The thirteen REAL errors
             * stood in between and could not be found.
             *
             * <strong>The condition carries the whole thing.</strong> Without
             * `context.RequestAborted.IsCancellationRequested` a timeout WE
             * caused would disappear too — an `HttpClient` that gives up throws
             * the same exception, and that is a genuine outage which deserves a
             * 500.
             *
             * The answer is 499 ("Client Closed Request"): nobody reads it any
             * more, but with it the telemetry distinguishes "aborted" from
             * "broken". A 200 would be a lie, a 500 a false alarm.
             */
            logger.LogDebug("Request aborted by the caller");

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 499;
            }
        }
        catch (Exception exception)
        {
            // The exception, not the request body: a failure is exactly when
            // somebody wants the payload and exactly when writing it out is
            // worst.
            logger.LogError(exception, "Unhandled request exception");

            if (context.Response.HasStarted)
            {
                throw;
            }

            await Write(context, StatusCodes.Status500InternalServerError,
                "Internal server error", "An unexpected error occurred.");
        }
    }

    /// <summary>Writes one problem document.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="status">The status code.</param>
    /// <param name="title">The kind of failure.</param>
    /// <param name="detail">What the caller is told. Never what they sent.</param>
    public static async Task Write(
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
