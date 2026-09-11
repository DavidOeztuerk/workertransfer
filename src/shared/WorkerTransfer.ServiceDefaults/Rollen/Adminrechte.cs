using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WorkerTransfer.ServiceDefaults.Rollen;

/// <summary>Welche Rechte dieser Dienst nur einem <c>admin</c> gibt.</summary>
/// <remarks>
/// <para><strong>Eine Liste je Dienst, keine gemeinsame.</strong> Was ein
/// Unternehmen bindet, weiss nur der Dienst, dem die Handlung gehört —
/// „veröffentlichen" ist in jobs-service eine Aussage nach aussen und anderswo
/// gar nichts. Eine gemeinsame Liste wäre die zweite Stelle, an der dieselbe
/// Frage beantwortet wird, und zwei gehen auseinander.</para>
///
/// <para><strong>Was NICHT in dieser Liste steht, darf jedes Mitglied.</strong>
/// Das ist die Vorgabe und sie ist Absicht: ein zu enges Recht macht aus einer
/// Einladung eine Zuschauerkarte, und dann legt jemand einen zweiten Admin an,
/// um arbeiten zu können — und „admin" ist wieder ein Wort in einer Tabelle.
/// Ein Mitglied braucht deshalb auch keine Richtlinie: die Prüfung „handelst du
/// für eine Firma?" steht schon am Endpunkt.</para>
/// </remarks>
public sealed class Adminrechte
{
    private readonly HashSet<string> _rechte;

    /// <summary>Mit den Rechten, die diesem Dienst gehören.</summary>
    /// <param name="rechte">Jedes Recht, das ein <c>admin</c> braucht.</param>
    public Adminrechte(IEnumerable<string> rechte)
    {
        ArgumentNullException.ThrowIfNull(rechte);
        _rechte = [.. rechte];
    }

    /// <summary>Verlangt dieses Recht einen <c>admin</c>?</summary>
    /// <param name="recht">Der Name aus der Richtlinie.</param>
    public bool NurAdmin(string recht) => _rechte.Contains(recht);

    /// <summary>Jedes Recht, das dieser Dienst kennt — für Tests und Berichte.</summary>
    public IReadOnlyCollection<string> Alle => _rechte;
}

/// <summary>Meldet die Firmenrechte eines Dienstes an.</summary>
public static class AdminrechteErweiterungen
{
    /// <summary>Girders Präfix für dynamisch aufgelöste Richtlinien.</summary>
    /// <remarks>
    /// Sein <c>PermissionPolicyProvider</c> baut aus jedem so benannten
    /// Richtliniennamen zur Laufzeit eine Richtlinie. Deshalb muss keine vorher
    /// angemeldet werden — und deshalb ist wichtig, dass der Anbieter überhaupt
    /// läuft: ohne ihn beantwortet niemand diese Namen, und das Gerüst lehnt
    /// <em>jede</em> Anfrage an den geschützten Endpunkt ab.
    /// </remarks>
    public const string Praefix = "Permission:";

    /// <summary>Der Richtlinienname zu einem Recht.</summary>
    /// <param name="recht">Das Recht, etwa <c>jobs.publish</c>.</param>
    public static string Richtlinie(string recht) => Praefix + recht;

    /// <summary>
    /// Verdrahtet die Rollenauskunft und den Handler, der die genannten Rechte
    /// aus ihr beantwortet.
    /// </summary>
    /// <param name="dienste">Der Container.</param>
    /// <param name="konfiguration">Woher <c>Identity:Adresse</c> kommt.</param>
    /// <param name="nurAdmin">Die Rechte, die einen <c>admin</c> verlangen.</param>
    /// <remarks>
    /// Nicht in <c>AddWorkerTransferDefaults</c>, und das ist dieselbe Trennung
    /// wie überall hier: der MECHANISMUS darf nicht elfmal beantwortet werden,
    /// die LISTE ist eine Entscheidung und steht dort, wo ein Leser sie sieht.
    /// </remarks>
    public static IServiceCollection AddAdminrechte(
        this IServiceCollection dienste,
        IConfiguration konfiguration,
        params string[] nurAdmin)
    {
        ArgumentNullException.ThrowIfNull(dienste);
        ArgumentNullException.ThrowIfNull(konfiguration);
        ArgumentNullException.ThrowIfNull(nurAdmin);

        if (nurAdmin.Length == 0)
        {
            // Ein Dienst, der diese Zeile ruft und nichts nennt, hat sie
            // vergessen zu fuellen — und bekaeme eine Verdrahtung, die nie
            // etwas entscheidet. Das sieht aus wie Schutz.
            throw new ArgumentException(
                "Ohne ein einziges Recht ist diese Verdrahtung wirkungslos.", nameof(nurAdmin));
        }

        dienste.Configure<Firmenrolleneinstellungen>(
            konfiguration.GetSection(Firmenrolleneinstellungen.Abschnitt));

        dienste.AddHttpClient(HttpFirmenrollen.Klient);
        dienste.AddSingleton(new Adminrechte(nurAdmin));
        dienste.TryAddScoped<IFirmenrollen, HttpFirmenrollen>();
        dienste.AddScoped<IAuthorizationHandler, Adminrecht>();

        return dienste;
    }
}
