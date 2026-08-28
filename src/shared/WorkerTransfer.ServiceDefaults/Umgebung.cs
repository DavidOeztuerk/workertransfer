using DotNetEnv;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>
/// Lädt die Konfiguration aus einer <c>.env</c>-Datei in die Prozessumgebung.
/// </summary>
/// <remarks>
/// <para><strong>Vor <c>CreateBuilder</c>, nicht danach.</strong> Der
/// Konfigurationsaufbau von ASP.NET liest die Umgebungsvariablen einmal, beim
/// Bauen. Wer die Datei danach lädt, hat sie geladen und niemand liest sie.
/// Deshalb ist das ein statischer Aufruf in der ersten Zeile jedes Dienstes und
/// keine Registrierung.</para>
///
/// <para><strong>Die Rangfolge ist Umgebung vor <c>appsettings</c></strong>, und
/// zwar von selbst: <c>Env.Load</c> setzt Prozessvariablen, und der
/// Umgebungsanbieter steht in ASP.NET über dem JSON-Anbieter. Girder macht es an
/// seiner wichtigsten Stelle ausdrücklich genauso — <c>JWT_SECRET</c> schlägt
/// <c>JwtSettings:Secret</c>, und fehlt beides, bricht der Start mit einer
/// Ausnahme ab, die den Schlüssel <em>benennt</em>.</para>
///
/// <para><strong>Eine vorhandene Variable wird nicht überschrieben.</strong> Was
/// die Umgebung schon mitbringt, gewinnt: in Compose und in Kubernetes kommt sie
/// von dort, und eine Datei im Bild dürfte das nie übersteuern. Die Datei ist
/// für den Fall gedacht, dass ein Mensch einen Dienst allein startet.</para>
///
/// <para><strong>Fehlt die Datei, passiert nichts</strong> — das ist kein
/// Fehler, sondern der Normalfall im Container. Was fehlt, meldet der, der es
/// braucht, und zwar mit Namen.</para>
///
/// <para>Später füllt Infisical die Umgebung. Es füttert <c>.env</c>, es ersetzt
/// diesen Mechanismus nicht — der Code ändert sich dafür also nicht.</para>
/// </remarks>
public static class Umgebung
{
    /// <summary>Der Name der Datei ohne Umgebungsteil.</summary>
    public const string Datei = ".env";

    /// <summary>
    /// Lädt <c>.env.&lt;umgebung&gt;</c>, sonst <c>.env</c>, sonst nichts.
    /// </summary>
    /// <param name="verzeichnis">
    /// Wo gesucht wird. Ohne Angabe das Arbeitsverzeichnis — also das, was ein
    /// Dienst beim Start sieht. Der Parameter existiert für den Test: das
    /// Arbeitsverzeichnis zu wechseln ist prozessweit und wirft parallel
    /// laufende Reihen um.
    /// </param>
    /// <returns>Welche Datei geladen wurde, oder <c>null</c>.</returns>
    public static string? Laden(string? verzeichnis = null)
    {
        var umgebung =
            (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development")
            .ToLowerInvariant();

        var wo = verzeichnis ?? Directory.GetCurrentDirectory();

        foreach (var name in new[] { $"{Datei}.{umgebung}", Datei })
        {
            var kandidat = Path.Combine(wo, name);

            if (!File.Exists(kandidat))
            {
                continue;
            }

            // NoClobber: eine gesetzte Variable bleibt. TraversePath aus, damit
            // nicht versehentlich eine Datei aus einem Elternverzeichnis gilt —
            // welche Geheimnisse ein Dienst bekommt, darf nicht davon abhaengen,
            // wo jemand ihn gestartet hat.
            Env.Load(kandidat, new LoadOptions(setEnvVars: true, clobberExistingVars: false));

            return kandidat;
        }

        return null;
    }
}
