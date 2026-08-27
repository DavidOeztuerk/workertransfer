using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>
/// Turns an unhandled failure into an RFC 9457 problem document.
/// </summary>
/// <remarks>
/// Stands in for Girder's <c>UseExceptionHandling()</c>, which writes an
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
        catch (FluentValidation.ValidationException ungueltig)
        {
            // DIE STUFE IST SCHARF, NUR LEER.
            //
            // `AddCQRS` haengt Girders `ValidationBehavior` bereits in jede
            // Pipeline und ruft bereits `AddValidatorsFromAssemblies`. Es steigt
            // sofort aus, solange es keinen Validator findet — und wir hatten
            // keinen. Wer den ersten schreibt, bekaeme ohne diesen Zweig eine
            // 500 fuer eine Eingabe, die der Aufrufer falsch gemacht hat.
            //
            // 422 und nicht 400: der Rumpf war lesbar, sein Inhalt ist es nicht.
            // Genau die Unterscheidung, die die handgeschriebenen Pruefungen an
            // den Endpunkten schon treffen.
            //
            // GENANNT WERDEN NUR DIE FELDNAMEN, nie die Werte. `ErrorMessage`
            // wird bewusst nicht durchgereicht: FluentValidation setzt in seine
            // Vorgabemeldungen Platzhalter ein, und `{PropertyValue}` waere der
            // Wert einer Person in einem Fehlerdokument — das landet in
            // Bildschirmfotos und Fehlerberichten. Ein Feldname ist Form, kein
            // Inhalt.
            var felder = string.Join(
                ", ",
                ungueltig.Errors.Select(fehler => fehler.PropertyName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.Ordinal));

            logger.LogWarning("Validation failed for {Fields}", felder);

            if (context.Response.HasStarted)
            {
                throw;
            }

            await Schreibe(context, StatusCodes.Status422UnprocessableEntity,
                "Request failed",
                felder.Length > 0 ? $"invalid: {felder}" : "validation failed");
        }
        catch (BadHttpRequestException kaputt)
        {
            // EIN KAPUTTER RUMPF IST KEIN SERVERFEHLER.
            //
            // Unlesbares JSON — eine offene Klammer, ein falscher Typ — laesst
            // die Modellbindung von ASP.NET werfen, und ohne diesen Zweig kam
            // das als 500 zurueck: an JEDEM Endpunkt, mit Stapelabzug im
            // Protokoll. Gemessen an D3, gefunden mit einem abgeschnittenen
            // JSON-Rumpf.
            //
            // Der Abbruchcode kommt aus der Ausnahme selbst (400) und wird
            // nicht hier erfunden — sie weiss besser, was sie meint.
            //
            // Die Meldung wird NICHT durchgereicht. Sie nennt den Parameter und
            // die Byteposition, und das ist schon eine Aussage ueber das, was
            // jemand geschickt hat. `detail` sagt die Form, nie den Inhalt.
            logger.LogWarning(
                "Malformed request body ({Status})", kaputt.StatusCode);

            if (context.Response.HasStarted)
            {
                throw;
            }

            await Schreibe(context, kaputt.StatusCode,
                "Request failed", "malformed request body");
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

            await Schreibe(context, StatusCodes.Status500InternalServerError,
                "Internal server error", "An unexpected error occurred.");
        }
    }

    /// <summary>Writes one problem document.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="status">The status code.</param>
    /// <param name="title">The kind of failure.</param>
    /// <param name="detail">What the caller is told. Never what they sent.</param>
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
