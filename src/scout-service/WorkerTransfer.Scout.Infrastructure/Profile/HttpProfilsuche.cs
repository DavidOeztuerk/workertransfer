using System.Net;
using System.Net.Http.Json;
using Girder.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Contracts;
using WorkerTransfer.Scout.Domain.Suchen;

namespace WorkerTransfer.Scout.Infrastructure.Profile;

/// <summary>Wo profile-service erreichbar ist, und was uns dort ausweist.</summary>
public sealed class Profileinstellungen
{
    /// <summary>Der Abschnitt, an den das gebunden wird.</summary>
    public const string Abschnitt = "Profile";

    /// <summary>Die Basisadresse von profile-service.</summary>
    /// <remarks>
    /// Steht in der Konfiguration, weil die Egress-Grenze ihre erlaubten Hosts
    /// von dort ableitet (<c>Dienstgrundlage.GerufeneHosts</c>). Ein Ziel, das
    /// nur im Quelltext steht, wird abgewiesen — ohne Protokollzeile.
    /// </remarks>
    public string Adresse { get; set; } = "http://profile-service:8003";

    /// <summary>Das gemeinsame Geheimnis der internen Türen.</summary>
    /// <remarks>
    /// <strong>Leer heisst: es wird nicht gesucht</strong> — und zwar als
    /// Fehlschlag, nicht als leeres Ergebnis. Eine leere Trefferliste sähe aus
    /// wie „es gibt niemanden", und das ist eine Aussage über Menschen, die aus
    /// einer fehlenden Umgebungsvariablen stammte.
    /// </remarks>
    public string Geheimnis { get; set; } = string.Empty;

    /// <summary>Wie lange auf eine Antwort gewartet wird.</summary>
    /// <remarks>
    /// Fünf Sekunden, wie bei jedem anderen Dienst-zu-Dienst-Aufruf hier. OHNE
    /// diese Zeile gilt die Vorgabe von <c>HttpClient</c>: HUNDERT Sekunden.
    /// </remarks>
    public TimeSpan Geduld { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Fragt profile-service durch die interne Tür.</summary>
/// <remarks>
/// <para>Die Profile bleiben, wo sie hingehören (ADR-0004): dieser Dienst hält
/// keine Kopie. Eine Kopie veraltete gegen jede Änderung und wäre beim ersten
/// Widerruf eine zweite Wahrheit.</para>
///
/// <para>Die Antwort wird mit <see cref="FremdprofilseiteV1"/> gelesen und
/// nicht mit dem Typ des Absenders: die beiden Dienste teilen keinen Vertrag,
/// und ein geteilter Typ wäre eine Übersetzungsschicht, die man bei einer
/// Änderung auf beiden Seiten zugleich anfassen müsste. Dass die Felder
/// wirklich ankommen, hält eine Reihe in <c>WorkerTransfer.Ganzes.Tests</c>
/// fest — sie serialisiert den Typ des Absenders und liest ihn mit dem des
/// Empfängers.</para>
/// </remarks>
public sealed class HttpProfilsuche(
    IHttpClientFactory fabrik,
    IOptions<Profileinstellungen> einstellungen,
    ILogger<HttpProfilsuche> protokoll) : IProfilsuche
{
    /// <summary>Der Name des Mandanten in der Clientfabrik.</summary>
    public const string Client = "profile";

    private const string Geheimniskopf = "X-Notify-Secret";

    private readonly Profileinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<Profilfundseite> SucheAsync(
        Suchfilter filter,
        int seitenlaenge,
        string? zeiger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var abfrage = new List<string>
        {
            $"limit={seitenlaenge.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
        };

        // Nur GENANNTE Worte gehen hinaus. Es gibt hier gar keinen Weg, einen
        // Beleg mitzuschicken — `Suchfilter` traegt keinen (ADR-0036 Auflage 3).
        abfrage.AddRange(filter.GenannteWorte.Select(
            wort => $"skill={Uri.EscapeDataString(wort)}"));

        if (filter.Ort.Length > 0)
        {
            abfrage.Add($"location={Uri.EscapeDataString(filter.Ort)}");
        }

        if (filter.NurRemote)
        {
            abfrage.Add("remote=true");
        }

        if (!string.IsNullOrEmpty(zeiger))
        {
            abfrage.Add($"cursor={Uri.EscapeDataString(zeiger)}");
        }

        var seite = await Hole<FremdprofilseiteV1>(
            $"/internal/profiles/search?{string.Join('&', abfrage)}", cancellationToken);

        return seite is null
            ? new Profilfundseite([], null)
            : new Profilfundseite([.. seite.Items.Select(Zur)], seite.Next);
    }

    /// <inheritdoc />
    public async Task<Profilfund?> HoleAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var profil = await Hole<FremdprofilV1>(
            $"/internal/profiles/{wer.Value}", cancellationToken);

        return profil is null ? null : Zur(profil);
    }

    private static Profilfund Zur(FremdprofilV1 profil) =>
        new(
            new SubjectId(profil.SubjectId),
            profil.Headline,
            profil.Location,
            profil.RemoteOk,
            profil.Skills ?? []);

    /// <summary>Ein Aufruf. <c>null</c> heisst 404, alles andere wirft.</summary>
    private async Task<T?> Hole<T>(string pfad, CancellationToken cancellationToken)
        where T : class
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            throw new ProfilsucheSchweigt("Profile:Adresse ist nicht gesetzt.");
        }

        if (string.IsNullOrEmpty(_einstellungen.Geheimnis))
        {
            // Als Fehlschlag und nicht als leeres Ergebnis: „nicht
            // eingerichtet" darf nicht aussehen wie „es gibt niemanden".
            throw new ProfilsucheSchweigt("Profile:Geheimnis ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(Client);
        client.Timeout = _einstellungen.Geduld;

        using var anfrage = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(_einstellungen.Adresse), pfad));

        anfrage.Headers.Add(Geheimniskopf, _einstellungen.Geheimnis);

        HttpResponseMessage antwort;

        try
        {
            antwort = await client.SendAsync(anfrage, cancellationToken);
        }
        catch (HttpRequestException fehler)
        {
            // Die Art, nie der Inhalt: was gefragt wurde, ist eine Suche nach
            // Menschen und gehoert nicht ins Protokoll.
            protokoll.LogWarning(
                "profile-service ist nicht erreichbar: {Art}", fehler.GetType().Name);

            throw new ProfilsucheSchweigt("profile-service unreachable");
        }
        catch (TaskCanceledException fehler) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("profile-service hat nicht rechtzeitig geantwortet.");

            throw new ProfilsucheSchweigt($"profile-service timed out ({fehler.GetType().Name})");
        }

        using (antwort)
        {
            if (antwort.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!antwort.IsSuccessStatusCode)
            {
                protokoll.LogWarning(
                    "profile-service antwortete mit {Status}.", (int)antwort.StatusCode);

                throw new ProfilsucheSchweigt(
                    $"profile-service returned {(int)antwort.StatusCode}");
            }

            try
            {
                return await antwort.Content.ReadFromJsonAsync<T>(cancellationToken)
                       ?? throw new ProfilsucheSchweigt("profile-service sent an empty answer");
            }
            catch (Exception fehler) when (fehler is not ProfilsucheSchweigt)
            {
                protokoll.LogWarning("profile-service antwortete unverständlich.");

                throw new ProfilsucheSchweigt("profile-service sent an unusable answer");
            }
        }
    }
}
