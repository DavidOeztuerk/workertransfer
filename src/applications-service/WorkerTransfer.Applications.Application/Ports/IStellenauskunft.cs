using Noelia.Core.Identity;

namespace WorkerTransfer.Applications.Application.Ports;

/// <summary>Was eine JobApplication von der Job braucht.</summary>
/// <remarks>
/// Was hier steht, steht auch in der ANZEIGE — jedes Feld ist öffentlich
/// lesbar, ohne Anmeldung. Das ist die Bedingung dafür, dass es in einen
/// Anschreiben-Kontext darf (ADR-0034): der Brief bezieht sich auf das, was
/// das Unternehmen selbst geschrieben hat, und auf nichts sonst.
/// <para>
/// <c>Firma</c> ist die Ausnahme und keine Aussage: sie sagt, wem die Job
/// gehört, damit die JobApplication dorthin findet. Sie geht nicht in den Prompt.
/// </para>
/// </remarks>
public sealed record PublicJob(
    Guid Id,
    TenantId Firma,
    string Titel,
    string Beschreibung = "",
    string Ort = "",
    IReadOnlyList<string>? Faehigkeiten = null)
{
    /// <summary>Was die Anzeige an Fähigkeiten nennt.</summary>
    public IReadOnlyList<string> Faehigkeiten { get; init; } = Faehigkeiten ?? [];
}

/// <summary>Der Jobs-Dienst schweigt — wir wissen nicht, ob es die Job gibt.</summary>
/// <remarks>
/// Nicht als „gibt es nicht" behandeln: das wäre eine Behauptung über eine
/// Ausschreibung, die vielleicht offen ist, und die Person bekäme eine Absage,
/// die niemand ausgesprochen hat.
/// </remarks>
public sealed class JobLookupSilent(string reason, Exception? ursache = null)
    : Exception(reason, ursache);

/// <summary>Fragt den Jobs-Dienst nach einer öffentlichen Job.</summary>
public interface IJobLookup
{
    /// <summary>Die Job, oder <c>null</c>, wenn es sie öffentlich nicht gibt.</summary>
    /// <remarks>
    /// <c>null</c> deckt „existiert nicht", „ist noch ein Entwurf" und „ist
    /// geschlossen" ab — der Jobs-Dienst hält die drei ohnehin
    /// ununterscheidbar, und das ist hier genau richtig.
    /// </remarks>
    /// <exception cref="StelleSchweigt">Der Jobs-Dienst hat nicht geantwortet.</exception>
    Task<PublicJob?> GetAsync(Guid job, CancellationToken cancellationToken = default);
}
