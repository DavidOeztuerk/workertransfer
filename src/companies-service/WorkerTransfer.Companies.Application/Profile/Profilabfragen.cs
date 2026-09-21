using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Companies.Application.Nachrichten;
using WorkerTransfer.Companies.Domain.EmployerProfiles;

namespace WorkerTransfer.Companies.Application.Profile;

/// <summary>Das eigene Profile.</summary>
/// <remarks>
/// Ohne Ergebnistyp: „noch keins angelegt" ist ein Zustand, kein Fehler, und
/// die Oberfläche zeigt darauf ein leeres Formular.
/// </remarks>
public sealed record MyProfileQuery(TenantId Firma) : IAbfrage<EmployerProfile?>;

/// <summary>Das öffentliche Profile eines Unternehmens.</summary>
/// <remarks>
/// Anders als beim eigenen ist „noch keins" hier kein Formular, sondern eine
/// Job, die anonym bleibt — der Endpunkt antwortet 404. Ein Profile zu
/// erzwingen, bevor jemand ausschreiben darf, wäre eine Kopplung zwischen zwei
/// Diensten für eine Regel, die niemand verlangt hat.
/// </remarks>
public sealed record PublicProfileQuery(TenantId Firma) : IAbfrage<EmployerProfile?>;

/// <summary>Die Karriere-Seite hinter einem Kürzel.</summary>
public sealed record ProfileBySlugQuery(string Slug) : IAbfrage<EmployerProfile?>;

/// <summary>Beantwortet die drei Lesefragen.</summary>
/// <remarks>
/// Kein Consent-Aufruf, und das ist kein Versehen: ein EmployerProfile ist
/// eine Aussage des Unternehmens über sich selbst, und es gibt niemanden, der
/// einwilligen könnte. Es öffentlich zu machen ist sein Zweck; es hinter eine
/// Anmeldung zu legen wäre das Gegenteil — genau wie bei den Jobs.
/// </remarks>
public sealed class ProfileQueries(IProfileStore store) :
    IRequestHandler<MyProfileQuery, EmployerProfile?>,
    IRequestHandler<PublicProfileQuery, EmployerProfile?>,
    IRequestHandler<ProfileBySlugQuery, EmployerProfile?>
{
    /// <inheritdoc />
    public Task<EmployerProfile?> Handle(
        MyProfileQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.GetAsync(request.Firma, cancellationToken);
    }

    /// <inheritdoc />
    public Task<EmployerProfile?> Handle(
        PublicProfileQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.GetAsync(request.Firma, cancellationToken);
    }

    /// <inheritdoc />
    public Task<EmployerProfile?> Handle(
        ProfileBySlugQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.GetAsync(request.Slug, cancellationToken);
    }
}
