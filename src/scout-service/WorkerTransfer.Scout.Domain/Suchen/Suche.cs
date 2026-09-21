using Noelia.Core.Identity;

namespace WorkerTransfer.Scout.Domain.Searches;

/// <summary>Eine gespeicherte ResumeRequest — und ausdrücklich kein gespeichertes Ergebnis.</summary>
/// <remarks>
/// <para><strong>Die vierte Entscheidung aus ADR-0036 steht in diesem Typ.</strong>
/// Eine gespeicherte Search hält Filter. Sie hält keinen Hit, keine
/// Kennung eines Menschen, keinen Zeitpunkt eines Laufs und keine Zahl über
/// irgendjemanden — weil ein gespeichertes Ergebnis über Menschen gegen einen
/// Revocation veraltet. Ein Revocation muss beim nächsten Aufruf wirken (ADR-0013),
/// und ein Hit-Zwischenspeicher wäre genau der Bruch dieser Regel.</para>
///
/// <para>Aus demselben Grund gibt es <em>keinen</em> nächtlichen Lauf, der
/// „neue Hit" an das Unternehmen meldete: das wäre derselbe Zwischenspeicher
/// mit einem Wecker davor, plus eine zweite Benachrichtigung neben der an die
/// Person.</para>
///
/// <para>Sie gehört einem <em>Menschen in einem Unternehmen</em>, nicht dem
/// Unternehmen allein: <see cref="Wer"/> ist die Personenspalte, an der die
/// Löschkaskade diesen Dienst erkennt (ADR-0027). Wer geht, nimmt seine
/// gespeicherten Searches mit.</para>
/// </remarks>
public sealed class Search
{
    /// <summary>Wie lang der Name einer gespeicherten Search sein darf.</summary>
    public const int HoechstlaengeName = 80;

    /// <summary>Wie viele Searches ein Mensch je Unternehmen ablegen darf.</summary>
    /// <remarks>
    /// Eine Grenze, damit aus dem Ablegen kein Speicher wird, den niemand
    /// aufräumt. Grosszügig genug, dass niemand sie im Alltag trifft.
    /// </remarks>
    public const int HoechstzahlJeMensch = 50;

    private Search(
        Guid id,
        TenantId company,
        SubjectId subject,
        string name,
        SearchFilter filter,
        DateTimeOffset angelegtAm)
    {
        Id = id;
        Firma = company;
        Wer = subject;
        Name = name;
        Filter = filter;
        AngelegtAm = angelegtAm;
    }

    /// <summary>Welche Search.</summary>
    public Guid Id { get; }

    /// <summary>Für welches Unternehmen sie gilt.</summary>
    public TenantId Firma { get; }

    /// <summary>Wer sie abgelegt hat.</summary>
    public SubjectId Wer { get; }

    /// <summary>Wie die Person sie genannt hat.</summary>
    public string Name { get; }

    /// <summary>Die ResumeRequest. Mehr steht hier nicht.</summary>
    public SearchFilter Filter { get; }

    /// <summary>Wann sie abgelegt wurde.</summary>
    public DateTimeOffset AngelegtAm { get; }

    /// <summary>Legt eine Search an.</summary>
    /// <exception cref="Eingabefehler">Der Name fehlt oder ist zu lang.</exception>
    public static Search Lege_an(
        TenantId company, SubjectId subject, string? name, SearchFilter filter, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var gekuerzt = (name ?? string.Empty).Trim();

        return gekuerzt.Length switch
        {
            0 => throw new Eingabefehler("a saved search needs a name"),
            > HoechstlaengeName => throw new Eingabefehler(
                $"a name may not be longer than {HoechstlaengeName} characters"),
            _ => new Search(Guid.CreateVersion7(), company, subject, gekuerzt, filter, now)
        };
    }

    /// <summary>Die Search, wie eine Zeile sie hält.</summary>
    public static Search Stelle_her(
        Guid id,
        TenantId company,
        SubjectId subject,
        string name,
        SearchFilter filter,
        DateTimeOffset angelegtAm) =>
        new(id, company, subject, name, filter, angelegtAm);
}

/// <summary>Findet und speichert gespeicherte Searches.</summary>
public interface ISearchStore
{
    /// <summary>Legt eine Search an.</summary>
    Task AppendAsync(Search search, CancellationToken cancellationToken = default);

    /// <summary>Die Searches dieses Menschen in diesem Unternehmen, neueste zuerst.</summary>
    /// <remarks>
    /// Je Mensch <em>und</em> je Unternehmen: wer für zwei Firmen arbeitet, soll
    /// die Searches der einen nicht in der anderen sehen. Und wer aus einem
    /// Unternehmen ausscheidet, nimmt seine Searches nicht dorthin mit, wo er
    /// später arbeitet.
    /// </remarks>
    Task<IReadOnlyList<Search>> ForHumanAsync(
        TenantId company, SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Entfernt eine Search, wenn sie diesem Menschen gehört.</summary>
    /// <returns><c>true</c>, wenn etwas entfernt wurde.</returns>
    Task<bool> RemoveAsync(
        Guid id, TenantId company, SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Alles, was dieser Dienst über einen Menschen hält.</summary>
    /// <returns>Wie viel absichtlich stehen blieb. Immer null.</returns>
    Task<int> DeleteAsync(SubjectId subject, CancellationToken cancellationToken = default);
}
