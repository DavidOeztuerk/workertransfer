using MediatR;

namespace WorkerTransfer.Jobs.Application.Messages;

/// <summary>Marks a message that changes something.</summary>
/// <remarks>
/// Deliberately without a type parameter: the transaction scope is generic over
/// request and response and cannot reflect over a type's interfaces on every
/// single dispatch to find <c>ICommandMessage&lt;TResponse&gt;</c>.
/// <para>
/// Deliberately not Noelia's <c>ICommand&lt;T&gt;</c>: that is
/// <c>IRequest&lt;ApiResponse&lt;T&gt;&gt;</c>, so every response would come in
/// an envelope with <c>Success</c>, <c>Errors</c> and a trace id — a second
/// error model beside the RFC 9457 documents this service writes anyway. Two
/// error models means: every endpoint translates.
/// </para>
/// </remarks>
public interface ICommandMessage;

/// <summary>A message that changes something and answers with <typeparamref name="TResponse"/>.</summary>
/// <typeparam name="TResponse">What the handler answers.</typeparam>
public interface ICommandMessage<out TResponse> : IRequest<TResponse>, ICommandMessage;

/// <summary>A message that only reads.</summary>
/// <remarks>
/// No transaction is opened for it. A read that needs one is no read.
/// </remarks>
/// <typeparam name="TResponse">What the handler answers.</typeparam>
public interface IQueryMessage<out TResponse> : IRequest<TResponse>;
