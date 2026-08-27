using MediatR;

namespace WorkerTransfer.Notification.Application.Nachrichten;

/// <summary>Markiert eine Nachricht, die etwas ändert.</summary>
public interface IBefehl;

/// <summary>Eine ändernde Nachricht mit Antwort.</summary>
public interface IBefehl<out TAntwort> : IRequest<TAntwort>, IBefehl;

/// <summary>Eine Nachricht, die nur liest.</summary>
public interface IAbfrage<out TAntwort> : IRequest<TAntwort>;
