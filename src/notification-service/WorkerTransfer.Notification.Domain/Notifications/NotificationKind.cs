namespace WorkerTransfer.Notification.Domain.Notifications;

/// <summary>What somebody is notified about — and a message never says more.</summary>
/// <remarks>
/// A mail lands in an inbox, and that inbox may be the one at the current
/// employer: on their servers, in their backups, in the view of their
/// administrators. A line like "Acme GmbH would like to see your market status"
/// would be exactly the disclosure this platform is built against — sent
/// voluntarily and in the clear.
/// <para>
/// So a notification does not say what it is about. The kind stands
/// <em>here</em>, behind the sign-in, where who reads is checked; what goes out
/// is the same sentence for every kind.
/// </para>
/// </remarks>
public enum NotificationKind
{
    /// <summary>A company asked for the CV.</summary>
    ResumeRequest,

    /// <summary>A company asked for the market status.</summary>
    MarketRequest,

    /// <summary>An application moved.</summary>
    ApplicationUpdate,

    /// <summary>A transfer case moved.</summary>
    TransferUpdate,

    /// <summary>An application arrived at a company.</summary>
    ApplicationReceived,

    /// <summary>This person's profile turned up in a search.</summary>
    /// <remarks>
    /// <para>The disclosure from ADR-0033, and expressly <em>instead of</em> a
    /// log: the draft once wanted a list of "who looked at whom and when". That
    /// would be the most sensitive table in the system. A message reaches the
    /// same person, can be switched off like any other kind — and leaves behind
    /// no searchable register.</para>
    ///
    /// <para>It names <strong>no company</strong>. Who searches is a statement
    /// about the company, and the person can do nothing with it as long as
    /// nobody has approached them. That does not stand here as a rule; it is
    /// already redeemed: a notification carries only a kind.</para>
    ///
    /// <para>And it is the only kind with a <em>daily cap of its own</em> —
    /// see <see cref="NotificationKinds.DailyCap"/>.</para>
    /// </remarks>
    ProfileDiscovered,

    /// <summary>A company opened a conversation.</summary>
    /// <remarks>
    /// <para>The kind from ADR-0037, and it is something different from
    /// <see cref="ProfileDiscovered"/>. There it says "somebody found you", and
    /// exactly for that reason it may name no company: who searches is a
    /// statement about the company, and the person can do nothing with it
    /// <em>as long as nobody has approached them</em>.</para>
    ///
    /// <para>Here exactly that has happened. A conversation comes into being
    /// only with a company the person has already granted stage 1 — so the
    /// occasion is their own earlier act, and what happens now is something
    /// they want to know. Without this message a conversation would be running
    /// that they learn of at their next visit.</para>
    ///
    /// <para>And it names <strong>no company</strong> either: a notification
    /// carries only a kind. Who it was stands behind the sign-in in their own
    /// list.</para>
    /// </remarks>
    AdvisorConversation,

    /// <summary>Something moved on a work sample of this person's.</summary>
    /// <remarks>
    /// <para>The kind from ADR-0042, and it carries <em>two</em> movements: "a
    /// task was set for you" and "your evaluation is in". One kind for both, as
    /// with <see cref="ApplicationUpdate"/> and <see cref="TransferUpdate"/> —
    /// two would be two switches in the settings for the same thing.</para>
    ///
    /// <para><strong>The second movement is the reason this kind
    /// exists.</strong> ADR-0042 promises: the person <em>sees</em> the
    /// evaluation, always, even on a rejection. Without this message they would
    /// learn of it only at their next visit — and a judgement the judged does
    /// not read is exactly what is not built there.</para>
    ///
    /// <para>And it names <strong>no company</strong> either and carries
    /// neither task text nor evaluation: a notification carries only a kind. A
    /// line "Your work sample was rejected" would otherwise perhaps land in the
    /// inbox at the current employer.</para>
    /// </remarks>
    AssessmentUpdate,

    /// <summary>A message arrived in one of this person's conversations.</summary>
    /// <remarks>
    /// The ninth kind (ADR-0047). It names <strong>neither the sender nor a
    /// word of the text</strong>: the mail may land in an inbox the current
    /// employer reads, and "Stahlbau Nord wrote to you" is exactly the line
    /// that must not stand there. Who wrote what is read behind the sign-in.
    /// </remarks>
    MessageReceived
}

/// <summary>The words a kind travels under on the wire.</summary>
/// <remarks>
/// The same words the sending services write into their outbox —
/// <c>resume_request</c>, <c>market_request</c>, <c>application_update</c>,
/// <c>transfer_update</c>, <c>application_received</c>, <c>profile_discovered</c>,
/// <c>advisor_conversation</c>, <c>assessment_update</c>, <c>message_received</c>. Two spellings for one
/// kind would be two chances to drop a message silently.
/// </remarks>
public static class NotificationKinds
{
    /// <summary>All nine, for the settings.</summary>
    public static readonly IReadOnlyList<NotificationKind> All =
    [
        NotificationKind.ResumeRequest,
        NotificationKind.MarketRequest,
        NotificationKind.ApplicationUpdate,
        NotificationKind.TransferUpdate,
        NotificationKind.ApplicationReceived,
        NotificationKind.ProfileDiscovered,
        NotificationKind.AdvisorConversation,
        NotificationKind.AssessmentUpdate,
        NotificationKind.MessageReceived
    ];

    /// <summary>The word for a kind.</summary>
    public static string Word(NotificationKind kind) => kind switch
    {
        NotificationKind.ResumeRequest => "resume_request",
        NotificationKind.MarketRequest => "market_request",
        NotificationKind.ApplicationUpdate => "application_update",
        NotificationKind.TransferUpdate => "transfer_update",
        NotificationKind.ApplicationReceived => "application_received",
        NotificationKind.ProfileDiscovered => "profile_discovered",
        NotificationKind.AdvisorConversation => "advisor_conversation",
        NotificationKind.AssessmentUpdate => "assessment_update",
        NotificationKind.MessageReceived => "message_received",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>The kind for a word, or <c>null</c>.</summary>
    public static NotificationKind? Read(string? word) => word switch
    {
        "resume_request" => NotificationKind.ResumeRequest,
        "market_request" => NotificationKind.MarketRequest,
        "application_update" => NotificationKind.ApplicationUpdate,
        "transfer_update" => NotificationKind.TransferUpdate,
        "application_received" => NotificationKind.ApplicationReceived,
        "profile_discovered" => NotificationKind.ProfileDiscovered,
        "advisor_conversation" => NotificationKind.AdvisorConversation,
        "assessment_update" => NotificationKind.AssessmentUpdate,
        "message_received" => NotificationKind.MessageReceived,
        _ => null
    };

    /// <summary>
    /// How often a kind may occur at most — or <c>null</c> for "as often as it
    /// happens".
    /// </summary>
    /// <remarks>
    /// <para><strong>A cap of its own per kind, expressly not the hourly
    /// throttle across all kinds</strong> (ADR-0033). The throttle protects the
    /// inbox from frequency; it lets the <em>entry</em> come into being always,
    /// because that lies behind the sign-in and gives nothing away.</para>
    ///
    /// <para>With <c>profile_discovered</c> exactly that is different, and in
    /// it lies the whole reason for this method: one entry per hit would be a
    /// <em>counter over one's own visibility</em> — by a detour the register
    /// that must not come into being here. So here the entry itself falls away,
    /// not only the mail.</para>
    ///
    /// <para>One day is a choice, not a derivation: shorter condenses nothing,
    /// because a company searches several times in one afternoon; longer
    /// swallows the disclosure for whose sake the scout may be built at
    /// all.</para>
    /// </remarks>
    public static TimeSpan? DailyCap(NotificationKind kind) =>
        kind == NotificationKind.ProfileDiscovered ? TimeSpan.FromDays(1) : null;
}
