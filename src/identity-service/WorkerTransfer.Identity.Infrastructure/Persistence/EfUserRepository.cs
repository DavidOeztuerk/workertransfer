using System.Text.Json;
using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Infrastructure.Persistence;

/// <summary>Reads and writes the <c>users</c> table.</summary>
public sealed class EfUserRepository(IdentityDbContext context, TimeProvider uhr) : IUserRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// The comparison is the database's. <c>users.email</c> is citext, so it is
    /// case-insensitive there; comparing in memory would make one account into
    /// two.
    /// </remarks>
    public async Task<User?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var row = await context.Users
            .FirstOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);

        return row is null ? null : ZumAggregat(row);
    }

    /// <inheritdoc />
    public async Task<User?> FindByIdAsync(
        SubjectId id,
        CancellationToken cancellationToken = default)
    {
        var row = await context.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == id.Value, cancellationToken);

        return row is null ? null : ZumAggregat(row);
    }

    /// <inheritdoc />
    public Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var jetzt = uhr.GetUtcNow().UtcDateTime;

        context.Users.Add(new UserRow
        {
            Id = user.Id.Value,
            Email = user.Email,
            PasswordHash = user.PasswordHash,
            DisplayName = user.DisplayName,
            GivenName = user.GivenName,
            FamilyName = user.FamilyName,
            Status = user.Status,
            Roles = JsonSerializer.Serialize(user.Roles),
            PendingCompanyName = user.PendingCompanyName,
            Language = Sprachwahl.Etikett(user.Kontosprache),
            CreatedAt = jetzt,
            UpdatedAt = jetzt,
            Version = 1
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reads the row tracked, which the context does not do by default. The
    /// default is right for every read path — the aggregate normalises, and a
    /// tracked read would mark rows modified that nobody touched — and it is
    /// wrong here, where the point <em>is</em> to modify. Reading it inside the
    /// caller's transaction also gives the concurrency token an original to
    /// compare against.
    /// <para>
    /// Only what an aggregate can change is written: neither the address nor
    /// the password entry has a way to change on it, so writing them back would
    /// only be an opportunity to write them back wrong.
    /// </para>
    /// </remarks>
    public async Task SaveAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var row = await context.Users
            .AsTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == user.Id.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No account {user.Id} to save. Was it added in this transaction?");

        row.Status = user.Status;
        row.PendingCompanyName = user.PendingCompanyName;
        row.Language = Sprachwahl.Etikett(user.Kontosprache);
        row.GivenName = user.GivenName;
        row.FamilyName = user.FamilyName;
        row.UpdatedAt = uhr.GetUtcNow().UtcDateTime;
        row.Version += 1;
    }

    private static User ZumAggregat(UserRow row) => User.Restore(
        new SubjectId(row.Id),
        row.Email,
        row.PasswordHash,
        row.DisplayName,
        row.Status,
        JsonSerializer.Deserialize<List<string>>(row.Roles) ?? [],
        row.PendingCompanyName,
        Sprachwahl.Aus(row.Language),
        row.GivenName,
        row.FamilyName);
}
