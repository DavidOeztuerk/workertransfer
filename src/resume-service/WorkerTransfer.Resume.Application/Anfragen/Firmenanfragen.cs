using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Resume.Application.Nachrichten;
using WorkerTransfer.Resume.Domain.Anfragen;

namespace WorkerTransfer.Resume.Application.Anfragen;

/// <summary>The company's own requests. Without a "does it still hold" field.</summary>
/// <remarks>
/// The company already has the answer in the form of the data it does or does
/// not get; a field here would be a second statement about the same fact — and
/// one that could be polled without ever reading a résumé.
/// </remarks>
public sealed record FirmenanfragenAbfrage(TenantId Firma) : IAbfrage<IReadOnlyList<Anfrage>>;

/// <inheritdoc cref="FirmenanfragenAbfrage" />
public sealed class FirmenanfragenHandler(IAnfragenSpeicher speicher)
    : IRequestHandler<FirmenanfragenAbfrage, IReadOnlyList<Anfrage>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Anfrage>> Handle(
        FirmenanfragenAbfrage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return speicher.FuerFirmaAsync(request.Firma, cancellationToken);
    }
}
