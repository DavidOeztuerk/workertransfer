using System.Globalization;
using System.Net;
using Microsoft.Extensions.Configuration;

namespace WorkerTransfer.Nachweis;

/// <summary>Wo ein Ziel liegt, soweit sein Name es hergibt.</summary>
public enum Lage
{
    /// <summary>Im eigenen Netz: ein Dienstname, eine private Adresse, die Schleife.</summary>
    EigenesNetz,

    /// <summary>Im öffentlichen Netz. Welches Recht dort gilt, sagt der Name nicht.</summary>
    Draussen
}

/// <summary>Jeder Host, den ein Dienst laut seiner Konfiguration ruft.</summary>
/// <remarks>
/// <para><strong>Eine Ableitung, keine Liste.</strong> Gelesen wird die ganze
/// Konfiguration; jeder Wert, der sich als absolute http- oder https-Adresse
/// lesen lässt, gibt seinen Host her. Eine zweite Liste wäre die, die als Erste
/// veraltet — und ihr Veralten fiele niemandem auf, weil ein nicht erfasster
/// Aufruf von der Egress-Grenze einfach abgewiesen wird.</para>
///
/// <para><strong>Sie steht hier und nicht in <c>Dienstgrundlage</c>, weil sie
/// zwei Leser hat.</strong> Die Egress-Grenze erlaubt genau diese Hosts, und
/// <c>wt.grenze.ziele</c> berichtet genau diese Hosts. Zwei Ableitungen über
/// dieselbe Frage sind zwei Wahrheiten: der Bericht nennte dann Ziele, die die
/// Grenze nicht durchlässt, oder — schlimmer — die Grenze ließe Ziele durch,
/// die im Bericht fehlen.</para>
///
/// <para>Verbindungszeichenfolgen fallen durch (<c>Host=postgres;…</c> ist
/// keine URL), und das ist richtig: Postgres wird nicht über einen
/// <c>HttpClient</c> gerufen, die Egress-Grenze sieht es nie — und ein
/// Datenbankhost gehört in kein Empfängerverzeichnis nach Art. 30 DSGVO.</para>
/// </remarks>
public static class Zielkunde
{
    /// <summary>Jeder Host aus der Konfiguration, einmal, sortiert.</summary>
    /// <param name="konfiguration">Die Konfiguration des Dienstes.</param>
    /// <returns>Die Hostnamen.</returns>
    public static IReadOnlyList<string> Hosts(IConfiguration konfiguration)
    {
        ArgumentNullException.ThrowIfNull(konfiguration);

        return
        [
            .. konfiguration.AsEnumerable()
                .Select(eintrag => eintrag.Value)
                .Where(wert => !string.IsNullOrWhiteSpace(wert))
                .Select(wert =>
                    Uri.TryCreate(wert, UriKind.Absolute, out var adresse)
                    && (adresse.Scheme == Uri.UriSchemeHttp
                        || adresse.Scheme == Uri.UriSchemeHttps)
                        ? adresse.Host
                        : null)
                .Where(host => host is { Length: > 0 })
                .Select(host => host!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(host => host, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>Wo ein Host liegt, soweit sein Name es hergibt.</summary>
    /// <remarks>
    /// <para><strong>Aus dem Namen und nicht aus einer Auflösung.</strong> Ein
    /// DNS-Eintrag wechselt, eine IP-Geolokalisierung ist eine Schätzung, und
    /// eine Schätzung in einem Nachweis ist schlimmer als eine Lücke: sie sieht
    /// aus wie eine Messung. Was der Name hergibt, gibt er sicher her — ein
    /// Name ohne Punkt ist kein öffentlicher Name, eine RFC-1918-Adresse ist
    /// keine öffentliche Adresse.</para>
    ///
    /// <para><strong>Und weiter geht es nicht.</strong> In welchem Land ein
    /// Anbieter unter <c>api.anthropic.com</c> verarbeitet, sagt dieser Name
    /// nicht, und niemand hier soll so tun. Das ist die Frage, die der
    /// Rechtsbezug an den Menschen weitergibt — Kap. V DSGVO braucht eine
    /// Garantie, und welche dieses Ziel deckt, steht in einem Aktenschrank.</para>
    /// </remarks>
    /// <param name="host">Der Hostname.</param>
    /// <returns>Wo er liegt.</returns>
    public static Lage Wo(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        if (IPAddress.TryParse(host, out var adresse))
        {
            return IstPrivat(adresse) ? Lage.EigenesNetz : Lage.Draussen;
        }

        // Ein Name ohne Punkt ist ein Name im eigenen Netz: so heissen die
        // Dienste in Compose (`consent-service`) und die Service-Objekte in
        // Kubernetes. Ein oeffentlicher Name hat immer eine Domaene.
        if (!host.Contains('.', StringComparison.Ordinal))
        {
            return Lage.EigenesNetz;
        }

        return Innen.Any(ende =>
            host.Equals(ende, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + ende, StringComparison.OrdinalIgnoreCase))
            ? Lage.EigenesNetz
            : Lage.Draussen;
    }

    /// <summary>Wie eine Lage in einem Satz heißt.</summary>
    /// <param name="lage">Welche.</param>
    /// <returns>Das Wort.</returns>
    public static string Wort(Lage lage) =>
        lage == Lage.EigenesNetz ? "eigenes Netz" : "öffentliches Netz";

    /// <summary>Namensenden, die kein öffentliches Netz bezeichnen.</summary>
    /// <remarks>
    /// <c>host.docker.internal</c> steht hier, weil es genau das Gegenteil von
    /// „draußen“ meint: die Maschine, auf der der Behälter läuft. Es ist die
    /// Adresse, unter der ein Ollama im eigenen Haus antwortet — und der Fall,
    /// an dem die Egress-Grenze am 10.09.2026 den Anschreiben-Agenten
    /// abgewiesen hat.
    /// </remarks>
    private static readonly string[] Innen =
    [
        "localhost",
        "local",
        "internal",
        "host.docker.internal",
        "svc.cluster.local"
    ];

    /// <summary>Ob eine Adresse im eigenen Netz liegt.</summary>
    private static bool IstPrivat(IPAddress adresse)
    {
        if (IPAddress.IsLoopback(adresse))
        {
            return true;
        }

        if (adresse.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            // IPv6: die eindeutig lokalen (fc00::/7) und die verbindungslokalen.
            return adresse.IsIPv6LinkLocal
                   || adresse.IsIPv6SiteLocal
                   || (adresse.GetAddressBytes()[0] & 0xFE) == 0xFC;
        }

        var bytes = adresse.GetAddressBytes();

        return bytes[0] switch
        {
            10 => true,
            127 => true,
            172 => bytes[1] is >= 16 and <= 31,
            192 => bytes[1] == 168,
            169 => bytes[1] == 254,
            _ => false
        };
    }

    /// <summary>Eine Zahl, wie sie in einen deutschen Satz gehört.</summary>
    /// <param name="wert">Die Zahl.</param>
    /// <returns>Sie als Text, unabhängig von der Kultur der Maschine.</returns>
    internal static string Zahl(int wert) =>
        wert.ToString(CultureInfo.InvariantCulture);
}
