using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Infrastructure.Persistence;

/// <summary>The application address in its row.</summary>
/// <remarks>
/// Whoever never set anything has no row and gets an empty one. Writing a row
/// for every account would be asserting an address nobody stored.
/// </remarks>
public sealed class EfPostalAddresses(IdentityDbContext context, TimeProvider clock) : IPostalAddresses
{
    /// <inheritdoc />
    public async Task<PostalAddress> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var row = await context.Addresses
            .FirstOrDefaultAsync(entry => entry.SubjectId == subject.Value, cancellationToken);

        return row is null
            ? PostalAddress.Empty(subject)
            : PostalAddress.Restore(
                subject,
                row.Line1,
                row.Line2,
                row.PostalCode,
                row.City,
                row.Country,
                row.Phone);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        PostalAddress address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        var now = clock.GetUtcNow().UtcDateTime;
        var row = await context.Addresses
            .AsTracking()
            .FirstOrDefaultAsync(
                entry => entry.SubjectId == address.Subject.Value, cancellationToken);

        if (row is null)
        {
            row = new PostalAddressRow
            {
                SubjectId = address.Subject.Value,
                CreatedAt = now
            };
            context.Addresses.Add(row);
        }

        row.Line1 = address.Line1;
        row.Line2 = address.Line2;
        row.PostalCode = address.PostalCode;
        row.City = address.City;
        row.Country = address.Country;
        row.Phone = address.Phone;
        row.UpdatedAt = now;
    }
}
