using Noelia.Core.Identity;

namespace WorkerTransfer.Applications.Application.Ports;

/// <summary>Schreiben und Überarbeiten laufen hinter der ResumeRequest weiter.</summary>
public enum CoverLetterJobKind
{
    /// <summary>Ein neues Anschreiben.</summary>
    Schreiben,

    /// <summary>Offene Anmerkungen umsetzen.</summary>
    Ueberarbeiten
}

/// <summary>Ein Auftrag an das Modell, gebunden an den Aufrufer.</summary>
/// <param name="Wer">Wessen Entwurf.</param>
/// <param name="EntwurfId">Welcher Entwurf.</param>
/// <param name="Traeger">Das Zugriffstoken, damit die Auskunft denselben sieht.</param>
/// <param name="Art">Schreiben oder überarbeiten.</param>
public sealed record CoverLetterJob(
    SubjectId Wer, Guid EntwurfId, string? Traeger, CoverLetterJobKind Art);

/// <summary>Nimmt Aufträge entgegen, ohne auf das Modell zu warten.</summary>
public interface ICoverLetterQueue
{
    /// <summary>Stellt den Auftrag hinten an. <c>false</c>, wenn er schon läuft.</summary>
    bool Plane(CoverLetterJob job);

    /// <summary>Ob dieser Entwurf gerade geschrieben wird.</summary>
    bool Laeuft(Guid entwurfId);
}
