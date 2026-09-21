using Noelia.Core.Identity;
using WorkerTransfer.Scout.Domain.Hit;

namespace WorkerTransfer.Scout.Application.Ports;

/// <summary>Was an Belegen zu einem Menschen vorliegt.</summary>
/// <param name="Belege">Topics und Sprachnamen mit Projekt und Link.</param>
/// <param name="Stand">Was über die Vollständigkeit dieser Liste gilt.</param>
public sealed record EvidenceSheet(IReadOnlyList<Evidence> Belege, EvidenceState State);

/// <summary>Holt Belege zu einem Hit — nie, um ihn zu finden.</summary>
/// <remarks>
/// <para><strong>Die dritte Auflage aus ADR-0036 steht in der Reihenfolge der
/// Aufrufe.</strong> Gesucht wird über <see cref="IProfilsuche"/> und
/// ausschliesslich über <em>genannte</em> Worte; dieser Port wird erst
/// aufgerufen, wenn die Hit schon feststehen und die Release geprüft ist.
/// Ein Evidence darf einen Menschen also schmücken, aber nie auffindbar machen:
/// ein Topic ist eine Aussage über ein Repository, und wer danach suchte,
/// machte sie stillschweigend zu einer über den Menschen (ADR-0033).</para>
///
/// <para>Die Release der Belege ist eine eigene
/// (<c>github.visibility:public</c>) und wird von github-service selbst
/// geprüft. Dieser Dienst fragt im Namen des Aufrufers und bekommt nichts, was
/// nicht freigegeben ist.</para>
///
/// <para>Ein Fehlschlag ist <strong>kein Fehlschlag der Search</strong>: der
/// Hit bleibt, und sein <see cref="Belegstand"/> sagt, dass hier etwas
/// fehlt. Wer nichts auf GitHub hat, ist nicht schlechter, sondern woanders
/// (ADR-0022 §3) — und wer gerade nicht antwortet, sagt über die Person gar
/// nichts.</para>
/// </remarks>
public interface IEvidenceSource
{
    /// <summary>Die Belege zu einem Menschen. Wirft nicht.</summary>
    Task<EvidenceSheet> GetAsync(SubjectId subject, CancellationToken cancellationToken = default);
}
