using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Konto;

/// <summary>Die Bewerbungsanschrift zum Anzeigen — nie an ein Modell.</summary>
public sealed record PostalAddressView(
    string Zeile1,
    string Zeile2,
    string Postleitzahl,
    string Ort,
    string Land,
    string Telefon);

/// <summary>Die eigene PostalAddress lesen.</summary>
public sealed record PostalAddressQuery(SubjectId Wer) : IAbfrage<PostalAddressView>;

/// <inheritdoc cref="AnschriftAbfrage" />
public sealed class PostalAddressHandler(IPostalAddresses store)
    : IRequestHandler<PostalAddressQuery, PostalAddressView>
{
    /// <inheritdoc />
    public async Task<PostalAddressView> Handle(
        PostalAddressQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ansicht(await store.GetAsync(request.Wer, cancellationToken));
    }

    internal static PostalAddressView Ansicht(PostalAddress state) =>
        new(state.Zeile1, state.Zeile2, state.Postleitzahl, state.Ort, state.Land, state.Telefon);
}

/// <summary>Die eigene PostalAddress schreiben.</summary>
public sealed record SetPostalAddressCommand(
    SubjectId Wer,
    string? Zeile1,
    string? Zeile2,
    string? Postleitzahl,
    string? Ort,
    string? Land,
    string? Telefon) : IBefehl<PostalAddressView>;

/// <inheritdoc cref="AnschriftSetzenBefehl" />
public sealed class SetPostalAddressHandler(IPostalAddresses store)
    : IRequestHandler<SetPostalAddressCommand, PostalAddressView>
{
    /// <inheritdoc />
    public async Task<PostalAddressView> Handle(
        SetPostalAddressCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var state = await store.GetAsync(request.Wer, cancellationToken);
        state.Set(
            request.Zeile1,
            request.Zeile2,
            request.Postleitzahl,
            request.Ort,
            request.Land,
            request.Telefon);
        await store.SaveAsync(state, cancellationToken);
        return PostalAddressHandler.Ansicht(state);
    }
}
