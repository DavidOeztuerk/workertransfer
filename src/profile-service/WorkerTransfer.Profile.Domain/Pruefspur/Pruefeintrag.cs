using Noelia.Core.Identity;

namespace WorkerTransfer.Profile.Domain.Pruefspur;

/// <summary>Ein Schlüssel, der nicht auf der Liste steht.</summary>
/// <param name="schluessel">Der abgelehnte Schlüssel.</param>
public sealed class AuditDataError(string key)
    : Exception($"Der Prüfspur-Schlüssel '{key}' steht nicht auf der Liste.")
{
    /// <summary>Der abgelehnte Schlüssel.</summary>
    public string Schluessel { get; } = key;
}

/// <summary>Ein Eintrag in der Spur. Unveränderlich, sobald geschrieben.</summary>
/// <remarks>
/// Frei von personenbezogenen Daten <em>durch Bau</em>, nicht durch Durchsicht:
/// der Konstruktor lehnt jeden Schlüssel ab, der nicht auf
/// <see cref="ErlaubteSchluessel"/> steht. Eine Spur wird lange nach dem
/// Schreiben von Menschen gelesen, die nicht dabei waren — und eine Überschrift
/// oder ein Freitext, der einmal hineingeraten ist, steht in jeder Sicherung.
/// </remarks>
public sealed class AuditEntry
{
    /// <summary>Die einzigen Schlüssel, die ein Eintrag tragen darf.</summary>
    /// <remarks>
    /// Technische Tatsachen über eine ResumeRequest, nie Aussagen über einen
    /// Menschen. Was die Person geschrieben hat — Überschrift, Text, Ort,
    /// Fähigkeiten — steht ausdrücklich nicht darauf: der Inhalt des Profils
    /// gehört in die Profiltabelle, wo er gelöscht wird, und nicht in eine
    /// Spur, die ihn überlebt.
    /// </remarks>
    public static IReadOnlySet<string> ErlaubteSchluessel { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "ip", "user_agent" };

    /// <exception cref="PruefdatenFehler">
    /// <paramref name="daten"/> trägt einen Schlüssel, der nicht erlaubt ist.
    /// </exception>
    public AuditEntry(
        AuditAction action,
        DateTimeOffset geschehenAm,
        SubjectId? akteur = null,
        TenantId? company = null,
        SubjectId? betroffen = null,
        string? korrelation = null,
        IReadOnlyDictionary<string, string>? daten = null)
    {
        if (daten is not null)
        {
            foreach (var key in daten.Keys)
            {
                if (!ErlaubteSchluessel.Contains(key))
                {
                    throw new AuditDataError(key);
                }
            }
        }

        Handlung = action;
        GeschehenAm = geschehenAm;
        Akteur = akteur;
        Firma = company;
        Betroffen = betroffen;
        Korrelation = korrelation;
        Daten = daten ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>Was geschehen ist.</summary>
    public AuditAction Handlung { get; }

    /// <summary>Wann.</summary>
    public DateTimeOffset GeschehenAm { get; }

    /// <summary>Wer es getan hat, soweit bekannt.</summary>
    /// <remarks>
    /// <c>null</c> bei der Löschkaskade: dort handelt kein Mensch, sondern der
    /// Auftrag, den einer vor Stunden erteilt hat.
    /// </remarks>
    public SubjectId? Akteur { get; }

    /// <summary>Für welches Unternehmen gehandelt wurde, oder <c>null</c>.</summary>
    public TenantId? Firma { get; }

    /// <summary>An wem, wo das vom Akteur abweicht.</summary>
    public SubjectId? Betroffen { get; }

    /// <summary>Die ResumeRequest, damit sich eine Geschichte über Dienste hinweg lesen lässt.</summary>
    public string? Korrelation { get; }

    /// <summary>Technisches Beiwerk, nur von der Liste.</summary>
    public IReadOnlyDictionary<string, string> Daten { get; }
}
