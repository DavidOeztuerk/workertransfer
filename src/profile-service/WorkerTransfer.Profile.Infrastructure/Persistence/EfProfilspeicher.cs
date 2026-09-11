using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Profile.Domain.Faehigkeiten;
using WorkerTransfer.Skills;
using WorkerTransfer.Profile.Domain.Profile;

namespace WorkerTransfer.Profile.Infrastructure.Persistence;

/// <summary>Die Profile in Postgres.</summary>
public sealed class EfProfilspeicher(ProfileDbContext context) : IProfilspeicher
{
    /// <inheritdoc />
    public async Task<Profil?> HoleAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var zeile = await context.Profile
            .FirstOrDefaultAsync(spalte => spalte.Id == wer.Value, cancellationToken);

        return zeile is null ? null : ZurDomaene(zeile);
    }

    /// <inheritdoc />
    public async Task SpeichereAsync(Profil profil, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profil);

        var zeile = await context.Profile
            .AsTracking()
            .FirstOrDefaultAsync(spalte => spalte.Id == profil.Wer.Value, cancellationToken);

        if (zeile is null)
        {
            context.Profile.Add(new ProfilZeile
            {
                Id = profil.Wer.Value,
                Ueberschrift = profil.Ueberschrift,
                Text = profil.Text,
                Ort = profil.Ort,
                RemoteMoeglich = profil.RemoteMoeglich,
                Faehigkeiten = [.. profil.Faehigkeiten.Werte],
                AngelegtAm = profil.AngelegtAm,
                GeaendertAm = profil.GeaendertAm
            });

            return;
        }

        zeile.Ueberschrift = profil.Ueberschrift;
        zeile.Text = profil.Text;
        zeile.Ort = profil.Ort;
        zeile.RemoteMoeglich = profil.RemoteMoeglich;
        zeile.Faehigkeiten = [.. profil.Faehigkeiten.Werte];
        zeile.GeaendertAm = profil.GeaendertAm;
    }

    /// <inheritdoc />
    public async Task<Profilseite> SeiteAsync(
        Seitenanfrage anfrage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(anfrage);

        var abfrage = context.Profile
            .OrderByDescending(spalte => spalte.GeaendertAm)
            .ThenByDescending(spalte => spalte.Id)
            .AsQueryable();

        // Groß-/Kleinschreibung egal, weil die Fähigkeitenliste beim Speichern
        // schon so entdoppelt: „Python“ und „python“ sind dort dieselbe
        // Fähigkeit, und eine Suche, die sie unterschiede, widerspräche der
        // eigenen Datenhaltung.
        //
        // Verglichen wird zur Abfragezeit statt über eine gespiegelte
        // kleingeschriebene Spalte: die wäre eine zweite Kopie derselben Daten.
        // Wird es eng, ist ein GIN-Index über genau diesen Ausdruck die
        // Antwort — keine zweite Spalte.
        var gesuchte = (anfrage.Faehigkeiten ?? [])
            .Select(faehigkeit => Wortschatz.Kanonisch(faehigkeit).ToUpperInvariant())
            .ToArray();

        if (anfrage.Irgendeine)
        {
            // ODER: mindestens eines der Worte genügt. Ein Treffer darf also
            // etwas NICHT nennen — und genau das macht die Häkchenliste des
            // scout-service erst zu einer Auskunft (ADR-0036).
            if (gesuchte.Length > 0)
            {
                abfrage = abfrage.Where(spalte =>
                    spalte.Faehigkeiten.Any(eintrag => gesuchte.Contains(eintrag.ToUpper())));
            }
        }
        else
        {
            // UND: alles muss genannt sein. Die Bedingung von `/candidates`,
            // unverändert.
            foreach (var gesucht in gesuchte)
            {
                abfrage = abfrage.Where(spalte =>
                    spalte.Faehigkeiten.Any(eintrag => eintrag.ToUpper() == gesucht));
            }
        }

        if (anfrage.Ort.Length > 0)
        {
            var muster = $"%{anfrage.Ort.Trim()}%";
            abfrage = abfrage.Where(spalte => EF.Functions.ILike(spalte.Ort, muster));
        }

        if (anfrage.NurRemote)
        {
            // Nur in eine Richtung: `remote_ok = false` heißt „nicht ja
            // gesagt“, nicht „lehne ab“. Ein Filter darauf schlösse Menschen
            // aus, die schlicht nichts angekreuzt haben.
            abfrage = abfrage.Where(spalte => spalte.RemoteMoeglich);
        }

        if (anfrage.Ab is { } ab)
        {
            abfrage = abfrage.Where(spalte =>
                spalte.GeaendertAm < ab.GeaendertAm
                || (spalte.GeaendertAm == ab.GeaendertAm && spalte.Id < ab.Wer.Value));
        }

        // Eine Zeile mehr holen, als gezeigt wird: so steht ohne zweite Abfrage
        // fest, ob es weitergeht — und ohne ein COUNT, das die Gesamtzahl
        // nennte, die hier niemand nennen darf.
        var zeilen = await abfrage.Take(anfrage.Anzahl + 1).ToListAsync(cancellationToken);
        var weiter = zeilen.Count > anfrage.Anzahl;

        if (weiter)
        {
            zeilen.RemoveAt(zeilen.Count - 1);
        }

        var letzte = zeilen.Count > 0 ? zeilen[^1] : null;

        return new Profilseite(
            [.. zeilen.Select(ZurDomaene)],
            weiter && letzte is not null
                ? new Seitenzeiger(letzte.GeaendertAm, new SubjectId(letzte.Id))
                : null);
    }

    /// <inheritdoc />
    public async Task<int> LoescheAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        // Ohne SaveChanges: der Löschbefehl läuft in der Transaktionsklammer,
        // und die Prüfspur muss mit ihm abschließen oder gar nicht.
        var zeile = await context.Profile
            .AsTracking()
            .FirstOrDefaultAsync(spalte => spalte.Id == wer.Value, cancellationToken);

        if (zeile is not null)
        {
            context.Profile.Remove(zeile);
        }

        // Immer 0. Dieser Dienst kennt keinen Aufbewahrungsfall: es gibt hier
        // nichts, was einem anderen gehört.
        return 0;
    }

    /// <summary>Baut das Aggregat aus der Zeile.</summary>
    /// <remarks>
    /// Über <see cref="Profil.Stelle_her"/> und damit ohne erneute Prüfung: eine
    /// gespeicherte Zeile war bei ihrer Entstehung gültig, und sie beim Lesen
    /// abzulehnen hieße, jemandem sein Profil zu entziehen, weil sich eine
    /// Obergrenze geändert hat.
    /// <para>
    /// Die Fähigkeiten laufen trotzdem durch <see cref="Faehigkeitenliste"/>:
    /// das ist der einzige Eingang, der die Liste baut, und ein neuer Eintrag im
    /// Wortschatz wirkt dann auch auf Zeilen, die vor ihm geschrieben wurden —
    /// ohne Datenwanderung.
    /// </para>
    /// </remarks>
    private static Profil ZurDomaene(ProfilZeile zeile) =>
        Profil.Stelle_her(
            new SubjectId(zeile.Id),
            zeile.Ueberschrift,
            zeile.Text,
            zeile.Ort,
            zeile.RemoteMoeglich,
            Faehigkeitenliste.Stelle_her(zeile.Faehigkeiten),
            zeile.AngelegtAm,
            zeile.GeaendertAm);
}
