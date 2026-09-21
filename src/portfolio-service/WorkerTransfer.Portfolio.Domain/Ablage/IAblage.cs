using Noelia.Core.Identity;

namespace WorkerTransfer.Portfolio.Domain.Ablage;

/// <summary>Eine abgelegte Datei, so wie sie ausgeliefert wird.</summary>
/// <param name="Inhalt">Der Datenstrom. Der Aufrufer schließt ihn.</param>
/// <param name="Medientyp">Was der Browser damit anfangen soll.</param>
public sealed record Abgelegtes(Stream Inhalt, string Medientyp) : IAsyncDisposable
{
    /// <inheritdoc />
    public ValueTask DisposeAsync() => Inhalt.DisposeAsync();
}

/// <summary>Wo die Anhänge liegen.</summary>
/// <remarks>
/// <b>Eine Schnittstelle, eine Umsetzung.</b> Das Vorgängerpaket erklärte fünf
/// schwere Abhängigkeiten — pillow, python-magic, boto3, minio, azure — für
/// null Konsumenten, und genau das machte es unbaubar (ADR-0021). Solange keine
/// EnvironmentFile ein Objektlager verlangt, gibt es hier keines.
/// <para>
/// Der Schlüssel entsteht aus <c>SubjectId</c> und Name und nie aus einer
/// Eingabe. Das ist die Struktur, die verhindert, dass jemand mit einem fremden
/// Namen an eine fremde Datei kommt.
/// </para>
/// </remarks>
public interface IDocumentStore
{
    /// <summary>Legt eine Datei ab und gibt den Namen zurück, unter dem sie liegt.</summary>
    /// <remarks>
    /// Der Name wird <em>hier</em> vergeben, nicht vom Client geschickt: ein
    /// vom Aufrufer gewählter Name ist ein Pfad, den jemand formen kann.
    /// </remarks>
    Task<string> PutAsync(
        SubjectId subject,
        string dateiname,
        string medientyp,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>Holt eine Datei, oder <c>null</c>, wenn es sie nicht gibt.</summary>
    Task<Abgelegtes?> GetAsync(
        SubjectId subject, string name, CancellationToken cancellationToken = default);

    /// <summary>Löscht alles, was zu dieser Person abgelegt ist.</summary>
    /// <returns>Wie viele Dateien verschwunden sind.</returns>
    /// <remarks>
    /// Eine Datei, die nach der Löschung noch auf der Platte liegt, ist genau
    /// das stille Scheitern, gegen das ADR-0027 antritt: die Zeile ist weg, die
    /// Arbeitsprobe nicht.
    /// </remarks>
    Task<int> DeleteAllAsync(SubjectId subject, CancellationToken cancellationToken = default);
}
