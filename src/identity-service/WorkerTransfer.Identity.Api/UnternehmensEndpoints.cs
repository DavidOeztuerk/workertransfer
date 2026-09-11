using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Registrierung;
using WorkerTransfer.Identity.Application.Unternehmen;
using WorkerTransfer.Identity.Api.Berechtigung;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Api;

/// <summary>What a caller sends to found a company. No domain — see below.</summary>
public sealed record CreateCompanyBody(string Name);

/// <summary>What a caller sends to invite somebody.</summary>
public sealed record InviteBody(string Email, string Role);

/// <summary>What a caller sends to take an invitation up.</summary>
public sealed record AcceptInvitationBody(string Token);

/// <summary><c>/companies</c>, <c>/me/companies</c> and <c>/invitations</c>.</summary>
public static class UnternehmensEndpoints
{
    /// <summary>Maps the eight endpoints around companies.</summary>
    public static IEndpointRouteBuilder MapUnternehmensEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/companies", async (
            CreateCompanyBody body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await Nicht_angemeldet(context);
                return;
            }

            // The domain is NOT in the body. It comes from the creator's
            // confirmed address; the client names only the name. What it cannot
            // send, it cannot forge (ADR-0017/0019).
            var ergebnis = await mediator.Send(
                new UnternehmenGruendenBefehl(handelnder.Subject, body.Name), cancellationToken);

            switch (ergebnis)
            {
                case Firmenergebnis.Angelegt angelegt:
                    context.Response.StatusCode = StatusCodes.Status201Created;
                    await context.Response.WriteAsJsonAsync(
                        new Dictionary<string, string>
                        {
                            ["id"] = angelegt.Firma.Id.ToString(),
                            ["name"] = angelegt.Firma.Name,
                            ["domain"] = angelegt.Firma.Domain.Value
                        },
                        cancellationToken);
                    return;

                case Firmenergebnis.Abgelehnt abgelehnt:
                    await Abgelehnt(context, abgelehnt.Code);
                    return;
            }
        });

        app.MapGet("/me/companies", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await Nicht_angemeldet(context);
                return;
            }

            var meine = await mediator.Send(
                new MeineUnternehmenAbfrage(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                meine.Select(eintrag => new Dictionary<string, string>
                {
                    ["id"] = eintrag.Tenant.ToString(),
                    ["name"] = eintrag.Name,
                    ["domain"] = eintrag.Domain,
                    ["role"] = MembershipRoleNames.ToDatabase(eintrag.Role)
                }),
                cancellationToken);
        });

        var firma = app.MapGroup("/companies/{tenantId:guid}");

        firma.MapPost("/invitations", async (
            Guid tenantId,
            InviteBody body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await Nicht_angemeldet(context);
                return;
            }

            if (!Rolle(body.Role, out var rolle))
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status400BadRequest,
                    "Request failed", "role must be 'admin' or 'member'");
                return;
            }

            await Beantworte(context, cancellationToken, async () =>
            {
                var einladung = await mediator.Send(
                    new MitgliedEinladenBefehl(
                        handelnder.Subject, new TenantId(tenantId), body.Email, rolle),
                    cancellationToken);

                context.Response.StatusCode = StatusCodes.Status201Created;
                // The answer is the same whether or not the address has an
                // account — otherwise this endpoint would be a way to ask about
                // platform membership without asking the consent ledger.
                await context.Response.WriteAsJsonAsync(Einladung(einladung), cancellationToken);
            });
        })
        // Die Rechtepruefung steht jetzt HIER statt im Befehl. Aufgeloest wird
        // sie von `Mitgliedschaftsrecht` aus der Mitgliedschaftstabelle, nicht
        // aus dem Token — wer entfernt wird, ist bei der naechsten Anfrage
        // draussen.
        .RequireAuthorization(Firmenrechte.Richtlinie(Firmenrechte.Einladen));

        firma.MapGet("/invitations", async (
            Guid tenantId,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await Nicht_angemeldet(context);
                return;
            }

            await Beantworte(context, cancellationToken, async () =>
            {
                var offene = await mediator.Send(
                    new OffeneEinladungenAbfrage(handelnder.Subject, new TenantId(tenantId)),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(
                    offene.Select(Einladung), cancellationToken);
            });
        });

        firma.MapDelete("/invitations/{invitationId:guid}", async (
            Guid tenantId,
            Guid invitationId,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await Nicht_angemeldet(context);
                return;
            }

            await Beantworte(context, cancellationToken, async () =>
            {
                await mediator.Send(
                    new EinladungZuruecknehmenBefehl(
                        handelnder.Subject, new TenantId(tenantId), invitationId),
                    cancellationToken);

                context.Response.StatusCode = StatusCodes.Status204NoContent;
            });
        })
        // Die Kehrseite des Einladens, und darum dasselbe Recht: bis heute
        // konnte ein `member` die Einladung einer Kollegin zuruecknehmen, weil
        // hier niemand fragte.
        .RequireAuthorization(Firmenrechte.Richtlinie(Firmenrechte.EinladungZuruecknehmen));

        firma.MapGet("/members", async (
            Guid tenantId,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await Nicht_angemeldet(context);
                return;
            }

            await Beantworte(context, cancellationToken, async () =>
            {
                var mitglieder = await mediator.Send(
                    new MitgliederAbfrage(handelnder.Subject, new TenantId(tenantId)),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(
                    mitglieder.Select(mitglied => new Dictionary<string, string>
                    {
                        ["user_id"] = mitglied.Subject.ToString(),
                        ["display_name"] = mitglied.DisplayName,
                        ["role"] = MembershipRoleNames.ToDatabase(mitglied.Role)
                    }),
                    cancellationToken);
            });
        });

        firma.MapDelete("/members/{memberId:guid}", async (
            Guid tenantId,
            Guid memberId,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await Nicht_angemeldet(context);
                return;
            }

            await Beantworte(context, cancellationToken, async () =>
            {
                await mediator.Send(
                    new MitgliedEntfernenBefehl(
                        handelnder.Subject, new TenantId(tenantId), new SubjectId(memberId)),
                    cancellationToken);

                context.Response.StatusCode = StatusCodes.Status204NoContent;
            });
        })
        .RequireAuthorization(Firmenrechte.Richtlinie(Firmenrechte.Entfernen));

        app.MapPost("/invitations/accept", async (
            AcceptInvitationBody body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await Nicht_angemeldet(context);
                return;
            }

            // Erst Anmeldung, DANN Eingabe: umgekehrt lernte ein Fremder aus
            // dem Unterschied 422/401, dass es diesen Endpunkt gibt. Ohne die
            // Pruefung kam `{}` als 500 zurueck (D2).
            if (string.IsNullOrWhiteSpace(body.Token))
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "token is required");
                return;
            }

            await Beantworte(context, cancellationToken, async () =>
            {
                var mitgliedschaft = await mediator.Send(
                    new EinladungAnnehmenBefehl(handelnder.Subject, body.Token),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(
                    new Dictionary<string, string>
                    {
                        ["id"] = mitgliedschaft.Tenant.ToString(),
                        ["name"] = mitgliedschaft.Name,
                        ["domain"] = mitgliedschaft.Domain,
                        ["role"] = MembershipRoleNames.ToDatabase(mitgliedschaft.Role)
                    },
                    cancellationToken);
            });
        });

        return app;
    }

    private static Dictionary<string, string?> Einladung(Invitation einladung) => new()
    {
        ["id"] = einladung.Id.ToString(),
        ["email"] = einladung.Email,
        ["role"] = MembershipRoleNames.ToDatabase(einladung.Role),
        ["status"] = InvitationStatusNames.ToDatabase(einladung.Status),
        ["created_at"] = einladung.CreatedAt.ToString("O"),
        ["expires_at"] = einladung.ExpiresAt.ToString("O")
    };

    private static bool Rolle(string roh, out MembershipRole rolle)
    {
        switch (roh?.Trim().ToLowerInvariant())
        {
            case "admin":
                rolle = MembershipRole.Admin;
                return true;
            case "member":
                rolle = MembershipRole.Member;
                return true;
            default:
                rolle = MembershipRole.Member;
                return false;
        }
    }

    /// <summary>
    /// Turns the domain's refusals into status codes — in one place, so they
    /// cannot drift apart across eight routes.
    /// </summary>
    private static async Task Beantworte(
        HttpContext context,
        CancellationToken cancellationToken,
        Func<Task> arbeit)
    {
        try
        {
            await arbeit();
        }
        catch (NotAMemberException)
        {
            // Like a foreign resource: somebody who is not a member must not be
            // able to tell whether the company exists.
            await ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status404NotFound, "Request failed", "no such company");
        }
        catch (LastAdminMayNotLeaveException fehler)
        {
            // 409, not 403: the caller MAY remove, just not this one member
            // right now. A 403 would deny them a permission they have.
            await ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status409Conflict, "Request failed", fehler.Message);
        }
        catch (Exception fehler) when (fehler is OnlyAdminsMayInviteException
                                              or OnlyAdminsMayRemoveException)
        {
            // A statement about the caller, not about the resource — a 403
            // reveals nothing they do not already know.
            await ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status403Forbidden, "Request failed", fehler.Message);
        }
        catch (Exception fehler) when (fehler is InvitationExpiredException
                                              or NotYourInvitationException)
        {
            await ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status400BadRequest, "Request failed", fehler.Message);
        }
        catch (InvitationInvalidException fehler)
        {
            await ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status404NotFound, "Request failed", fehler.Message);
        }

        _ = cancellationToken;
    }

    private static Task Nicht_angemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Schreibe(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");

    private static Task Abgelehnt(HttpContext context, string code) => code switch
    {
        "account_not_confirmed" => ProblemDetailsMiddleware.Schreibe(
            context, StatusCodes.Status403Forbidden, "Request failed",
            "Confirm your email address before creating a company"),

        "public_email_domain" => ProblemDetailsMiddleware.Schreibe(
            context, StatusCodes.Status422UnprocessableEntity, "Request failed",
            "A public email provider cannot be claimed as a company"),

        // 409 and not 404: the caller is being told their own domain is taken,
        // which they can see from their own address. It is not a probe.
        "domain_already_claimed" => ProblemDetailsMiddleware.Schreibe(
            context, StatusCodes.Status409Conflict, "Request failed",
            "That domain already belongs to a company"),

        _ => ProblemDetailsMiddleware.Schreibe(
            context, StatusCodes.Status400BadRequest, "Request failed",
            "A company name must not be empty")
    };
}
