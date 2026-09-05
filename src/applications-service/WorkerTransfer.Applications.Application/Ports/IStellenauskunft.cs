using Girder.Core.Identity;

namespace WorkerTransfer.Applications.Application.Ports;

/// <summary>Was eine Bewerbung von der Stelle braucht.</summary>
/// <remarks>
/// Was hier steht, steht auch in der ANZEIGE — jedes Feld ist öffentlich
/// lesbar, ohne Anmeldung. Das ist die Bedingung dafür, dass es in einen
/// Anschreiben-Kontext darf (ADR-0034): der Brief bezieht sich auf das, was
/// das Unternehmen selbst geschrieben hat, und auf nichts sonst.
/// <para>
/// <c>Firma</c> ist die Ausnahme und keine Aussage: sie sagt, wem die Stelle
/// gehört, damit die Bewerbung dorthin findet. Sie geht nicht in den Prompt.
/// </para>
/// </remarks>
public sealed record OeffentlicheStelle(
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

/// <summary>Der Jobs-Dienst schweigt — wir wissen nicht, ob es die Stelle gibt.</summary>
/// <remarks>
/// Nicht als „gibt es nicht" behandeln: das wäre eine Behauptung über eine
/// Ausschreibung, die vielleicht offen ist, und die Person bekäme eine Absage,
/// die niemand ausgesprochen hat.
/// </remarks>
public sealed class StelleSchweigt(string grund, Exception? ursache = null)
    : Exception(grund, ursache);

/// <summary>Fragt den Jobs-Dienst nach einer öffentlichen Stelle.</summary>
public interface IStellenauskunft
{
    /// <summary>Die Stelle, oder <c>null</c>, wenn es sie öffentlich nicht gibt.</summary>
    /// <remarks>
    /// <c>null</c> deckt „existiert nicht", „ist noch ein Entwurf" und „ist
    /// geschlossen" ab — der Jobs-Dienst hält die drei ohnehin
    /// ununterscheidbar, und das ist hier genau richtig.
    /// </remarks>
    /// <exception cref="StelleSchweigt">Der Jobs-Dienst hat nicht geantwortet.</exception>
    Task<OeffentlicheStelle?> HoleAsync(Guid stelle, CancellationToken cancellationToken = default);
}
