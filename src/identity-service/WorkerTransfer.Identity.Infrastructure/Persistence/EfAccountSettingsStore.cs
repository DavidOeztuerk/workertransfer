using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Infrastructure.Persistence;

/// <summary>A person's settings, in their row.</summary>
/// <remarks>
/// <strong>Whoever never set anything has no row</strong> — and gets the
/// default. Writing a row for every account at creation would be asserting a
/// decision nobody took; and the default stands in the aggregate anyway, where
/// one reads it.
/// </remarks>
public sealed class EfAccountSettingsStore(IdentityDbContext context, TimeProvider clock)
    : IAccountSettingsStore
{
    /// <inheritdoc />
    public async Task<AccountSettings> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var row = await context.AccountSettings
            .FirstOrDefaultAsync(entry => entry.SubjectId == subject.Value, cancellationToken);

        return row is null
            ? AccountSettings.Default(subject)
            : AccountSettings.Restore(
                subject,
                row.DeleteAfterMonths,
                Provider(row.AiProvider),
                row.AiModel,
                row.AiKeyEncrypted,
                row.AiKeyTail,
                row.AiAuditLog);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        AccountSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var now = clock.GetUtcNow().UtcDateTime;

        var row = await context.AccountSettings
            .AsTracking()
            .FirstOrDefaultAsync(
                entry => entry.SubjectId == settings.Subject.Value, cancellationToken);

        if (row is null)
        {
            row = new AccountSettingsRow
            {
                SubjectId = settings.Subject.Value,
                CreatedAt = now
            };
            context.AccountSettings.Add(row);
        }

        row.DeleteAfterMonths = settings.DeleteAfterMonths;
        row.AiProvider = Label(settings.Provider);
        row.AiBaseUrl = settings.Address;
        row.AiModel = settings.Model;
        row.AiKeyEncrypted = settings.EncryptedKey;
        row.AiKeyTail = settings.KeyTail;
        row.AiAuditLog = settings.AiAuditLog;
        row.UpdatedAt = now;
    }

    /// <summary>The label in the column — snake_case, as everywhere on the wire.</summary>
    private static string Label(AiProvider provider) => provider switch
    {
        AiProvider.OpenAi => "openai",
        AiProvider.Anthropic => "anthropic",
        _ => "none"
    };

    /// <summary>
    /// An unknown label becomes <see cref="AiProvider.None"/> — not an error.
    /// </summary>
    /// <remarks>
    /// That is the reticent direction: whoever finds a row after a rollback with
    /// a provider that no longer exists should ask nobody and not crash. A row
    /// from before ADR-0051 saying <c>openai_compatible</c> pointed at a server
    /// of the person's own; that choice no longer exists, and the row reads as
    /// "the platform's model".
    /// </remarks>
    private static AiProvider Provider(string label) => label switch
    {
        "openai" => AiProvider.OpenAi,
        "anthropic" => AiProvider.Anthropic,
        _ => AiProvider.None
    };
}
