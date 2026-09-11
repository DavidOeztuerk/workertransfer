using Girder.Core.Identity;

namespace WorkerTransfer.Scout.Domain.Suchen;

/// <summary>Eine gespeicherte Anfrage — und ausdrücklich kein gespeichertes Ergebnis.</summary>
/// <remarks>
/// <para><strong>Die vierte Entscheidung aus ADR-0036 steht in diesem Typ.</strong>
/// Eine gespeicherte Suche hält Filter. Sie hält keinen Treffer, keine
/// Kennung eines Menschen, keinen Zeitpunkt eines Laufs und keine Zahl über
/// irgendjemanden — weil ein gespeichertes Ergebnis über Menschen gegen einen
/// Widerruf veraltet. Ein Widerruf muss beim nächsten Aufruf wirken (ADR-0013),
/// und ein Treffer-Zwischenspeicher wäre genau der Bruch dieser Regel.</para>
///
/// <para>Aus demselben Grund gibt es <em>keinen</em> nächtlichen Lauf, der
/// „neue Treffer" an das Unternehmen meldete: das wäre derselbe Zwischenspeicher
/// mit einem Wecker davor, plus eine zweite Benachrichtigung neben der an die
/// Person.</para>
///
/// <para>Sie gehört einem <em>Menschen in einem Unternehmen</em>, nicht dem
/// Unternehmen allein: <see cref="Wer"/> ist die Personenspalte, an der die
/// Löschkaskade diesen Dienst erkennt (ADR-0027). Wer geht, nimmt seine
/// gespeicherten Suchen mit.</para>
/// </remarks>
public sealed class Suche
{
    /// <summary>Wie lang der Name einer gespeicherten Suche sein darf.</summary>
    public const int HoechstlaengeName = 80;

    /// <summary>Wie viele Suchen ein Mensch je Unternehmen ablegen darf.</summary>
    /// <remarks>
    /// Eine Grenze, damit aus dem Ablegen kein Speicher wird, den niemand
    /// aufräumt. Grosszügig genug, dass niemand sie im Alltag trifft.
    /// </remarks>
    public const int HoechstzahlJeMensch = 50;

    private Suche(
        Guid id,
        TenantId firma,
        SubjectId wer,
        string name,
        Suchfilter filter,
        DateTimeOffset angelegtAm)
    {
        Id = id;
        Firma = firma;
        Wer = wer;
        Name = name;
        Filter = filter;
        AngelegtAm = angelegtAm;
    }

    /// <summary>Welche Suche.</summary>
    public Guid Id { get; }

    /// <summary>Für welches Unternehmen sie gilt.</summary>
    public TenantId Firma { get; }

    /// <summary>Wer sie abgelegt hat.</summary>
    public SubjectId Wer { get; }

    /// <summary>Wie die Person sie genannt hat.</summary>
    public string Name { get; }

    /// <summary>Die Anfrage. Mehr steht hier nicht.</summary>
    public Suchfilter Filter { get; }

    /// <summary>Wann sie abgelegt wurde.</summary>
    public DateTimeOffset AngelegtAm { get; }

    /// <summary>Legt eine Suche an.</summary>
    /// <exception cref="Eingabefehler">Der Name fehlt oder ist zu lang.</exception>
    public static Suche Lege_an(
        TenantId firma, SubjectId wer, string? name, Suchfilter filter, DateTimeOffset jetzt)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var gekuerzt = (name ?? string.Empty).Trim();

        return gekuerzt.Length switch
        {
            0 => throw new Eingabefehler("a saved search needs a name"),
            > HoechstlaengeName => throw new Eingabefehler(
                $"a name may not be longer than {HoechstlaengeName} characters"),
            _ => new Suche(Guid.CreateVersion7(), firma, wer, gekuerzt, filter, jetzt)
        };
    }

    /// <summary>Die Suche, wie eine Zeile sie hält.</summary>
    public static Suche Stelle_her(
        Guid id,
        TenantId firma,
        SubjectId wer,
        string name,
        Suchfilter filter,
        DateTimeOffset angelegtAm) =>
        new(id, firma, wer, name, filter, angelegtAm);
}

/// <summary>Findet und speichert gespeicherte Suchen.</summary>
public interface ISuchspeicher
{
    /// <summary>Legt eine Suche an.</summary>
    Task FuegeHinzuAsync(Suche suche, CancellationToken cancellationToken = default);

    /// <summary>Die Suchen dieses Menschen in diesem Unternehmen, neueste zuerst.</summary>
    /// <remarks>
    /// Je Mensch <em>und</em> je Unternehmen: wer für zwei Firmen arbeitet, soll
    /// die Suchen der einen nicht in der anderen sehen. Und wer aus einem
    /// Unternehmen ausscheidet, nimmt seine Suchen nicht dorthin mit, wo er
    /// später arbeitet.
    /// </remarks>
    Task<IReadOnlyList<Suche>> FuerMenschAsync(
        TenantId firma, SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Entfernt eine Suche, wenn sie diesem Menschen gehört.</summary>
    /// <returns><c>true</c>, wenn etwas entfernt wurde.</returns>
    Task<bool> EntferneAsync(
        Guid id, TenantId firma, SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Alles, was dieser Dienst über einen Menschen hält.</summary>
    /// <returns>Wie viel absichtlich stehen blieb. Immer null.</returns>
    Task<int> LoescheAsync(SubjectId wer, CancellationToken cancellationToken = default);
}
