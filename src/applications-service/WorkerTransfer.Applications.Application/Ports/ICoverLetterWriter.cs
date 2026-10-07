using WorkerTransfer.Applications.Domain.Applications;

namespace WorkerTransfer.Applications.Application.Ports;

/// <summary>No provider is set up — or it does not answer.</summary>
/// <remarks>
/// The message lands in the draft's <c>error</c> field and in the problem detail
/// of <c>/write</c> and <c>/revise</c>, and the interface classifies it by its
/// words (<c>web/src/features/work/lib/draftErrors.ts</c>): "no writing-help
/// provider", "timeout", "empty response", a 4xx/5xx number. Whoever rewords a
/// reason checks it against that file — otherwise the person reads "the
/// provider answers" where she should read "nothing is set up".
/// </remarks>
public sealed class CoverLetterUnavailable(string message) : Exception(message)
{
    /// <summary>The one sentence for "nothing is set up".</summary>
    public const string NoProvider = "No writing-help provider is set up.";
}

/// <summary>A station from one's own résumé, as the model sees it.</summary>
public sealed record CareerStation(
    string Employer,
    string Title,
    string Period,
    string Description,
    IReadOnlyList<string> Technologies);

/// <summary>
/// What may go into the prompt — and with it also: what may not (ADR-0034).
/// </summary>
/// <remarks>
/// <strong>This class IS the boundary.</strong> It carries two things and
/// nothing third:
/// <list type="number">
///   <item>the <em>own</em> details of the applying person,</item>
///   <item>the <em>public</em> job advert.</item>
/// </list>
/// <para>
/// <strong>Never a third person.</strong> No other applicants, no comparison
/// figures, no assessment — nothing that would be a statement <em>about</em>
/// somebody. A test pins the field list, as ADR-0024 does for the other two
/// consumers.
/// </para>
/// <para>
/// <strong>The company name may stand here</strong>, unlike in
/// <c>AdvertDraft</c>. There it would be the beginning of a statement about a
/// company; here it is the addressee. Nobody can write a letter without knowing
/// to whom.
/// </para>
/// </remarks>
public sealed record CoverLetterContext(
    string JobTitle,
    string Company,
    string JobLocation,
    string JobDescription,
    IReadOnlyList<string> RequestedSkills,
    string OwnName,
    string OwnHeadline,
    string OwnText,
    IReadOnlyList<string> OwnSkills,
    IReadOnlyList<CareerStation> OwnCareer,
    string Language)
{
    /// <summary>The rules that stand above every call.</summary>
    /// <remarks>
    /// <strong>"Invent nothing" is not politeness.</strong> An invented sentence
    /// in the cover letter is a false statement the person is liable for, not
    /// the platform. And the rule against self-rating is ADR-0022 at the place
    /// where it is easiest to overlook: "I am 90 % suitable" would be a number
    /// about a human being, only from their own pen.
    /// <para>
    /// <strong>The prompt stays German</strong>: it tells the model to write in
    /// the language the account names, and its wording, the labels and the
    /// <c>BETREFF:</c> marker <see cref="Drafts.CoverLetterFormat"/> reads back
    /// were measured in German. Translating it would change what the model
    /// writes, not what the code says.
    /// </para>
    /// </remarks>
    public const string Rules = """
        Du schreibst ein Bewerbungsanschreiben in der Ich-Form.

        Sprache: genau die angegebene (de, en oder fr). Schreibe wie ein
        Muttersprachler. Nur vollstaendige, grammatisch korrekte Saetze.
        Keine Wortschoepfungen, keine falschen Verbformen, keine woertlich
        uebersetzten Floskeln, keine halben Konstruktionen.

        1. Verwende ausschliesslich die angegebenen Angaben der Person. Erfinde
           KEINE Qualifikationen, keine Arbeitgeber, keine Zeitraeume, keine
           Abschluesse, keine Orte, keine Familie und keine Zahlen. Was nicht
           dasteht, existiert nicht — auch keinen Hof, keine Oma, keine Tools.
        2. Ist der Werdegang dünn: erfinde nichts. Schreib trotzdem einen
           vollstaendigen Brief — Anrede, zwei bis vier Absaetze, Schluss —
           aus Ueberschrift, Text, Faehigkeiten und der Stellenanzeige.
        3. Beziehe dich konkret auf die Stellenbeschreibung. Belege nur mit
           Angaben, die dastehen.
        4. Keine Selbstbewertung in Zahlen — kein Prozentwert, keine Note, kein
           "zu 90 % geeignet".
        5. Keine Floskeln wie "hiermit bewerbe ich mich".
        6. Haupttext 180 bis 250 Woerter — eine Seite, nicht ein duenner Absatz.
        7. Die Signatur ist GENAU der volle Name aus den Angaben, kein Spitzname
           und kein anderer Name.
        8. Angaben in eckigen Klammern wie [PERSON_NAME], [COMPANY] oder
           [EMPLOYER_1] sind Platzhalter fuer echte Angaben. Uebernimm sie
           zeichengenau dort, wo die Angabe hingehoert. Ersetze sie nie durch
           einen erfundenen Namen und erfinde keine neuen.

        Ausgabeformat, genau so, vollstaendige Woerter, kein fehlender Buchstabe:
        BETREFF: <Betreffzeile>
        ---
        <Anschreiben mit Anrede, Absaetzen und Gruss, ohne Briefkopf>
        """;
}

/// <summary>Drafts a cover letter — on request, and only then.</summary>
public interface ICoverLetterWriter
{
    /// <summary>Writes. Keeps nothing.</summary>
    /// <exception cref="CoverLetterUnavailable">No provider, or it is silent.</exception>
    Task<string> WriteAsync(
        AiAccess access,
        CoverLetterContext context,
        Func<string, Task>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Revises along the comments.</summary>
    /// <remarks>
    /// The comments are the whole instruction — verbatim, with their passage,
    /// and with the instruction to leave everything uncommented alone.
    /// </remarks>
    /// <exception cref="CoverLetterUnavailable">No provider, or it is silent.</exception>
    Task<string> ReviseAsync(
        AiAccess access,
        CoverLetterContext context,
        string subjectLine,
        string text,
        IReadOnlyList<string> comments,
        Func<string, Task>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>One's own details, fetched rather than believed.</summary>
/// <remarks>
/// <strong>The context is filled SERVER-SIDE.</strong> Letting the browser send
/// it would be shorter and would make the promise of ADR-0034 worthless: "only
/// one's own data" is a claim only the server can keep. It is fetched with the
/// caller's token, so exactly what this person may see anyway.
/// </remarks>
public interface IApplicantLookup
{
    /// <summary>Profile and career of the applying person.</summary>
    /// <exception cref="ApplicantLookupSilent">A service involved did not answer.</exception>
    Task<OwnDetails> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The letterhead — civil name and postal address. Never part of <see cref="CoverLetterContext"/>.
/// </summary>
/// <remarks>
/// A port of its own so nobody mixes it into the prompt (ADR-0038). A failure
/// delivers empty, not an exception: sending must not fail on the postal
/// address.
/// </remarks>
public interface IContactLookup
{
    /// <summary>What stands on the letterhead at the moment of sending.</summary>
    Task<ApplicantContact> GetContactAsync(CancellationToken cancellationToken = default);
}

/// <summary>What the person wrote about herself.</summary>
/// <param name="Language">
/// In which language the letter is to come about — from the account, not from a
/// header of the request (ADR-0031).
/// </param>
public sealed record OwnDetails(
    string Name,
    string Headline,
    string Text,
    IReadOnlyList<string> Skills,
    IReadOnlyList<CareerStation> Career,
    string Language = "de");

/// <summary>What the company the letter goes to is called.</summary>
/// <remarks>
/// The name comes from the <em>public</em> employer profile — the same one
/// everybody reads on the careers page. It is the letter's addressee and not a
/// statement about the company.
/// </remarks>
public interface ICompanyNameLookup
{
    /// <summary>The name, or empty when none can be had.</summary>
    /// <remarks>
    /// Empty instead of an exception: a letter without a company name is a
    /// letter with "Sehr geehrte Damen und Herren", and that is no error case.
    /// Letting an application fail because an employer profile is missing would
    /// be strictness at the expense of the wrong person.
    /// </remarks>
    Task<string> NameAsync(
        Noelia.Core.Identity.TenantId company, CancellationToken cancellationToken = default);
}

/// <summary>A service holding one's own details did not answer.</summary>
public sealed class ApplicantLookupSilent(string reason, Exception? cause = null)
    : Exception(reason, cause);
