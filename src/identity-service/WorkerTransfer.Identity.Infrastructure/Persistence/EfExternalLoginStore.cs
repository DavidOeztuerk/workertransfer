using Microsoft.EntityFrameworkCore;
using Noelia.Core.Identity;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Infrastructure.Persistence;

/// <summary>Reads and writes <c>external_logins</c> (ADR-0053).</summary>
public sealed class EfExternalLoginStore(IdentityDbContext context) : IExternalLoginStore
{
    /// <inheritdoc />
    public async Task<ExternalLogin?> FindAsync(
        ExternalProvider provider, string subject, CancellationToken cancellationToken = default)
    {
        var word = ExternalProviders.Word(provider);
        var row = await context.ExternalLogins.FirstOrDefaultAsync(
            entry => entry.Provider == word && entry.ProviderSubject == subject, cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalLogin>> ForUserAsync(SubjectId user, CancellationToken cancellationToken = default)
    {
        var rows = await context.ExternalLogins
            .Where(entry => entry.UserId == user.Value)
            .OrderBy(entry => entry.CreatedAt)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(ToDomain)];
    }

    /// <inheritdoc />
    public Task AddAsync(ExternalLogin login, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(login);

        context.ExternalLogins.Add(new ExternalLoginRow
        {
            Provider = ExternalProviders.Word(login.Provider),
            ProviderSubject = login.Subject,
            UserId = login.User.Value,
            CreatedAt = login.CreatedAt.UtcDateTime
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(SubjectId user, ExternalProvider provider, CancellationToken cancellationToken = default)
    {
        var word = ExternalProviders.Word(provider);
        return await context.ExternalLogins
            .Where(entry => entry.UserId == user.Value && entry.Provider == word)
            .ExecuteDeleteAsync(cancellationToken) > 0;
    }

    private static ExternalLogin ToDomain(ExternalLoginRow row) =>
        new(
            ExternalProviders.Read(row.Provider) ?? throw new InvalidOperationException($"Unknown provider: {row.Provider}"),
            row.ProviderSubject,
            new SubjectId(row.UserId),
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero));
}
