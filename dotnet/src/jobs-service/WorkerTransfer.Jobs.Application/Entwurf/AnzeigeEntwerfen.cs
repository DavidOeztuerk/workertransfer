using MediatR;
using WorkerTransfer.Jobs.Application.Nachrichten;
using WorkerTransfer.Jobs.Application.Ports;

namespace WorkerTransfer.Jobs.Application.Entwurf;

/// <summary>Hilft einem Unternehmen, seine eigene Anzeige zu formulieren.</summary>
/// <remarks>
/// Der einzige Firmenagent, der ohne eigene Abwägung gebaut werden kann: er
/// sagt nie etwas <em>über</em> jemanden.
/// <para>
/// Nichts wird gespeichert — nicht der Prompt, nicht die Antwort. Kein
/// Gedächtnis, kein Vektorspeicher, kein Plan-Act-Reflect, kein Eintrag im
/// Consent-Ledger: der Knopf ist die Einwilligung, informiert und je Benutzung
/// (ADR-0024).
/// </remarks>
public sealed record AnzeigeEntwerfenAbfrage(
    string Titel,
    string Beschreibung,
    IReadOnlyList<string> Faehigkeiten,
    string Ort,
    string Wunsch) : IAbfrage<string>;

/// <inheritdoc cref="AnzeigeEntwerfenAbfrage" />
public sealed class AnzeigeEntwerfenHandler(IEntwerfer entwerfer)
    : IRequestHandler<AnzeigeEntwerfenAbfrage, string>
{
    /// <inheritdoc />
    /// <exception cref="EntwurfNichtVerfuegbar">Kein Anbieter, oder er schweigt.</exception>
    public Task<string> Handle(
        AnzeigeEntwerfenAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Ohne tenant_id und ohne Firmennamen — die Kontextklasse hat keine
        // Felder dafür, also kann auch nichts hineinrutschen.
        return entwerfer.EntwirfAsync(
            new Anzeigenentwurf(
                request.Titel, request.Beschreibung, request.Faehigkeiten,
                request.Ort, request.Wunsch),
            cancellationToken);
    }
}
