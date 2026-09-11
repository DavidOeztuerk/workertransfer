using Girder.Core.Identity;
using WorkerTransfer.Advisor.Domain.Mandate;

namespace WorkerTransfer.Advisor.Domain.Gespraeche;

/// <summary>Dieser Schritt ist von hier aus nicht möglich.</summary>
public sealed class UebergangNichtErlaubt(Gespraechsstand jetzt, string was)
    : Exception($"A {Gespraechsstaende.Wort(jetzt)} conversation cannot be {was}");

/// <summary>Das ist das Gespräch eines anderen Menschen.</summary>
public sealed class NichtDeins() : Exception("This conversation belongs to someone else");

/// <summary>Wo ein Gespräch steht.</summary>
/// <remarks>
/// Vier Stände, und keiner davon ist eine Stufe. Der Stand sagt, ob noch
/// gesprochen wird; die <see cref="Stufe"/> sagt, wie weit geöffnet ist — und
/// sie kommt aus dem Ledger, nicht von hier.
/// </remarks>
public enum Gespraechsstand
{
    /// <summary>Man spricht.</summary>
    Laufend,

    /// <summary>Die Person ist einverstanden. Jetzt darf übergeben werden.</summary>
    Zugestimmt,

    /// <summary>Übergeben an transfer-service. Hier ist es damit zu Ende.</summary>
    Uebergeben,

    /// <summary>Beendet — von wem auch immer.</summary>
    Beendet
}

/// <summary>Die Worte, mit denen ein Stand auf der Leitung steht.</summary>
public static class Gespraechsstaende
{
    /// <summary>Das Wort zum Stand.</summary>
    public static string Wort(Gespraechsstand stand) => stand switch
    {
        Gespraechsstand.Laufend => "talking",
        Gespraechsstand.Zugestimmt => "agreed",
        Gespraechsstand.Uebergeben => "handed_over",
        Gespraechsstand.Beendet => "ended",
        _ => throw new ArgumentOutOfRangeException(nameof(stand))
    };

    /// <summary>Die Stände, in denen noch gesprochen wird.</summary>
    public static readonly IReadOnlyList<Gespraechsstand> Laufende =
    [
        Gespraechsstand.Laufend, Gespraechsstand.Zugestimmt
    ];
}

/// <summary>
/// Ein Gespräch zwischen einem Menschen und einem Unternehmen.
/// </summary>
/// <remarks>
/// <para><strong>Es lebt hier, solange es um Stufen und Mandat geht</strong>
/// (ADR-0037 Entscheidung 4). Erst die Einigung wird ein Vorgang in
/// transfer-service — und dort steht der Dreieckskonsens, der hier
/// ausdrücklich <em>nicht</em> nachgebaut wird.</para>
///
/// <para><strong>Keine Stufenspalte.</strong> Ein <c>hasStage</c> wäre die
/// Stufe als zweite Tür neben dem Ledger; die Stufe wird bei jedem Lesen dort
/// erfragt. Was dieses Aggregat hält, ist die Beziehung und ihr Stand, sonst
/// nichts.</para>
///
/// <para><strong>Und kein Nachrichtenverlauf.</strong> ADR-0037 Entscheidung 5
/// lässt offen, ob Menschen hier tippen; solange das offen ist, gibt es keine
/// Tabelle dafür — und damit auch keine Frage, ob eine KI hineinschreiben darf.
/// Der <see cref="Anlass"/> ist das eine Feld, das die Firma beim Eröffnen
/// schreibt.</para>
/// </remarks>
public sealed class Gespraech
{
    /// <summary>Wie lang der Anlass sein darf.</summary>
    public const int HoechstlaengeAnlass = 2000;

    private Gespraech(
        Guid id,
        SubjectId wer,
        TenantId firma,
        Gespraechsstand stand,
        string anlass,
        DateTimeOffset eroeffnetAm,
        DateTimeOffset geaendertAm)
    {
        Id = id;
        Wer = wer;
        Firma = firma;
        Stand = stand;
        Anlass = anlass;
        EroeffnetAm = eroeffnetAm;
        GeaendertAm = geaendertAm;
    }

    /// <summary>Welches Gespräch.</summary>
    public Guid Id { get; }

    /// <summary>Mit wem.</summary>
    public SubjectId Wer { get; }

    /// <summary>Welches Unternehmen.</summary>
    public TenantId Firma { get; }

    /// <summary>Wo es steht.</summary>
    public Gespraechsstand Stand { get; private set; }

    /// <summary>Was das Unternehmen beim Eröffnen geschrieben hat.</summary>
    public string Anlass { get; }

    /// <summary>Wann es begann.</summary>
    public DateTimeOffset EroeffnetAm { get; }

    /// <summary>Wann sich zuletzt etwas änderte.</summary>
    public DateTimeOffset GeaendertAm { get; private set; }

    /// <summary>Läuft es noch?</summary>
    public bool Laeuft => Gespraechsstaende.Laufende.Contains(Stand);

    /// <summary>Ein Unternehmen eröffnet ein Gespräch.</summary>
    /// <exception cref="Eingabefehler">Der Anlass ist zu lang.</exception>
    public static Gespraech Eroeffne(
        SubjectId wer, TenantId firma, string? anlass, DateTimeOffset jetzt) =>
        new(Guid.CreateVersion7(), wer, firma, Gespraechsstand.Laufend,
            Text(anlass), jetzt, jetzt);

    /// <summary>Das Gespräch, wie eine Zeile es hält.</summary>
    public static Gespraech Stelle_her(
        Guid id,
        SubjectId wer,
        TenantId firma,
        Gespraechsstand stand,
        string anlass,
        DateTimeOffset eroeffnetAm,
        DateTimeOffset geaendertAm) =>
        new(id, wer, firma, stand, anlass, eroeffnetAm, geaendertAm);

    /// <summary>Die Person ist einverstanden.</summary>
    /// <remarks>
    /// Nur die Person, und das ist der ganze Punkt: die Zustimmung, aus der ein
    /// Vorgang wird, gehört dem Menschen, um den es geht.
    /// </remarks>
    public void Stimme_zu(SubjectId durch, DateTimeOffset jetzt)
    {
        Meins(durch);
        Genau(Gespraechsstand.Laufend, "agreed to");
        Nach(Gespraechsstand.Zugestimmt, jetzt);
    }

    /// <summary>
    /// Das Unternehmen macht daraus einen Vorgang.
    /// </summary>
    /// <remarks>
    /// <strong>Erst nach der Zustimmung der Person</strong> — und deshalb ist
    /// die Übergabe ein Schritt des Unternehmens: der Vorgang in
    /// transfer-service beginnt damit, dass ein Unternehmen Interesse zeigt,
    /// und dort greifen dann dessen eigene Bedingungen (Marktfreigabe,
    /// Ansprechbarkeit). Hier wird davon nichts wiederholt.
    /// </remarks>
    public void Uebergib(DateTimeOffset jetzt)
    {
        Genau(Gespraechsstand.Zugestimmt, "handed over");
        Nach(Gespraechsstand.Uebergeben, jetzt);
    }

    /// <summary>Jemand beendet das Gespräch.</summary>
    /// <remarks>
    /// Aus jedem laufenden Stand, von beiden Seiten. Ein Verfahren, aus dem man
    /// nicht aussteigen kann, ist kein Verfahren, sondern eine Falle.
    /// </remarks>
    public void Beende(DateTimeOffset jetzt)
    {
        if (!Laeuft)
        {
            throw new UebergangNichtErlaubt(Stand, "ended");
        }

        Nach(Gespraechsstand.Beendet, jetzt);
    }

    private static string Text(string? wert)
    {
        var bereinigt = (wert ?? string.Empty).Trim();

        return bereinigt.Length > HoechstlaengeAnlass
            ? throw new Eingabefehler(
                $"the opening note exceeds {HoechstlaengeAnlass} characters")
            : bereinigt;
    }

    private void Meins(SubjectId akteur)
    {
        if (akteur != Wer)
        {
            throw new NichtDeins();
        }
    }

    private void Genau(Gespraechsstand gewollt, string was)
    {
        if (Stand != gewollt)
        {
            throw new UebergangNichtErlaubt(Stand, was);
        }
    }

    private void Nach(Gespraechsstand ziel, DateTimeOffset jetzt)
    {
        Stand = ziel;
        GeaendertAm = jetzt;
    }
}

/// <summary>Findet und speichert Gespräche.</summary>
public interface IGespraechsspeicher
{
    /// <summary>Ein Gespräch, oder <c>null</c>.</summary>
    Task<Gespraech?> HoleAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Das laufende Gespräch zwischen diesen beiden, falls es eines gibt.</summary>
    /// <remarks>
    /// Genau eines je (Mensch, Unternehmen) — sonst stünden zwei Stufenstände
    /// nebeneinander, die dieselben Fähigkeiten meinen.
    /// </remarks>
    Task<Gespraech?> HoleLaufendesAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder schreibt zurück.</summary>
    Task SichereAsync(Gespraech gespraech, CancellationToken cancellationToken = default);

    /// <summary>Die Gespräche dieser Person, neueste zuerst.</summary>
    Task<IReadOnlyList<Gespraech>> FuerPersonAsync(
        SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Die Gespräche dieses Unternehmens, neueste zuerst.</summary>
    Task<IReadOnlyList<Gespraech>> FuerFirmaAsync(
        TenantId firma, CancellationToken cancellationToken = default);

    /// <summary>Alles, was dieser Dienst über einen Menschen hält.</summary>
    /// <returns>Wie viel absichtlich stehen blieb. Immer null.</returns>
    Task<int> LoescheAsync(SubjectId wer, CancellationToken cancellationToken = default);
}
