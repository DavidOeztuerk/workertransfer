using Girder.Core.Identity;

namespace WorkerTransfer.Applications.Application.Ports;

/// <summary>Der KI-Zugang der Person, die den Entwurf anfordert.</summary>
/// <remarks>
/// Kommt aus den Kontoeinstellungen (identity-service), nicht aus einer
/// Umgebungsvariable der Plattform. Die Oberfläche richtet Ollama oder MiniMax
/// ein; ohne diese Abfrage sähe der Anschreiben-Dienst immer nur
/// <c>Draft__Schluessel</c> und antwortete 503, während die Person glaubt,
/// sie hätte schon jemanden gefragt.
/// </remarks>
/// <param name="Anbieter"><c>none</c>, <c>openai_compatible</c> oder <c>anthropic</c>.</param>
/// <param name="Adresse">Die vollständige URL des Endpunkts.</param>
/// <param name="Modell">Welches Modell.</param>
/// <param name="Schluessel">Klartext. Bei einem eigenen Server oft leer.</param>
public sealed record KiZugang(
    string Anbieter, string Adresse, string Modell, string Schluessel)
{
    /// <summary>Ob mit diesen Angaben ein Modell gefragt werden kann.</summary>
    public bool IstEingerichtet =>
        Adresse.Length > 0
        && Modell.Length > 0
        && Anbieter switch
        {
            "openai_compatible" => true,
            "anthropic" => Schluessel.Length > 0,
            _ => false
        };
}

/// <summary>Holt den KI-Zugang der Person.</summary>
public interface IKiZugangAbfrage
{
    /// <summary>Was diese Person eingerichtet hat — oder nichts.</summary>
    Task<KiZugang> HoleAsync(SubjectId wer, CancellationToken cancellationToken = default);
}
