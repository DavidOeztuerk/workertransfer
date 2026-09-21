using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Nachrichten;
using WorkerTransfer.Outbox;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Applications;

namespace WorkerTransfer.Applications.Application.Entwuerfe;

/// <summary>Wie ein Schritt am Entwurf ausgegangen ist.</summary>
public abstract record DraftResult
{
    private DraftResult()
    {
    }

    /// <summary>Hat geklappt.</summary>
    public sealed record Erledigt(ApplicationDraft Entwurf) : DraftResult;

    /// <summary>Gibt es nicht — oder gehört jemand anderem. Von außen dasselbe.</summary>
    public sealed record None : DraftResult;

    /// <summary>Von hier aus geht dieser Schritt nicht.</summary>
    public sealed record StateConflict(string Grund) : DraftResult;

    /// <summary>Mit der Eingabe stimmt etwas nicht.</summary>
    public sealed record Eingabe(string Grund) : DraftResult;

    /// <summary>Es ist kein Anbieter eingerichtet — und das sagt die Oberfläche.</summary>
    public sealed record NoProvider(string Grund) : DraftResult;
}

/// <summary>Für mehrere Jobs auf einmal einen Entwurf beginnen.</summary>
/// <remarks>
/// <strong>Die Massenhandlung liegt hier und nirgends sonst.</strong> Am
/// Ausgang steht sie nicht: gesendet wird je Stück, nach je einer Release
/// (ADR-0034). Hier richtet sie nichts an — es entstehen Entwürfe, die
/// niemand gesehen hat und die niemanden erreichen.
/// </remarks>
public sealed record EntwuerfeAnlegenBefehl(SubjectId Wer, IReadOnlyList<Guid> Jobs)
    : IBefehl<IReadOnlyList<ApplicationDraft>>;

/// <summary>Das Modell soll jetzt schreiben.</summary>
/// <remarks>
/// Ein eigener Schritt und kein Anhängsel des Anlegens: das Schreiben dauert
/// Sekunden je Stück, und zwanzig davon in einer ResumeRequest wären eine ResumeRequest,
/// die niemand abwarten kann. Die Oberfläche ruft ihn je Entwurf und zeigt
/// dabei, wie weit sie ist.
/// </remarks>
public sealed record WriteDraftCommand(SubjectId Wer, Guid Id) : IBefehl<DraftResult>;

/// <summary>Die eigenen Entwürfe.</summary>
public sealed record MyDraftsQuery(SubjectId Wer)
    : IAbfrage<IReadOnlyList<ApplicationDraft>>;

/// <summary>Ein Entwurf.</summary>
public sealed record DraftQuery(SubjectId Wer, Guid Id) : IAbfrage<ApplicationDraft?>;

/// <summary>Selbst am Text ändern.</summary>
public sealed record ChangeDraftCommand(SubjectId Wer, Guid Id, string? Betreff, string? Text)
    : IBefehl<DraftResult>;

/// <summary>Wählen, was mitgeht.</summary>
public sealed record BeilagenWaehlenBefehl(
    SubjectId Wer, Guid Id, bool CurriculumVitae, IReadOnlyList<Guid> Unterlagen)
    : IBefehl<DraftResult>;

/// <summary>Etwas anmerken — mit oder ohne markierte Job.</summary>
public sealed record AnmerkenBefehl(SubjectId Wer, Guid Id, string? Text, string? Zitat)
    : IBefehl<DraftResult>;

/// <summary>Das Modell soll die Anmerkungen umsetzen.</summary>
public sealed record ReviseCommand(SubjectId Wer, Guid Id) : IBefehl<DraftResult>;

/// <summary>So, und nicht anders.</summary>
public sealed record FreigebenBefehl(SubjectId Wer, Guid Id) : IBefehl<DraftResult>;

/// <summary>Einen Entwurf wegwerfen.</summary>
public sealed record DeleteDraftCommand(SubjectId Wer, Guid Id) : IBefehl<bool>;

/// <summary>Führt die Entwürfe.</summary>
public sealed class DraftCommands(
    IDraftStore store,
    IJobLookup jobs,
    IKiZugangAbfrage ki,
    TimeProvider uhr) :
    IRequestHandler<EntwuerfeAnlegenBefehl, IReadOnlyList<ApplicationDraft>>,
    IRequestHandler<WriteDraftCommand, DraftResult>,
    IRequestHandler<MyDraftsQuery, IReadOnlyList<ApplicationDraft>>,
    IRequestHandler<DraftQuery, ApplicationDraft?>,
    IRequestHandler<ChangeDraftCommand, DraftResult>,
    IRequestHandler<BeilagenWaehlenBefehl, DraftResult>,
    IRequestHandler<AnmerkenBefehl, DraftResult>,
    IRequestHandler<ReviseCommand, DraftResult>,
    IRequestHandler<FreigebenBefehl, DraftResult>,
    IRequestHandler<DeleteDraftCommand, bool>
{
    /// <summary>Wie viele Jobs eine Massenhandlung tragen darf.</summary>
    /// <remarks>
    /// Ohne Grenze baut ein Aufrufer mit einer Adresse beliebig viele
    /// Modellaufrufe. Zwanzig ist die Zahl, bei der eine Prüfrunde noch eine
    /// Prüfrunde ist und kein Durchklicken.
    /// </remarks>
    public const int HoechsteAufEinmal = 20;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApplicationDraft>> Handle(
        EntwuerfeAnlegenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var drafts = new List<ApplicationDraft>();

        foreach (var id in request.Jobs.Distinct().Take(HoechsteAufEinmal))
        {
            // Zweimal fuer dieselbe Job zu entwerfen kostet einen
            // Modellaufruf und verwirrt die Liste. Der offene Entwurf kommt
            // stattdessen zurueck.
            var offener = await store.OpenOneAsync(request.Wer, id, cancellationToken);

            if (offener is not null)
            {
                drafts.Add(offener);
                continue;
            }

            var job = await jobs.GetAsync(id, cancellationToken);

            if (job is null)
            {
                continue;
            }

            var draft = ApplicationDraft.Beginne(
                job.Id, job.Firma, request.Wer, uhr.GetUtcNow());

            await store.SaveAsync(draft, cancellationToken);
            drafts.Add(draft);
        }

        return drafts;
    }

    /// <inheritdoc />
    public async Task<DraftResult> Handle(
        WriteDraftCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var draft = await store.GetAsync(request.Wer, request.Id, cancellationToken);

        if (draft is null)
        {
            return new DraftResult.None();
        }

        // Der Modellaufruf läuft hinter der ResumeRequest (CoverLetterWorker).
        // Hier nur: ohne Anbieter sofort scheitern, sonst Entsteht merken.
        KiZugang zugang;

        try
        {
            zugang = await ki.GetAsync(request.Wer, cancellationToken);
        }
        catch (CoverLetterUnavailable error)
        {
            draft.Scheitere(error.Message, uhr.GetUtcNow());
            await store.SaveAsync(draft, cancellationToken);

            return new DraftResult.NoProvider(error.Message);
        }

        if (!zugang.IstEingerichtet)
        {
            var reason = "Es ist kein Entwurfsanbieter eingerichtet.";
            draft.Scheitere(reason, uhr.GetUtcNow());
            await store.SaveAsync(draft, cancellationToken);

            return new DraftResult.NoProvider(reason);
        }

        try
        {
            // Auch aus Entsteht: sonst bleibt SchreibenBegonnen leer (Anlegen
            // setzt es nicht), und die Oberfläche zählt bei jedem Besuch von 0.
            // Leeren nur, wenn wirklich neu geschrieben wird — ein laufendes
            // Schreiben darf sein Bruchstück nicht verlieren.
            draft.Beginne_schreiben(
                uhr.GetUtcNow(),
                leeren: draft.State != DraftState.Entsteht);
        }
        catch (DraftStepNotAllowed error)
        {
            return new DraftResult.StateConflict(error.Message);
        }

        await store.SaveAsync(draft, cancellationToken);

        return new DraftResult.Erledigt(draft);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ApplicationDraft>> Handle(
        MyDraftsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.MineAsync(request.Wer, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ApplicationDraft?> Handle(
        DraftQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.GetAsync(request.Wer, request.Id, cancellationToken);
    }

    /// <inheritdoc />
    public Task<DraftResult> Handle(
        ChangeDraftCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Am_entwurf(
            request.Wer, request.Id,
            draft => draft.Aendere_selbst(request.Betreff, request.Text, uhr.GetUtcNow()),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<DraftResult> Handle(
        BeilagenWaehlenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Am_entwurf(
            request.Wer, request.Id,
            draft => draft.Waehle_beilagen(
                request.CurriculumVitae, request.Unterlagen, uhr.GetUtcNow()),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<DraftResult> Handle(
        AnmerkenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Am_entwurf(
            request.Wer, request.Id,
            draft => draft.Merke_an(request.Text, request.Zitat, uhr.GetUtcNow()),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DraftResult> Handle(
        ReviseCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var draft = await store.GetAsync(request.Wer, request.Id, cancellationToken);

        if (draft is null)
        {
            return new DraftResult.None();
        }

        // KEIN AUFRUF OHNE AUFTRAG. Ohne offene Anmerkung waere das die
        // Reflexionsstufe aus ADR-0024 unter anderem Namen.
        if (!draft.HatOffeneAnmerkungen)
        {
            return new DraftResult.StateConflict(
                "Es gibt keine offene Anmerkung — nichts zu überarbeiten.");
        }

        KiZugang zugang;

        try
        {
            zugang = await ki.GetAsync(request.Wer, cancellationToken);
        }
        catch (CoverLetterUnavailable error)
        {
            return new DraftResult.NoProvider(error.Message);
        }

        if (!zugang.IstEingerichtet)
        {
            return new DraftResult.NoProvider(
                "Es ist kein Entwurfsanbieter eingerichtet.");
        }

        try
        {
            draft.Beginne_schreiben(uhr.GetUtcNow(), leeren: false);
        }
        catch (DraftStepNotAllowed error)
        {
            return new DraftResult.StateConflict(error.Message);
        }

        await store.SaveAsync(draft, cancellationToken);

        return new DraftResult.Erledigt(draft);
    }

    /// <inheritdoc />
    public Task<DraftResult> Handle(
        FreigebenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Am_entwurf(
            request.Wer, request.Id,
            draft => draft.Gib_frei(uhr.GetUtcNow()),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> Handle(
        DeleteDraftCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var draft = await store.GetAsync(request.Wer, request.Id, cancellationToken);

        if (draft is null)
        {
            return false;
        }

        await store.DeleteAsync(request.Wer, request.Id, cancellationToken);

        return true;
    }

    /// <summary>Ein Schritt am Entwurf, mit immer derselben Fehlerbehandlung.</summary>
    private async Task<DraftResult> Am_entwurf(
        SubjectId subject, Guid id, Action<ApplicationDraft> step,
        CancellationToken cancellationToken)
    {
        var draft = await store.GetAsync(subject, id, cancellationToken);

        if (draft is null)
        {
            return new DraftResult.None();
        }

        try
        {
            step(draft);
        }
        catch (DraftStepNotAllowed error)
        {
            return new DraftResult.StateConflict(error.Message);
        }
        catch (OffeneAnmerkungen error)
        {
            return new DraftResult.StateConflict(error.Message);
        }
        catch (Eingabefehler error)
        {
            return new DraftResult.Eingabe(error.Message);
        }

        await store.SaveAsync(draft, cancellationToken);

        return new DraftResult.Erledigt(draft);
    }

}

/// <summary>Das Ausgabeformat des Modells, gelesen statt geraten.</summary>
/// <remarks>
/// <c>BETREFF: …\n---\n&lt;Text&gt;</c>. Robust gegen das, was Modelle
/// drumherum schreiben: Markdown-Zäune, führende Erklärungen, fehlender
/// Trennstrich. Ein Parser, der nur den Idealfall kann, macht aus einer
/// leichten Abweichung ein leeres Anschreiben.
/// </remarks>
public static class CoverLetterFormat
{
    /// <summary>Betreff und Text.</summary>
    public static (string Betreff, string Text) Lies(string? ausgabe)
    {
        var ganzes = (ausgabe ?? string.Empty).Trim();
        var betreff = string.Empty;
        var text = ganzes;

        var marke = ganzes.IndexOf("BETREFF:", StringComparison.OrdinalIgnoreCase);

        if (marke >= 0)
        {
            var danach = ganzes[(marke + "BETREFF:".Length)..];
            var strich = danach.IndexOf("---", StringComparison.Ordinal);

            if (strich >= 0)
            {
                betreff = danach[..strich];
                text = danach[(strich + 3)..];
            }
            else
            {
                var umbruch = danach.IndexOf('\n');

                if (umbruch < 0)
                {
                    // Betreffzeile noch nicht fertig. Den Rohtext als Körper
                    // zeigen — sonst bleibt der Brief minutenlang leer, während
                    // das Modell den Betreff tippt.
                    betreff = string.Empty;
                    text = ganzes;
                }
                else
                {
                    betreff = danach[..umbruch];
                    text = danach[(umbruch + 1)..];
                }
            }
        }

        return (
            betreff.Trim().Trim('*', '#', ' ', '"'),
            text.Trim().Trim('`').Trim());
    }
}

/// <summary>Den freigegebenen Entwurf als JobApplication abschicken.</summary>
/// <remarks>
/// <strong>Freigeben und Senden sind zwei Handlungen.</strong> Ein Knopf
/// „freigeben und senden" spart einen Klick und nimmt der Release ihren Sinn:
/// sie ist der Moment, in dem jemand sagt „so, und nicht anders", und der
/// braucht einen eigenen (ADR-0034).
/// </remarks>
public sealed record SendDraftCommand(SubjectId Wer, Guid Id) : IBefehl<DraftResult>;

/// <summary>Macht aus einem Entwurf eine JobApplication.</summary>
public sealed class SendDraftHandler(
    IDraftStore drafts,
    IApplicationStore applications,
    IJobLookup jobs,
    IConsentWriter ledger,
    IOutbox outbox,
    ICompanyMemberLookup members,
    IKontaktauskunft kontakt,
    TimeProvider uhr) : IRequestHandler<SendDraftCommand, DraftResult>
{
    /// <inheritdoc />
    public async Task<DraftResult> Handle(
        SendDraftCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var draft = await drafts.GetAsync(request.Wer, request.Id, cancellationToken);

        if (draft is null)
        {
            return new DraftResult.None();
        }

        if (draft.State != DraftState.Freigegeben)
        {
            return new DraftResult.StateConflict(
                "Nur ein freigegebener Entwurf geht hinaus.");
        }

        // Die Job noch einmal fragen: zwischen dem Entwurf und dem Versand
        // koennen Tage liegen, und eine geschlossene Ausschreibung nimmt keine
        // JobApplication mehr an.
        var job = await jobs.GetAsync(draft.Job, cancellationToken);

        if (job is null)
        {
            return new DraftResult.StateConflict(
                "Diese Stelle ist nicht mehr ausgeschrieben.");
        }

        var firmenMitglieder = await members.GetAsync(job.Firma, cancellationToken);

        var now = uhr.GetUtcNow();
        var mitgeschickt = new Mitgeschicktes(
            draft.TeiltLebenslauf, Portfolio: false, draft.Unterlagen);
        var briefkopf = await kontakt.GetContactAsync(cancellationToken);

        var vorhanden = await applications.GetAsync(job.Id, request.Wer, cancellationToken);
        JobApplication application;

        try
        {
            if (vorhanden is null)
            {
                application = JobApplication.Schicke_ab(
                    job.Id, job.Firma, request.Wer, draft.Text, mitgeschickt, now,
                    briefkopf);
            }
            else
            {
                vorhanden.Schicke_erneut(draft.Text, mitgeschickt, now, briefkopf);
                application = vorhanden;
            }
        }
        catch (UebergangNichtErlaubt error)
        {
            return new DraftResult.StateConflict(error.Message);
        }
        catch (Eingabefehler error)
        {
            return new DraftResult.Eingabe(error.Message);
        }

        // ERST DER LEDGER, DANN DER VORGANG, DANN DER COMMIT — dieselbe
        // Reihenfolge wie beim gewoehnlichen Abschicken, und aus demselben
        // Grund: es darf keine JobApplication geben, die das Unternehmen nicht
        // lesen darf.
        await ledger.GrantAsync(
            request.Wer,
            ConsentKey.Fuer(
                application.Firma,
                mitgeschickt.CurriculumVitae,
                mitgeschickt.Portfolio,
                mitgeschickt.Unterlagen.Count > 0),
            cancellationToken);

        await applications.SaveAsync(application, cancellationToken);

        draft.Vermerke_versand(now);
        await drafts.SaveAsync(draft, cancellationToken);

        // Die Person erfaehrt, dass ihre JobApplication heraus ist.
        await outbox.NoteAsync(
            request.Wer, Benachrichtigungsarten.Bewegt, cancellationToken);

        foreach (var member in firmenMitglieder)
        {
            await outbox.NoteAsync(member, Benachrichtigungsarten.Angekommen, cancellationToken);
        }

        return new DraftResult.Erledigt(draft);
    }
}
