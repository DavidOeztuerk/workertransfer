using Microsoft.Extensions.DependencyInjection;

namespace WorkerTransfer.Nachweis.Pruefungen;

/// <summary>Steht zwischen der Frage an den Ledger und dem Ledger noch etwas?</summary>
/// <remarks>
/// <para><strong>Was hier gemessen wird, und was nicht.</strong> Gemessen wird,
/// welcher Typ tatsächlich aufgelöst wird, wenn dieser Dienst das
/// Einwilligungstor benutzt — und ob das derselbe ist, der unmittelbar mit dem
/// Ledger spricht. Ein Zwischenspeicher, ein Umhüller, ein „nur für eine
/// Minute“ kommt in diesem System nicht als Konfiguration, sondern als ein
/// weiterer Typ zwischen den beiden. Genau den sieht diese Prüfung.</para>
///
/// <para><strong>Sie behauptet nicht, einen Widerruf ausprobiert zu
/// haben.</strong> Das könnte sie nur, indem sie in den Ledger schriebe, und
/// eine Betriebsauskunft, die Einwilligungen einer Person gewährt und widerruft,
/// um sich selbst zu belegen, wäre der schlechteste Tausch in diesem Baum. Was
/// sie zeigt, ist die Vorbedingung, an der ADR-0013 in der Praxis scheitert:
/// dass niemand einen Cache dazwischengeschoben hat.</para>
///
/// <para><strong>Warum <see cref="Stand.Fehlt"/> und nicht ein Hinweis, wenn
/// dort etwas steht.</strong> ADR-0013 ist keine Empfehlung: ein Widerruf muss
/// beim nächsten Lesen wirken. Ein Zwischenspeicher davor ist kein
/// Leistungsdetail, sondern ein Regelbruch — und er ist unsichtbar, weil alles
/// weiterhin funktioniert, nur eben mit dem Stand von vorhin.</para>
/// </remarks>
/// <typeparam name="TTor">Der Port, über den dieser Dienst den Ledger fragt.</typeparam>
/// <param name="anbieter">Der Container — es wird in einem eigenen Bereich aufgelöst.</param>
/// <param name="unmittelbar">Der Adapter, der wirklich mit dem Ledger spricht.</param>
public sealed class Widerrufspruefung<TTor>(IServiceProvider anbieter, Type unmittelbar)
    : IPruefung
    where TTor : notnull
{
    /// <inheritdoc />
    public string Id => "wt.einwilligung.wirkt";

    /// <inheritdoc />
    public Bereich Bereich => Bereich.Einwilligung;

    /// <inheritdoc />
    /// <remarks>
    /// Art. 7 Abs. 3 verlangt, dass ein Widerruf wirkt. Dass er beim NÄCHSTEN
    /// Lesen wirkt und nicht beim übernächsten, ist die Tatsache, die hier
    /// gemessen wird — und Art. 5 Abs. 2 ist der Grund, sie aufzuschreiben.
    /// </remarks>
    public IReadOnlyList<Rechtsbezug> Bezuege =>
    [
        Rechtsbezuege.Widerruf,
        Rechtsbezuege.Rechenschaft
    ];

    /// <inheritdoc />
    public Task<Befund> LaufenAsync(CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(anbieter);
        ArgumentNullException.ThrowIfNull(unmittelbar);

        // Ein eigener Bereich, weil das Tor je Anfrage lebt. Aus dem
        // Wurzelanbieter heraus aufzuloesen wuerfe bei einem bereichsgebundenen
        // Dienst — und das saehe aus wie ein Befund, waere aber ein Fehler
        // dieser Pruefung.
        using var bereich = anbieter.CreateScope();

        var aufgeloest = bereich.ServiceProvider.GetService<TTor>();

        if (aufgeloest is null)
        {
            return Task.FromResult(new Befund(
                Id,
                Bereich,
                Stand.NichtAnwendbar,
                $"Dieser Dienst fragt den Ledger nicht: für {Kurz(typeof(TTor))} "
                + "ist nichts registriert.",
                "Wer hier eine Sichtbarkeit entscheidet, fragt den Ledger — und "
                + "zwar synchron und ohne Zwischenspeicher (ADR-0013)."));
        }

        var tatsaechlich = aufgeloest.GetType();

        if (tatsaechlich == unmittelbar)
        {
            return Task.FromResult(new Befund(
                Id,
                Bereich,
                Stand.Erfuellt,
                $"Zwischen der Frage ({Kurz(typeof(TTor))}) und dem Ledger "
                + $"steht kein weiterer Typ: aufgelöst wird {Kurz(unmittelbar)}, "
                + "der Adapter, der unmittelbar fragt. Kein Zwischenspeicher, "
                + "kein Umhüller.",
                "Ein Umhüller an dieser Stelle — auch einer, der nur für eine "
                + "Minute merkt — stößt diesen Befund um. Ein Widerruf wirkte "
                + "dann beim übernächsten Lesen, und niemand sähe es."));
        }

        return Task.FromResult(new Befund(
            Id,
            Bereich,
            Stand.Fehlt,
            $"Zwischen der Frage ({Kurz(typeof(TTor))}) und dem Ledger steht "
            + $"{Kurz(tatsaechlich)} statt {Kurz(unmittelbar)}.",
            "ADR-0013: ein Widerruf muss beim nächsten Lesen wirken. Solange "
            + "ein anderer Typ dazwischensteht, ist ungeklärt, ob er die "
            + "Antwort aufhebt — und ein Cache an dieser Stelle ist kein "
            + "Leistungsdetail, sondern ein Regelbruch, den man nicht bemerkt."));
    }

    /// <summary>Ein Typname ohne Namensraum — eine Gestalt, kein Wert.</summary>
    private static string Kurz(Type typ) => typ.Name;
}
