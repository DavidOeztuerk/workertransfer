using MediatR;

namespace WorkerTransfer.Messages.Application.Messages;

/// <summary>Marks a message that changes something.</summary>
/// <remarks>
/// Not generic, because the transaction behavior is generic over request and
/// response and should not have to think about a type's interfaces on every
/// dispatch.
/// <para>
/// Expressly not Noelia's <c>ICommand&lt;T&gt;</c>: that is
/// <c>IRequest&lt;ApiResponse&lt;T&gt;&gt;</c> and would bring a second error
/// envelope beside the RFC 9457 documents this service answers with.
/// </para>
/// </remarks>
public interface ICommandMessage;

/// <summary>A changing message with a response.</summary>
public interface ICommandMessage<out TResponse> : IRequest<TResponse>, ICommandMessage;

/// <summary>A message that only reads. No transaction is opened for it.</summary>
public interface IQueryMessage<out TResponse> : IRequest<TResponse>;
