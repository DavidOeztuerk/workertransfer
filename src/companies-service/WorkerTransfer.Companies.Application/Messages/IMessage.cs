using MediatR;

namespace WorkerTransfer.Companies.Application.Messages;

/// <summary>Marks a message that changes something.</summary>
public interface ICommandMessage;

/// <summary>A changing message with a response.</summary>
public interface ICommandMessage<out TResponse> : IRequest<TResponse>, ICommandMessage;

/// <summary>A message that only reads.</summary>
public interface IQueryMessage<out TResponse> : IRequest<TResponse>;
