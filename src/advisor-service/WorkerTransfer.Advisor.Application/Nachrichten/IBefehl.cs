using MediatR;

namespace WorkerTransfer.Advisor.Application.Nachrichten;

/// <summary>Markiert eine Nachricht, die etwas ändert.</summary>
/// <remarks>
/// Nicht generisch, weil das Transaktions-Behavior über Anfrage und Antwort
/// generisch ist und nicht bei jedem Versand über die Schnittstellen eines Typs
/// nachdenken soll.
/// <para>
/// Ausdrücklich nicht Girders <c>ICommand&lt;T&gt;</c>: das ist
/// <c>IRequest&lt;ApiResponse&lt;T&gt;&gt;</c> und brächte einen zweiten
/// Fehlerumschlag neben die RFC-9457-Dokumente, die dieser Dienst antwortet.
/// </para>
/// </remarks>
public interface IBefehl;

/// <summary>Eine ändernde Nachricht mit Antwort.</summary>
public interface IBefehl<out TAntwort> : IRequest<TAntwort>, IBefehl;

/// <summary>Eine Nachricht, die nur liest. Für sie wird keine Klammer geöffnet.</summary>
public interface IAbfrage<out TAntwort> : IRequest<TAntwort>;
