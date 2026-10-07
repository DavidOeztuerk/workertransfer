using System.Text.Json.Serialization;

namespace WorkerTransfer.Messages.Contracts;

/// <summary>A message, as its own side or the other side reads it.</summary>
/// <remarks>
/// <para><c>origin</c> is only ever filled for the own side: whether a model
/// drafted a message the other side received is open question J2, and until
/// it is answered the text belongs to whoever released it (ADR-0047).</para>
///
/// <para>No author id goes out. The person sees "the company", not the
/// colleague; the company sees the person it already knows from the
/// application.</para>
/// </remarks>
public sealed record MessageV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("own")] bool Own,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("sent_at")] DateTimeOffset? SentAt,
    [property: JsonPropertyName("origin")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Origin = null);

/// <summary>The conversation of one process, as the caller's side sees it.</summary>
public sealed record ConversationV1(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("reference")] Guid Reference,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("messages")] IReadOnlyList<MessageV1> Messages);

/// <summary>One line of "my conversations".</summary>
/// <remarks>
/// <para>No unread count and no preview: see ADR-0047, "Kein Gelesen-Zähler".</para>
///
/// <para><strong>The counterpart, and only the counterpart.</strong> The
/// person sees which company (<c>company_id</c>) — it wrote to them; the
/// company sees which person (<c>person_id</c>) — it applied to or was
/// approached by it, and the profile behind that id answers only while the
/// person releases it.</para>
/// </remarks>
public sealed record ConversationSummaryV1(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("reference")] Guid Reference,
    [property: JsonPropertyName("last_message_at")] DateTimeOffset LastMessageAt,
    [property: JsonPropertyName("company_id")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? CompanyId = null,
    [property: JsonPropertyName("person_id")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? PersonId = null);

/// <summary>A company's first message to a person — released by a recruiter.</summary>
public sealed record OpenApproachV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("drafted")] bool Drafted = false);

/// <summary>A person's unsolicited application — sending it releases her profile to the company.</summary>
public sealed record OpenInitiativeV1(
    [property: JsonPropertyName("company_id")] Guid CompanyId,
    [property: JsonPropertyName("text")] string? Text);

/// <summary>The AI advisor's words to the person — never stored (ADR-0049).</summary>
public sealed record AdviceV1(
    [property: JsonPropertyName("advice")] string Advice);

/// <summary>What a human being sends.</summary>
public sealed record WriteMessageV1(
    [property: JsonPropertyName("text")] string? Text);

/// <summary>Releasing a draft — as it stands, or changed.</summary>
public sealed record ReleaseDraftV1(
    [property: JsonPropertyName("text")] string? Text = null);

/// <summary>
/// "A message arrived" — to notification-service, through the outbox.
/// </summary>
/// <remarks>
/// An id and a kind, never the sender and never a word of the text: the mail
/// may land in an inbox the current employer reads.
/// </remarks>
public sealed record MessageNotificationV1(
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("kind")] string Kind);
