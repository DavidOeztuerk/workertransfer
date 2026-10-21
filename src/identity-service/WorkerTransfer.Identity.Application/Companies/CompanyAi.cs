using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Account;
using WorkerTransfer.Identity.Application.Messages;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Companies;

/// <summary>Which model writes for a company — for showing, without the key.</summary>
/// <param name="Provider">The provider as a label: none, openai, anthropic.</param>
/// <param name="Address">Its address, set by the platform.</param>
/// <param name="Model">Its model.</param>
/// <param name="KeyPresent">Whether a key is stored.</param>
/// <param name="KeyTail">Its last four characters.</param>
public sealed record CompanyAiView(
    string Provider, string Address, string Model, bool KeyPresent, string KeyTail);

/// <summary>Read a company's AI access. Who may is decided at the endpoint (<c>admin</c>).</summary>
/// <param name="Company">Whose.</param>
public sealed record CompanyAiQuery(TenantId Company) : IQueryMessage<CompanyAiView>;

/// <inheritdoc cref="CompanyAiQuery" />
public sealed class CompanyAiHandler(ICompanyAiSettingsStore store)
    : IRequestHandler<CompanyAiQuery, CompanyAiView>
{
    /// <inheritdoc />
    public async Task<CompanyAiView> Handle(
        CompanyAiQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var state = await store.GetAsync(request.Company, cancellationToken);
        return View(state);
    }

    internal static CompanyAiView View(CompanyAiSettings state) =>
        new(SettingsHandler.Label(state.Provider),
            state.Address,
            state.Model,
            state.EncryptedKey.Length > 0,
            state.KeyTail);
}

/// <summary>Set a company's provider and model — without the key.</summary>
/// <param name="Company">Whose.</param>
/// <param name="Provider">The provider as a label.</param>
/// <param name="Model">Its model; empty takes the provider's default.</param>
public sealed record SetCompanyAiCommand(TenantId Company, string Provider, string Model)
    : ICommandMessage<CompanyAiView>;

/// <inheritdoc cref="SetCompanyAiCommand" />
public sealed class SetCompanyAiHandler(ICompanyAiSettingsStore store)
    : IRequestHandler<SetCompanyAiCommand, CompanyAiView>
{
    /// <inheritdoc />
    public async Task<CompanyAiView> Handle(
        SetCompanyAiCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var state = await store.GetAsync(request.Company, cancellationToken);
        state.SetAi(SettingsHandler.Provider(request.Provider), request.Model);

        await store.SaveAsync(state, cancellationToken);
        return CompanyAiHandler.View(state);
    }
}

/// <summary>Store a company's key or remove it.</summary>
/// <param name="Company">Whose.</param>
/// <param name="PlainText">The key. Empty means REMOVE.</param>
public sealed record SetCompanyAiKeyCommand(TenantId Company, string PlainText)
    : ICommandMessage<bool>;

/// <inheritdoc cref="SetCompanyAiKeyCommand" />
public sealed class SetCompanyAiKeyHandler(ICompanyAiSettingsStore store, ISecrets secrets)
    : IRequestHandler<SetCompanyAiKeyCommand, bool>
{
    /// <inheritdoc />
    public async Task<bool> Handle(
        SetCompanyAiKeyCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var state = await store.GetAsync(request.Company, cancellationToken);
        var plainText = request.PlainText.Trim();

        if (plainText.Length == 0)
        {
            state.ClearKey();
        }
        else
        {
            state.SetKey(secrets.Encrypt(plainText), secrets.Suffix(plainText));
        }

        await store.SaveAsync(state, cancellationToken);
        return true;
    }
}

/// <summary>A company's AI access — internal, with the key in the clear.</summary>
/// <param name="Company">Whose.</param>
public sealed record InternalCompanyAiAccessQuery(TenantId Company) : IQueryMessage<InternalAiAccess>;

/// <inheritdoc cref="InternalCompanyAiAccessQuery" />
/// <remarks>
/// The same rule as for a person (<see cref="InternalAiAccessHandler"/>):
/// without a key there is no own access, whatever provider is chosen, and the
/// asking service takes the platform's model.
/// </remarks>
public sealed class InternalCompanyAiAccessHandler(ICompanyAiSettingsStore store, ISecrets secrets)
    : IRequestHandler<InternalCompanyAiAccessQuery, InternalAiAccess>
{
    /// <inheritdoc />
    public async Task<InternalAiAccess> Handle(
        InternalCompanyAiAccessQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var state = await store.GetAsync(request.Company, cancellationToken);
        var key = secrets.Decrypt(state.EncryptedKey) ?? string.Empty;

        if (state.Provider == AiProvider.None || key.Length == 0 || state.Model.Length == 0)
        {
            return InternalAiAccess.None;
        }

        var wire = state.Provider == AiProvider.Anthropic ? "anthropic" : "openai_compatible";

        return new InternalAiAccess(wire, state.Address, state.Model, key);
    }
}
