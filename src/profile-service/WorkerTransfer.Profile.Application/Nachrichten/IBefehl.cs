using MediatR;

namespace WorkerTransfer.Profile.Application.Nachrichten;

/// <summary>Markiert eine Nachricht, die etwas ändert.</summary>
/// <remarks>
/// Absichtlich ohne Typparameter: die Transaktionsklammer ist generisch über
/// Anfrage und Antwort und kann nicht bei jedem einzelnen Versand über die
/// Schnittstellen eines Typs reflektieren, um <c>IBefehl&lt;TAntwort&gt;</c> zu
/// finden.
/// <para>
/// Bewusst nicht Girders <c>ICommand&lt;T&gt;</c>: das ist
/// <c>IRequest&lt;ApiResponse&lt;T&gt;&gt;</c>, jede Antwort käme also in einem
/// Umschlag mit <c>Success</c>, <c>Errors</c> und einer Ablaufkennung — ein
/// zweites Fehlermodell neben den RFC-9457-Dokumenten, die dieser Dienst
/// ohnehin schreibt. Zwei Fehlermodelle heißt: jeder Endpunkt übersetzt.
/// </para>
/// </remarks>
public interface IBefehl;

/// <summary>Eine Nachricht, die etwas ändert und mit <typeparamref name="TAntwort"/> antwortet.</summary>
/// <typeparam name="TAntwort">Was der Handler antwortet.</typeparam>
public interface IBefehl<out TAntwort> : IRequest<TAntwort>, IBefehl;

/// <summary>Eine Nachricht, die nur liest.</summary>
/// <remarks>
/// Für sie wird keine Transaktion geöffnet. Ein Lesen, das eine braucht, ist
/// kein Lesen.
/// </remarks>
/// <typeparam name="TAntwort">Was der Handler antwortet.</typeparam>
public interface IAbfrage<out TAntwort> : IRequest<TAntwort>;
