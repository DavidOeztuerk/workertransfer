using System.Text.Json;
using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Infrastructure.Persistence;

/// <summary>Reads accounts out of the <c>users</c> table.</summary>
public sealed class EfUserRepository(IdentityDbContext context) : IUserRepository
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

    private static User ZumAggregat(UserRow row) => new()
    {
        Id = new SubjectId(row.Id),
        Email = row.Email,
        PasswordHash = row.PasswordHash,
        DisplayName = row.DisplayName,
        Status = AccountStatusNames.FromDatabase(row.Status),
        Roles = JsonSerializer.Deserialize<List<string>>(row.Roles) ?? []
    };
}
