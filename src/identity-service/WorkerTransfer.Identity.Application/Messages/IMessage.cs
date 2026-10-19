using MediatR;

namespace WorkerTransfer.Identity.Application.Messages;

/// <summary>Marks a message that changes something.</summary>
/// <remarks>
/// Non-generic on purpose: the transaction behaviour is generic over the
/// request and the response and cannot ask whether a type implements
/// <c>ICommandMessage&lt;TResponse&gt;</c> without reflecting over its interfaces on
/// every single dispatch.
/// <para>
/// Deliberately not Noelia's <c>ICommand&lt;T&gt;</c>. That one is
/// <c>IRequest&lt;ApiResponse&lt;T&gt;&gt;</c>, so every handler would answer in
/// an envelope carrying <c>Success</c>, <c>Errors</c> and a trace id — a second
/// error model beside the RFC 9457 documents this service already answers with,
/// and two error models means every endpoint translates between them.
/// </para>
/// </remarks>
public interface ICommandMessage;

/// <summary>A message that changes something and answers with <typeparamref name="TResponse"/>.</summary>
/// <typeparam name="TResponse">What the handler answers.</typeparam>
public interface ICommandMessage<out TResponse> : IRequest<TResponse>, ICommandMessage;

/// <summary>A message that only reads.</summary>
/// <remarks>
/// No transaction is opened for these. A read that needs one is not a read.
/// </remarks>
/// <typeparam name="TResponse">What the handler answers.</typeparam>
public interface IQueryMessage<out TResponse> : IRequest<TResponse>;
