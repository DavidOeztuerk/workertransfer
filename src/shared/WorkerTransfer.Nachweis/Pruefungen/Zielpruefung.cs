using Microsoft.Extensions.Configuration;

namespace WorkerTransfer.Nachweis.Pruefungen;

/// <summary>Welche Hosts dieser Dienst anspricht — und wessen Recht dort gilt.</summary>
/// <remarks>
/// <para><strong>Sie liest dieselbe Ableitung, die die Egress-Grenze
/// erlaubt</strong> (<see cref="Zielkunde.Hosts"/>). Das ist die ganze Zusage
/// dieser Prüfung: was hier steht, ist nicht eine gepflegte Liste neben der
/// Wirklichkeit, sondern dieselbe Quelle.</para>
///
/// <para><strong>Was sie nicht sagt.</strong> In welchem Land ein öffentlicher
/// Anbieter verarbeitet, sagt sein Name nicht — und auflösen wäre eine
/// Schätzung im Gewand einer Messung. Sie teilt deshalb in zwei Lagen, die ein
/// Name sicher hergibt, und gibt die Rechtsraumfrage an einen Menschen weiter.</para>
///
/// <para><strong><see cref="Stand.Hinweis"/> ist hier kein Mangel.</strong> Ein
/// Ziel im öffentlichen Netz ist der Normalfall, sobald jemand ein Modell
/// benutzt, das nicht im eigenen Haus steht. Was es auslöst, ist eine Frage —
/// welche Garantie es deckt —, und Fragen sind Hinweise.</para>
/// </remarks>
/// <param name="konfiguration">Die Konfiguration des Dienstes.</param>
public sealed class Zielpruefung(IConfiguration konfiguration) : IPruefung
{
    /// <inheritdoc />
    public string Id => "wt.grenze.ziele";

    /// <inheritdoc />
    public Bereich Bereich => Bereich.Grenze;

    /// <inheritdoc />
    /// <remarks>
    /// Ein Ziel ist ein Empfänger, und ein Empfänger im öffentlichen Netz ist
    /// womöglich einer in einem Drittland. Welches, sagt sein Name nicht — und
    /// genau das steht im <c>Leser</c>.
    /// </remarks>
    public IReadOnlyList<Rechtsbezug> Bezuege =>
    [
        Rechtsbezuege.Verzeichnis,
        Rechtsbezuege.Drittland
    ];

    /// <inheritdoc />
    public Task<Befund> LaufenAsync(CancellationToken ct = default)
    {
        var hosts = Zielkunde.Hosts(konfiguration);

        if (hosts.Count == 0)
        {
            return Task.FromResult(new Befund(
                Id,
                Bereich,
                Stand.NichtAnwendbar,
                "Dieser Dienst ruft laut seiner Konfiguration keine Adresse — "
                + "die Egress-Grenze lässt damit nichts hinaus.",
                "Wer ihm ein Ziel gibt, trägt es in die Umgebung ein; nur von "
                + "dort erfährt die Grenze davon."));
        }

        var draussen = hosts.Where(host => Zielkunde.Wo(host) == Lage.Draussen).ToList();
        var innen = hosts.Where(host => Zielkunde.Wo(host) == Lage.EigenesNetz).ToList();

        var zusammenfassung =
            $"{Zielkunde.Zahl(hosts.Count)} Ziel(e): "
            + $"{Zielkunde.Zahl(innen.Count)} im eigenen Netz"
            + (innen.Count > 0 ? $" ({string.Join(", ", innen)})" : "")
            + $", {Zielkunde.Zahl(draussen.Count)} im öffentlichen Netz"
            + (draussen.Count > 0 ? $" ({string.Join(", ", draussen)})" : "")
            + ". Die Grenze weist jedes Ziel ab, das hier nicht steht.";

        return Task.FromResult(new Befund(
            Id,
            Bereich,
            draussen.Count > 0 ? Stand.Hinweis : Stand.Erfuellt,
            zusammenfassung,
            draussen.Count > 0
                ? "Für jedes Ziel im öffentlichen Netz gehört in den "
                  + "Aktenschrank, wer dort verarbeitet, in welchem Land, und "
                  + "welche Garantie die Übermittlung deckt."
                : "Ein neuer Wert in der Umgebung, der sich als http-Adresse "
                  + "lesen lässt, erweitert diese Liste — und damit das, was "
                  + "das Haus verlassen darf."));
    }
}
