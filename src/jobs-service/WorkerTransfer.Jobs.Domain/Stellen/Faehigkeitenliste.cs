using WorkerTransfer.Skills;

namespace WorkerTransfer.Jobs.Domain.Jobs;

/// <summary>Was die Job verlangt — kanonisiert und reihenfolgetreu.</summary>
/// <remarks>
/// Eine zweite Umsetzung derselben Regeln wie in <c>profile-service</c>, und
/// zwar absichtlich: Fähigkeiten an einer Job sind ein Modell dieses
/// Dienstes und bleiben bei ihm (ADR-0004). Geteilt ist nur, was geteilt sein
/// <em>muss</em> — der Vocabulary und die Höchstlänge, beide in
/// <c>WorkerTransfer.Skills</c>.
/// <para>
/// Verglichen wird ohnehin nicht hier, sondern im Browser, und ohne Rücksicht
/// auf Groß- und Kleinschreibung.
/// </para>
/// </remarks>
public sealed class SkillList
{
    /// <summary>
    /// Zwanzig. Danach ist es eine Wunschliste, die ohnehin niemand durchliest.
    /// </summary>
    /// <remarks>
    /// Weniger als ein Profile hält, und das ist in Ordnung: kleiner darf die
    /// Job sein, länger nicht — siehe <see cref="MaxLength"/>.
    /// </remarks>
    public const int Hoechstzahl = 20;

    /// <inheritdoc cref="SkillLimits.MaxLength" />
    public const int MaxLength = SkillLimits.MaxLength;

    private SkillList(IReadOnlyList<string> values) => Werte = values;

    /// <summary>Nichts genannt.</summary>
    public static SkillList Leer { get; } = new([]);

    /// <summary>Die Anforderungen, in der Reihenfolge, in der sie kamen.</summary>
    public IReadOnlyList<string> Werte { get; }

    /// <summary>Baut die Liste, oder weist sie ab.</summary>
    /// <exception cref="Faehigkeitsfehler">Zu viele, oder eine zu lange.</exception>
    public static SkillList Aus(IEnumerable<string>? roh) => Build(roh, pruefe: true);

    /// <summary>
    /// Die Liste, wie eine Zeile sie hält.
    /// </summary>
    /// <remarks>
    /// Ohne Prüfung der Anzahl: eine gespeicherte Zeile, die eine später
    /// gesenkte Grenze reißt, soll lesbar bleiben. Sonst wäre die Senkung einer
    /// Zahl ein Datenverlust.
    /// </remarks>
    public static SkillList Stelle_her(IEnumerable<string>? gespeichert) =>
        Build(gespeichert, pruefe: false);

    private static SkillList Build(IEnumerable<string>? roh, bool pruefe)
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
        foreach (var entry in Vocabulary.CanonicaliseAll(roh))
        {
            var value = entry.Trim();

            if (value.Length == 0)
            {
                continue;
            }

            if (pruefe && value.Length > MaxLength)
            {
                throw new SkillError(
                    $"Eine Fähigkeit darf höchstens {MaxLength} Zeichen haben.");
            }

            // Groß- und Kleinschreibung ist keine zweite Anforderung. Die erste
            // Schreibweise gewinnt — so hat das Unternehmen sie geschrieben,
            // und so steht sie später in der Liste, die die Person sieht.
            if (gesehen.Add(value))
            {
                bereinigt.Add(value);
            }
        }

        // Erst entdoppeln, dann zählen.
        return pruefe && bereinigt.Count > Hoechstzahl
            ? throw new SkillError($"Höchstens {Hoechstzahl} Fähigkeiten sind erlaubt.")
            : new SkillList(bereinigt);
    }
}
