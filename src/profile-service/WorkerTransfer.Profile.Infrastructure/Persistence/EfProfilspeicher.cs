using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Profile.Domain.Faehigkeiten;
using WorkerTransfer.Skills;
using WorkerTransfer.Profile.Domain.Profile;

namespace WorkerTransfer.Profile.Infrastructure.Persistence;

/// <summary>Die PersonProfile in Postgres.</summary>
public sealed class EfProfileStore(ProfileDbContext context) : IProfileStore
{
    /// <inheritdoc />
    public async Task<PersonProfile?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var row = await context.PersonProfile
            .FirstOrDefaultAsync(column => column.Id == subject.Value, cancellationToken);

        return row is null ? null : ZurDomaene(row);
    }

    /// <inheritdoc />
    public async Task StoreAsync(PersonProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var row = await context.PersonProfile
            .AsTracking()
            .FirstOrDefaultAsync(column => column.Id == profile.Wer.Value, cancellationToken);

        if (row is null)
        {
            context.PersonProfile.Add(new ProfileRow
            {
                Id = profile.Wer.Value,
                Ueberschrift = profile.Ueberschrift,
                Text = profile.Text,
                Ort = profile.Ort,
                RemoteMoeglich = profile.RemoteMoeglich,
                Faehigkeiten = [.. profile.Faehigkeiten.Werte],
                Pendelbereitschaft = profile.Pendelbereitschaft,
                Umzugsbereitschaft = profile.Umzugsbereitschaft,
                AngelegtAm = profile.AngelegtAm,
                GeaendertAm = profile.GeaendertAm
            });

            return;
        }

        row.Ueberschrift = profile.Ueberschrift;
        row.Text = profile.Text;
        row.Ort = profile.Ort;
        row.RemoteMoeglich = profile.RemoteMoeglich;
        row.Faehigkeiten = [.. profile.Faehigkeiten.Werte];
        row.Pendelbereitschaft = profile.Pendelbereitschaft;
        row.Umzugsbereitschaft = profile.Umzugsbereitschaft;
        row.GeaendertAm = profile.GeaendertAm;
    }

    /// <inheritdoc />
    public async Task<ProfilePage> PageAsync(
        PageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var abfrage = context.PersonProfile
            .OrderByDescending(column => column.GeaendertAm)
            .ThenByDescending(column => column.Id)
            .AsQueryable();

        // Groß-/Kleinschreibung egal, weil die Fähigkeitenliste beim Speichern
        // schon so entdoppelt: „Python“ und „python“ sind dort dieselbe
        // Fähigkeit, und eine Search, die sie unterschiede, widerspräche der
        // eigenen Datenhaltung.
        //
        // Verglichen wird zur Abfragezeit statt über eine gespiegelte
        // kleingeschriebene Spalte: die wäre eine zweite Kopie derselben Daten.
        // Wird es eng, ist ein GIN-Index über genau diesen Ausdruck die
        // Antwort — keine zweite Spalte.
        var gesuchte = (request.Faehigkeiten ?? [])
            .Select(skill => Vocabulary.Canonicalise(skill).ToUpperInvariant())
            .ToArray();

        // ODER: mindestens eines der Worte genügt. Ein Hit darf also etwas
        // NICHT nennen — und genau das macht die Häkchenliste des scout-service
        // erst zu einer Auskunft (ADR-0036). Das UND von `/candidates` ist mit
        // dieser Route gefallen.
        if (gesuchte.Length > 0)
        {
            abfrage = abfrage.Where(column =>
                column.Faehigkeiten.Any(entry => gesuchte.Contains(entry.ToUpper())));
        }

        if (request.Ort.Length > 0)
        {
            var pattern = $"%{request.Ort.Trim()}%";
            abfrage = abfrage.Where(column => EF.Functions.ILike(column.Ort, pattern));
        }

        if (request.NurRemote)
        {
            // Nur in eine Richtung: `remote_ok = false` heißt „nicht ja
            // gesagt“, nicht „lehne ab“. Ein Filter darauf schlösse Menschen
            // aus, die schlicht nichts angekreuzt haben.
            abfrage = abfrage.Where(column => column.RemoteMoeglich);
        }

        if (request.Ab is { } ab)
        {
            abfrage = abfrage.Where(column =>
                column.GeaendertAm < ab.GeaendertAm
                || (column.GeaendertAm == ab.GeaendertAm && column.Id < ab.Wer.Value));
        }

        // Eine Zeile mehr holen, als gezeigt wird: so steht ohne zweite Abfrage
        // fest, ob es weitergeht — und ohne ein COUNT, das die Gesamtzahl
        // nennte, die hier niemand nennen darf.
        var rows = await abfrage.Take(request.Seitenlaenge + 1).ToListAsync(cancellationToken);
        var weiter = rows.Count > request.Seitenlaenge;

        if (weiter)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var letzte = rows.Count > 0 ? rows[^1] : null;

        return new ProfilePage(
            [.. rows.Select(ZurDomaene)],
            weiter && letzte is not null
                ? new PageCursor(letzte.GeaendertAm, new SubjectId(letzte.Id))
                : null);
    }

    /// <inheritdoc />
    public async Task<int> DeleteAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        // Ohne SaveChanges: der Löschbefehl läuft in der Transaktionsklammer,
        // und die Prüfspur muss mit ihm abschließen oder gar nicht.
        var row = await context.PersonProfile
            .AsTracking()
            .FirstOrDefaultAsync(column => column.Id == subject.Value, cancellationToken);

        if (row is not null)
        {
            context.PersonProfile.Remove(row);
        }

        // Immer 0. Dieser Dienst kennt keinen Aufbewahrungsfall: es gibt hier
        // nichts, was einem anderen gehört.
        return 0;
    }

    /// <summary>Baut das Aggregat aus der Zeile.</summary>
    /// <remarks>
    /// Über <see cref="Profil.Stelle_her"/> und damit ohne erneute Prüfung: eine
    /// gespeicherte Zeile war bei ihrer Entstehung gültig, und sie beim Lesen
    /// abzulehnen hieße, jemandem sein PersonProfile zu entziehen, weil sich eine
    /// Obergrenze geändert hat.
    /// <para>
    /// Die Fähigkeiten laufen trotzdem durch <see cref="Faehigkeitenliste"/>:
    /// das ist der einzige Eingang, der die Liste baut, und ein neuer Eintrag im
    /// Vocabulary wirkt dann auch auf Zeilen, die vor ihm geschrieben wurden —
    /// ohne Datenwanderung.
    /// </para>
    /// </remarks>
    private static PersonProfile ZurDomaene(ProfileRow row) =>
        PersonProfile.Stelle_her(
            new SubjectId(row.Id),
            row.Ueberschrift,
            row.Text,
            row.Ort,
            row.RemoteMoeglich,
            SkillList.Stelle_her(row.Faehigkeiten),
            row.AngelegtAm,
            row.GeaendertAm,
            row.Pendelbereitschaft,
            row.Umzugsbereitschaft);
}
