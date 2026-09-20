using System.Diagnostics.CodeAnalysis;

namespace WorkerTransfer.Nachweis;

/// <summary>Eine Lesung: alle Befunde eines Dienstes zu einem Zeitpunkt.</summary>
/// <remarks>
/// <para><strong>Eine Lesung, zwei Darstellungen.</strong> Die Seite und
/// <c>bericht.json</c> kommen aus demselben Objekt. Zwei Abfragepfade zu einer
/// Aussage laufen auseinander, und beim ersten Mal merkt es niemand: die Seite
/// zeigt dann einen Befund, den die Daten nicht kennen, oder umgekehrt — und
/// welcher von beiden in das unterschriebene Dokument geriet, sagt hinterher
/// niemand mehr.</para>
///
/// <para>Die Befunde stehen nach <see cref="Befund.Id"/> sortiert. Das ist
/// keine Kosmetik: über dieselbe Lage gelesen ergibt die Lesung dieselbe
/// Reihenfolge, und erst dann kann eine Signatur über die kanonische Form
/// nachgerechnet werden (Phase 4).</para>
/// </remarks>
/// <param name="Dienst">Wer geantwortet hat.</param>
/// <param name="Zeitpunkt">Wann gelesen wurde.</param>
/// <param name="Befunde">Was vorgefunden wurde, nach Kennung sortiert.</param>
public sealed record Lesung(
    string Dienst,
    DateTimeOffset Zeitpunkt,
    IReadOnlyList<Befund> Befunde)
{
    /// <summary>Die Befunde eines Bereichs.</summary>
    /// <param name="bereich">Welcher.</param>
    /// <returns>Seine Befunde, in der Reihenfolge der Lesung.</returns>
    public IReadOnlyList<Befund> Aus(Bereich bereich) =>
        [.. Befunde.Where(befund => befund.Bereich == bereich)];

    /// <summary>Ob irgendein Befund eine Zusage als nicht eingelöst meldet.</summary>
    /// <remarks>
    /// Nur <see cref="Stand.Fehlt"/>. <see cref="Stand.Hinweis"/> ist kein
    /// Mangel, sondern eine Frage an einen Menschen — ein Tor, das darauf rot
    /// geht, wird nach der zweiten Woche abgeschaltet.
    /// </remarks>
    public bool IrgendetwasFehlt => Befunde.Any(befund => befund.Stand == Stand.Fehlt);
}

/// <summary>Fährt die Prüfungen eines Dienstes und macht daraus eine Lesung.</summary>
/// <remarks>
/// <para><strong>Er fängt jede Ausnahme, und das ist seine eigentliche
/// Aufgabe.</strong> Eine Prüfung sieht bei ihrem Gegenstand nach, und
/// Gegenstände sind hier Anbieter, Datenbanken und Ledger. Deren Ausnahmen
/// tragen Endpunkte, Verbindungszeichenfolgen und gelegentlich die Nutzlast —
/// <c>ex.Message</c> durchzureichen hieße, genau das in ein Dokument zu
/// schreiben, das jemand herumreicht. Übrig bleibt der <em>Typname</em>, und
/// der ist eine Gestalt.</para>
///
/// <para><strong>Eine abgebrochene Prüfung meldet <see cref="Stand.Fehlt"/>,
/// nicht <see cref="Stand.Hinweis"/>.</strong> Ihr Gegenstand bleibt unbelegt,
/// und unbelegt ist in einem Nachweis kein Zwischenzustand. Das Tor geht damit
/// rot, wenn eine Prüfung kaputt ist — was richtig ist: eine Prüfung, die nicht
/// antwortet, sieht sonst aus wie eine, die nichts gefunden hat.</para>
/// </remarks>
/// <param name="dienstname">Wie der Dienst sich nennt.</param>
/// <param name="pruefungen">Was dieser Dienst über sich beantworten kann.</param>
/// <param name="zeit">Die Uhr — als Anbieter, damit ein Test sie stellen kann.</param>
public sealed class Nachweislauf(
    string dienstname,
    IEnumerable<IPruefung> pruefungen,
    TimeProvider zeit)
{
    /// <summary>Wie lange eine einzelne Prüfung antworten darf.</summary>
    /// <remarks>
    /// Eine Prüfung fragt bei einem Gegenstand nach, und ein Gegenstand kann
    /// hängen. Ohne dieses Budget hinge die Seite mit ihm — und eine
    /// Betriebsauskunft, die hängt, wird genau dann nicht gelesen, wenn sie
    /// gebraucht wird. Zehn Sekunden: großzügiger als die fünf, die der Ledger
    /// bekommt, weil hier niemand vor einem Formular sitzt.
    /// </remarks>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    /// <summary>Fährt alles und berichtet.</summary>
    /// <param name="ct">Bricht ab, wenn der Aufrufer auflegt.</param>
    /// <returns>Die Lesung.</returns>
    public async Task<Lesung> LeseAsync(CancellationToken ct = default)
    {
        var befunde = new List<Befund>();

        // Nacheinander und nicht nebenläufig. Der Gewinn wäre eine halbe
        // Sekunde; der Preis wäre, dass vierzehn Prüfungen gleichzeitig an
        // ihren Gegenständen ziehen — und der Gegenstand ist hier der laufende
        // Betrieb.
        foreach (var pruefung in pruefungen)
        {
            befunde.Add(await SicherAsync(pruefung, ct));
        }

        return new Lesung(
            dienstname,
            zeit.GetUtcNow(),
            [.. befunde.OrderBy(befund => befund.Id, StringComparer.Ordinal)]);
    }

    /// <summary>Eine Prüfung, mit Budget und ohne durchgereichte Ausnahme.</summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Der Sinn dieser Stelle: was ein Gegenstand wirft, ist "
                        + "nicht vorhersagbar, und genau deshalb darf keine "
                        + "Meldung durch. Der Typname bleibt, der Text nicht.")]
    private async Task<Befund> SicherAsync(IPruefung pruefung, CancellationToken ct)
    {
        using var frist = CancellationTokenSource.CreateLinkedTokenSource(ct);
        frist.CancelAfter(Budget);

        try
        {
            var befund = await pruefung.LaufenAsync(frist.Token);

            // Die Bezüge der Prüfung hängen sich an den Befund, falls die
            // Prüfung sie nicht selbst angehängt hat. Damit steht die
            // Pflichtenseite auf der Lesung und muss nicht ein zweites Mal in
            // den Container sehen — zwei Wege zu einer Aussage.
            return befund.Bezuege.Count > 0
                ? befund
                : befund with { Bezuege = pruefung.Bezuege };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Der Aufrufer hat aufgelegt. Nichts ist kaputt, und der Lauf
            // gehört abgebrochen statt in einen Befund verwandelt.
            throw;
        }
        catch (Exception fehler)
        {
            return new Befund(
                pruefung.Id,
                pruefung.Bereich,
                Stand.Fehlt,
                $"Die Prüfung ist abgebrochen ({fehler.GetType().Name}). Ihr "
                + "Gegenstand bleibt damit unbelegt.",
                "Im Protokoll des Dienstes nachsehen, woran die Prüfung "
                + "scheitert. Solange sie nicht antwortet, sagt dieser Nachweis "
                + "über ihren Gegenstand nichts.")
            {
                Bezuege = pruefung.Bezuege
            };
        }
    }
}
