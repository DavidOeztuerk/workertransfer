using System.Text.Json;
using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Notification.Domain.Benachrichtigungen;

namespace WorkerTransfer.Notification.Infrastructure.Persistence;

/// <summary>Liest und schreibt <c>notification_preferences</c>.</summary>
public sealed class EfWunschspeicher(NotificationDbContext kontext) : IWunschspeicher
{
    /// <inheritdoc />
    public async Task<Benachrichtigungswunsch?> HoleAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Wuensche
            .FirstOrDefaultAsync(kandidat => kandidat.Id == wer.Value, cancellationToken);

        return zeile is null ? null : ZumAggregat(zeile);
    }

    /// <inheritdoc />
    public async Task SichereAsync(
        Benachrichtigungswunsch wunsch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wunsch);

        var vorhanden = await kontext.Wuensche
            .AsTracking()
            .FirstOrDefaultAsync(kandidat => kandidat.Id == wunsch.Wer.Value, cancellationToken);

        if (vorhanden is null)
        {
            kontext.Wuensche.Add(new WunschZeile
            {
                Id = wunsch.Wer.Value,
                Kinds = Schreibe(wunsch.Schalter),
                LastSentAt = wunsch.ZuletztGesendetAm?.UtcDateTime
            });

            return;
        }

        vorhanden.Kinds = Schreibe(wunsch.Schalter);
        vorhanden.LastSentAt = wunsch.ZuletztGesendetAm?.UtcDateTime;
    }

    private static string Schreibe(IReadOnlyDictionary<Benachrichtigungsart, bool> schalter) =>
        JsonSerializer.Serialize(
            schalter.ToDictionary(paar => Benachrichtigungsarten.Wort(paar.Key), paar => paar.Value));

    private static Benachrichtigungswunsch ZumAggregat(WunschZeile zeile)
    {
        var gelesen = JsonSerializer.Deserialize<Dictionary<string, bool>>(zeile.Kinds) ?? [];
        var schalter = new Dictionary<Benachrichtigungsart, bool>();

        foreach (var (wort, gewollt) in gelesen)
        {
            // Ein Wort, das keine Art ist, wird übergangen statt geworfen: eine
            // gestrichene Art soll kein Postfach unlesbar machen.
            if (Benachrichtigungsarten.Lies(wort) is { } art)
            {
                schalter[art] = gewollt;
            }
        }

        return Benachrichtigungswunsch.Stelle_her(
            new SubjectId(zeile.Id),
            schalter,
            zeile.LastSentAt is { } zuletzt ? new DateTimeOffset(zuletzt, TimeSpan.Zero) : null);
    }
}

/// <summary>Liest und schreibt <c>notifications</c>.</summary>
public sealed class EfEingangsspeicher(NotificationDbContext kontext) : IEingangsspeicher
{
    /// <inheritdoc />
    public Task FuegeHinzuAsync(Eingang eingang, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eingang);

        kontext.Eingaenge.Add(new EingangsZeile
        {
            Id = eingang.Id,
            UserId = eingang.Wer.Value,
            Kind = Benachrichtigungsarten.Wort(eingang.Art),
            CreatedAt = eingang.AngelegtAm.UtcDateTime,
            ReadAt = eingang.GelesenAm?.UtcDateTime
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Eingang>> FuerPersonAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var zeilen = await kontext.Eingaenge
            .Where(kandidat => kandidat.UserId == wer.Value)
            .OrderByDescending(kandidat => kandidat.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. zeilen.Select(ZumAggregat)];
    }

    /// <inheritdoc />
    public Task<int> LiesAllesAsync(
        SubjectId wer, DateTimeOffset jetzt, CancellationToken cancellationToken = default) =>
        kontext.Eingaenge
            .Where(zeile => zeile.UserId == wer.Value && zeile.ReadAt == null)
            .ExecuteUpdateAsync(
                setzen => setzen.SetProperty(
                    zeile => zeile.ReadAt, (DateTime?)jetzt.UtcDateTime),
                cancellationToken);

    /// <inheritdoc />
    public Task<bool> GabEsSeitAsync(
        SubjectId wer,
        Benachrichtigungsart art,
        DateTimeOffset seit,
        CancellationToken cancellationToken = default)
    {
        var wort = Benachrichtigungsarten.Wort(art);
        var ab = seit.UtcDateTime;

        // Genau die Spalten, auf denen der Index liegt (user_id, created_at) —
        // plus die Art, die auf dieser kurzen Menge nichts mehr kostet.
        //
        // ECHT GROESSER, nicht „groesser gleich": genau einen Tag spaeter geht
        // wieder etwas hinaus. Dieselbe Grenze wie bei der stuendlichen Drossel
        // (`jetzt - zuletzt >= Drossel`) — zwei Kappen, die an der Grenze
        // verschieden entscheiden, sind zwei Regeln, die aussehen wie eine.
        return kontext.Eingaenge.AnyAsync(
            zeile => zeile.UserId == wer.Value
                     && zeile.Kind == wort
                     && zeile.CreatedAt > ab,
            cancellationToken);
    }

    private static Eingang ZumAggregat(EingangsZeile zeile) =>
        Eingang.Stelle_her(
            zeile.Id,
            new SubjectId(zeile.UserId),
            Benachrichtigungsarten.Lies(zeile.Kind)
            ?? throw new InvalidOperationException(
                $"Unbekannte Benachrichtigungsart in der Datenbank: {zeile.Kind}"),
            new DateTimeOffset(zeile.CreatedAt, TimeSpan.Zero),
            zeile.ReadAt is { } gelesen ? new DateTimeOffset(gelesen, TimeSpan.Zero) : null);
}
