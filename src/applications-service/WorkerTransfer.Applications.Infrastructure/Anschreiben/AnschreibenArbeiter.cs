using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkerTransfer.Applications.Application.Entwuerfe;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Bewerbungen;
using WorkerTransfer.Applications.Infrastructure.Security;

namespace WorkerTransfer.Applications.Infrastructure.Anschreiben;

/// <summary>
/// Schreibt im Hintergrund. Die HTTP-Anfrage kehrt zurück, sobald der Auftrag
/// angenommen ist — sonst hängt der Browser 133 Sekunden (gemessen) und ein
/// Seitenwechsel bricht das Schreiben ab.
/// </summary>
public sealed class AnschreibenArbeiter(
    IServiceScopeFactory speicher,
    ILogger<AnschreibenArbeiter> protokoll) : BackgroundService, IAnschreibenSchlange
{
    private readonly Channel<Anschreibenauftrag> _kanal =
        Channel.CreateUnbounded<Anschreibenauftrag>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    private readonly ConcurrentDictionary<Guid, byte> _laeuft = new();

    /// <inheritdoc />
    public bool Plane(Anschreibenauftrag auftrag)
    {
        ArgumentNullException.ThrowIfNull(auftrag);

        if (!_laeuft.TryAdd(auftrag.EntwurfId, 0))
        {
            return false;
        }

        if (_kanal.Writer.TryWrite(auftrag))
        {
            protokoll.LogInformation(
                "Anschreiben {EntwurfId} angestellt ({Art})",
                auftrag.EntwurfId, auftrag.Art);
            return true;
        }

        _laeuft.TryRemove(auftrag.EntwurfId, out _);
        return false;
    }

    /// <inheritdoc />
    public bool Laeuft(Guid entwurfId) => _laeuft.ContainsKey(entwurfId);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var auftrag in _kanal.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await FuehreAusAsync(auftrag, stoppingToken);
            }
            catch (Exception fehler) when (fehler is not OperationCanceledException)
            {
                protokoll.LogWarning(
                    fehler,
                    "Anschreibenauftrag {EntwurfId} ist fehlgeschlagen",
                    auftrag.EntwurfId);
            }
            finally
            {
                _laeuft.TryRemove(auftrag.EntwurfId, out _);
            }
        }
    }

    private async Task FuehreAusAsync(
        Anschreibenauftrag auftrag, CancellationToken stoppingToken)
    {
        using var bereich = speicher.CreateScope();
        using var _ = HttpAufrufertoken.Mit(auftrag.Traeger);

        var entwuerfe = bereich.ServiceProvider.GetRequiredService<IEntwurfsspeicher>();
        var einheit = bereich.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var anschreiber = bereich.ServiceProvider.GetRequiredService<IAnschreiber>();
        var ki = bereich.ServiceProvider.GetRequiredService<IKiZugangAbfrage>();
        var stellen = bereich.ServiceProvider.GetRequiredService<IStellenauskunft>();
        var bewerber = bereich.ServiceProvider.GetRequiredService<IBewerberauskunft>();
        var unternehmen = bereich.ServiceProvider.GetRequiredService<IUnternehmensauskunft>();
        var uhr = bereich.ServiceProvider.GetRequiredService<TimeProvider>();

        // SichereAsync allein committet nicht — das tut nur die Befehlsklammer.
        // Der Worker ist kein Befehl. Ohne diese Klammer bleibt der Brief leer,
        // obwohl das Modell fertig ist (gemessen: 600–900 Zeichen, Stand weiter
        // Entsteht, GET stellt alle drei Minuten neu an).
        Task HaltFest(Bewerbungsentwurf stand, CancellationToken token) =>
            einheit.InEinerTransaktionAsync(async ct =>
            {
                await entwuerfe.SichereAsync(stand, ct);
                return 0;
            }, token);

        var entwurf = await WarteAufStandAsync(entwuerfe, auftrag, stoppingToken);

        if (entwurf is null)
        {
            return;
        }

        if (entwurf.SchreibenBegonnen is null)
        {
            entwurf.Beginne_schreiben(uhr.GetUtcNow(), leeren: false);
            await HaltFest(entwurf, stoppingToken);
        }

        KiZugang zugang;

        try
        {
            zugang = await ki.HoleAsync(auftrag.Wer, stoppingToken);
        }
        catch (AnschreibenNichtVerfuegbar fehler)
        {
            await ScheitereAsync(HaltFest, entwurf, fehler.Message, uhr, stoppingToken);
            return;
        }

        if (!zugang.IstEingerichtet)
        {
            await ScheitereAsync(
                HaltFest, entwurf, "Es ist kein Entwurfsanbieter eingerichtet.", uhr, stoppingToken);
            return;
        }

        var stelle = await stellen.HoleAsync(entwurf.Stelle, stoppingToken);

        if (stelle is null)
        {
            await ScheitereAsync(
                HaltFest, entwurf, "Die Stelle ist nicht mehr offen.", uhr, stoppingToken);
            return;
        }

        Anschreibenkontext kontext;

        try
        {
            var eigen = await bewerber.HoleAsync(stoppingToken);
            var firma = await unternehmen.NameAsync(stelle.Firma, stoppingToken);

            kontext = new Anschreibenkontext(
                stelle.Titel, firma, stelle.Ort, stelle.Beschreibung, stelle.Faehigkeiten,
                eigen.Name, eigen.Ueberschrift, eigen.Text, eigen.Faehigkeiten,
                eigen.Werdegang, eigen.Sprache);
        }
        catch (BewerberSchweigt fehler)
        {
            await ScheitereAsync(HaltFest, entwurf, fehler.Message, uhr, stoppingToken);
            return;
        }

        var zuletzt = 0;
        var zuletztWann = uhr.GetUtcNow();

        async Task Fortschritt(string roh)
        {
            var jetzt = uhr.GetUtcNow();

            // Erstes Token immer schreiben — sonst bleibt der Brief leer, bis
            // vierzig Zeichen oder 400 ms zusammen sind, und ein langsames
            // Modell sieht aus, als schriebe es nicht.
            if (zuletzt > 0
                && roh.Length - zuletzt < 40
                && jetzt - zuletztWann < TimeSpan.FromMilliseconds(400))
            {
                return;
            }

            if (zuletzt == 0)
            {
                protokoll.LogInformation(
                    "Anschreiben {EntwurfId}: erstes Bruchstück ({Zeichen} Zeichen)",
                    auftrag.EntwurfId, roh.Length);
            }

            zuletzt = roh.Length;
            zuletztWann = jetzt;
            var (betreff, text) = Anschreibenformat.Lies(roh);
            var stand = await entwuerfe.HoleAsync(auftrag.Wer, auftrag.EntwurfId, stoppingToken);

            if (stand is null || stand.Stand != Entwurfsstand.Entsteht)
            {
                return;
            }

            try
            {
                stand.Nimm_bruchstueck(betreff, text, jetzt);
                await HaltFest(stand, stoppingToken);
            }
            catch (Exception speicherFehler) when (speicherFehler is not OperationCanceledException)
            {
                // Ein Speicherfehler darf den Strom nicht töten — sonst stirbt
                // das Modell nach dem ersten Zeichen, und GET stellt es neu an.
                protokoll.LogWarning(
                    speicherFehler,
                    "Anschreiben {EntwurfId}: Bruchstück nicht gesichert",
                    auftrag.EntwurfId);
            }
        }

        try
        {
            string ausgabe;

            protokoll.LogInformation(
                "Anschreiben {EntwurfId}: Modell {Modell} über {Anbieter}",
                auftrag.EntwurfId, zugang.Modell, zugang.Anbieter);

            if (auftrag.Art == AnschreibenauftragArt.Ueberarbeiten)
            {
                var auftraege = entwurf.OffeneAnmerkungen
                    .Select(eintrag => eintrag.Zitat.Length > 0
                        ? $"Zur markierten Stelle [{eintrag.Zitat}]: {eintrag.Text}"
                        : eintrag.Text)
                    .ToArray();

                ausgabe = await anschreiber.UeberarbeiteAsync(
                    zugang, kontext, entwurf.Betreff, entwurf.Text, auftraege,
                    Fortschritt, stoppingToken);

                var frisch = await entwuerfe.HoleAsync(auftrag.Wer, auftrag.EntwurfId, stoppingToken);

                if (frisch is null)
                {
                    return;
                }

                var (betreff, text) = Anschreibenformat.Lies(ausgabe);
                frisch.Ueberarbeite(betreff, text, uhr.GetUtcNow());
                await HaltFest(frisch, stoppingToken);
                return;
            }

            ausgabe = await anschreiber.SchreibeAsync(
                zugang, kontext, Fortschritt, stoppingToken);

            protokoll.LogInformation(
                "Anschreiben {EntwurfId}: Modell fertig ({Zeichen} Zeichen)",
                auftrag.EntwurfId, ausgabe.Length);

            var danach = await entwuerfe.HoleAsync(auftrag.Wer, auftrag.EntwurfId, stoppingToken);

            if (danach is null)
            {
                return;
            }

            var fertig = Anschreibenformat.Lies(ausgabe);
            danach.Nimm_text_an(fertig.Betreff, fertig.Text, uhr.GetUtcNow());
            await HaltFest(danach, stoppingToken);
        }
        catch (AnschreibenNichtVerfuegbar fehler)
        {
            protokoll.LogWarning(
                "Anschreiben {EntwurfId} abgebrochen: {Grund}",
                auftrag.EntwurfId, fehler.Message);

            var stand = await entwuerfe.HoleAsync(auftrag.Wer, auftrag.EntwurfId, stoppingToken);

            if (stand is null)
            {
                return;
            }

            if (stand.Text.Length > 0)
            {
                stand.Nimm_text_an(stand.Betreff, stand.Text, uhr.GetUtcNow());
            }
            else
            {
                stand.Scheitere(fehler.Message, uhr.GetUtcNow());
            }

            await HaltFest(stand, stoppingToken);
        }
    }

    private static async Task<Bewerbungsentwurf?> WarteAufStandAsync(
        IEntwurfsspeicher speicher,
        Anschreibenauftrag auftrag,
        CancellationToken stoppingToken)
    {
        for (var versuch = 0; versuch < 20; versuch++)
        {
            var entwurf = await speicher.HoleAsync(
                auftrag.Wer, auftrag.EntwurfId, stoppingToken);

            if (entwurf is not null && entwurf.Stand == Entwurfsstand.Entsteht)
            {
                return entwurf;
            }

            await Task.Delay(50, stoppingToken);
        }

        return await speicher.HoleAsync(auftrag.Wer, auftrag.EntwurfId, stoppingToken);
    }

    private static async Task ScheitereAsync(
        Func<Bewerbungsentwurf, CancellationToken, Task> halteFest,
        Bewerbungsentwurf entwurf,
        string grund,
        TimeProvider uhr,
        CancellationToken stoppingToken)
    {
        entwurf.Scheitere(grund, uhr.GetUtcNow());
        await halteFest(entwurf, stoppingToken);
    }
}
