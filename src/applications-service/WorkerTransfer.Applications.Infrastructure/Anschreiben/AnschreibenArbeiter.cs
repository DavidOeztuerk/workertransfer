using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkerTransfer.Applications.Application.Entwuerfe;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Applications;
using WorkerTransfer.Applications.Infrastructure.Security;

namespace WorkerTransfer.Applications.Infrastructure.Anschreiben;

/// <summary>
/// Schreibt im Hintergrund. Die HTTP-ResumeRequest kehrt zurück, sobald der Auftrag
/// angenommen ist — sonst hängt der Browser 133 Sekunden (gemessen) und ein
/// Seitenwechsel bricht das Schreiben ab.
/// </summary>
public sealed class CoverLetterWorker(
    IServiceScopeFactory store,
    ILogger<CoverLetterWorker> protokoll) : BackgroundService, ICoverLetterQueue
{
    private readonly Channel<CoverLetterJob> _kanal =
        Channel.CreateUnbounded<CoverLetterJob>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    private readonly ConcurrentDictionary<Guid, byte> _laeuft = new();

    /// <inheritdoc />
    public bool Plane(CoverLetterJob letterJob)
    {
        ArgumentNullException.ThrowIfNull(letterJob);

        if (!_laeuft.TryAdd(letterJob.EntwurfId, 0))
        {
            return false;
        }

        if (_kanal.Writer.TryWrite(letterJob))
        {
            protokoll.LogInformation(
                "Anschreiben {EntwurfId} angestellt ({Art})",
                letterJob.EntwurfId, letterJob.Art);
            return true;
        }

        _laeuft.TryRemove(letterJob.EntwurfId, out _);
        return false;
    }

    /// <inheritdoc />
    public bool Laeuft(Guid entwurfId) => _laeuft.ContainsKey(entwurfId);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var letterJob in _kanal.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await PerformAsync(letterJob, stoppingToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                protokoll.LogWarning(
                    error,
                    "Anschreibenauftrag {EntwurfId} ist fehlgeschlagen",
                    letterJob.EntwurfId);
            }
            finally
            {
                _laeuft.TryRemove(letterJob.EntwurfId, out _);
            }
        }
    }

    private async Task PerformAsync(
        CoverLetterJob letterJob, CancellationToken stoppingToken)
    {
        using var scope = store.CreateScope();
        using var _ = HttpAufrufertoken.Mit(letterJob.Traeger);

        var drafts = scope.ServiceProvider.GetRequiredService<IDraftStore>();
        var einheit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var anschreiber = scope.ServiceProvider.GetRequiredService<ICoverLetterWriter>();
        var ki = scope.ServiceProvider.GetRequiredService<IKiZugangAbfrage>();
        var jobs = scope.ServiceProvider.GetRequiredService<IJobLookup>();
        var bewerber = scope.ServiceProvider.GetRequiredService<IApplicantLookup>();
        var unternehmen = scope.ServiceProvider.GetRequiredService<IUnternehmensauskunft>();
        var uhr = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        // SaveAsync allein committet nicht — das tut nur die Befehlsklammer.
        // Der Worker ist kein Befehl. Ohne diese Klammer bleibt der Brief leer,
        // obwohl das Modell fertig ist (gemessen: 600–900 Zeichen, State weiter
        // Entsteht, GET stellt alle drei Minuten neu an).
        Task HaltFest(ApplicationDraft state, CancellationToken token) =>
            einheit.InTransactionAsync(async ct =>
            {
                await drafts.SaveAsync(state, ct);
                return 0;
            }, token);

        var draft = await WaitForStateAsync(drafts, letterJob, stoppingToken);

        if (draft is null)
        {
            return;
        }

        if (draft.SchreibenBegonnen is null)
        {
            draft.Beginne_schreiben(uhr.GetUtcNow(), leeren: false);
            await HaltFest(draft, stoppingToken);
        }

        KiZugang zugang;

        try
        {
            zugang = await ki.GetAsync(letterJob.Wer, stoppingToken);
        }
        catch (CoverLetterUnavailable error)
        {
            await FailAsync(HaltFest, draft, error.Message, uhr, stoppingToken);
            return;
        }

        if (!zugang.IstEingerichtet)
        {
            await FailAsync(
                HaltFest, draft, "Es ist kein Entwurfsanbieter eingerichtet.", uhr, stoppingToken);
            return;
        }

        var job = await jobs.GetAsync(draft.Job, stoppingToken);

        if (job is null)
        {
            await FailAsync(
                HaltFest, draft, "Die Stelle ist nicht mehr offen.", uhr, stoppingToken);
            return;
        }

        CoverLetterContext context;

        try
        {
            var eigen = await bewerber.GetAsync(stoppingToken);
            var company = await unternehmen.NameAsync(job.Firma, stoppingToken);

            context = new CoverLetterContext(
                job.Titel, company, job.Ort, job.Beschreibung, job.Faehigkeiten,
                eigen.Name, eigen.Ueberschrift, eigen.Text, eigen.Faehigkeiten,
                eigen.Werdegang, eigen.Sprache);
        }
        catch (ApplicantLookupSilent error)
        {
            await FailAsync(HaltFest, draft, error.Message, uhr, stoppingToken);
            return;
        }

        var zuletzt = 0;
        var zuletztWann = uhr.GetUtcNow();

        async Task Fortschritt(string roh)
        {
            var now = uhr.GetUtcNow();

            // Erstes Token immer schreiben — sonst bleibt der Brief leer, bis
            // vierzig Zeichen oder 400 ms zusammen sind, und ein langsames
            // Modell sieht aus, als schriebe es nicht.
            if (zuletzt > 0
                && roh.Length - zuletzt < 40
                && now - zuletztWann < TimeSpan.FromMilliseconds(400))
            {
                return;
            }

            if (zuletzt == 0)
            {
                protokoll.LogInformation(
                    "Anschreiben {EntwurfId}: erstes Bruchstück ({Zeichen} Zeichen)",
                    letterJob.EntwurfId, roh.Length);
            }

            zuletzt = roh.Length;
            zuletztWann = now;
            var (betreff, text) = CoverLetterFormat.Lies(roh);
            var state = await drafts.GetAsync(letterJob.Wer, letterJob.EntwurfId, stoppingToken);

            if (state is null || state.State != DraftState.Entsteht)
            {
                return;
            }

            try
            {
                state.Nimm_bruchstueck(betreff, text, now);
                await HaltFest(state, stoppingToken);
            }
            catch (Exception speicherFehler) when (speicherFehler is not OperationCanceledException)
            {
                // Ein Speicherfehler darf den Strom nicht töten — sonst stirbt
                // das Modell nach dem ersten Zeichen, und GET stellt es neu an.
                protokoll.LogWarning(
                    speicherFehler,
                    "Anschreiben {EntwurfId}: Bruchstück nicht gesichert",
                    letterJob.EntwurfId);
            }
        }

        try
        {
            string ausgabe;

            protokoll.LogInformation(
                "Anschreiben {EntwurfId}: Modell {Modell} über {Anbieter}",
                letterJob.EntwurfId, zugang.Modell, zugang.Anbieter);

            if (letterJob.Art == CoverLetterJobKind.Ueberarbeiten)
            {
                var auftraege = draft.OffeneAnmerkungen
                    .Select(entry => entry.Zitat.Length > 0
                        ? $"Zur markierten Stelle [{entry.Zitat}]: {entry.Text}"
                        : entry.Text)
                    .ToArray();

                ausgabe = await anschreiber.ReviseAsync(
                    zugang, context, draft.Betreff, draft.Text, auftraege,
                    Fortschritt, stoppingToken);

                var frisch = await drafts.GetAsync(letterJob.Wer, letterJob.EntwurfId, stoppingToken);

                if (frisch is null)
                {
                    return;
                }

                var (betreff, text) = CoverLetterFormat.Lies(ausgabe);
                frisch.Ueberarbeite(betreff, text, uhr.GetUtcNow());
                await HaltFest(frisch, stoppingToken);
                return;
            }

            ausgabe = await anschreiber.WriteAsync(
                zugang, context, Fortschritt, stoppingToken);

            protokoll.LogInformation(
                "Anschreiben {EntwurfId}: Modell fertig ({Zeichen} Zeichen)",
                letterJob.EntwurfId, ausgabe.Length);

            var danach = await drafts.GetAsync(letterJob.Wer, letterJob.EntwurfId, stoppingToken);

            if (danach is null)
            {
                return;
            }

            var fertig = CoverLetterFormat.Lies(ausgabe);
            danach.Nimm_text_an(fertig.Betreff, fertig.Text, uhr.GetUtcNow());
            await HaltFest(danach, stoppingToken);
        }
        catch (CoverLetterUnavailable error)
        {
            protokoll.LogWarning(
                "Anschreiben {EntwurfId} abgebrochen: {Grund}",
                letterJob.EntwurfId, error.Message);

            var state = await drafts.GetAsync(letterJob.Wer, letterJob.EntwurfId, stoppingToken);

            if (state is null)
            {
                return;
            }

            if (state.Text.Length > 0)
            {
                state.Nimm_text_an(state.Betreff, state.Text, uhr.GetUtcNow());
            }
            else
            {
                state.Scheitere(error.Message, uhr.GetUtcNow());
            }

            await HaltFest(state, stoppingToken);
        }
    }

    private static async Task<ApplicationDraft?> WaitForStateAsync(
        IDraftStore store,
        CoverLetterJob letterJob,
        CancellationToken stoppingToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var draft = await store.GetAsync(
                letterJob.Wer, letterJob.EntwurfId, stoppingToken);

            if (draft is not null && draft.State == DraftState.Entsteht)
            {
                return draft;
            }

            await Task.Delay(50, stoppingToken);
        }

        return await store.GetAsync(letterJob.Wer, letterJob.EntwurfId, stoppingToken);
    }

    private static async Task FailAsync(
        Func<ApplicationDraft, CancellationToken, Task> halteFest,
        ApplicationDraft draft,
        string reason,
        TimeProvider uhr,
        CancellationToken stoppingToken)
    {
        draft.Scheitere(reason, uhr.GetUtcNow());
        await halteFest(draft, stoppingToken);
    }
}
