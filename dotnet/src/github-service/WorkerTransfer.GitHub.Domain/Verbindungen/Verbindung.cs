using System.Security.Cryptography;
using Girder.Core.Identity;

namespace WorkerTransfer.GitHub.Domain.Verbindungen;

/// <summary>Der Benutzername taugt nicht.</summary>
public sealed class Loginfehler(string was) : Exception($"GitHub login {was}");

/// <summary>Diese Verbindung ist noch nicht nachgewiesen.</summary>
public sealed class NichtNachgewiesen() : Exception("This connection is not verified yet");

/// <summary>Diese Verbindung ist bereits nachgewiesen.</summary>
public sealed class SchonNachgewiesen() : Exception("This connection is already verified");

/// <summary>Ein Beleg. Alle Felder kommen von GitHub, keins ist gerechnet.</summary>
/// <remarks>
/// <c>Sterne</c> steht hier, weil GitHub es meldet — <em>weitergegeben, nicht
/// ausgewertet</em>. Es ist kein Sortierschlüssel und darf keiner werden: eine
/// Reihenfolge über Menschen nach Sternen wäre die ADR-0022-Punktzahl durch die
/// Hintertür, auch wenn sie „Aktivität" hieße.
/// </remarks>
public sealed record Repository(
    string Name,
    string Beschreibung,
    string? Sprache,
    int Sterne,
    string Adresse,
    DateTimeOffset? ZuletztGeschoben);

/// <summary>Die Verbindung zu einem GitHub-Konto — bewiesen, nicht behauptet.</summary>
/// <remarks>
/// <strong>Was hier nicht steht, ist der Punkt</strong> (ADR-0022): keine
/// Punktzahl, keine abgeleiteten Eigenschaften, kein „Können" aus Bytes. Ein
/// Repository ist ein Beleg mit einem Link; wer wissen will, ob der Code gut
/// ist, klickt darauf. Das ist die einzige ehrliche Bewertung, die dieses
/// System anbieten kann.
/// <para>
/// Die Grenze verläuft zwischen einer Aussage über ein <em>Repository</em> —
/// eine Tatsache — und einer über einen <em>Menschen</em> — ein Urteil, das er
/// nicht kommentieren kann. „Dieses Repository ist laut GitHub zu 80 % Go" darf
/// hier stehen; „diese Person kann Go" nicht. Wer Go ins Profil schreibt, ist
/// die Person selbst.
/// </para>
/// </remarks>
public sealed class Verbindung
{
    /// <summary>GitHubs eigene Grenze für Benutzernamen.</summary>
    public const int HoechstlaengeLogin = 39;

    private readonly List<Repository> _repositories;

    private Verbindung(
        SubjectId wer,
        string login,
        string einmalzeichenfolge,
        DateTimeOffset? nachgewiesenAm,
        DateTimeOffset? geholtAm,
        List<Repository> repositories)
    {
        Wer = wer;
        Login = login;
        Einmalzeichenfolge = einmalzeichenfolge;
        NachgewiesenAm = nachgewiesenAm;
        GeholtAm = geholtAm;
        _repositories = repositories;
    }

    /// <summary>Wessen Verbindung. Sie <em>ist</em> der Schlüssel.</summary>
    public SubjectId Wer { get; }

    /// <summary>Der GitHub-Benutzername.</summary>
    public string Login { get; private set; }

    /// <summary>Die Zeichenfolge, die im öffentlichen Gist stehen muss.</summary>
    /// <remarks>
    /// <strong>Kein Geheimnis</strong>, sondern eine Einmalzeichenfolge. Sie
    /// beweist nur, dass jemand mit Zugriff auf das Konto sie dort
    /// hingeschrieben hat.
    /// </remarks>
    public string Einmalzeichenfolge { get; private set; }

    /// <summary><c>null</c>, solange nicht bewiesen.</summary>
    public DateTimeOffset? NachgewiesenAm { get; private set; }

    /// <summary>Wann der Abzug zuletzt geholt wurde.</summary>
    public DateTimeOffset? GeholtAm { get; private set; }

    /// <summary>Der Abzug, neueste zuerst.</summary>
    public IReadOnlyList<Repository> Repositories => _repositories;

    /// <summary>Ist die Verbindung bewiesen?</summary>
    public bool Nachgewiesen => NachgewiesenAm is not null;

    /// <summary>Die Beschreibung, die im öffentlichen Gist stehen muss.</summary>
    /// <remarks>
    /// In der <em>Beschreibung</em>, nicht im Inhalt: die Gist-Liste liefert sie
    /// mit, ein Inhalt bräuchte einen Abruf je Gist. Bei sechzig Anfragen pro
    /// Stunde ohne Token ist das kein Detail.
    /// </remarks>
    public static string Gistbeschreibung(string einmalzeichenfolge) =>
        $"workertransfer-verify-{einmalzeichenfolge}";

    /// <summary>Nennt einen Benutzernamen und würfelt die Einmalzeichenfolge.</summary>
    /// <exception cref="Loginfehler">Der Benutzername taugt nicht.</exception>
    public static Verbindung Oeffne(SubjectId wer, string login) =>
        new(wer, Geprueft(login), NeueZeichenfolge(), null, null, []);

    /// <summary>Die Verbindung, wie eine Zeile sie hält.</summary>
    public static Verbindung Stelle_her(
        SubjectId wer,
        string login,
        string einmalzeichenfolge,
        DateTimeOffset? nachgewiesenAm,
        DateTimeOffset? geholtAm,
        IReadOnlyList<Repository> repositories) =>
        new(wer, login, einmalzeichenfolge, nachgewiesenAm, geholtAm, [.. repositories]);

    /// <summary>Ein anderes Konto nennen — der Nachweis fällt damit weg.</summary>
    /// <remarks>
    /// Ohne dieses Zurücksetzen könnte jemand ein Konto nachweisen und danach
    /// den Namen auf ein fremdes ändern: der Nachweis stünde noch, wäre aber
    /// für ein anderes Konto erbracht worden.
    /// </remarks>
    /// <exception cref="Loginfehler">Der Benutzername taugt nicht.</exception>
    public void Nenne_neu(string login)
    {
        var neuer = Geprueft(login);

        if (string.Equals(neuer, Login, StringComparison.OrdinalIgnoreCase))
        {
            // Nur die Schreibweise hat sich geändert — das ist kein anderes
            // Konto, und ein erbrachter Nachweis gilt weiter.
            Login = neuer;
            return;
        }

        Login = neuer;
        Einmalzeichenfolge = NeueZeichenfolge();
        NachgewiesenAm = null;
        GeholtAm = null;
        _repositories.Clear();
    }

    /// <summary>Hält fest, dass der Nachweis erbracht ist.</summary>
    /// <exception cref="SchonNachgewiesen">Er war schon erbracht.</exception>
    public void Weise_nach(DateTimeOffset jetzt)
    {
        if (Nachgewiesen)
        {
            throw new SchonNachgewiesen();
        }

        NachgewiesenAm = jetzt;
    }

    /// <summary>Legt den Abzug ab. Nur für eine nachgewiesene Verbindung.</summary>
    /// <remarks>
    /// Ohne diese Prüfung könnte jemand einen fremden Benutzernamen eintragen
    /// und dessen Arbeit als seine zeigen — ohne je Zugriff auf das Konto
    /// gehabt zu haben.
    /// </remarks>
    /// <exception cref="NichtNachgewiesen">Die Verbindung ist nicht bewiesen.</exception>
    public void Lege_ab(IReadOnlyList<Repository> repositories, DateTimeOffset jetzt)
    {
        ArgumentNullException.ThrowIfNull(repositories);

        if (!Nachgewiesen)
        {
            throw new NichtNachgewiesen();
        }

        // Neueste zuerst. NICHT nach Sternen: die messen Sichtbarkeit, nicht
        // Arbeit — und eine Sortierung ist bereits eine Wertung.
        _repositories.Clear();
        _repositories.AddRange(
            repositories
                .OrderByDescending(eintrag => eintrag.ZuletztGeschoben is not null)
                .ThenByDescending(eintrag => eintrag.ZuletztGeschoben));

        GeholtAm = jetzt;
    }

    /// <summary>Sechzehn Byte urlsafe.</summary>
    private static string NeueZeichenfolge() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static string Geprueft(string? roh)
    {
        var login = (roh ?? string.Empty).Trim().TrimStart('@');

        if (login.Length == 0)
        {
            throw new Loginfehler("must not be empty");
        }

        if (login.Length > HoechstlaengeLogin)
        {
            throw new Loginfehler($"must not exceed {HoechstlaengeLogin} characters");
        }

        // GitHubs Regeln: Buchstaben, Ziffern und einzelne Bindestriche, nicht
        // am Rand. Streng zu prüfen erspart einen Abruf, der ohnehin nichts
        // findet — und verhindert, dass ein Pfadfragment in eine Adresse
        // wandert.
        if (login.StartsWith('-') || login.EndsWith('-') || login.Contains("--", StringComparison.Ordinal))
        {
            throw new Loginfehler("has misplaced hyphens");
        }

        return login.All(zeichen => char.IsAsciiLetterOrDigit(zeichen) || zeichen == '-')
            ? login
            : throw new Loginfehler("may only contain letters, digits and hyphens");
    }
}

/// <summary>Findet und speichert Verbindungen.</summary>
public interface IVerbindungsspeicher
{
    /// <summary>Die Verbindung dieser Person, oder <c>null</c>.</summary>
    Task<Verbindung?> HoleAsync(SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder schreibt zurück.</summary>
    Task SichereAsync(Verbindung verbindung, CancellationToken cancellationToken = default);

    /// <summary>Trennen heißt löschen — der Abzug verschwindet mit.</summary>
    Task LoescheAsync(SubjectId wer, CancellationToken cancellationToken = default);
}
