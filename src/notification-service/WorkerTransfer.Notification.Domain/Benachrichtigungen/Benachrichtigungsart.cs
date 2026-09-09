namespace WorkerTransfer.Notification.Domain.Benachrichtigungen;

/// <summary>Worüber benachrichtigt wird — und mehr sagt eine Nachricht nie.</summary>
/// <remarks>
/// Eine Mail landet in einem Postfach, und dieses Postfach kann das Postfach
/// beim aktuellen Arbeitgeber sein: auf dessen Servern, in dessen Sicherungen,
/// im Blick seiner Administratoren. Eine Zeile wie „Acme GmbH möchte deinen
/// Marktstatus sehen" wäre genau die Auskunft, gegen die diese Plattform gebaut
/// ist — freiwillig verschickt und im Klartext.
/// <para>
/// Deshalb sagt eine Benachrichtigung nicht, worum es geht. Die Art steht
/// <em>hier</em>, hinter der Anmeldung, wo geprüft wird, wer liest; hinaus geht
/// für jede Art derselbe Satz.
/// </para>
/// </remarks>
public enum Benachrichtigungsart
{
    /// <summary>Ein Unternehmen hat nach dem Lebenslauf gefragt.</summary>
    ResumeRequest,

    /// <summary>Ein Unternehmen hat nach dem Marktstatus gefragt.</summary>
    MarketRequest,

    /// <summary>Eine Bewerbung hat sich bewegt.</summary>
    ApplicationUpdate,

    /// <summary>Ein Transfer-Vorgang hat sich bewegt.</summary>
    TransferUpdate,

    /// <summary>Eine Bewerbung ist bei einem Unternehmen eingegangen.</summary>
    ApplicationReceived
}

/// <summary>Die Worte, mit denen eine Art auf der Leitung steht.</summary>
/// <remarks>
/// Dieselben Worte, die die absendenden Dienste in ihre Outbox schreiben —
/// <c>resume_request</c>, <c>market_request</c>, <c>application_update</c>,
/// <c>transfer_update</c>, <c>application_received</c>. Zwei Schreibweisen für dieselbe Art wären zwei
/// Gelegenheiten, eine Nachricht stillschweigend fallen zu lassen.
/// </remarks>
public static class Benachrichtigungsarten
{
    /// <summary>Alle fünf, für die Einstellungen.</summary>
    public static readonly IReadOnlyList<Benachrichtigungsart> Alle =
    [
        Benachrichtigungsart.ResumeRequest,
        Benachrichtigungsart.MarketRequest,
        Benachrichtigungsart.ApplicationUpdate,
        Benachrichtigungsart.TransferUpdate,
        Benachrichtigungsart.ApplicationReceived
    ];

    /// <summary>Das Wort zur Art.</summary>
    public static string Wort(Benachrichtigungsart art) => art switch
    {
        Benachrichtigungsart.ResumeRequest => "resume_request",
        Benachrichtigungsart.MarketRequest => "market_request",
        Benachrichtigungsart.ApplicationUpdate => "application_update",
        Benachrichtigungsart.TransferUpdate => "transfer_update",
        Benachrichtigungsart.ApplicationReceived => "application_received",
        _ => throw new ArgumentOutOfRangeException(nameof(art))
    };

    /// <summary>Die Art zum Wort, oder <c>null</c>.</summary>
    public static Benachrichtigungsart? Lies(string? wort) => wort switch
    {
        "resume_request" => Benachrichtigungsart.ResumeRequest,
        "market_request" => Benachrichtigungsart.MarketRequest,
        "application_update" => Benachrichtigungsart.ApplicationUpdate,
        "transfer_update" => Benachrichtigungsart.TransferUpdate,
        "application_received" => Benachrichtigungsart.ApplicationReceived,
        _ => null
    };
}
