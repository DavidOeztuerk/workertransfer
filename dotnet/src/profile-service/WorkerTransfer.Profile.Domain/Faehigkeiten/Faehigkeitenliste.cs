namespace WorkerTransfer.Profile.Domain.Faehigkeiten;

/// <summary>Zu viele Fähigkeiten, oder eine zu lange.</summary>
/// <param name="grund">Was zu viel war.</param>
public sealed class Faehigkeitsfehler(string grund) : Eingabefehler(grund);

/// <summary>Fähigkeiten als normalisierte, reihenfolgetreue Liste.</summary>
/// <remarks>
/// Die Reihenfolge ist die der Person: sie hat die wichtigste zuerst getippt,
/// und eine Sortierung nach Alphabet oder nach Häufigkeit wäre eine fremde
/// Aussage darüber, was an ihr wichtig ist.
/// <para>
/// <b>Erst umbenennen, dann entdoppeln</b> (ADR-0023). Andersherum stünden
/// „Postgres“ und „PostgreSQL“ zweimal in der Liste — und der Abgleich im
/// Browser zeigte demselben Menschen dieselbe Fähigkeit doppelt.
/// </para>
/// </remarks>
public sealed class Faehigkeitenliste
{
    /// <summary>Wie viele Fähigkeiten ein Profil trägt.</summary>
    /// <remarks>
    /// Gezählt wird <em>nach</em> dem Entdoppeln: sonst wiese eine Liste mit
    /// einunddreißigmal „Python“ jemanden ab, obwohl daraus eine einzige
    /// Fähigkeit wird.
    /// </remarks>
    public const int Hoechstzahl = 30;

    /// <summary>Wie lang eine einzelne Fähigkeit sein darf.</summary>
    public const int Hoechstlaenge = 50;

    /// <summary>Die leere Liste — jemand, der nichts eingetragen hat.</summary>
    /// <remarks>
    /// „Nichts gesagt“ ist nicht „nichts gekonnt“. Der Abgleich zeigt für sie
    /// keine Bilanz, nicht „0 von 3“.
    /// </remarks>
    public static Faehigkeitenliste Leer { get; } = new([]);

    private Faehigkeitenliste(IReadOnlyList<string> werte) => Werte = werte;

    /// <summary>Die Fähigkeiten, kanonisch und entdoppelt.</summary>
    public IReadOnlyList<string> Werte { get; }

    /// <summary>Baut die Liste aus dem, was jemand getippt hat.</summary>
    /// <param name="roh">Die Eingabe, unverändert.</param>
    /// <exception cref="Faehigkeitsfehler">
    /// Eine Fähigkeit ist zu lang, oder es sind nach dem Entdoppeln zu viele.
    /// </exception>
    public static Faehigkeitenliste Aus(IEnumerable<string>? roh) => Baue(roh, pruefe: true);

    /// <summary>Baut die Liste aus einer gespeicherten Zeile.</summary>
    /// <remarks>
    /// Prüft die Grenzen <em>nicht</em>. Eine gespeicherte Zeile war bei ihrer
    /// Entstehung gültig; sie beim Lesen abzulehnen hieße, jemandem sein Profil
    /// zu entziehen, weil jemand anderes später eine Obergrenze gesenkt hat.
    /// <para>
    /// Umbenannt und entdoppelt wird trotzdem: so wirkt ein neuer Eintrag im
    /// Wortschatz auch auf Zeilen, die vor ihm geschrieben wurden — ohne
    /// Datenwanderung.
    /// </para>
    /// </remarks>
    /// <param name="gespeichert">Was in der Spalte steht.</param>
    public static Faehigkeitenliste Stelle_her(IEnumerable<string>? gespeichert) =>
        Baue(gespeichert, pruefe: false);

    private static Faehigkeitenliste Baue(IEnumerable<string>? roh, bool pruefe)
    {
        if (roh is null)
        {
            return Leer;
        }

        var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sauber = new List<string>();

        foreach (var eintrag in Wortschatz.KanonischAlle(roh))
        {
            if (pruefe && eintrag.Length > Hoechstlaenge)
            {
                throw new Faehigkeitsfehler(
                    $"Eine Fähigkeit darf höchstens {Hoechstlaenge} Zeichen haben.");
            }

            // Groß-/Kleinschreibung ist keine zweite Fähigkeit. Die erste
            // Schreibweise gewinnt — so hat die Person sie eingetragen.
            if (gesehen.Add(eintrag))
            {
                sauber.Add(eintrag);
            }
        }

        return pruefe && sauber.Count > Hoechstzahl
            ? throw new Faehigkeitsfehler($"Höchstens {Hoechstzahl} Fähigkeiten sind erlaubt.")
            : new Faehigkeitenliste(sauber);
    }
}
