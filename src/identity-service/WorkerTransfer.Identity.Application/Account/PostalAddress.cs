using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Messages;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Account;

/// <summary>The application address for showing — never to a model.</summary>
public sealed record PostalAddressView(
    string Line1,
    string Line2,
    string PostalCode,
    string City,
    string Country,
    string Phone);

/// <summary>Read one's own postal address.</summary>
public sealed record PostalAddressQuery(SubjectId Subject) : IQueryMessage<PostalAddressView>;

/// <inheritdoc cref="PostalAddressQuery" />
public sealed class PostalAddressHandler(IPostalAddresses store)
    : IRequestHandler<PostalAddressQuery, PostalAddressView>
{
    /// <inheritdoc />
    public async Task<PostalAddressView> Handle(
        PostalAddressQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return View(await store.GetAsync(request.Subject, cancellationToken));
    }

    internal static PostalAddressView View(PostalAddress state) =>
        new(state.Line1, state.Line2, state.PostalCode, state.City, state.Country, state.Phone);
}

/// <summary>Write one's own postal address.</summary>
public sealed record SetPostalAddressCommand(
    SubjectId Subject,
    string? Line1,
    string? Line2,
    string? PostalCode,
    string? City,
    string? Country,
    string? Phone) : ICommandMessage<PostalAddressView>;

/// <inheritdoc cref="SetPostalAddressCommand" />
public sealed class SetPostalAddressHandler(IPostalAddresses store)
    : IRequestHandler<SetPostalAddressCommand, PostalAddressView>
{
    /// <inheritdoc />
    public async Task<PostalAddressView> Handle(
        SetPostalAddressCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var state = await store.GetAsync(request.Subject, cancellationToken);
        state.Set(
            request.Line1,
            request.Line2,
            request.PostalCode,
            request.City,
            request.Country,
            request.Phone);
        await store.SaveAsync(state, cancellationToken);
        return PostalAddressHandler.View(state);
    }
}
