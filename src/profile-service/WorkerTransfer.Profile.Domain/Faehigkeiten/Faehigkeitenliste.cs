using WorkerTransfer.Skills;

namespace WorkerTransfer.Profile.Domain.Faehigkeiten;

/// <summary>Zu viele Fähigkeiten, oder eine zu lange.</summary>
/// <param name="grund">Was zu viel war.</param>
public sealed class SkillError(string reason) : Eingabefehler(reason);

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
public sealed class SkillList
{
    /// <summary>Wie viele Fähigkeiten ein PersonProfile trägt.</summary>
    /// <remarks>
    /// Gezählt wird <em>nach</em> dem Entdoppeln: sonst wiese eine Liste mit
    /// einunddreißigmal „Python“ jemanden ab, obwohl daraus eine einzige
    /// Fähigkeit wird.
    /// </remarks>
    public const int Hoechstzahl = 30;

    /// <summary>Wie lang eine einzelne Fähigkeit sein darf.</summary>
    /// <inheritdoc cref="Faehigkeitsgrenzen.Hoechstlaenge" />
    public const int Hoechstlaenge = SkillLimits.Hoechstlaenge;

    /// <summary>Die leere Liste — jemand, der nichts eingetragen hat.</summary>
    /// <remarks>
    /// „Nichts gesagt“ ist nicht „nichts gekonnt“. Der Abgleich zeigt für sie
    /// keine Bilanz, nicht „0 von 3“.
    /// </remarks>
    public static SkillList Leer { get; } = new([]);

    private SkillList(IReadOnlyList<string> values) => Werte = values;

    /// <summary>Die Fähigkeiten, kanonisch und entdoppelt.</summary>
    public IReadOnlyList<string> Werte { get; }

    /// <summary>Baut die Liste aus dem, was jemand getippt hat.</summary>
    /// <param name="roh">Die Eingabe, unverändert.</param>
    /// <exception cref="Faehigkeitsfehler">
    /// Eine Fähigkeit ist zu lang, oder es sind nach dem Entdoppeln zu viele.
    /// </exception>
    public static SkillList Aus(IEnumerable<string>? roh) => Build(roh, pruefe: true);

    /// <summary>Baut die Liste aus einer gespeicherten Zeile.</summary>
    /// <remarks>
    /// Prüft die Grenzen <em>nicht</em>. Eine gespeicherte Zeile war bei ihrer
    /// Entstehung gültig; sie beim Lesen abzulehnen hieße, jemandem sein PersonProfile
    /// zu entziehen, weil jemand anderes später eine Obergrenze gesenkt hat.
    /// <para>
    /// Umbenannt und entdoppelt wird trotzdem: so wirkt ein neuer Eintrag im
    /// Vocabulary auch auf Zeilen, die vor ihm geschrieben wurden — ohne
    /// Datenwanderung.
    /// </para>
    /// </remarks>
    /// <param name="gespeichert">Was in der Spalte steht.</param>
    public static SkillList Stelle_her(IEnumerable<string>? gespeichert) =>
        Build(gespeichert, pruefe: false);

    private static SkillList Build(IEnumerable<string>? roh, bool pruefe)
    {
        if (roh is null)
        {
            return Leer;
        }

        var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sauber = new List<string>();

        foreach (var entry in Vocabulary.KanonischAlle(roh))
        {
            if (pruefe && entry.Length > Hoechstlaenge)
            {
                throw new SkillError(
                    $"Eine Fähigkeit darf höchstens {Hoechstlaenge} Zeichen haben.");
            }

            // Groß-/Kleinschreibung ist keine zweite Fähigkeit. Die erste
            // Schreibweise gewinnt — so hat die Person sie eingetragen.
            if (gesehen.Add(entry))
            {
                sauber.Add(entry);
            }
        }

        return pruefe && sauber.Count > Hoechstzahl
            ? throw new SkillError($"Höchstens {Hoechstzahl} Fähigkeiten sind erlaubt.")
            : new SkillList(sauber);
    }
}
