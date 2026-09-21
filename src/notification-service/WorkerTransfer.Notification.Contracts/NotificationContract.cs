using System.Text.Json.Serialization;

namespace WorkerTransfer.Notification.Contracts;

/// <summary>What a service sends to notify somebody.</summary>
/// <remarks>
/// Two fields, and it must never become more. No text, no reason, no company
/// name: the kind is enough to show the right place in the application, and the
/// application checks who reads. A field for a sentence would be the line in
/// which "Acme GmbH would like to see your market status" one day stands.
/// </remarks>
public sealed record NotifyV1(
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("kind")] string Kind);

/// <summary>One entry in the inbox.</summary>
public sealed record InboxEntryV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("read_at")] DateTimeOffset? ReadAt);

/// <summary>The eight switches.</summary>
/// <remarks>
/// Without the throttle: it is no setting but a property of the service, and a
/// field for it would be an invitation to switch it off.
/// <para>
/// <c>application_received</c> has the default <c>true</c>: a new kind must not
/// stay away silently because an older body does not know it. If the field is
/// missing, the switch stays on.
/// </para>
/// <para>
/// <c>profile_discovered</c> — "your profile was discovered" — stands at
/// <c>true</c> for the same reason, and it can expressly be switched off
/// <em>on its own</em> (ADR-0033): it is the only disclosure a person gets
/// about their own visibility, and whoever does not want it should be able to
/// switch it off singly, without switching everything else off with it.
/// </para>
/// <para>
/// <c>advisor_conversation</c> — "a company opened a conversation" (ADR-0037) —
/// stands at <c>true</c> as well. It is the counterpart to
/// <c>profile_discovered</c>: there somebody searched, here somebody has
/// <em>approached</em>, and a company the person had already released something
/// to themselves.
/// </para>
/// <para>
/// <c>assessment_update</c> — "something moved on your work sample" (ADR-0042) —
/// stands at <c>true</c> as well. It carries two movements, and the second is a
/// promise: the person <em>sees</em> their evaluation, always. Whoever switches
/// it off loses the mail and not the entry — that stands behind the sign-in in
/// their own list.
/// </para>
/// </remarks>
public sealed record NotificationPreferencesV1(
    [property: JsonPropertyName("resume_request")] bool ResumeRequest,
    [property: JsonPropertyName("market_request")] bool MarketRequest,
    [property: JsonPropertyName("application_update")] bool ApplicationUpdate,
    [property: JsonPropertyName("transfer_update")] bool TransferUpdate,
    [property: JsonPropertyName("application_received")] bool ApplicationReceived = true,
    [property: JsonPropertyName("profile_discovered")] bool ProfileDiscovered = true,
    [property: JsonPropertyName("advisor_conversation")] bool AdvisorConversation = true,
    [property: JsonPropertyName("assessment_update")] bool AssessmentUpdate = true);
