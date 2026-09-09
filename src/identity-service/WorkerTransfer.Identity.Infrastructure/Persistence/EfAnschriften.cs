using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Infrastructure.Persistence;

/// <summary>Die Bewerbungsanschrift in ihrer Zeile.</summary>
/// <remarks>
/// Wer nie etwas gesetzt hat, hat keine Zeile und bekommt leer. Für jedes
/// Konto eine Zeile zu schreiben hiesse, eine Anschrift zu behaupten, die
/// niemand hinterlegt hat.
/// </remarks>
public sealed class EfAnschriften(IdentityDbContext kontext, TimeProvider uhr) : IAnschriften
{
    /// <inheritdoc />
    public async Task<Anschrift> HoleAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Addresses
            .FirstOrDefaultAsync(eintrag => eintrag.SubjectId == wer.Value, cancellationToken);

        return zeile is null
            ? Anschrift.Leer(wer)
            : Anschrift.Wiederherstellen(
                wer,
                zeile.Line1,
                zeile.Line2,
                zeile.PostalCode,
                zeile.City,
                zeile.Country,
                zeile.Phone);
    }

    /// <inheritdoc />
    public async Task SichereAsync(
        Anschrift anschrift, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(anschrift);

        var jetzt = uhr.GetUtcNow().UtcDateTime;
        var zeile = await kontext.Addresses
            .AsTracking()
            .FirstOrDefaultAsync(
                eintrag => eintrag.SubjectId == anschrift.Wer.Value, cancellationToken);

        if (zeile is null)
        {
            zeile = new AnschriftRow
            {
                SubjectId = anschrift.Wer.Value,
                CreatedAt = jetzt
            };
            kontext.Addresses.Add(zeile);
        }

        zeile.Line1 = anschrift.Zeile1;
        zeile.Line2 = anschrift.Zeile2;
        zeile.PostalCode = anschrift.Postleitzahl;
        zeile.City = anschrift.Ort;
        zeile.Country = anschrift.Land;
        zeile.Phone = anschrift.Telefon;
        zeile.UpdatedAt = jetzt;
    }
}
