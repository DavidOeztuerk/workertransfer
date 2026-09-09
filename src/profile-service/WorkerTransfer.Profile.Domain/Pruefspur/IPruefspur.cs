namespace WorkerTransfer.Profile.Domain.Pruefspur;

/// <summary>Hängt an die Spur an.</summary>
/// <remarks>
/// Nur Anhängen — kein Lesen, kein Löschen. Lesen ist die Aufgabe eines
/// Betreibers und nicht die dieses Dienstes; Löschen wäre das Gegenteil dessen,
/// wofür die Spur da ist.
/// <para>
/// Der Schreibvorgang tritt der Transaktion bei, die der Aufrufer offen hat.
/// Das ist der ganze Zweck: ein Eintrag, der außerhalb der Transaktion
/// entsteht, die ihn verursacht hat, überlebt einen Rücklauf oder fehlt nach
/// einem Abschluss — und sagt in beiden Fällen etwas, das nicht geschehen ist.
/// </para>
/// </remarks>
public interface IPruefspur
{
    /// <param name="eintrag">Was festzuhalten ist.</param>
    /// <param name="cancellationToken">Bricht den Schreibvorgang ab.</param>
    Task HaengeAnAsync(Pruefeintrag eintrag, CancellationToken cancellationToken = default);
}
