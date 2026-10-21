using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Infrastructure.Persistence;

/// <summary>A company's AI access, in its row. No row means the platform's model.</summary>
public sealed class EfCompanyAiSettingsStore(IdentityDbContext context, TimeProvider clock)
    : ICompanyAiSettingsStore
{
    /// <inheritdoc />
    public async Task<CompanyAiSettings> GetAsync(
        TenantId company, CancellationToken cancellationToken = default)
    {
        var row = await context.CompanyAiSettings
            .FirstOrDefaultAsync(entry => entry.TenantId == company.Value, cancellationToken);

        return row is null
            ? CompanyAiSettings.Default(company)
            : CompanyAiSettings.Restore(
                company, Provider(row.AiProvider), row.AiModel, row.AiKeyEncrypted, row.AiKeyTail, row.AiBaseUrl);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        CompanyAiSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var row = await context.CompanyAiSettings
            .AsTracking()
            .FirstOrDefaultAsync(entry => entry.TenantId == settings.Company.Value, cancellationToken);

        if (row is null)
        {
            row = new CompanyAiSettingsRow { TenantId = settings.Company.Value };
            context.CompanyAiSettings.Add(row);
        }

        row.AiProvider = settings.Provider switch
        {
            AiProvider.OpenAi => "openai",
            AiProvider.Anthropic => "anthropic",
            AiProvider.OwnServer => "own",
            _ => "none"
        };
        row.AiModel = settings.Model;
        row.AiBaseUrl = settings.OwnAddress;
        row.AiKeyEncrypted = settings.EncryptedKey;
        row.AiKeyTail = settings.KeyTail;
        row.UpdatedAt = clock.GetUtcNow().UtcDateTime;
    }

    /// <summary>An unknown label reads as the platform's model — never as an error.</summary>
    private static AiProvider Provider(string label) => label switch
    {
        "openai" => AiProvider.OpenAi,
        "anthropic" => AiProvider.Anthropic,
        "own" => AiProvider.OwnServer,
        _ => AiProvider.None
    };
}
