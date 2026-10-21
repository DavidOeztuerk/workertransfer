using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Account;
using WorkerTransfer.Identity.Application.Messages;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Companies;

/// <summary>Which model writes for a company — for showing, without the key.</summary>
/// <param name="Provider">The provider as a label: none, openai, anthropic, own.</param>
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
        new(Label(state.Provider),
            state.Address,
            state.Model,
            state.EncryptedKey.Length > 0,
            state.KeyTail);

    /// <summary>The wire label; <c>own</c> exists only for companies.</summary>
    internal static string Label(AiProvider provider) =>
        provider == AiProvider.OwnServer ? "own" : SettingsHandler.Label(provider);
}

/// <summary>Set a company's provider and model — without the key.</summary>
/// <param name="Company">Whose.</param>
/// <param name="Provider">The provider as a label: none, openai, anthropic, own.</param>
/// <param name="Model">Its model; empty takes the provider's default (none for <c>own</c>).</param>
/// <param name="Address">The company's own server — read only for <c>own</c>.</param>
public sealed record SetCompanyAiCommand(TenantId Company, string Provider, string Model, string Address = "")
    : ICommandMessage<CompanyAiView>;

/// <inheritdoc cref="SetCompanyAiCommand" />
/// <remarks>
/// The own server is checked against the domain identity proved at the
/// founding (ADR-0019) — read here from the company, never from the request.
/// </remarks>
public sealed class SetCompanyAiHandler(ICompanyAiSettingsStore store, ICompanyRepository companies)
    : IRequestHandler<SetCompanyAiCommand, CompanyAiView>
{
    /// <inheritdoc />
    /// <exception cref="OwnServerRefused">The own server's address or model does not hold.</exception>
    public async Task<CompanyAiView> Handle(
        SetCompanyAiCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var state = await store.GetAsync(request.Company, cancellationToken);

        if (request.Provider == "own")
        {
            var company = await companies.FindByIdAsync(request.Company, cancellationToken);
            state.SetOwnServer(request.Address, request.Model, company?.Domain.Value ?? string.Empty);
        }
        else
        {
            state.SetAi(SettingsHandler.Provider(request.Provider), request.Model);
        }

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
/// without a key there is no own access, whatever cloud provider is chosen,
/// and the asking service takes the platform's model. The company's own server
/// is the one exception: it may answer without a key.
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

        // The own server may run without a key; a cloud provider never does.
        if (state.Provider == AiProvider.None || state.Model.Length == 0
            || (key.Length == 0 && state.Provider != AiProvider.OwnServer))
        {
            return InternalAiAccess.None;
        }

        var wire = state.Provider == AiProvider.Anthropic ? "anthropic" : "openai_compatible";

        return new InternalAiAccess(wire, state.Address, state.Model, key);
    }
}
