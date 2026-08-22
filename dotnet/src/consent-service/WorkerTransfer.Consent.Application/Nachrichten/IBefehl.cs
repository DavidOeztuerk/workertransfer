using MediatR;

namespace WorkerTransfer.Consent.Application.Nachrichten;

/// <summary>Marks a message that changes something.</summary>
/// <remarks>
/// Non-generic on purpose: the transaction behaviour is generic over the
/// request and the response and cannot ask whether a type implements
/// <c>IBefehl&lt;TAntwort&gt;</c> without reflecting over its interfaces on
/// every single dispatch.
/// <para>
/// Deliberately not Girder's <c>ICommand&lt;T&gt;</c>, which answers in an
/// <c>ApiResponse</c> envelope carrying <c>Success</c>, <c>Errors</c> and a
/// trace id — a second error model beside the RFC 9457 documents this service
/// answers with, and two error models means every endpoint translates between
/// them.
/// </para>
/// </remarks>
public interface IBefehl;

/// <summary>A message that changes something and answers with <typeparamref name="TAntwort"/>.</summary>
/// <typeparam name="TAntwort">What the handler answers.</typeparam>
public interface IBefehl<out TAntwort> : IRequest<TAntwort>, IBefehl;

/// <summary>A message that only reads.</summary>
/// <remarks>
/// No transaction is opened for these. A read that needs one is not a read.
/// <para>
/// And no cache either, ever, for the ones that carry a check: a withdrawal has
/// to take effect on the very next read (ADR-0013). That is why nothing in this
/// service implements Girder's <c>ICacheableQuery</c> — a test pins the
/// absence, and <c>AddCQRS</c> then leaves both cache behaviours out of the
/// pipeline entirely.
/// </para>
/// </remarks>
/// <typeparam name="TAntwort">What the handler answers.</typeparam>
public interface IAbfrage<out TAntwort> : IRequest<TAntwort>;
