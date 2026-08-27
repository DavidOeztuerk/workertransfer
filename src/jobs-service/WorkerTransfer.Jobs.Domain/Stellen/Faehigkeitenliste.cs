using WorkerTransfer.Skills;

namespace WorkerTransfer.Jobs.Domain.Stellen;

/// <summary>Was die Stelle verlangt — kanonisiert und reihenfolgetreu.</summary>
/// <remarks>
/// Eine zweite Umsetzung derselben Regeln wie in <c>profile-service</c>, und
/// zwar absichtlich: Fähigkeiten an einer Stelle sind ein Modell dieses
/// Dienstes und bleiben bei ihm (ADR-0004). Geteilt ist nur, was geteilt sein
/// <em>muss</em> — der Wortschatz und die Höchstlänge, beide in
/// <c>WorkerTransfer.Skills</c>.
/// <para>
/// Verglichen wird ohnehin nicht hier, sondern im Browser, und ohne Rücksicht
/// auf Groß- und Kleinschreibung.
/// </para>
/// </remarks>
public sealed class Faehigkeitenliste
{
    /// <summary>
    /// Zwanzig. Danach ist es eine Wunschliste, die ohnehin niemand durchliest.
    /// </summary>
    /// <remarks>
    /// Weniger als ein Profil hält, und das ist in Ordnung: kleiner darf die
    /// Stelle sein, länger nicht — siehe <see cref="Hoechstlaenge"/>.
    /// </remarks>
    public const int Hoechstzahl = 20;

    /// <inheritdoc cref="Faehigkeitsgrenzen.Hoechstlaenge" />
    public const int Hoechstlaenge = Faehigkeitsgrenzen.Hoechstlaenge;

    private Faehigkeitenliste(IReadOnlyList<string> werte) => Werte = werte;

    /// <summary>Nichts genannt.</summary>
    public static Faehigkeitenliste Leer { get; } = new([]);

    /// <summary>Die Anforderungen, in der Reihenfolge, in der sie kamen.</summary>
    public IReadOnlyList<string> Werte { get; }

    /// <summary>Baut die Liste, oder weist sie ab.</summary>
    /// <exception cref="Faehigkeitsfehler">Zu viele, oder eine zu lange.</exception>
    public static Faehigkeitenliste Aus(IEnumerable<string>? roh) => Baue(roh, pruefe: true);

    /// <summary>
    /// Die Liste, wie eine Zeile sie hält.
    /// </summary>
    /// <remarks>
    /// Ohne Prüfung der Anzahl: eine gespeicherte Zeile, die eine später
    /// gesenkte Grenze reißt, soll lesbar bleiben. Sonst wäre die Senkung einer
    /// Zahl ein Datenverlust.
    /// </remarks>
    public static Faehigkeitenliste Stelle_her(IEnumerable<string>? gespeichert) =>
        Baue(gespeichert, pruefe: false);

    private static Faehigkeitenliste Baue(IEnumerable<string>? roh, bool pruefe)
    {
        if (roh is null)
        {
            return Leer;
        }

        List<string> bereinigt = [];
        HashSet<string> gesehen = new(StringComparer.OrdinalIgnoreCase);

        // Erst umbenennen, DANN entdoppeln (ADR-0023). Andersherum würde aus
        // „Postgres, PostgreSQL" zweimal derselbe Eintrag, und die Anzeige
        // zählte eine Anforderung doppelt.
        foreach (var eintrag in Wortschatz.KanonischAlle(roh))
        {
            var wert = eintrag.Trim();

            if (wert.Length == 0)
            {
                continue;
            }

            if (pruefe && wert.Length > Hoechstlaenge)
            {
                throw new Faehigkeitsfehler(
                    $"Eine Fähigkeit darf höchstens {Hoechstlaenge} Zeichen haben.");
            }

            // Groß- und Kleinschreibung ist keine zweite Anforderung. Die erste
            // Schreibweise gewinnt — so hat das Unternehmen sie geschrieben,
            // und so steht sie später in der Liste, die die Person sieht.
            if (gesehen.Add(wert))
            {
                bereinigt.Add(wert);
            }
        }

        // Erst entdoppeln, dann zählen.
        return pruefe && bereinigt.Count > Hoechstzahl
            ? throw new Faehigkeitsfehler($"Höchstens {Hoechstzahl} Fähigkeiten sind erlaubt.")
            : new Faehigkeitenliste(bereinigt);
    }
}
