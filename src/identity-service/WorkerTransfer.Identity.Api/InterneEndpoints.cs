using Noelia.Core.Identity;
using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Identity;
using WorkerTransfer.Identity.Application.Konto;
using WorkerTransfer.Identity.Application.Unternehmen;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Infrastructure.Post;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Api;

/// <summary>Interne Dienst-zu-Dienst-Endpunkte — hinter dem gemeinsamen Geheimnis.</summary>
/// <remarks>
/// Diese Endpunkte haben KEINE Gateway-Route. Sie liegen unter `/internal/`
/// und verhalten sich wie `/internal/notify` und `/internal/notifications`:
/// ohne das Geheimnis antworten sie mit 404 (nicht 401), damit sie sich nicht
/// über eine Methode verraten, die niemand benutzt.
/// </remarks>
public static class InternalEndpoints
{
    private const string Geheimniskopf = "X-Notify-Secret";

    /// <summary>Bildet die internen Routen ab.</summary>
    public static IEndpointRouteBuilder MapInterneEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/internal/companies/{tenantId:guid}/members", async (
            Guid tenantId,
            IMediator mediator,
            IOptions<NotifySettings> einstellungen,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!DarfMelden(context, einstellungen.Value))
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "Not Found");
                return;
            }

            var members = await mediator.Send(
                new InternalMembersQuery(new TenantId(tenantId)),
                cancellationToken);

            var response = new CompanyMembersResponseV1(
                [.. members.Select(m => new CompanyMemberV1(m.Subject.Value))]);

            await context.Response.WriteAsJsonAsync(response, cancellationToken);
        });

        // Die Rolle eines Menschen in einem Unternehmen — der Draht, ueber den
        // die anderen zehn Dienste ein Firmenrecht beantworten.
        //
        // Sie steht ABSICHTLICH nicht im Token: ein Token lebt weiter, nachdem
        // jemand aus einer Firma entfernt wurde, und die Entfernung wirkte dann
        // erst beim Ablauf. Hier wird je ResumeRequest gefragt, und ein Revocation
        // wirkt bei der naechsten.
        //
        // EINE Frage, EINE Antwort: die Mitgliederliste daneben gaebe fuer jede
        // Rechtepruefung die ganze Belegschaft heraus, um eine Zeile daraus zu
        // lesen.
        app.MapGet("/internal/companies/{tenantId:guid}/members/{subjectId:guid}/role", async (
            Guid tenantId,
            Guid subjectId,
            IMediator mediator,
            IOptions<NotifySettings> einstellungen,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!DarfMelden(context, einstellungen.Value))
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "Not Found");
                return;
            }

            var role = await mediator.Send(
                new InternalRoleQuery(new TenantId(tenantId), new SubjectId(subjectId)),
                cancellationToken);

            // 200 mit `null` und nicht 404: „kein Mitglied" ist eine Antwort auf
            // die gestellte Frage. Ein 404 waere von „Geheimnis falsch" nicht zu
            // unterscheiden — und der Aufrufer koennte die beiden nicht
            // auseinanderhalten, wo genau das den Unterschied zwischen „darf
            // nicht" und „wir wissen es nicht" ausmacht.
            await context.Response.WriteAsJsonAsync(
                new CompanyRoleV1(role is { } r ? MembershipRoleNames.ToDatabase(r) : null),
                cancellationToken);
        });

        // Wer ein Unternehmen IST — Name und bewiesene Domain, nicht seine
        // Belegschaft. advisor-service braucht die Domain, damit ein Ausschluss
        // greifen kann (ADR-0037): eine Person nennt die Unternehmen, mit denen
        // kein Conversation zustande kommt, und sie nennt sie durch ihre Domain.
        //
        // Eine zweite Domaintabelle in advisor-service waere die, die als Erste
        // veraltet — mit einem Ausschluss, der dann stillschweigend nicht mehr
        // greift, und das ist der eine Fall, in dem Stille am teuersten ist.
        app.MapGet("/internal/companies/{tenantId:guid}", async (
            Guid tenantId,
            IMediator mediator,
            IOptions<NotifySettings> einstellungen,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!DarfMelden(context, einstellungen.Value))
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "Not Found");
                return;
            }

            var company = await mediator.Send(
                new InternalCompanyQuery(new TenantId(tenantId)), cancellationToken);

            if (company is null)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "Not Found");
                return;
            }

            await context.Response.WriteAsJsonAsync(
                new UnternehmensauskunftV1(company.Name, company.Domain.Value), cancellationToken);
        });

        // Klarname und Kontakt — die empfindlichste dieser Tueren.
        //
        // DAS GEHEIMNIS IST HIER NICHT DIE GANZE ERLAUBNIS. Es beweist, dass ein
        // Dienst fragt, nicht dass er fragen darf. Die Erlaubnis steht im
        // Ledger, und advisor-service holt sie VOR diesem Aufruf: Stufe 3 ist
        // `advisor.identity:tenant:<id>` (ADR-0037). Wer diese Tuer fuer etwas
        // anderes benutzt, holt sich die Pruefung dazu.
        //
        // Zwei Felder, keine PostalAddress, kein Telefon (ADR-0038) — und keine
        // Liste: sie antwortet auf genau eine Kennung.
        app.MapGet("/internal/account/{subjectId:guid}/identity", async (
            Guid subjectId,
            IMediator mediator,
            IOptions<NotifySettings> einstellungen,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!DarfMelden(context, einstellungen.Value))
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "Not Found");
                return;
            }

            var person = await mediator.Send(
                new InternalPersonQuery(new SubjectId(subjectId)), cancellationToken);

            if (person is null)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "Not Found");
                return;
            }

            await context.Response.WriteAsJsonAsync(
                new PersonenauskunftV1(person.Name, person.Email), cancellationToken);
        });

        app.MapGet("/internal/account/{subjectId:guid}/ai", async (
            Guid subjectId,
            IMediator mediator,
            IOptions<NotifySettings> einstellungen,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!DarfMelden(context, einstellungen.Value))
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "Not Found");
                return;
            }

            var zugang = await mediator.Send(
                new InternalAiAccessQuery(new SubjectId(subjectId)),
                cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new KiZugangV1(zugang.Anbieter, zugang.Adresse, zugang.Modell, zugang.Schluessel),
                cancellationToken);
        });

        return app;
    }

    private static bool DarfMelden(HttpContext context, NotifySettings einstellungen)
    {
        if (string.IsNullOrEmpty(einstellungen.Geheimnis)
            || !context.Request.Headers.TryGetValue(Geheimniskopf, out var vorgelegt))
        {
            return false;
        }

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(vorgelegt.ToString()),
            System.Text.Encoding.UTF8.GetBytes(einstellungen.Geheimnis));
    }
}
