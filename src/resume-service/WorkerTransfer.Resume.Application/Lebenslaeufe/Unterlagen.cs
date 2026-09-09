using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Ablage;
using WorkerTransfer.Resume.Application.Nachrichten;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Lebenslaeufe;

namespace WorkerTransfer.Resume.Application.Lebenslaeufe;

/// <summary>Wie das Annehmen einer Unterlage ausgegangen ist.</summary>
public abstract record Unterlagenergebnis
{
    private Unterlagenergebnis()
    {
    }

    /// <summary>Angenommen.</summary>
    public sealed record Erledigt(Unterlage Unterlage) : Unterlagenergebnis;

    /// <summary>Mit der Datei stimmt etwas nicht.</summary>
    public sealed record Abgelehnt(string Grund) : Unterlagenergebnis;

    /// <summary>Es sind schon genug.</summary>
    public sealed record ZuViele : Unterlagenergebnis;
}

/// <summary>Eine Unterlage beilegen.</summary>
/// <param name="Inhalt">Die Bytes. Was sie sind, entscheidet ihre Signatur.</param>
public sealed record UnterlageHinzufuegenBefehl(
    SubjectId Wer,
    string? Name,
    Unterlagenart Art,
    ReadOnlyMemory<byte> Inhalt) : IBefehl<Unterlagenergebnis>;

/// <summary>Die eigenen Unterlagen.</summary>
public sealed record MeineUnterlagenAbfrage(SubjectId Wer) : IAbfrage<IReadOnlyList<Unterlage>>;

/// <summary>Der Inhalt einer Unterlage, samt ihrer Zeile.</summary>
public sealed record UnterlageInhaltAbfrage(SubjectId Wer, Guid Id)
    : IAbfrage<(Unterlage Unterlage, byte[] Inhalt)?>;

/// <summary>Eine Unterlage wieder wegnehmen.</summary>
public sealed record UnterlageLoeschenBefehl(SubjectId Wer, Guid Id) : IBefehl<bool>;

/// <summary>Diese Datei ist der Lebenslauf — die vorige Lebenslauf-Datei nicht mehr.</summary>
public sealed record UnterlageAlsLebenslaufBefehl(SubjectId Wer, Guid Id) : IBefehl<bool>;

/// <summary>Die Vorlage des eigenen Lebenslaufs wählen.</summary>
public sealed record VorlageWaehlenBefehl(SubjectId Wer, Vorlage Vorlage) : IBefehl<bool>;

/// <summary>Führt die Unterlagen.</summary>
public sealed class Unterlagenbefehle(
    IUnterlagenSpeicher speicher,
    ILebenslaufSpeicher lebenslaeufe,
    IAblage ablage,
    TimeProvider uhr) :
    IRequestHandler<UnterlageHinzufuegenBefehl, Unterlagenergebnis>,
    IRequestHandler<MeineUnterlagenAbfrage, IReadOnlyList<Unterlage>>,
    IRequestHandler<UnterlageInhaltAbfrage, (Unterlage Unterlage, byte[] Inhalt)?>,
    IRequestHandler<UnterlageLoeschenBefehl, bool>,
    IRequestHandler<UnterlageAlsLebenslaufBefehl, bool>,
    IRequestHandler<VorlageWaehlenBefehl, bool>
{
    /// <inheritdoc />
    public async Task<Unterlagenergebnis> Handle(
        UnterlageHinzufuegenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // DER TYP KOMMT AUS DEN BYTES. Ein `Content-Type` und eine Endung sind
        // beide frei waehlbar; wer ihnen glaubt, nimmt eine ausfuehrbare Datei
        // entgegen, weil jemand sie `zeugnis.png` genannt hat.
        var typ = Typerkennung.Erkenne(request.Inhalt.Span);

        if (typ is null)
        {
            return new Unterlagenergebnis.Abgelehnt(
                "only PNG, JPEG and PDF are accepted");
        }

        var vorhandene = await speicher.AlleAsync(request.Wer, cancellationToken);

        if (request.Art == Unterlagenart.Lebenslauf)
        {
            foreach (var alt in vorhandene.Where(u => u.Art == Unterlagenart.Lebenslauf))
            {
                await speicher.LoescheAsync(request.Wer, alt.Id, cancellationToken);
                await ablage.LoescheAsync(alt.Ablageschluessel, cancellationToken);
            }

            vorhandene = await speicher.AlleAsync(request.Wer, cancellationToken);
        }

        if (vorhandene.Count >= Unterlage.HoechsteAnzahl)
        {
            return new Unterlagenergebnis.ZuViele();
        }

        Unterlage unterlage;

        try
        {
            unterlage = Unterlage.Nimm_an(
                request.Wer, request.Name, request.Art, typ,
                request.Inhalt.Length, uhr.GetUtcNow());
        }
        catch (Unterlagenfehler fehler)
        {
            return new Unterlagenergebnis.Abgelehnt(fehler.Message);
        }

        // ERST DIE BYTES, DANN DIE ZEILE. Andersherum zeigte die Liste einen
        // Eintrag, dessen Inhalt es nie gab — und ein Klick darauf liefe ins
        // Leere, ohne dass jemand sagen koennte, warum. Eine Datei ohne Zeile
        // ist dagegen nur Platz, den ein Aufraeumen findet.
        await ablage.LegeAbAsync(unterlage.Ablageschluessel, request.Inhalt, cancellationToken);
        await speicher.SichereAsync(unterlage, cancellationToken);

        return new Unterlagenergebnis.Erledigt(unterlage);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Unterlage>> Handle(
        MeineUnterlagenAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return speicher.AlleAsync(request.Wer, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(Unterlage Unterlage, byte[] Inhalt)?> Handle(
        UnterlageInhaltAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var unterlage = await speicher.HoleAsync(request.Wer, request.Id, cancellationToken);

        if (unterlage is null)
        {
            return null;
        }

        var inhalt = await ablage.HoleAsync(unterlage.Ablageschluessel, cancellationToken);

        // Zeile ohne Datei: von aussen dasselbe wie „gibt es nicht". Etwas
        // anderes zu antworten hiesse, ueber einen eigenen Fehler zu reden,
        // wo der Aufrufer nichts davon hat.
        return inhalt is null ? null : (unterlage, inhalt);
    }

    /// <inheritdoc />
    public async Task<bool> Handle(
        UnterlageLoeschenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var unterlage = await speicher.HoleAsync(request.Wer, request.Id, cancellationToken);

        if (unterlage is null)
        {
            return false;
        }

        // ERST DIE ZEILE, DANN DIE BYTES — genau andersherum als beim Anlegen.
        // Bricht es dazwischen ab, bleibt eine Datei ohne Zeile: unerreichbar
        // und aufraeumbar. Andersherum bliebe eine Zeile ohne Datei stehen und
        // sähe fuer die Person aus wie eine Unterlage, die es noch gibt.
        await speicher.LoescheAsync(request.Wer, request.Id, cancellationToken);
        await ablage.LoescheAsync(unterlage.Ablageschluessel, cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> Handle(
        UnterlageAlsLebenslaufBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var alle = await speicher.AlleAsync(request.Wer, cancellationToken);
        var treffer = alle.FirstOrDefault(u => u.Id == request.Id);

        if (treffer is null)
        {
            return false;
        }

        foreach (var unterlage in alle)
        {
            if (unterlage.Id == request.Id)
            {
                unterlage.Ordne_zu(Unterlagenart.Lebenslauf);
            }
            else if (unterlage.Art == Unterlagenart.Lebenslauf)
            {
                unterlage.Ordne_zu(Unterlagenart.Sonstiges);
            }
            else
            {
                continue;
            }

            await speicher.SichereAsync(unterlage, cancellationToken);
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> Handle(
        VorlageWaehlenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lebenslauf = await lebenslaeufe.HoleAsync(request.Wer, cancellationToken);

        if (lebenslauf is null)
        {
            return false;
        }

        lebenslauf.Waehle_vorlage(request.Vorlage, uhr.GetUtcNow());
        await lebenslaeufe.SichereAsync(lebenslauf, cancellationToken);

        return true;
    }
}

/// <summary>Die Unterlagen einer anderen Person — wenn sie freigegeben sind.</summary>
/// <remarks>
/// <strong>Der Blick des Unternehmens auf die Bewerbungsmappe.</strong> Er
/// hängt am Ledger und an nichts sonst: kein Feld an der Unterlage sagt, wer
/// sie sehen darf, und keine Bewerbungstabelle wird hier befragt (ADR-0020).
/// Ein Widerruf wirkt auf den nächsten Aufruf (ADR-0013).
/// </remarks>
public sealed record SichtbareUnterlagenAbfrage(SubjectId Wer, TenantId Firma)
    : IAbfrage<IReadOnlyList<Unterlage>>;

/// <summary>Der Inhalt einer freigegebenen Unterlage.</summary>
public sealed record SichtbarerUnterlagenInhaltAbfrage(SubjectId Wer, TenantId Firma, Guid Id)
    : IAbfrage<(Unterlage Unterlage, byte[] Inhalt)?>;

/// <summary>Beantwortet die Fragen des Unternehmens.</summary>
public sealed class Unterlagenabfragen(
    IUnterlagenSpeicher speicher,
    IAblage ablage,
    IEinwilligungstor tor) :
    IRequestHandler<SichtbareUnterlagenAbfrage, IReadOnlyList<Unterlage>>,
    IRequestHandler<SichtbarerUnterlagenInhaltAbfrage, (Unterlage Unterlage, byte[] Inhalt)?>
{
    /// <inheritdoc />
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    public async Task<IReadOnlyList<Unterlage>> Handle(
        SichtbareUnterlagenAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ERST FRAGEN, DANN LESEN. Andersherum stünden die Daten schon im
        // Speicher, wenn der Ledger nein sagt — und der nächste, der hier eine
        // Zeile einfügt, hätte sie in der Hand.
        var darf = await tor.DarfUnterlagenSehenAsync(
            request.Wer, request.Firma, cancellationToken);

        return darf
            ? await speicher.AlleAsync(request.Wer, cancellationToken)
            : [];
    }

    /// <inheritdoc />
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    public async Task<(Unterlage Unterlage, byte[] Inhalt)?> Handle(
        SichtbarerUnterlagenInhaltAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var darf = await tor.DarfUnterlagenSehenAsync(
            request.Wer, request.Firma, cancellationToken);

        if (!darf)
        {
            return null;
        }

        var unterlage = await speicher.HoleAsync(request.Wer, request.Id, cancellationToken);

        if (unterlage is null)
        {
            return null;
        }

        var inhalt = await ablage.HoleAsync(unterlage.Ablageschluessel, cancellationToken);

        return inhalt is null ? null : (unterlage, inhalt);
    }
}
