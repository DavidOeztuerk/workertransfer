using System.Text.Json.Serialization;

namespace WorkerTransfer.Scout.Contracts;

/// <summary>A tick on the wire: a word and a yes or no.</summary>
/// <remarks>
/// <strong>And no sum beside it.</strong> "2 of 3" would be a number about a
/// human being (ADR-0022, ADR-0036 condition 2) — and it would hide the only
/// thing that helps: <em>which</em> word is missing. Whoever adds a field here
/// answers first: is it copied, or is it computed? Only the first may join.
/// </remarks>
public sealed record TickV1(
    [property: JsonPropertyName("word")] string Word,
    [property: JsonPropertyName("named")] bool Named);

/// <summary>A piece of evidence with origin and link.</summary>
/// <remarks>
/// <c>origin</c> says who said it — a human being on a repository
/// (<c>topic</c>) or GitHub's language detection (<c>language</c>). Without it
/// both would read like a statement about the person.
/// <para>
/// <strong>No byte count, no share, no stars.</strong> Exactly from those the
/// deleted package computed its "skill" (ADR-0022 §2). What is not there nobody
/// can add up.
/// </para>
/// </remarks>
public sealed record EvidenceV1(
    [property: JsonPropertyName("word")] string Word,
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("url")] string Url);

/// <summary>A human being who was found.</summary>
/// <remarks>
/// <para><strong>The tick list and the evidence stand in the contract, not in
/// the UI alone</strong> (ADR-0036 decision 3). If that lay only there, the
/// browser would compute a number from the raw data — and ADR-0022 would be back
/// through the back door.</para>
///
/// <para><c>evidence_state</c> is always set, even when there would be nothing
/// to say. Whoever has nothing on GitHub is not worse but elsewhere
/// (ADR-0022 §3): the same answer shape, one word beside it, no empty box and no
/// greyed-out card. A word and no sentence, because the UI phrases it in the
/// reader's language (ADR-0031).</para>
///
/// <para>No <c>fit</c>, no <c>score</c>, no <c>rank</c>, no <c>percent</c> —
/// and no <c>matched_count</c> either, which would be the same under another
/// name.</para>
/// </remarks>
public sealed record HitV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("headline")] string Headline,
    [property: JsonPropertyName("bio")] string Bio,
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("remote_ok")] bool RemoteOk,
    [property: JsonPropertyName("named")] IReadOnlyList<string> Named,
    [property: JsonPropertyName("checks")] IReadOnlyList<TickV1> Checks,
    [property: JsonPropertyName("evidence")] IReadOnlyList<EvidenceV1> Evidence,
    [property: JsonPropertyName("evidence_state")] string EvidenceState,
    /// <summary>The tick for the distance — <c>reachable</c>, <c>further</c>, <c>unsaid</c>.</summary>
    /// <remarks>
    /// <para><strong>And NO kilometre figure beside it.</strong> The distance is
    /// computed and reduced at once to one of these three words; it stands in no
    /// field of this contract (ADR-0041). A number would look like a measurement
    /// but would be the distance between two town centres — and as soon as two
    /// of them stand one below the other, it ranks people.</para>
    ///
    /// <para><c>unsaid</c> is not a no. It means: the person said nothing, or no
    /// job was named, or a place is unknown to us. Whoever said nothing gets no
    /// cross (ADR-0022 §3).</para>
    /// </remarks>
    [property: JsonPropertyName("reach")] string Reach,
    /// <summary>What the person said — as a word, or <c>null</c>.</summary>
    [property: JsonPropertyName("reach_commute")] string? ReachCommute,
    /// <summary>What the job demands — as a word, or <c>null</c>.</summary>
    [property: JsonPropertyName("reach_attendance")] string? ReachAttendance);

/// <summary>A page of hits.</summary>
/// <remarks>
/// <strong>Without a total</strong> (ADR-0026): through the difference to the
/// page length it would reveal how many profiles are <em>not</em> released. The
/// same shape as every other page here: <c>items</c> and <c>next</c>.
/// </remarks>
public sealed record HitPageV1(
    [property: JsonPropertyName("items")] IReadOnlyList<HitV1> Items,
    [property: JsonPropertyName("next")] string? Next);

/// <summary>A saved question.</summary>
/// <remarks>
/// Filters and a name. <strong>No hit, no identifier of a human being who was
/// found, no time of a run</strong> — a saved result about people goes stale
/// against a revocation (ADR-0036 decision 4).
/// </remarks>
public sealed record SearchV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("skills")] IReadOnlyList<string> Skills,
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("remote")] bool Remote,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

/// <summary>What somebody sends to save a search.</summary>
public sealed record CreateSearchV1(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("skills")] IReadOnlyList<string>? Skills,
    [property: JsonPropertyName("location")] string? Location,
    [property: JsonPropertyName("remote")] bool Remote = false);

/// <summary>What somebody sends to have an approach drafted.</summary>
/// <remarks>
/// <c>skills</c> are the words that were searched for — the requester's own
/// words. They stand in the body and not in a saved search, because an approach
/// may also belong to a search nobody saved.
/// </remarks>
public sealed record ApproachRequestV1(
    [property: JsonPropertyName("skills")] IReadOnlyList<string>? Skills,
    [property: JsonPropertyName("wish")] string? Wish);

/// <summary>The draft.</summary>
/// <remarks>
/// <strong>A draft and no dispatch</strong> (ADR-0036 condition 4). This
/// service writes to nobody: the text goes to the browser of whoever asked, and
/// lies there in a form. There is therefore no <c>sent_at</c> here and no
/// recipient — and there is to be none.
/// </remarks>
public sealed record ApproachDraftV1(
    [property: JsonPropertyName("draft")] string Draft);

/// <summary>What this service sends to notification-service.</summary>
/// <remarks>
/// <para><strong>A typed contract and no anonymous object.</strong> The field
/// names are the wire: the receiver declares
/// <c>[JsonPropertyName("user_id")]</c>, and <c>userId</c> silently bound to
/// <c>Guid.Empty</c> there — measured, and the whole notification path was dead
/// as a result (18 outbox rows, not a single mail).</para>
///
/// <para>Two fields, and there must never be more. The outbox stays free of
/// content (ADR-0025), and the message names <strong>no company</strong>
/// (ADR-0033): who is searching is a statement about the company, and the person
/// can do nothing with it as long as nobody has approached them.</para>
/// </remarks>
public sealed record DiscoveryNotificationV1(
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("kind")] string Kind);

/// <summary>A profile as this service reads it from profile-service.</summary>
/// <remarks>
/// <para><strong>The reading side of a seam between two services.</strong> The
/// sender declares the same wire in <c>WorkerTransfer.Profile.Contracts</c> — a
/// <em>shared</em> type would be a coupling between two services that touches
/// both at once at every change.</para>
///
/// <para>That the fields really arrive is pinned by
/// <c>ProfileSearchWireTests</c>: it serialises the sender's type and reads it
/// with this one. A comparison of names would not — it would check the same
/// string twice and break together with it.</para>
///
/// <para><c>skills</c> is the provenance class <em>named</em>. There is no
/// field for evidence here, and that is the third condition of ADR-0036 as a
/// type: what does not arrive cannot be used for finding.</para>
/// </remarks>
public sealed record ForeignProfileV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("headline")] string Headline,
    [property: JsonPropertyName("bio")] string Bio,
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("remote_ok")] bool RemoteOk,
    [property: JsonPropertyName("skills")] IReadOnlyList<string> Skills,
    /// <summary>The commute stage as a WORD, never as a number (ADR-0041).</summary>
    [property: JsonPropertyName("commute_km")] string? CommuteKm = null,
    [property: JsonPropertyName("relocation")] string? Relocation = null);

/// <summary>A page of the internal profile search.</summary>
public sealed record ForeignProfilePageV1(
    [property: JsonPropertyName("items")] IReadOnlyList<ForeignProfileV1> Items,
    [property: JsonPropertyName("next")] string? Next);

/// <summary>A repository as this service reads it from github-service.</summary>
/// <remarks>
/// <para>What is read is the <em>set</em> of topics and language names. The
/// byte counts do not even arrive there (ADR-0022 §2, ADR-0033), and
/// <c>stars</c> is deliberately not read here: an ordering of people by stars
/// would be the ADR-0022 score through the back door, even if it were called
/// "activity".</para>
/// </remarks>
public sealed record ForeignRepositoryV1(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("languages")] IReadOnlyList<string>? Languages,
    [property: JsonPropertyName("topics")] IReadOnlyList<string>? Topics);

/// <summary>A GitHub connection as this service reads it.</summary>
/// <remarks>
/// <c>languages_complete</c> is read and passed on: a set that is incomplete
/// and pretends not to be is the silent completeness of ADR-0022 §3.
/// </remarks>
public sealed record ForeignConnectionV1(
    [property: JsonPropertyName("repositories")] IReadOnlyList<ForeignRepositoryV1>? Repositories,
    [property: JsonPropertyName("languages_complete")] bool LanguagesComplete);

/// <summary>A job advert as this service reads it from jobs-service.</summary>
/// <remarks>
/// <para><strong>Three fields, and no more is read.</strong> No title, no
/// description, no skills: what this service needs from an advert to form a
/// tick is where it lies and whether somebody has to come. What does not arrive
/// cannot be passed on (ADR-0004).</para>
///
/// <para><c>remote_mode</c> carries <c>none</c>, <c>hybrid</c> or <c>full</c> —
/// jobs-service's <c>RemoteMode</c>, read unchanged. It IS the attendance of
/// ADR-0041 §2; a second field for it would be a second truth about the same
/// question.</para>
/// </remarks>
public sealed record ForeignJobV1(
    [property: JsonPropertyName("location")] string? Location,
    [property: JsonPropertyName("postal_code")] string? PostalCode,
    [property: JsonPropertyName("remote_mode")] string? RemoteMode);
