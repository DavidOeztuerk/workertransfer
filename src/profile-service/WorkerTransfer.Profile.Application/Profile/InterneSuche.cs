using MediatR;
using WorkerTransfer.Profile.Application.Nachrichten;
using WorkerTransfer.Profile.Domain.Profile;

namespace WorkerTransfer.Profile.Application.Profile;

/// <summary>Eine Seite Profile für einen anderen Dienst — ohne Ledgerfrage.</summary>
/// <param name="Anzahl">Wie viele Zeilen die Seite höchstens trägt.</param>
/// <param name="Zeiger">Wo es weitergeht, als Text aus der Anfrage.</param>
/// <param name="Faehigkeiten">
/// Die gesuchten Worte. <strong>Mindestens eines</strong> muss jemand
/// <em>genannt</em> haben — ODER, nicht UND.
/// </param>
/// <param name="Ort">Teiltext.</param>
/// <param name="NurRemote">Nur, wer Remote ausdrücklich angekreuzt hat.</param>
/// <remarks>
/// <para><strong>Ohne Ledgerfrage, und das ist die auffälligste Zeile dieses
/// Typs.</strong> Sie fehlt nicht, sie steht woanders: scout-service fragt den
/// Ledger für die ganze Seite auf einmal, weil es dort genau EINE Stelle geben
/// soll, an der über Sichtbarkeit entschieden wird (ADR-0036 Entscheidung 1).
/// Zwei Stellen wären zwei Wahrheiten — und die stille Hälfte davon zeigt
/// Profile, die niemand freigegeben hat.</para>
///
/// <para>Deshalb hängt diese Abfrage an einer Tür hinter dem gemeinsamen
/// Geheimnis und hat keine Gateway-Route. Wer sie aufruft, ist ein Dienst, und
/// er hat den Ledger noch zu fragen.</para>
///
/// <para><strong>ODER und nicht UND, anders als <c>/candidates</c>.</strong>
/// Unter UND erfüllte jeder Treffer alle Bedingungen — jedes Häkchen des
/// scout-service wäre gesetzt, und „welche Fähigkeit fehlt" hätte keine
/// Antwort (ADR-0036 Entscheidung 2). Auch die erste Auflage wäre dann leer:
/// es gäbe nichts, wonach man sortieren könnte. Die Auskunft, die der Scout
/// geben soll, beginnt hier.</para>
///
/// <para>Gesucht wird ausschliesslich über <em>genannte</em> Fähigkeiten
/// (ADR-0033): über das, was eine Person selbst in ihr Profil getippt hat. Ein
/// Beleg — ein GitHub-Topic, eine Technologie an einer Station — ist eine
/// Aussage über ein Artefakt, und wer danach suchte, machte sie stillschweigend
/// zu einer über den Menschen. profile-service kennt Belege gar nicht, und
/// diese Abfrage ist der Grund, das so zu lassen.</para>
/// </remarks>
public sealed record InterneProfilsucheAbfrage(
    int Anzahl,
    string? Zeiger,
    IReadOnlyList<string>? Faehigkeiten = null,
    string Ort = "",
    bool NurRemote = false) : IAbfrage<Profilseite>;

/// <summary>Reicht die Seite durch, in stabiler Reihenfolge.</summary>
/// <remarks>
/// Dieselbe Obergrenze wie <see cref="KandidatenHandler.Hoechstzahl"/> und aus
/// demselben Grund: die Sammelfrage des Ledgers trägt höchstens hundert Paare,
/// also fünfzig Menschen — wer hier mehr herausgäbe, baute eine Seite, deren
/// Freigabe niemand am Stück prüfen kann.
/// </remarks>
public sealed class InterneProfilsucheHandler(IProfilspeicher speicher)
    : IRequestHandler<InterneProfilsucheAbfrage, Profilseite>
{
    /// <inheritdoc />
    public Task<Profilseite> Handle(
        InterneProfilsucheAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var anzahl = Math.Clamp(
            request.Anzahl <= 0 ? KandidatenHandler.Vorgabe : request.Anzahl,
            1,
            KandidatenHandler.Hoechstzahl);

        var faehigkeiten = (request.Faehigkeiten ?? [])
            .Select(eintrag => eintrag.Trim())
            .Where(eintrag => eintrag.Length > 0)
            .Take(KandidatenHandler.HoechstzahlFilter)
            .ToArray();

        return speicher.SeiteAsync(
            new Seitenanfrage(
                anzahl, Seitenzeiger.Lies(request.Zeiger), faehigkeiten,
                request.Ort, request.NurRemote, Irgendeine: true),
            cancellationToken);
    }
}
