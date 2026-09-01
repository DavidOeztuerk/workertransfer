using System.Text.Json.Serialization;
using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Consent.Application.Einwilligung;
using WorkerTransfer.Consent.Domain.Ledger;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Consent.Api;

/// <summary>What a caller sends to give a permission.</summary>
/// <remarks>
/// <para><strong>Die Drahtnamen stehen ausdrücklich da.</strong> Der Draht dieser
/// Plattform ist snake_case; ohne <c>[JsonPropertyName]</c> fiele
/// <c>SubjectId</c> auf <c>subjectId</c> zurück, während die Oberfläche
/// <c>subject_id</c> schickt (<c>apps/web/src/consent/client.ts:38,63</c>).</para>
///
/// <para>Was das kostete, war gemessen kein stiller Schaden, sondern ein
/// vollständiger Ausfall: der Wert kam als <c>Guid.Empty</c> an, der Wächter
/// verglich ihn mit dem Aufrufer und antwortete <c>403 „a consent belongs to its
/// subject"</c>. <strong>Über die Oberfläche konnte niemand etwas freigeben oder
/// zurücknehmen</strong> — bei dem Dienst, auf den sich alle anderen stützen.
/// Dass nichts Falsches ins Buch geriet, ist das Verdienst des Wächters, nicht
/// dieser Zeile.</para>
/// </remarks>
/// <param name="Reason">Optional: giving a permission needs no justification.</param>
public sealed record GrantBody(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("capability")] string Capability,
    [property: JsonPropertyName("reason")] string? Reason = null);

/// <summary>What a caller sends to take one back.</summary>
/// <param name="Reason">Mandatory: withdrawing must always be explainable.</param>
public sealed record RevokeBody(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("capability")] string Capability,
    [property: JsonPropertyName("reason")] string Reason);

/// <summary>What a caller sends to ask about one pair.</summary>
public sealed record CheckBody(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("capability")] string Capability);

/// <summary>What a caller sends to ask about many pairs at once.</summary>
public sealed record CheckBatchBody(IReadOnlyList<CheckBody> Pairs);

/// <summary><c>/consent/*</c> — the ledger's whole surface.</summary>
public static class EinwilligungsEndpoints
{
    /// <summary>Maps the five endpoints of the ledger.</summary>
    public static IEndpointRouteBuilder MapEinwilligungsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var consent = app.MapGroup("/consent");

        consent.MapPost("/grant", async (
            GrantBody body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await NichtAngemeldet(context);
                return;
            }

            var ergebnis = await mediator.Send(
                new EinwilligungErteilenBefehl(
                    handelnder.Subject, Firma(handelnder),
                    body.SubjectId, body.Capability, body.Reason),
                cancellationToken);

            await Beantworte(context, ergebnis, cancellationToken);
        });

        consent.MapPost("/revoke", async (
            RevokeBody body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await NichtAngemeldet(context);
                return;
            }

            var ergebnis = await mediator.Send(
                new EinwilligungWiderrufenBefehl(
                    handelnder.Subject, Firma(handelnder),
                    body.SubjectId, body.Capability, body.Reason),
                cancellationToken);

            await Beantworte(context, ergebnis, cancellationToken);
        });

        // Deliberately takes no subject — not in the path and not as a query.
        // A foreign list would say which OTHER companies hold access to
        // somebody, which is exactly what this page exists to make visible to
        // its owner and to nobody else.
        consent.MapGet("/me", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await NichtAngemeldet(context);
                return;
            }

            var freigaben = await mediator.Send(
                new MeineFreigabenAbfrage(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                freigaben.Select(freigabe => new Dictionary<string, string>
                {
                    ["capability"] = freigabe.Faehigkeit.Value,
                    ["granted_at"] = freigabe.Seit.ToString("O")
                }),
                cancellationToken);
        });

        // WITH the reason, unlike every cross-subject read. The withdrawal
        // reason is free text the person wrote about themselves; towards them
        // there is no ground to withhold it. Outwards it stays hidden — that is
        // the difference between "belongs to them" and "is anybody else's
        // business".
        consent.MapGet("/me/history", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await NichtAngemeldet(context);
                return;
            }

            var verlauf = await mediator.Send(
                new MeineGeschichteAbfrage(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                verlauf.Select(eintrag => new Dictionary<string, string?>
                {
                    ["capability"] = eintrag.Capability.Value,
                    ["action"] = eintrag.Action.ToString().ToUpperInvariant(),
                    ["recorded_at"] = eintrag.RecordedAt.ToString("O"),
                    ["reason"] = eintrag.Reason?.Value
                }),
                cancellationToken);
        });

        // Open to any authenticated caller about any person — that is what makes
        // the ledger usable as an enabler. It answers WITHOUT the withdrawal
        // reason, because that reason is free text somebody wrote about
        // themselves and must not ride along on a query anyone may issue.
        consent.MapPost("/check", async (
            CheckBody body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is null)
            {
                await NichtAngemeldet(context);
                return;
            }

            var ergebnis = await mediator.Send(
                new EinwilligungPruefenAbfrage(body.SubjectId, body.Capability),
                cancellationToken);

            switch (ergebnis)
            {
                case Pruefergebnis.Stand stand:
                    await context.Response.WriteAsJsonAsync(
                        Antwort(body.SubjectId, body.Capability, stand.Zustand),
                        cancellationToken);
                    return;

                case Pruefergebnis.Unbrauchbar unbrauchbar:
                    await ProblemDetailsMiddleware.Schreibe(
                        context, StatusCodes.Status422UnprocessableEntity,
                        "Request failed", unbrauchbar.Detail);
                    return;
            }
        });

        // One question in one round trip, not a stock answer: the read stays
        // synchronous and nothing is cached (ADR-0013). It exists because a page
        // of candidates cost up to forty separate calls — 1.7 to 8.8 seconds,
        // longer than an interface waits.
        consent.MapPost("/check-batch", async (
            CheckBatchBody body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is null)
            {
                await NichtAngemeldet(context);
                return;
            }

            if (body.Pairs is not { Count: > 0 })
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "at least one pair is required");
                return;
            }

            // Derived, not chosen: one page is fifty people and a person can be
            // asked about two capabilities. A batch makes asking cheaper, and a
            // ceiling keeps that difference small.
            if (body.Pairs.Count > Einwilligungsgrenzen.HoechsteSammelgroesse)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity, "Request failed",
                    $"at most {Einwilligungsgrenzen.HoechsteSammelgroesse} pairs per request");
                return;
            }

            var ergebnis = await mediator.Send(
                new SammelpruefungAbfrage(
                    [.. body.Pairs.Select(paar =>
                        new EinwilligungPruefenAbfrage(paar.SubjectId, paar.Capability))]),
                cancellationToken);

            switch (ergebnis)
            {
                case Sammelergebnis.Staende staende:
                    // In the order of the questions, one per pair. The order is
                    // part of the contract: the caller maps answers onto its
                    // rows, and a pair asked twice would otherwise be ambiguous.
                    await context.Response.WriteAsJsonAsync(
                        new Dictionary<string, object>
                        {
                            ["results"] = staende.Zustaende
                                .Select((zustand, i) => Antwort(
                                    body.Pairs[i].SubjectId, body.Pairs[i].Capability, zustand))
                                .ToArray()
                        },
                        cancellationToken);
                    return;

                case Sammelergebnis.Unbrauchbar unbrauchbar:
                    await ProblemDetailsMiddleware.Schreibe(
                        context, StatusCodes.Status422UnprocessableEntity,
                        "Request failed", unbrauchbar.Detail);
                    return;
            }
        });

        return app;
    }

    /// <summary>
    /// The cross-subject answer: granted, deleted, and no reason.
    /// </summary>
    /// <remarks>
    /// Built here from the domain state rather than handed out as it is. The
    /// state carries the withdrawal reason because the subject's own views need
    /// it; this shape has no field for one, so it cannot leak by accident.
    /// </remarks>
    private static Dictionary<string, object> Antwort(
        Guid gegenstand, string faehigkeit, ConsentState zustand) => new()
    {
        ["subject_id"] = gegenstand,
        ["capability"] = faehigkeit,
        ["granted"] = zustand.Granted,
        ["deleted"] = zustand.Deleted
    };

    private static async Task Beantworte(
        HttpContext context,
        Einwilligungsergebnis ergebnis,
        CancellationToken cancellationToken)
    {
        switch (ergebnis)
        {
            case Einwilligungsergebnis.Stand stand:
                await context.Response.WriteAsJsonAsync(
                    new Dictionary<string, object?>
                    {
                        ["granted"] = stand.Zustand.Granted,
                        ["deleted"] = stand.Zustand.Deleted,
                        ["reason"] = stand.Zustand.Reason
                    },
                    cancellationToken);
                return;

            case Einwilligungsergebnis.FremderGegenstand:
                // Strict self-management: there is no delegation model, and
                // admin or guardian consent is a later, deliberate decision
                // rather than something allowed by omission.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "a consent belongs to its subject");
                return;

            case Einwilligungsergebnis.Unbrauchbar unbrauchbar:
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", unbrauchbar.Detail);
                return;
        }
    }

    /// <summary>
    /// The company the caller acts for, for the trail only.
    /// </summary>
    /// <remarks>
    /// It never decides anything here — a consent belongs to the person and
    /// follows them across employers, which is why the ledger has no tenant
    /// column (ADR-0017). It is recorded so an operator can see in what capacity
    /// somebody was acting, not so anyone can act for another.
    /// </remarks>
    private static TenantId? Firma(Principal handelnder) =>
        handelnder.Acting is Capacity.ForCompany firma ? firma.Tenant : null;

    private static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Schreibe(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");
}
