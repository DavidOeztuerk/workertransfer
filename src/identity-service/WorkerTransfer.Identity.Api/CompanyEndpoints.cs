using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Registration;
using WorkerTransfer.Identity.Application.Companies;
using WorkerTransfer.Identity.Api.Permissions;
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
public static class CompanyEndpoints
{
    /// <summary>Maps the eight endpoints around companies.</summary>
    public static IEndpointRouteBuilder MapCompanyEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/companies", async (
            CreateCompanyBody body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotAuthenticated(context);
                return;
            }

            // The domain is NOT in the body. It comes from the creator's
            // confirmed address; the client names only the name. What it cannot
            // send, it cannot forge (ADR-0017/0019).
            var result = await mediator.Send(
                new FoundCompanyCommand(actor.Subject, body.Name), cancellationToken);

            switch (result)
            {
                case CompanyResult.Created created:
                    context.Response.StatusCode = StatusCodes.Status201Created;
                    await context.Response.WriteAsJsonAsync(
                        new Dictionary<string, string>
                        {
                            ["id"] = created.Company.Id.ToString(),
                            ["name"] = created.Company.Name,
                            ["domain"] = created.Company.Domain.Value
                        },
                        cancellationToken);
                    return;

                case CompanyResult.Refused refused:
                    await Refused(context, refused.Code);
                    return;
            }
        });

        app.MapGet("/me/companies", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotAuthenticated(context);
                return;
            }

            var mine = await mediator.Send(
                new MyCompaniesQuery(actor.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                mine.Select(entry => new Dictionary<string, string>
                {
                    ["id"] = entry.Tenant.ToString(),
                    ["name"] = entry.Name,
                    ["domain"] = entry.Domain,
                    ["role"] = MembershipRoleNames.ToDatabase(entry.Role)
                }),
                cancellationToken);
        });

        var company = app.MapGroup("/companies/{tenantId:guid}");

        company.MapPost("/invitations", async (
            Guid tenantId,
            InviteBody body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotAuthenticated(context);
                return;
            }

            if (!ParseRole(body.Role, out var role))
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status400BadRequest,
                    "Request failed", "role must be 'admin' or 'member'");
                return;
            }

            await Respond(context, cancellationToken, async () =>
            {
                var invitation = await mediator.Send(
                    new InviteMemberCommand(
                        actor.Subject, new TenantId(tenantId), body.Email, role),
                    cancellationToken);

                context.Response.StatusCode = StatusCodes.Status201Created;
                // The answer is the same whether or not the address has an
                // account — otherwise this endpoint would be a way to ask about
                // platform membership without asking the consent ledger.
                await context.Response.WriteAsJsonAsync(InvitationJson(invitation), cancellationToken);
            });
        })
        // The permission check stands HERE now instead of in the command. It
        // is resolved by `MembershipPermission` out of the membership table,
        // not out of the token — whoever is removed is out at the next
        // request.
        .RequireAuthorization(CompanyPermissions.Policy(CompanyPermissions.Invite));

        company.MapGet("/invitations", async (
            Guid tenantId,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotAuthenticated(context);
                return;
            }

            await Respond(context, cancellationToken, async () =>
            {
                var open = await mediator.Send(
                    new OpenInvitationsQuery(actor.Subject, new TenantId(tenantId)),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(
                    open.Select(InvitationJson), cancellationToken);
            });
        });

        company.MapDelete("/invitations/{invitationId:guid}", async (
            Guid tenantId,
            Guid invitationId,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotAuthenticated(context);
                return;
            }

            await Respond(context, cancellationToken, async () =>
            {
                await mediator.Send(
                    new RevokeInvitationCommand(
                        actor.Subject, new TenantId(tenantId), invitationId),
                    cancellationToken);

                context.Response.StatusCode = StatusCodes.Status204NoContent;
            });
        })
        // The reverse side of inviting, and therefore the same permission:
        // until today a `member` could take a colleague's invitation back,
        // because nobody asked here.
        .RequireAuthorization(CompanyPermissions.Policy(CompanyPermissions.WithdrawInvitation));

        company.MapGet("/members", async (
            Guid tenantId,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotAuthenticated(context);
                return;
            }

            await Respond(context, cancellationToken, async () =>
            {
                var members = await mediator.Send(
                    new MembersQuery(actor.Subject, new TenantId(tenantId)),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(
                    members.Select(member => new Dictionary<string, string>
                    {
                        ["user_id"] = member.Subject.ToString(),
                        ["display_name"] = member.DisplayName,
                        ["role"] = MembershipRoleNames.ToDatabase(member.Role)
                    }),
                    cancellationToken);
            });
        });

        company.MapDelete("/members/{memberId:guid}", async (
            Guid tenantId,
            Guid memberId,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotAuthenticated(context);
                return;
            }

            await Respond(context, cancellationToken, async () =>
            {
                await mediator.Send(
                    new RemoveMemberCommand(
                        actor.Subject, new TenantId(tenantId), new SubjectId(memberId)),
                    cancellationToken);

                context.Response.StatusCode = StatusCodes.Status204NoContent;
            });
        })
        .RequireAuthorization(CompanyPermissions.Policy(CompanyPermissions.Remove));

        app.MapPost("/invitations/accept", async (
            AcceptInvitationBody body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotAuthenticated(context);
                return;
            }

            // Authentication first, THEN the input: the other way round a
            // stranger would learn from the difference 422/401 that this
            // endpoint exists. Without the check `{}` came back as a 500 (D2).
            if (string.IsNullOrWhiteSpace(body.Token))
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "token is required");
                return;
            }

            await Respond(context, cancellationToken, async () =>
            {
                var membership = await mediator.Send(
                    new AcceptInvitationCommand(actor.Subject, body.Token),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(
                    new Dictionary<string, string>
                    {
                        ["id"] = membership.Tenant.ToString(),
                        ["name"] = membership.Name,
                        ["domain"] = membership.Domain,
                        ["role"] = MembershipRoleNames.ToDatabase(membership.Role)
                    },
                    cancellationToken);
            });
        });

        return app;
    }

    private static Dictionary<string, string?> InvitationJson(Invitation invitation) => new()
    {
        ["id"] = invitation.Id.ToString(),
        ["email"] = invitation.Email,
        ["role"] = MembershipRoleNames.ToDatabase(invitation.Role),
        ["status"] = InvitationStatusNames.ToDatabase(invitation.Status),
        ["created_at"] = invitation.CreatedAt.ToString("O"),
        ["expires_at"] = invitation.ExpiresAt.ToString("O")
    };

    private static bool ParseRole(string raw, out MembershipRole role)
    {
        switch (raw?.Trim().ToLowerInvariant())
        {
            case "admin":
                role = MembershipRole.Admin;
                return true;
            case "member":
                role = MembershipRole.Member;
                return true;
            default:
                role = MembershipRole.Member;
                return false;
        }
    }

    /// <summary>
    /// Turns the domain's refusals into status codes — in one place, so they
    /// cannot drift apart across eight routes.
    /// </summary>
    private static async Task Respond(
        HttpContext context,
        CancellationToken cancellationToken,
        Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (NotAMemberException)
        {
            // Like a foreign resource: somebody who is not a member must not be
            // able to tell whether the company exists.
            await ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status404NotFound, "Request failed", "no such company");
        }
        catch (LastAdminMayNotLeaveException error)
        {
            // 409, not 403: the caller MAY remove, just not this one member
            // right now. A 403 would deny them a permission they have.
            await ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status409Conflict, "Request failed", error.Message);
        }
        catch (Exception error) when (error is OnlyAdminsMayInviteException
                                              or OnlyAdminsMayRemoveException)
        {
            // A statement about the caller, not about the resource — a 403
            // reveals nothing they do not already know.
            await ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status403Forbidden, "Request failed", error.Message);
        }
        catch (Exception error) when (error is InvitationExpiredException
                                              or NotYourInvitationException)
        {
            await ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status400BadRequest, "Request failed", error.Message);
        }
        catch (InvitationInvalidException error)
        {
            await ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status404NotFound, "Request failed", error.Message);
        }

        _ = cancellationToken;
    }

    private static Task NotAuthenticated(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");

    private static Task Refused(HttpContext context, string code) => code switch
    {
        "account_not_confirmed" => ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status403Forbidden, "Request failed",
            "Confirm your email address before creating a company"),

        "public_email_domain" => ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status422UnprocessableEntity, "Request failed",
            "A public email provider cannot be claimed as a company"),

        // 409 and not 404: the caller is being told their own domain is taken,
        // which they can see from their own address. It is not a probe.
        "domain_already_claimed" => ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status409Conflict, "Request failed",
            "That domain already belongs to a company"),

        _ => ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status400BadRequest, "Request failed",
            "A company name must not be empty")
    };
}
