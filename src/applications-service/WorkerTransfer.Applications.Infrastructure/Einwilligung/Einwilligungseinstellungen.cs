namespace WorkerTransfer.Applications.Infrastructure.Einwilligung;

/// <summary>Wo der Consent-Ledger antwortet.</summary>
public sealed class Einwilligungseinstellungen
{
    /// <summary>Der Abschnitt, an den das gebunden wird.</summary>
    public const string Abschnitt = "Consent";

    /// <summary>Die Basisadresse des Ledgers.</summary>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>Wie lange ein Aufruf dauern darf.</summary>
    /// <remarks>
    /// Kurz mit Absicht. Ein schweigender Ledger muss schnell zu einem 503
    /// werden; an ihm zu hängen machte aus einer langsamen Abhängigkeit einen
    /// Dienst, der gar nichts mehr beantwortet.
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);
}
