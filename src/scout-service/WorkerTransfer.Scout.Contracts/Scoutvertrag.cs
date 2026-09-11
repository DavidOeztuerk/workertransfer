using System.Text.Json.Serialization;

namespace WorkerTransfer.Scout.Contracts;

/// <summary>Ein Haken auf dem Draht: ein Wort und ein Ja oder Nein.</summary>
/// <remarks>
/// <strong>Und daneben keine Summe.</strong> „2 von 3" wäre eine Zahl über einen
/// Menschen (ADR-0022, ADR-0036 Auflage 2) — und sie verbärge das Einzige, was
/// hilft: <em>welches</em> Wort fehlt. Wer hier ein Feld ergänzt, beantwortet
/// zuerst: ist es abgeschrieben, oder ist es gerechnet? Nur das Erste darf dazu.
/// </remarks>
public sealed record HakenV1(
    [property: JsonPropertyName("word")] string Word,
    [property: JsonPropertyName("named")] bool Named);

/// <summary>Ein Beleg mit Herkunft und Link.</summary>
/// <remarks>
/// <c>origin</c> sagt, wer es gesagt hat — ein Mensch an ein Repository
/// (<c>topic</c>) oder GitHubs Sprachenerkennung (<c>language</c>). Ohne diese
/// Angabe läse sich beides wie eine Aussage über die Person.
/// <para>
/// <strong>Keine Byte-Zahl, kein Anteil, keine Sterne.</strong> Genau daraus
/// rechnete das gelöschte Paket sein „Können" (ADR-0022 §2). Was nicht da ist,
/// kann niemand aufsummieren.
/// </para>
/// </remarks>
public sealed record BelegV1(
    [property: JsonPropertyName("word")] string Word,
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("url")] string Url);

/// <summary>Ein Mensch, der gefunden wurde.</summary>
/// <remarks>
/// <para><strong>Die Häkchenliste und die Belege stehen im Vertrag, nicht in der
/// Oberfläche allein</strong> (ADR-0036 Entscheidung 3). Läge das nur dort,
/// rechnete der Browser sich aus den Rohdaten eine Zahl — und ADR-0022 wäre
/// durch die Hintertür da.</para>
///
/// <para><c>evidence_state</c> ist immer gesetzt, auch wenn es nichts zu sagen
/// gäbe. Wer nichts auf GitHub hat, ist nicht schlechter, sondern woanders
/// (ADR-0022 §3): dieselbe Antwortgestalt, ein Wort dazu, keine leere Box und
/// keine ausgegraute Karte. Ein Wort und kein Satz, weil die Oberfläche in der
/// Sprache der lesenden Person formuliert (ADR-0031).</para>
///
/// <para>Kein <c>fit</c>, kein <c>score</c>, kein <c>rank</c>, kein
/// <c>percent</c> — und auch kein <c>matched_count</c>, das dasselbe unter
/// anderem Namen wäre.</para>
/// </remarks>
public sealed record TrefferV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("headline")] string Headline,
    [property: JsonPropertyName("bio")] string Bio,
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("remote_ok")] bool RemoteOk,
    [property: JsonPropertyName("named")] IReadOnlyList<string> Named,
    [property: JsonPropertyName("checks")] IReadOnlyList<HakenV1> Checks,
    [property: JsonPropertyName("evidence")] IReadOnlyList<BelegV1> Evidence,
    [property: JsonPropertyName("evidence_state")] string EvidenceState);

/// <summary>Eine Seite Treffer.</summary>
/// <remarks>
/// <strong>Ohne Gesamtzahl</strong> (ADR-0026): sie verriete über die Differenz
/// zur Seitenlänge, wie viele Profile <em>nicht</em> freigegeben sind. Dieselbe
/// Gestalt wie jede andere Seite hier: <c>items</c> und <c>next</c>.
/// </remarks>
public sealed record TrefferseiteV1(
    [property: JsonPropertyName("items")] IReadOnlyList<TrefferV1> Items,
    [property: JsonPropertyName("next")] string? Next);

/// <summary>Eine gespeicherte Anfrage.</summary>
/// <remarks>
/// Filter und ein Name. <strong>Kein Treffer, keine Kennung eines gefundenen
/// Menschen, kein Zeitpunkt eines Laufs</strong> — ein gespeichertes Ergebnis
/// über Menschen veraltet gegen einen Widerruf (ADR-0036 Entscheidung 4).
/// </remarks>
public sealed record SucheV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("skills")] IReadOnlyList<string> Skills,
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("remote")] bool Remote,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

/// <summary>Was jemand schickt, um eine Suche abzulegen.</summary>
public sealed record SucheAnlegenV1(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("skills")] IReadOnlyList<string>? Skills,
    [property: JsonPropertyName("location")] string? Location,
    [property: JsonPropertyName("remote")] bool Remote = false);

/// <summary>Was jemand schickt, um sich eine Ansprache entwerfen zu lassen.</summary>
/// <remarks>
/// <c>skills</c> sind die Worte, nach denen gesucht wurde — die eigenen Worte
/// des Absenders. Sie stehen im Rumpf und nicht in einer gespeicherten Suche,
/// weil eine Ansprache auch zu einer Suche gehören darf, die niemand abgelegt
/// hat.
/// </remarks>
public sealed record AnspracheAnfrageV1(
    [property: JsonPropertyName("skills")] IReadOnlyList<string>? Skills,
    [property: JsonPropertyName("wish")] string? Wish);

/// <summary>Der Entwurf.</summary>
/// <remarks>
/// <strong>Ein Entwurf und kein Versand</strong> (ADR-0036 Auflage 4). Dieser
/// Dienst schreibt niemandem: der Text geht an den Browser dessen, der gefragt
/// hat, und liegt dort in einem Formular. Es gibt hier deshalb kein
/// <c>sent_at</c> und keinen Empfänger — und es soll auch keines geben.
/// </remarks>
public sealed record AnspracheentwurfV1(
    [property: JsonPropertyName("draft")] string Draft);

/// <summary>Was dieser Dienst an notification-service schickt.</summary>
/// <remarks>
/// <para><strong>Ein typisierter Vertrag und kein anonymes Objekt.</strong> Die
/// Feldnamen sind der Draht: der Empfänger deklariert
/// <c>[JsonPropertyName("user_id")]</c>, und <c>userId</c> band dort still auf
/// <c>Guid.Empty</c> — gemessen, und der ganze Benachrichtigungsweg war
/// daraufhin tot (18 Ausgangszeilen, keine einzige Mail).</para>
///
/// <para>Zwei Felder, und mehr dürfen es nie werden. Der Postausgang bleibt
/// inhaltsfrei (ADR-0025), und die Nachricht nennt <strong>kein
/// Unternehmen</strong> (ADR-0033): wer sucht, ist eine Aussage über das
/// Unternehmen, und die Person kann damit nichts anfangen, solange niemand sie
/// angesprochen hat.</para>
/// </remarks>
public sealed record EntdeckungsmeldungV1(
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("kind")] string Kind);

/// <summary>Ein Profil, wie dieser Dienst es von profile-service liest.</summary>
/// <remarks>
/// <para><strong>Die Leseseite einer Naht zwischen zwei Diensten.</strong> Der
/// Absender deklariert denselben Draht in <c>WorkerTransfer.Profile.Contracts</c>
/// — ein <em>geteilter</em> Typ wäre eine Kopplung zwischen zwei Diensten, die
/// bei jeder Änderung beide zugleich anfasst.</para>
///
/// <para>Dass die Felder wirklich ankommen, hält
/// <c>ProfilsuchedrahtTests</c> fest: es serialisiert den Typ des Absenders und
/// liest ihn mit diesem hier. Ein Namensvergleich täte das nicht — er prüfte
/// dieselbe Zeichenkette zweimal und ginge mit ihr gemeinsam kaputt.</para>
///
/// <para><c>skills</c> ist die Herkunftsklasse <em>genannt</em>. Es gibt hier
/// kein Feld für Belege, und das ist die dritte Auflage aus ADR-0036 als Typ:
/// was nicht ankommt, kann nicht zum Finden benutzt werden.</para>
/// </remarks>
public sealed record FremdprofilV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("headline")] string Headline,
    [property: JsonPropertyName("bio")] string Bio,
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("remote_ok")] bool RemoteOk,
    [property: JsonPropertyName("skills")] IReadOnlyList<string> Skills);

/// <summary>Eine Seite der internen Profilsuche.</summary>
public sealed record FremdprofilseiteV1(
    [property: JsonPropertyName("items")] IReadOnlyList<FremdprofilV1> Items,
    [property: JsonPropertyName("next")] string? Next);

/// <summary>Ein Repository, wie dieser Dienst es von github-service liest.</summary>
/// <remarks>
/// <para>Gelesen wird die <em>Menge</em> der Topics und Sprachnamen. Die
/// Byte-Zahlen kommen dort gar nicht erst an (ADR-0022 §2, ADR-0033), und
/// <c>stars</c> wird hier bewusst nicht gelesen: eine Reihenfolge über Menschen
/// nach Sternen wäre die ADR-0022-Punktzahl durch die Hintertür, auch wenn sie
/// „Aktivität" hiesse.</para>
/// </remarks>
public sealed record FremdrepositoryV1(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("languages")] IReadOnlyList<string>? Languages,
    [property: JsonPropertyName("topics")] IReadOnlyList<string>? Topics);

/// <summary>Eine GitHub-Verbindung, wie dieser Dienst sie liest.</summary>
/// <remarks>
/// <c>languages_complete</c> wird gelesen und weitergereicht: eine Menge, die
/// unvollständig ist und so tut, als wäre sie es nicht, ist die
/// stillschweigende Vollständigkeit aus ADR-0022 §3.
/// </remarks>
public sealed record FremdverbindungV1(
    [property: JsonPropertyName("repositories")] IReadOnlyList<FremdrepositoryV1>? Repositories,
    [property: JsonPropertyName("languages_complete")] bool LanguagesComplete);
