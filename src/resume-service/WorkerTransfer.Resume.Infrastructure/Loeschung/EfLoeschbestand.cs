using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Ablage;
using WorkerTransfer.Outbox;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Infrastructure.Persistence;

namespace WorkerTransfer.Resume.Infrastructure.Erasure;

/// <summary>What an erasure really touches in this service.</summary>
/// <remarks>
/// One pairing carries the whole design: a row <em>about</em> the person falls,
/// a row where the person merely <em>acted</em> keeps standing without their
/// name. The same distinction holds
/// for the requests and for the trail, and it has to, or a recruiter deleting
/// their private account would erase proceedings that belong to their employer
/// and concern somebody else entirely.
/// <para>
/// No retention switch anywhere. Nothing here was ever claimed to be under a
/// retention obligation, and a switch "just in case" is the precautionary
/// assumption ADR-0027 §3 abolishes.
/// </para>
/// </remarks>
public sealed class EfErasureLedger(ResumeDbContext context, IDocumentStore ablage) : IErasureLedger
{
    /// <inheritdoc />
    public async Task<int> DeleteAsync(
        SubjectId subject,
        CancellationToken cancellationToken = default)
    {
        // The résumé. `resumes.id` IS the subject id, and positions and
        // education lie as jsonb in the same row, so they fall with it.
        await context.Lebenslaeufe
            .Where(row => row.Id == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // DIE UNTERLAGEN — Zeilen UND Dateien.
        //
        // Zuerst die Schluessel lesen, dann die Zeilen loeschen, dann die
        // Dateien: nach `ExecuteDelete` weiss niemand mehr, wo die Bytes lagen,
        // und sie laegen fuer immer da. Die Oberflaeche saehe leer aus, die
        // Platte nicht — genau die gebrochene Zusage, die ADR-0027 ausschliesst.
        //
        // Die Dateien zuletzt, weil ein Abbruch dazwischen eine Datei ohne
        // Zeile hinterlaesst: unerreichbar und aufraeumbar. Andersherum bliebe
        // eine Zeile ohne Datei stehen und sähe aus wie eine DocumentRecord.
        var key = await context.Unterlagen
            .Where(row => row.SubjectId == subject.Value)
            .Select(row => row.StorageKey)
            .ToListAsync(cancellationToken);

        await context.Unterlagen
            .Where(row => row.SubjectId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // DER INDEX FAELLT MIT (ADR-0043). Er haelt, was auf Ausloesung in den
        // Unterlagen gelesen wurde — kanonische Namen aus einem Reference, also
        // personenbezogen. Ein Index ueber die Texte eines Menschen darf
        // ueberhaupt nur bestehen, solange er im selben Dienst liegt wie die
        // Dateien UND von derselben Erasure getroffen wird; ohne diese Zeile
        // waere die Bedingung gebrochen, unter der er gebaut werden durfte.
        // Gemessen wird das an der Reihe (`ErkennungsreiseTests`), nicht hier.
        await context.Funde
            .Where(row => row.SubjectId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var entry in key)
        {
            await ablage.DeleteAsync(entry, cancellationToken);
        }

        // Asked ABOUT them: the row is the statement "company X asked about
        // this human being".
        await context.Anfragen
            .Where(row => row.SubjectId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // Asked BY them: the proceeding belongs to the company and concerns a
        // third person. Only the name falls away.
        await context.Anfragen
            .Where(row => row.RequestedBy == subject.Value)
            .ExecuteUpdateAsync(
                setzen => setzen.SetProperty(row => row.RequestedBy, (Guid?)null),
                cancellationToken);

        await context.Pruefspur
            .Where(row => row.TargetId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        await context.Pruefspur
            .Where(row => row.ActorId == subject.Value)
            .ExecuteUpdateAsync(
                setzen => setzen.SetProperty(row => row.ActorId, (Guid?)null),
                cancellationToken);

        // An outstanding notification to an account that no longer exists.
        await context.Set<OutboxRow>()
            .Where(row => row.UserId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // Nothing stays. The receipt says so rather than leaving the origin to
        // guess (ADR-0027 §3).
        return 0;
    }
}
