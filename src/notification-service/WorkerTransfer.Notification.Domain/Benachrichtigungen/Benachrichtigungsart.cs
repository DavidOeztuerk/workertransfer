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
    ApplicationReceived,

    /// <summary>Das Profil dieser Person ist in einer Suche aufgetaucht.</summary>
    /// <remarks>
    /// <para>Die Auskunft aus ADR-0033, und ausdrücklich <em>statt</em> eines
    /// Protokolls: der Entwurf wollte einmal eine Liste „wer hat wen wann
    /// angesehen". Die wäre die sensibelste Tabelle des Systems. Eine Nachricht
    /// erreicht dieselbe Person, ist abbestellbar wie jede andere Art — und
    /// hinterlässt kein durchsuchbares Verzeichnis.</para>
    ///
    /// <para>Sie nennt <strong>kein Unternehmen</strong>. Wer sucht, ist eine
    /// Aussage über das Unternehmen, und die Person kann damit nichts anfangen,
    /// solange niemand sie angesprochen hat. Das steht hier nicht als Regel,
    /// sondern ist bereits eingelöst: eine Benachrichtigung trägt nur eine Art.</para>
    ///
    /// <para>Und sie ist die einzige Art mit einer <em>eigenen Tageskappe</em> —
    /// siehe <see cref="Benachrichtigungsarten.Tageskappe"/>.</para>
    /// </remarks>
    ProfileDiscovered,

    /// <summary>Ein Unternehmen hat ein Gespräch eröffnet.</summary>
    /// <remarks>
    /// <para>Die Art aus ADR-0037, und sie ist etwas anderes als
    /// <see cref="ProfileDiscovered"/>. Dort heisst es „jemand hat dich
    /// gefunden", und genau deshalb darf sie kein Unternehmen nennen: wer
    /// sucht, ist eine Aussage über das Unternehmen, und die Person kann damit
    /// nichts anfangen, <em>solange niemand sie angesprochen hat</em>.</para>
    ///
    /// <para>Hier ist genau das passiert. Ein Gespräch entsteht nur mit einem
    /// Unternehmen, dem die Person schon Stufe 1 erteilt hat — der Anlass ist
    /// also ihre eigene frühere Handlung, und was jetzt geschieht, will sie
    /// wissen. Ohne diese Nachricht liefe ein Gespräch, von dem sie erst beim
    /// nächsten Vorbeischauen erfährt.</para>
    ///
    /// <para>Und auch sie nennt <strong>kein Unternehmen</strong>: eine
    /// Benachrichtigung trägt nur eine Art. Wer es war, steht hinter der
    /// Anmeldung in ihrer eigenen Liste.</para>
    /// </remarks>
    AdvisorConversation,

    /// <summary>An einer Arbeitsprobe dieser Person hat sich etwas bewegt.</summary>
    /// <remarks>
    /// <para>Die Art aus ADR-0042, und sie trägt <em>zwei</em> Bewegungen: „dir
    /// wurde eine Aufgabe gestellt" und „deine Bewertung liegt vor". Eine Art
    /// für beide, wie bei <see cref="ApplicationUpdate"/> und
    /// <see cref="TransferUpdate"/> — zwei wären zwei Schalter in den
    /// Einstellungen für dieselbe Sache.</para>
    ///
    /// <para><strong>Die zweite Bewegung ist der Grund, dass es diese Art
    /// gibt.</strong> ADR-0042 verspricht: die Person <em>sieht</em> die
    /// Bewertung, immer, auch bei einer Absage. Ohne diese Nachricht erführe sie
    /// davon erst beim nächsten Vorbeischauen — und eine Beurteilung, die der
    /// Beurteilte nicht liest, ist genau das, was dort nicht gebaut wird.</para>
    ///
    /// <para>Und auch sie nennt <strong>kein Unternehmen</strong> und trägt
    /// weder Aufgabentext noch Bewertung: eine Benachrichtigung trägt nur eine
    /// Art. Eine Zeile „Ihre Arbeitsprobe wurde abgelehnt" landete sonst
    /// womöglich im Postfach beim jetzigen Arbeitgeber.</para>
    /// </remarks>
    AssessmentUpdate
}

/// <summary>Die Worte, mit denen eine Art auf der Leitung steht.</summary>
/// <remarks>
/// Dieselben Worte, die die absendenden Dienste in ihre Outbox schreiben —
/// <c>resume_request</c>, <c>market_request</c>, <c>application_update</c>,
/// <c>transfer_update</c>, <c>application_received</c>, <c>profile_discovered</c>,
/// <c>advisor_conversation</c>, <c>assessment_update</c>.
/// Zwei Schreibweisen für dieselbe Art wären zwei
/// Gelegenheiten, eine Nachricht stillschweigend fallen zu lassen.
/// </remarks>
public static class Benachrichtigungsarten
{
    /// <summary>Alle acht, für die Einstellungen.</summary>
    public static readonly IReadOnlyList<Benachrichtigungsart> Alle =
    [
        Benachrichtigungsart.ResumeRequest,
        Benachrichtigungsart.MarketRequest,
        Benachrichtigungsart.ApplicationUpdate,
        Benachrichtigungsart.TransferUpdate,
        Benachrichtigungsart.ApplicationReceived,
        Benachrichtigungsart.ProfileDiscovered,
        Benachrichtigungsart.AdvisorConversation,
        Benachrichtigungsart.AssessmentUpdate
    ];

    /// <summary>Das Wort zur Art.</summary>
    public static string Wort(Benachrichtigungsart art) => art switch
    {
        Benachrichtigungsart.ResumeRequest => "resume_request",
        Benachrichtigungsart.MarketRequest => "market_request",
        Benachrichtigungsart.ApplicationUpdate => "application_update",
        Benachrichtigungsart.TransferUpdate => "transfer_update",
        Benachrichtigungsart.ApplicationReceived => "application_received",
        Benachrichtigungsart.ProfileDiscovered => "profile_discovered",
        Benachrichtigungsart.AdvisorConversation => "advisor_conversation",
        Benachrichtigungsart.AssessmentUpdate => "assessment_update",
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
        "profile_discovered" => Benachrichtigungsart.ProfileDiscovered,
        "advisor_conversation" => Benachrichtigungsart.AdvisorConversation,
        "assessment_update" => Benachrichtigungsart.AssessmentUpdate,
        _ => null
    };

    /// <summary>
    /// Wie oft eine Art höchstens vorkommen darf — oder <c>null</c> für „so oft
    /// wie es passiert".
    /// </summary>
    /// <remarks>
    /// <para><strong>Eine eigene Kappe je Art, ausdrücklich nicht die stündliche
    /// Drossel über alle Arten</strong> (ADR-0033). Die Drossel schützt das
    /// Postfach vor Frequenz; sie lässt den <em>Eintrag</em> immer entstehen,
    /// weil der hinter der Anmeldung liegt und niemandem etwas verrät.</para>
    ///
    /// <para>Bei <c>profile_discovered</c> ist genau das anders, und darin liegt
    /// der ganze Grund für diese Methode: ein Eintrag je Treffer wäre ein
    /// <em>Zähler über die eigene Sichtbarkeit</em> — auf Umwegen das
    /// Verzeichnis, das hier nicht entstehen soll. Deshalb fällt hier der
    /// Eintrag selbst weg, nicht nur die Mail.</para>
    ///
    /// <para>Ein Tag ist eine Wahl, keine Ableitung: kürzer verdichtet nichts,
    /// weil eine Firma an einem Nachmittag mehrfach sucht; länger verschluckt
    /// die Auskunft, um derentwillen der Scout gebaut werden darf.</para>
    /// </remarks>
    public static TimeSpan? Tageskappe(Benachrichtigungsart art) =>
        art == Benachrichtigungsart.ProfileDiscovered ? TimeSpan.FromDays(1) : null;
}
