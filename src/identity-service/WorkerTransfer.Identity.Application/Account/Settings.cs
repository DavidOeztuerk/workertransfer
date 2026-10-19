using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Messages;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Account;

/// <summary>What a person decided about themselves — for showing.</summary>
/// <remarks>
/// <strong>Without the key.</strong> What goes back is only <c>KeyPresent</c>
/// and <c>KeyTail</c>: that is enough to recognise it again, and more must not
/// travel back over the wire. A secret you can retrieve is one you can siphon
/// off.
/// </remarks>
/// <param name="DeleteAfterMonths">The lapse, or <c>null</c>.</param>
/// <param name="Provider">The AI provider as a label.</param>
/// <param name="Address">Its address.</param>
/// <param name="Model">Its model.</param>
/// <param name="KeyPresent">Whether a key is stored.</param>
/// <param name="KeyTail">Its last four characters.</param>
/// <param name="AiAuditLog">Whether requests are logged.</param>
public sealed record SettingsView(
    int? DeleteAfterMonths,
    string Provider,
    string Address,
    string Model,
    bool KeyPresent,
    string KeyTail,
    bool AiAuditLog);

/// <summary>Read one's own settings. Always only one's own.</summary>
/// <param name="Subject">The caller.</param>
public sealed record SettingsQuery(SubjectId Subject) : IQueryMessage<SettingsView>;

/// <inheritdoc cref="SettingsQuery" />
public sealed class SettingsHandler(IAccountSettingsStore store)
    : IRequestHandler<SettingsQuery, SettingsView>
{
    /// <inheritdoc />
    public async Task<SettingsView> Handle(
        SettingsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var state = await store.GetAsync(request.Subject, cancellationToken);
        return View(state);
    }

    /// <summary>The aggregate becomes the view — without the secret.</summary>
    internal static SettingsView View(AccountSettings state) =>
        new(state.DeleteAfterMonths,
            Label(state.Provider),
            state.Address,
            state.Model,
            state.EncryptedKey.Length > 0,
            state.KeyTail,
            state.AiAuditLog);

    internal static string Label(AiProvider provider) => provider switch
    {
        AiProvider.OpenAiCompatible => "openai_compatible",
        AiProvider.Anthropic => "anthropic",
        _ => "none"
    };

    internal static AiProvider Provider(string? label) => label switch
    {
        "openai_compatible" => AiProvider.OpenAiCompatible,
        "anthropic" => AiProvider.Anthropic,
        _ => AiProvider.None
    };
}

/// <summary>Write one's own settings — without the key.</summary>
/// <remarks>
/// The key has a command of its own. Two reasons, and both count: it is rarely
/// changed and would otherwise travel over the wire at every save; and the
/// interface never gets it back, so it could not send it along at all without
/// asking for it afresh every time.
/// </remarks>
/// <param name="Subject">The caller.</param>
/// <param name="DeleteAfterMonths">The lapse, or <c>null</c>.</param>
/// <param name="Provider">The AI provider as a label.</param>
/// <param name="Address">Its address.</param>
/// <param name="Model">Its model.</param>
/// <param name="AiAuditLog">Whether requests are logged.</param>
public sealed record SetSettingsCommand(
    SubjectId Subject,
    int? DeleteAfterMonths,
    string Provider,
    string Address,
    string Model,
    bool AiAuditLog) : ICommandMessage<SettingsView>;

/// <inheritdoc cref="SetSettingsCommand" />
public sealed class SetSettingsHandler(IAccountSettingsStore store)
    : IRequestHandler<SetSettingsCommand, SettingsView>
{
    /// <inheritdoc />
    public async Task<SettingsView> Handle(
        SetSettingsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var state = await store.GetAsync(request.Subject, cancellationToken);

        state.SetPrivacy(request.DeleteAfterMonths);
        state.SetAi(
            SettingsHandler.Provider(request.Provider),
            request.Address,
            request.Model,
            request.AiAuditLog);

        await store.SaveAsync(state, cancellationToken);

        return SettingsHandler.View(state);
    }
}

/// <summary>Store a key or remove it.</summary>
/// <param name="Subject">The caller.</param>
/// <param name="PlainText">
/// The key. Empty means REMOVE and not "leave unchanged": a field that does
/// nothing when empty has no way back to "none".
/// </param>
public sealed record SetAiKeyCommand(SubjectId Subject, string PlainText) : ICommandMessage<bool>;

/// <inheritdoc cref="SetAiKeyCommand" />
public sealed class SetAiKeyHandler(
    IAccountSettingsStore store, ISecrets secrets)
    : IRequestHandler<SetAiKeyCommand, bool>
{
    /// <inheritdoc />
    public async Task<bool> Handle(
        SetAiKeyCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var state = await store.GetAsync(request.Subject, cancellationToken);
        var plainText = request.PlainText.Trim();

        if (plainText.Length == 0)
        {
            state.ClearKey();
        }
        else
        {
            state.SetKey(
                secrets.Encrypt(plainText), secrets.Suffix(plainText));
        }

        await store.SaveAsync(state, cancellationToken);
        return true;
    }
}

/// <summary>
/// A person's AI access — internal, with the key in the clear.
/// </summary>
/// <remarks>
/// Only over <c>/internal/account/{id}/ai</c>. The browser never gets this
/// shape: <c>GET /account/settings</c> deliberately does not carry the key.
/// </remarks>
public sealed record InternalAiAccess(
    string Provider, string Address, string Model, string Key);

/// <summary>Read a person's AI access — for other services, not for the browser.</summary>
public sealed record InternalAiAccessQuery(SubjectId Subject) : IQueryMessage<InternalAiAccess>;

/// <inheritdoc cref="InternalAiAccessQuery" />
public sealed class InternalAiAccessHandler(
    IAccountSettingsStore store, ISecrets secrets)
    : IRequestHandler<InternalAiAccessQuery, InternalAiAccess>
{
    /// <inheritdoc />
    public async Task<InternalAiAccess> Handle(
        InternalAiAccessQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var state = await store.GetAsync(request.Subject, cancellationToken);

        return new InternalAiAccess(
            SettingsHandler.Label(state.Provider),
            state.Address,
            state.Model,
            secrets.Decrypt(state.EncryptedKey) ?? string.Empty);
    }
}

/// <summary>
/// A person's civil name and contact — internal, and expressly without the postal address.
/// </summary>
/// <remarks>
/// <para><strong>Two fields, and they must never become more.</strong> The
/// civil name and the address one can answer at. No postal address, no
/// telephone: ADR-0038 files the postal address as a <em>template</em> for a
/// letterhead, and it belongs to an application the person sends themselves —
/// not in a company's view.</para>
/// </remarks>
public sealed record InternalPerson(string Name, string Email);

/// <summary>
/// What a human being is called — for advisor-service, behind the secret.
/// </summary>
/// <remarks>
/// <para><strong>The secret is not the whole permission here.</strong> It
/// proves that a service is asking, not that it may ask. The permission stands
/// in the ledger, and advisor-service fetches it <em>before</em> this call:
/// stage 3 is the capability <c>advisor.identity:tenant:&lt;id&gt;</c>
/// (ADR-0037 decision 2). Whoever uses this door for something else fetches the
/// check along with it — otherwise they hand out the civil name of a human
/// being who showed it to nobody.</para>
///
/// <para>That is why the door is so narrow: it answers on exactly one id, with
/// exactly two fields, and it knows no list.</para>
/// </remarks>
public sealed record InternalPersonQuery(SubjectId Subject) : IQueryMessage<InternalPerson?>;

/// <inheritdoc cref="InternalPersonQuery" />
public sealed class InternalPersonHandler(IUserRepository accounts)
    : IRequestHandler<InternalPersonQuery, InternalPerson?>
{
    /// <inheritdoc />
    public async Task<InternalPerson?> Handle(
        InternalPersonQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await accounts.FindByIdAsync(request.Subject, cancellationToken);

        // `FullName` falls back to the display name when no civil one is
        // stored — the same thing that stands under a cover letter. Empty stays
        // empty, and the caller then leaves the field out altogether: not
        // released and not present look the same.
        return account is null ? null : new InternalPerson(account.FullName, account.Email);
    }
}
