using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Infrastructure.Persistence;

/// <summary>Die Bewerbungsanschrift in ihrer Zeile.</summary>
/// <remarks>
/// Wer nie etwas gesetzt hat, hat keine Zeile und bekommt leer. Für jedes
/// Konto eine Zeile zu schreiben hiesse, eine PostalAddress zu behaupten, die
/// niemand hinterlegt hat.
/// </remarks>
public sealed class EfPostalAddresses(IdentityDbContext context, TimeProvider uhr) : IPostalAddresses
{
    /// <inheritdoc />
    public async Task<PostalAddress> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var row = await context.Addresses
            .FirstOrDefaultAsync(entry => entry.SubjectId == subject.Value, cancellationToken);

        return row is null
            ? PostalAddress.Leer(subject)
            : PostalAddress.Wiederherstellen(
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
        PostalAddress anschrift, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(anschrift);

        var now = uhr.GetUtcNow().UtcDateTime;
        var row = await context.Addresses
            .AsTracking()
            .FirstOrDefaultAsync(
                entry => entry.SubjectId == anschrift.Wer.Value, cancellationToken);

        if (row is null)
        {
            row = new PostalAddressRow
            {
                SubjectId = anschrift.Wer.Value,
                CreatedAt = now
            };
            context.Addresses.Add(row);
        }

        row.Line1 = anschrift.Zeile1;
        row.Line2 = anschrift.Zeile2;
        row.PostalCode = anschrift.Postleitzahl;
        row.City = anschrift.Ort;
        row.Country = anschrift.Land;
        row.Phone = anschrift.Telefon;
        row.UpdatedAt = now;
    }
}
