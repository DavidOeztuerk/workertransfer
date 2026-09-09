using Girder.Core.Identity;

namespace WorkerTransfer.Resume.Application.Ports;

/// <summary>The ledger did not answer. Not a business outcome — a system state.</summary>
/// <remarks>
/// Deliberately not a rule violation: <c>false</c> would be a statement about
/// the person that nobody can make right now. The endpoint turns it into 503,
/// which is neither "there is nothing here" nor showing the résumé.
/// </remarks>
public sealed class EinwilligungSchweigt(string grund, Exception? ursache = null)
    : Exception(grund, ursache);

/// <summary>The capability strings, built in exactly one place.</summary>
/// <remarks>
/// If the interface built them there would be two places that had to agree on
/// the format, and one of them would be in a browser.
/// </remarks>
public static class Einwilligungsschluessel
{
    /// <summary>
    /// Whoever may not see the profile may not ask for the résumé either.
    /// </summary>
    /// <remarks>
    /// Otherwise the request would be a channel for learning that a person
    /// exists here at all.
    /// </remarks>
    public const string Profil = "profile.visibility:public";

    /// <summary>
    /// The release names its recipient — this one company, never "everybody".
    /// </summary>
    /// <remarks>
    /// <c>profile.visibility:public</c> says "all companies";
    /// <c>resume.visibility:tenant:&lt;id&gt;</c> says "this one". There is no
    /// public counterpart and there must not be one: a résumé names real
    /// employers with dates, which is exactly what a current employer must not
    /// see.
    /// </remarks>
    public static string Lebenslauf(TenantId firma) => $"resume.visibility:tenant:{firma}";

    /// <summary>Zeugnisse und Zertifikate — eine eigene Fähigkeit.</summary>
    /// <remarks>
    /// <strong>Nicht unter <c>resume</c> mitgeführt</strong>, obwohl beides
    /// „Unterlagen" heißt: der Werdegang ist selbst geschriebener Text, ein
    /// Zeugnis ist ein Dokument Dritter mit Namen, Noten und Unterschriften.
    /// Wer das eine zeigen will und das andere nicht, muss das können — und
    /// eine Fähigkeit, die zwei Dinge freigibt, lässt sich nur ganz oder gar
    /// nicht widerrufen (ADR-0035).
    /// <para>
    /// Auch hier ohne öffentliches Gegenstück: es gibt <c>documents.visibility:
    /// tenant:&lt;id&gt;</c> und nichts, was „alle" hieße.
    /// </para>
    /// </remarks>
    public static string Unterlagen(TenantId firma) => $"documents.visibility:tenant:{firma}";
}

/// <summary>Reads <em>and</em> writes the ledger — unlike the profile service.</summary>
/// <remarks>
/// The person grants and withdraws through this service so that the capability
/// string is built in one place. Read synchronously and cached nowhere
/// (ADR-0013): a withdrawal has to take effect on the very next read, not on
/// the one after.
/// </remarks>
public interface IEinwilligungstor
{
    /// <summary>Whether this person's profile is released at all.</summary>
    /// <exception cref="EinwilligungSchweigt">The ledger did not answer.</exception>
    Task<bool> DarfProfilSehenAsync(SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Whether this company may read this person's résumé right now.</summary>
    /// <exception cref="EinwilligungSchweigt">The ledger did not answer.</exception>
    Task<bool> DarfLebenslaufLesenAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default);

    /// <summary>Ob dieses Unternehmen die Unterlagen dieser Person sehen darf.</summary>
    /// <remarks>
    /// Synchron und ohne Zwischenspeicher wie alles hier: ein Widerruf muss auf
    /// den nächsten Aufruf wirken, nicht auf den übernächsten (ADR-0013).
    /// </remarks>
    /// <exception cref="EinwilligungSchweigt">The ledger did not answer.</exception>
    Task<bool> DarfUnterlagenSehenAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same question for several pairs, in one round trip.
    /// </summary>
    /// <remarks>
    /// The answers come back <em>in the order of the questions</em>, one per
    /// pair. Nothing is cached and nothing is held in reserve — a batch is one
    /// question in one request, not a stock answer.
    /// </remarks>
    /// <exception cref="EinwilligungSchweigt">
    /// The ledger did not answer, or answered a different number of times than
    /// it was asked.
    /// </exception>
    Task<IReadOnlyList<bool>> DuerfenLebenslaufLesenAsync(
        IReadOnlyList<(SubjectId Wer, TenantId Firma)> paare,
        CancellationToken cancellationToken = default);

    /// <summary>Records the release for this one company.</summary>
    /// <exception cref="EinwilligungSchweigt">The ledger did not answer.</exception>
    Task ErteileAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default);

    /// <summary>Takes it back.</summary>
    /// <exception cref="EinwilligungSchweigt">The ledger did not answer.</exception>
    Task WiderrufeAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default);
}
