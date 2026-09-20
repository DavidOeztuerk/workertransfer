namespace WorkerTransfer.Nachweis.Pruefungen;

/// <summary>Was eine Aufzeichnung von Modellaufrufen abdeckt.</summary>
/// <remarks>
/// Die vier Angaben, nach denen Art. 12 KI-VO fragt, und keine fünfte:
/// <em>seit wann</em>, <em>bis wann</em>, <em>wie viele Ereignisse</em>, und
/// <em>ob sie seither unverändert sind</em>. Kein Inhalt — was in einem
/// Modellaufruf stand, gehört nicht in eine Aufzeichnung, die ein Nachweis
/// zusammenfasst.
/// </remarks>
/// <param name="Von">Der älteste aufgezeichnete Aufruf.</param>
/// <param name="Bis">Der jüngste.</param>
/// <param name="Ereignisse">Wie viele dazwischen liegen.</param>
/// <param name="Siegel">
/// Woran sich zeigen lässt, dass seither nichts geändert wurde — eine
/// fortgeschriebene Prüfsumme. Leer heißt: es gibt keins, und dann kann die
/// Aufzeichnung nur ihr Dasein belegen, nicht ihre Unversehrtheit.
/// </param>
public sealed record Aufzeichnungsspanne(
    DateTimeOffset Von,
    DateTimeOffset Bis,
    long Ereignisse,
    string Siegel);

/// <summary>Wer sagen kann, was aufgezeichnet wurde.</summary>
/// <remarks>
/// <para><strong>Diesen Port setzt heute nichts um, und das ist der
/// Befund.</strong> ADR-0024 entscheidet, dass nichts gespeichert wird — nicht
/// der Prompt, nicht die Antwort, kein Ledger-Eintrag —, und diese Entscheidung
/// ist das Gegenteil einer Aufzeichnungspflicht. Der Port steht hier, damit
/// <c>wt.ki.protokoll</c> etwas zu fragen hat, und damit der Tag, an dem
/// jemand aufzeichnen muss, eine Stelle hat, an der er es anmeldet, statt eine
/// zweite Wahrheit neben diesem Nachweis aufzubauen.</para>
///
/// <para>Art. 12 und Art. 26 Abs. 6 KI-VO binden Betreiber <strong>ab dem
/// 02.12.2027</strong>, und ob sie diesen Einsatz überhaupt erfassen, hängt an
/// der Anhang-III-Frage, die dieses Repositorium nicht beantwortet.</para>
/// </remarks>
public interface IModellaufzeichnung
{
    /// <summary>Was vorliegt, oder <c>null</c>, wenn nichts aufgezeichnet wurde.</summary>
    /// <param name="ct">Bricht ab, wenn der Aufrufer auflegt.</param>
    /// <returns>Die Spanne.</returns>
    Task<Aufzeichnungsspanne?> SpanneAsync(CancellationToken ct = default);
}

/// <summary>Wird jeder Modellaufruf aufgezeichnet — und lässt er sich unverändert zeigen?</summary>
/// <remarks>
/// <para><strong>Der Befund ist heute ein Hinweis und kein Mangel, und der
/// Unterschied ist die ganze Arbeit dieser Prüfung.</strong> Dass nichts
/// aufgezeichnet wird, ist hier eine Entscheidung (ADR-0024: „Nichts wird
/// gespeichert — nicht der Prompt, nicht die Antwort“), keine Lücke. Wer sie in
/// einen roten Befund verwandelte, bekäme ein Tor, das auf eine gewollte
/// Zurückhaltung rot geht — und ein solches Tor schaltet der nächste Mensch ab.</para>
///
/// <para><strong>Verschweigen darf man es trotzdem nicht.</strong> Wenn
/// jemand eines Tages feststellt, dass dieser Einsatz unter Anhang III fällt,
/// ist „es wird nichts aufgezeichnet“ die erste Tatsache, die er braucht — und
/// sie steht dann datiert in einem Dokument, statt in einem ADR gesucht werden
/// zu müssen.</para>
///
/// <para><strong>Die Kontoeinstellungen tragen einen Schalter dafür</strong>
/// (<c>ai_audit_log</c>), und <paramref name="schalterVorhanden"/> ist da, um
/// genau das zu sagen: ein Schalter, den niemand liest, ist eine Zusage an die
/// Person, die niemand einlöst. Das ist ein Befund, den ein Screenshot der
/// Kontoseite genau andersherum erzählt.</para>
/// </remarks>
/// <param name="nahtVorhanden">
/// Ob dieser Dienst <em>baulich</em> eine KI-Naht hat — unabhängig davon, ob in
/// dieser Instanz ein Anbieter eingetragen ist.
/// </param>
/// <param name="anbieterEingerichtet">
/// Ob in dieser Instanz wirklich jemand gerufen wird.
/// </param>
/// <param name="aufzeichnung">Wer Auskunft geben kann, oder <c>null</c>.</param>
/// <param name="schalterVorhanden">
/// Ob die Oberfläche einen Schalter „Anfragen protokollieren“ zeigt. Das ist
/// identity-service, und es ist ein Gegenstand für sich: ohne diesen Fall
/// fiele der interessante Befund weg — ein Schalter, den niemand liest, wäre
/// dort „nicht anwendbar“.
/// </param>
public sealed class Aufzeichnungspruefung(
    bool nahtVorhanden,
    bool anbieterEingerichtet,
    IModellaufzeichnung? aufzeichnung,
    bool schalterVorhanden = false) : IPruefung
{
    /// <inheritdoc />
    public string Id => "wt.ki.protokoll";

    /// <inheritdoc />
    public Bereich Bereich => Bereich.KI;

    /// <inheritdoc />
    /// <remarks>
    /// Beide KI-VO-Zitate tragen ihr Datum: sie binden ab dem 02.12.2027 und
    /// nur, soweit der Einsatz unter Anhang III fällt. Deshalb steht Anhang III
    /// daneben — ohne ihn liest sich die Aufzeichnungspflicht wie eine, die
    /// schon gilt.
    /// </remarks>
    public IReadOnlyList<Rechtsbezug> Bezuege =>
    [
        Rechtsbezuege.Aufzeichnung,
        Rechtsbezuege.Betreiberpflichten,
        Rechtsbezuege.AnhangIII
    ];

    /// <inheritdoc />
    public async Task<Befund> LaufenAsync(CancellationToken ct = default)
    {
        if (!nahtVorhanden && !schalterVorhanden)
        {
            return new Befund(
                Id,
                Bereich,
                Stand.NichtAnwendbar,
                "Dieser Dienst fragt kein Modell und hält keine Einstellung "
                + "darüber. Es gibt nichts aufzuzeichnen.",
                "Wer hier eine KI-Naht einzieht, beantwortet mit ihr die Frage, "
                + "ob ihre Aufrufe aufgezeichnet werden.");
        }

        // DER UNTERSCHIED ZWISCHEN „HAT KEINE NAHT" UND „HAT EINE, DIE
        // SCHWEIGT" — und er ist nicht kosmetisch.
        //
        // Gemessen am erzeugten Dokument: scout-service meldete hier „fragt kein
        // Modell", waehrend `wt.ki.naht` zwei Zeilen darueber die Feldmenge
        // seiner Ansprache auffuehrte. Zwei Befunde auf einer Seite, die sich
        // widersprechen — und wer das Dokument liest, glaubt entweder dem
        // einen oder dem anderen, ohne zu wissen, welchem.
        //
        // Die Naht ist BAULICH da; dass in dieser Instanz niemand gerufen wird,
        // ist eine Aussage ueber die Instanz. Beide Saetze sind wahr, und nur
        // zusammen sind sie nicht irrefuehrend.
        if (nahtVorhanden && !anbieterEingerichtet && !schalterVorhanden)
        {
            return new Befund(
                Id,
                Bereich,
                Stand.NichtAnwendbar,
                "Dieser Dienst hat eine KI-Naht, aber in dieser Instanz ist kein "
                + "Anbieter eingetragen: es wird niemand gerufen, und damit gibt "
                + "es nichts aufzuzeichnen.",
                "Sobald ein Anbieter eingetragen ist, wird aus diesem Befund ein "
                + "Hinweis — und die Frage nach Art. 12 KI-VO steht dann "
                + "wirklich im Raum.");
        }

        var spanne = aufzeichnung is null ? null : await aufzeichnung.SpanneAsync(ct);

        if (spanne is null)
        {
            return new Befund(
                Id,
                Bereich,
                Stand.Hinweis,
                "Modellaufrufe werden nicht aufgezeichnet. Das ist eine "
                + "Entscheidung und keine Lücke: nach ADR-0024 wird nichts "
                + "gespeichert — nicht der Prompt, nicht die Antwort, kein "
                + "Ledger-Eintrag. Der Knopf ist die Einwilligung, informiert "
                + "und je Benutzung."
                + (schalterVorhanden
                    ? " Die Kontoeinstellungen zeigen dennoch einen Schalter "
                      + "„Anfragen protokollieren“; ihn liest in diesem Baum "
                      + "niemand."
                    : ""),
                "Art. 12 und Art. 26 Abs. 6 KI-VO binden Betreiber ab dem "
                + "02.12.2027 und nur, soweit der Einsatz unter Anhang III "
                + "fällt. Wer aufzeichnen muss, setzt IModellaufzeichnung um "
                + "und meldet es an — und entscheidet dabei zuerst, wie eine "
                + "Aufzeichnung über Modellaufrufe nicht zu der Sammlung wird, "
                + "die dieser Entwurf gerade vermeidet."
                + (schalterVorhanden
                    ? " Bis dahin verspricht der Schalter in den "
                      + "Kontoeinstellungen etwas, das nicht geschieht."
                    : ""));
        }

        var unversehrt = spanne.Siegel.Length > 0;

        return new Befund(
            Id,
            Bereich,
            unversehrt ? Stand.Erfuellt : Stand.Hinweis,
            $"Aufgezeichnet sind {spanne.Ereignisse} Modellaufrufe vom "
            + $"{Tag(spanne.Von)} bis {Tag(spanne.Bis)}"
            + (unversehrt
                ? ", und die Aufzeichnung trägt ein fortgeschriebenes Siegel."
                : ", ohne Siegel — belegt ist ihr Dasein, nicht ihre "
                  + "Unversehrtheit."),
            unversehrt
                ? "Das Siegel nachzurechnen ist die Probe; ein Bruch darin "
                  + "heißt, dass jemand an der Aufzeichnung war."
                : "Ohne Siegel lässt sich eine nachträgliche Änderung nicht "
                  + "ausschließen — Art. 12 KI-VO fragt nach einer "
                  + "Aufzeichnung, auf die man sich stützen kann.");
    }

    /// <summary>Ein Datum, ohne Uhrzeit und ohne Kultur der Maschine.</summary>
    private static string Tag(DateTimeOffset zeitpunkt) =>
        zeitpunkt.UtcDateTime.ToString(
            "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);
}
