using MediatR;
using Noelia.Core.Identity;
using WorkerTransfer.Transfer.Application.Messages;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Domain.Employers;
using WorkerTransfer.Transfer.Domain.Releases;

namespace WorkerTransfer.Transfer.Application.Employers;

/// <summary>The person's route as she reads it: whom she named, and whether the ledger still says yes.</summary>
/// <remarks>
/// <c>Open</c> is <c>false</c> when she revoked on <c>/consents</c> and the row
/// still stands — then the route is closed, and the page says so instead of
/// pretending.
/// </remarks>
public sealed record RouteView(EmployerRoute Route, bool Open);

/// <summary>How opening a route turned out.</summary>
public abstract record RouteResult
{
    private RouteResult()
    {
    }

    /// <summary>Open.</summary>
    public sealed record Done(RouteView View) : RouteResult;

    /// <summary>No such company.</summary>
    public sealed record UnknownCompany : RouteResult;
}

/// <summary>Whether this company lets other companies ask about its staff.</summary>
public sealed record EmployerSettingQuery(TenantId Company) : IQueryMessage<bool>;

/// <summary>Switch it — an `admin` step, it binds the company.</summary>
public sealed record SetEmployerSettingCommand(TenantId Company, bool AcceptsInquiries)
    : ICommandMessage<EmployerSetting>;

/// <summary>The person's own route, or <c>null</c>.</summary>
public sealed record MyEmployerRouteQuery(SubjectId Subject) : IQueryMessage<RouteView?>;

/// <summary>Name the employer and open the route — one act (ADR-0050).</summary>
public sealed record OpenEmployerRouteCommand(SubjectId Subject, TenantId Employer)
    : ICommandMessage<RouteResult>;

/// <summary>Close the route: the consent goes, the row goes, running negotiations end.</summary>
public sealed record CloseEmployerRouteCommand(SubjectId Subject) : ICommandMessage<bool>;

/// <summary>Runs the two consents of the way to the employer (C1, ADR-0050).</summary>
public sealed class EmployerCommands(
    IEmployerSettingStore settings,
    IEmployerRouteStore routes,
    IReleaseStore releases,
    IConsentGate gate,
    IIdentityLookup identity,
    TimeProvider clock) :
    IRequestHandler<EmployerSettingQuery, bool>,
    IRequestHandler<SetEmployerSettingCommand, EmployerSetting>,
    IRequestHandler<MyEmployerRouteQuery, RouteView?>,
    IRequestHandler<OpenEmployerRouteCommand, RouteResult>,
    IRequestHandler<CloseEmployerRouteCommand, bool>
{
    /// <inheritdoc />
    public async Task<bool> Handle(EmployerSettingQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Never set means no: a company that did nothing has not allowed anything.
        return (await settings.GetAsync(request.Company, cancellationToken))?.AcceptsInquiries ?? false;
    }

    /// <inheritdoc />
    public async Task<EmployerSetting> Handle(SetEmployerSettingCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var setting = new EmployerSetting(request.Company, request.AcceptsInquiries, clock.GetUtcNow());
        await settings.SaveAsync(setting, cancellationToken);
        return setting;
    }

    /// <inheritdoc />
    public async Task<RouteView?> Handle(MyEmployerRouteQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await routes.GetAsync(request.Subject, cancellationToken) is not { } route)
        {
            return null;
        }

        // Asked, not assumed: a revocation on /consents closes the route while
        // this row still stands (ADR-0013).
        return new RouteView(
            route, await gate.EmployerRouteOpenAsync(request.Subject, route.Employer, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<RouteResult> Handle(OpenEmployerRouteCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await identity.CompanyExistsAsync(request.Employer, cancellationToken))
        {
            return new RouteResult.UnknownCompany();
        }

        var now = clock.GetUtcNow();

        if (await routes.GetAsync(request.Subject, cancellationToken) is { } previous
            && previous.Employer != request.Employer)
        {
            // Another employer named: the consent for the first does not
            // silently move to the second — it ends, and so does every
            // negotiation with the first.
            await gate.CloseEmployerRouteAsync(request.Subject, previous.Employer, cancellationToken);
            await CloseRunningAsync(request.Subject, previous.Employer, now, cancellationToken);
        }

        // The ledger first: a silent ledger leaves everything as it was rather
        // than a row that claims a consent nobody recorded.
        await gate.OpenEmployerRouteAsync(request.Subject, request.Employer, cancellationToken);

        var route = new EmployerRoute(request.Subject, request.Employer, now);
        await routes.SaveAsync(route, cancellationToken);

        return new RouteResult.Done(new RouteView(route, Open: true));
    }

    /// <inheritdoc />
    public async Task<bool> Handle(CloseEmployerRouteCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await routes.GetAsync(request.Subject, cancellationToken) is not { } route)
        {
            return true;
        }

        // Revoked before the row falls: if the delete failed, a row without a
        // consent is a closed route — the other order would leave a consent
        // without anybody knowing whom it names.
        await gate.CloseEmployerRouteAsync(request.Subject, route.Employer, cancellationToken);
        await routes.DeleteAsync(request.Subject, cancellationToken);
        await CloseRunningAsync(request.Subject, route.Employer, clock.GetUtcNow(), cancellationToken);

        return true;
    }

    private async Task CloseRunningAsync(
        SubjectId subject, TenantId employer, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var negotiation in await releases.RunningForAsync(subject, cancellationToken))
        {
            if (negotiation.Current != employer)
            {
                continue;
            }

            negotiation.Close(now);
            await releases.SaveAsync(negotiation, cancellationToken);
        }
    }
}
