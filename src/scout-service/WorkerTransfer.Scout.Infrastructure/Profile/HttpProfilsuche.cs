using System.Net;
using System.Net.Http.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Contracts;
using WorkerTransfer.Scout.Domain.Searches;
using WorkerTransfer.Scout.Domain.Hit;

namespace WorkerTransfer.Scout.Infrastructure.Profile;

/// <summary>Wo profile-service erreichbar ist, und was uns dort ausweist.</summary>
public sealed class ProfileSettings
{
    /// <summary>Der Section, an den das gebunden wird.</summary>
    public const string Section = "Profile";

    /// <summary>Die Basisadresse von profile-service.</summary>
    /// <remarks>
    /// Steht in der Konfiguration, weil die Egress-Grenze ihre erlaubten Hosts
    /// von dort ableitet (<c>ServiceFoundation.GerufeneHosts</c>). Ein Ziel, das
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
/// Revocation eine zweite Wahrheit.</para>
///
/// <para>Die Antwort wird mit <see cref="FremdprofilseiteV1"/> gelesen und
/// nicht mit dem Typ des Absenders: die beiden Dienste teilen keinen Vertrag,
/// und ein geteilter Typ wäre eine Übersetzungsschicht, die man bei einer
/// Änderung auf beiden Seiten zugleich anfassen müsste. Dass die Felder
/// wirklich ankommen, hält eine Reihe in <c>WorkerTransfer.Ganzes.Tests</c>
/// fest — sie serialisiert den Typ des Absenders und liest ihn mit dem des
/// Empfängers.</para>
/// </remarks>
public sealed class HttpProfileSearch(
    IHttpClientFactory fabrik,
    IOptions<ProfileSettings> einstellungen,
    ILogger<HttpProfileSearch> protokoll) : IProfileSearch
{
    /// <summary>Der Name des Mandanten in der Clientfabrik.</summary>
    public const string Client = "profile";

    private const string Geheimniskopf = "X-Notify-Secret";

    private readonly ProfileSettings _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<ProfileHitPage> SearchAsync(
        SearchFilter filter,
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
        // Evidence mitzuschicken — `SearchFilter` traegt keinen (ADR-0036 Auflage 3).
        abfrage.AddRange(filter.GenannteWorte.Select(
            word => $"skill={Uri.EscapeDataString(word)}"));

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

        var page = await Hole<ForeignProfilePageV1>(
            $"/internal/profiles/search?{string.Join('&', abfrage)}", cancellationToken);

        // EIN 404 IST HIER KEIN LEERES ERGEBNIS, und das war ein echter Fehler.
        //
        // Die Suchtuer hat keinen „nicht gefunden"-Fall: sie antwortet mit einer
        // Seite, oder sie laesst uns nicht herein — ohne das geteilte Geheimnis
        // antwortet sie 404 statt 401, damit sie sich nicht verraet. Genau das
        // traf zu, als `Notify__Geheimnis` bei profile-service fehlte: die Tuer
        // sagte 404, diese Job machte daraus `items: []`, der Endpunkt
        // antwortete 200 — und die Oberflaeche behauptete, es gebe niemanden.
        //
        // Gemessen am laufenden Stapel: zwoelf E2E-Reisen rot, und die
        // Routenkarte blieb gruen, weil sie Statuscodes prueft und keine
        // Ruempfe. „Nicht eingerichtet" darf nicht aussehen wie „es gibt
        // niemanden" — dieselbe Regel, die weiter unten fuer das leere
        // Geheimnis schon stand, nur an der Job, an der sie fehlte.
        return page is null
            ? throw new ProfileSearchSilent(
                "profile-service answered 404 to the search — the shared secret "
                + "is refused or the door is closed")
            : new ProfileHitPage([.. page.Items.Select(Zur)], page.Next);
    }

    /// <inheritdoc />
    public async Task<ProfileHit?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var profile = await Hole<ForeignProfileV1>(
            $"/internal/profiles/{subject.Value}", cancellationToken);

        return profile is null ? null : Zur(profile);
    }

    private static ProfileHit Zur(ForeignProfileV1 profile) =>
        new(
            new SubjectId(profile.SubjectId),
            profile.Headline,
            profile.Bio ?? string.Empty,
            profile.Location,
            profile.RemoteOk,
            profile.Skills ?? [],
            AvailabilityTerms.Stufe(profile.CommuteKm));

    /// <summary>Ein Aufruf. <c>null</c> heisst 404, alles andere wirft.</summary>
    /// <remarks>
    /// Was ein 404 BEDEUTET, entscheidet der Aufrufer: fuer ein einzelnes Profile
    /// „gibt es nicht", fuer die Suche „wir kommen nicht herein". Diese Methode
    /// reicht die Tatsache durch und deutet sie nicht.
    /// </remarks>
    private async Task<T?> Hole<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            throw new ProfileSearchSilent("Profile:Adresse ist nicht gesetzt.");
        }

        if (string.IsNullOrEmpty(_einstellungen.Geheimnis))
        {
            // Als Fehlschlag und nicht als leeres Ergebnis: „nicht
            // eingerichtet" darf nicht aussehen wie „es gibt niemanden".
            throw new ProfileSearchSilent("Profile:Geheimnis ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(Client);
        client.Timeout = _einstellungen.Geduld;

        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(_einstellungen.Adresse), path));

        request.Headers.Add(Geheimniskopf, _einstellungen.Geheimnis);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            // Die Art, nie der Inhalt: was gefragt wurde, ist eine Search nach
            // Menschen und gehoert nicht ins Protokoll.
            protokoll.LogWarning(
                "profile-service ist nicht erreichbar: {Art}", error.GetType().Name);

            throw new ProfileSearchSilent("profile-service unreachable");
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("profile-service hat nicht rechtzeitig geantwortet.");

            throw new ProfileSearchSilent($"profile-service timed out ({error.GetType().Name})");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                protokoll.LogWarning(
                    "profile-service antwortete mit {Status}.", (int)response.StatusCode);

                throw new ProfileSearchSilent(
                    $"profile-service returned {(int)response.StatusCode}");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                       ?? throw new ProfileSearchSilent("profile-service sent an empty answer");
            }
            catch (Exception error) when (error is not ProfileSearchSilent)
            {
                protokoll.LogWarning("profile-service antwortete unverständlich.");

                throw new ProfileSearchSilent("profile-service sent an unusable answer");
            }
        }
    }
}
