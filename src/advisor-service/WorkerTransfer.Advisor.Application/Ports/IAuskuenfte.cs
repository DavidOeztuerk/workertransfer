using Girder.Core.Identity;

namespace WorkerTransfer.Advisor.Application.Ports;

/// <summary>Ein anderer Dienst hat nicht geantwortet.</summary>
/// <param name="grund">Die Art des Fehlschlags, nie sein Inhalt.</param>
public sealed class AuskunftSchweigt(string grund) : Exception(grund);

/// <summary>Wer ein Unternehmen ist — Name und bewiesene Domain.</summary>
/// <param name="Domain">
/// Die Domain, auf der das Unternehmen beansprucht wurde (ADR-0019). Sie ist
/// hier der einzige interessante Teil: an ihr entscheidet sich, ob eine Person
/// dieses Unternehmen ausgeschlossen hat.
/// </param>
public sealed record Firmenbild(string Name, string Domain);

/// <summary>Fragt identity-service, wer ein Unternehmen ist.</summary>
public interface IFirmenauskunft
{
    /// <summary>Das Unternehmen, oder <c>null</c>, wenn es keines gibt.</summary>
    /// <exception cref="AuskunftSchweigt">identity-service antwortet nicht.</exception>
    Task<Firmenbild?> HoleAsync(TenantId firma, CancellationToken cancellationToken = default);
}

/// <summary>Bürgerlicher Name und Kontakt eines Menschen.</summary>
/// <remarks>
/// Nur diese zwei. Keine Anschrift, kein Telefon: ADR-0038 legt die Anschrift
/// als <em>Vorlage</em> für den Briefkopf ab, und sie gehört zu einer
/// Bewerbung, die die Person selbst sendet — nicht in die Ansicht eines
/// Unternehmens, das ein Gespräch führt.
/// </remarks>
public sealed record Personenbild(string Name, string Email);

/// <summary>Fragt identity-service, wie ein Mensch heißt.</summary>
/// <remarks>
/// <strong>Gefragt wird erst, nachdem der Ledger Stufe 3 bestätigt hat.</strong>
/// Die Reihenfolge ist die Zusage: über wen nichts freigegeben ist, über den
/// wird auch nichts nachgeschlagen — und es stünde sonst im Protokoll des
/// anderen Dienstes.
/// </remarks>
public interface IPersonenauskunft
{
    /// <summary>Der Mensch, oder <c>null</c>.</summary>
    /// <exception cref="AuskunftSchweigt">identity-service antwortet nicht.</exception>
    Task<Personenbild?> HoleAsync(SubjectId wer, CancellationToken cancellationToken = default);
}

/// <summary>Die Übergabe ist nicht zustande gekommen.</summary>
/// <remarks>
/// transfer-service hat geantwortet, und zwar ablehnend — etwa weil der
/// Marktstatus nicht freigegeben ist oder die Person nicht ansprechbar. Diese
/// Bedingungen stehen <em>dort</em> und werden hier nicht wiederholt; was hier
/// passiert, ist, die Ablehnung ehrlich weiterzureichen.
/// </remarks>
/// <param name="grund">Die Art des Fehlschlags, nie sein Inhalt.</param>
public sealed class UebergabeAbgelehnt(string grund) : Exception(grund);

/// <summary>Macht aus einer Einigung einen Vorgang in transfer-service.</summary>
/// <remarks>
/// <para><strong>Der Dreieckskonsens wird nicht nachgebaut.</strong>
/// transfer-service prüft beim Anlegen selbst, ob der Marktstatus diesem
/// Unternehmen freigegeben ist und ob die Person ansprechbar ist; es hält auch
/// fest, ob eine Freigabe des jetzigen Arbeitgebers nötig ist. Dieser Port ruft
/// genau diese Tür — er kennt keine dieser Regeln.</para>
/// </remarks>
public interface IVorgangsuebergabe
{
    /// <summary>Legt den Vorgang an und gibt seine Kennung zurück.</summary>
    /// <exception cref="AuskunftSchweigt">transfer-service antwortet nicht.</exception>
    /// <exception cref="UebergabeAbgelehnt">transfer-service lehnt ab.</exception>
    Task<Guid> UebergibAsync(
        SubjectId wer, string anlass, CancellationToken cancellationToken = default);
}
