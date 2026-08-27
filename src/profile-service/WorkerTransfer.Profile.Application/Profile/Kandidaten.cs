using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Profile.Application.Nachrichten;
using WorkerTransfer.Profile.Application.Ports;
using WorkerTransfer.Profile.Domain.Profile;

namespace WorkerTransfer.Profile.Application.Profile;

/// <summary>Eine Seite Kandidaten, gefiltert auf die freigegebenen.</summary>
/// <param name="Anzahl">Wie viele Zeilen die Seite höchstens trägt.</param>
/// <param name="Zeiger">Wo es weitergeht, als Text aus der Anfrage.</param>
/// <param name="Firma">Das Unternehmen des Aufrufers — aus dem Token.</param>
/// <param name="Faehigkeiten">Alle davon muss jemand nennen — UND, nicht ODER.</param>
/// <param name="Ort">Teiltext.</param>
/// <param name="NurRemote">Nur, wer Remote ausdrücklich angekreuzt hat.</param>
public sealed record KandidatenAbfrage(
    int Anzahl,
    string? Zeiger,
    TenantId Firma,
    IReadOnlyList<string>? Faehigkeiten = null,
    string Ort = "",
    bool NurRemote = false) : IAbfrage<Profilseite>;

/// <summary>Holt eine Seite und fragt den Ledger EINMAL für alle Zeilen.</summary>
/// <remarks>
/// Die Seite kann weniger Einträge liefern als angefragt. Nachzuladen, bis sie
/// voll ist, würde über die Anzahl der Runden verraten, wie viele Profile
/// <em>nicht</em> freigegeben sind — und genau das ist die Auskunft, die der
/// Ledger schützt (ADR-0020 §4). Aus demselben Grund nennt die Oberfläche keine
/// Gesamtzahl.
/// <para>
/// Niemand bekommt eine Punktzahl: die Seite ist nach Änderungszeit sortiert,
/// nicht nach Passung. Ein sortierter Kandidatenstapel mit Prozentwert wäre der
/// Gesamtscore aus ADR-0022 durch die Hintertür — und der Prozentwert verbärge
/// das Einzige, was hilft, nämlich <em>welche</em> Fähigkeit fehlt.
/// </para>
/// </remarks>
public sealed class KandidatenHandler(IProfilspeicher speicher, IEinwilligungstor tor)
    : IRequestHandler<KandidatenAbfrage, Profilseite>
{
    /// <summary>Obergrenze je Seite.</summary>
    /// <remarks>
    /// Jede Zeile kostet eine Frage an den Ledger; ohne Deckel baute ein
    /// Aufrufer mit einer einzigen URL eine beliebig teure Abfrage. Und die
    /// Sammelfrage trägt höchstens hundert Paare — zwei Fähigkeiten je Person,
    /// also fünfzig Personen.
    /// </remarks>
    public const int Hoechstzahl = 50;

    /// <summary>Was eine Seite trägt, wenn niemand etwas anderes sagt.</summary>
    public const int Vorgabe = 20;

    /// <summary>
    /// Wie viele Fähigkeiten gefiltert werden dürfen.
    /// </summary>
    /// <remarks>
    /// Dieselbe Überlegung wie beim Seitendeckel: ohne Grenze baut ein Aufrufer
    /// mit einer URL eine beliebig teure Abfrage.
    /// </remarks>
    public const int HoechstzahlFilter = 10;

    /// <inheritdoc />
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    public async Task<Profilseite> Handle(
        KandidatenAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var anzahl = Math.Clamp(
            request.Anzahl <= 0 ? Vorgabe : request.Anzahl, 1, Hoechstzahl);

        var faehigkeiten = (request.Faehigkeiten ?? [])
            .Select(eintrag => eintrag.Trim())
            .Where(eintrag => eintrag.Length > 0)
            .Take(HoechstzahlFilter)
            .ToArray();

        var seite = await speicher.SeiteAsync(
            new Seitenanfrage(
                anzahl, Seitenzeiger.Lies(request.Zeiger), faehigkeiten,
                request.Ort, request.NurRemote),
            cancellationToken);

        if (seite.Eintraege.Count == 0)
        {
            return seite;
        }

        // EINE Frage für die ganze Seite. Vorher standen hier so viele Aufrufe
        // wie Zeilen mal Fähigkeiten — bis zu vierzig, jeder mit eigenem
        // Verbindungsaufbau. Parallel war nicht falsch, nur nicht genug:
        // aufsummierte Latenzen fielen weg, der Aufwand je Anfrage blieb.
        // Weiterhin synchron und ohne Zwischenspeicher (ADR-0013).
        var urteile = await tor.DarfSehenAlleAsync(
            [.. seite.Eintraege.Select(profil => profil.Wer)], request.Firma, cancellationToken);

        if (urteile.Count != seite.Eintraege.Count)
        {
            // Eine Antwort, die nicht zu den Fragen passt, lässt sich nicht
            // zuordnen — und falsch zuzuordnen hieße, das Profil der falschen
            // Person zu zeigen.
            throw new EinwilligungSchweigt(
                $"{urteile.Count} Antworten auf {seite.Eintraege.Count} Fragen");
        }

        var sichtbar = seite.Eintraege
            .Where((_, stelle) => urteile[stelle])
            .ToArray();

        return seite with { Eintraege = sichtbar };
    }
}
