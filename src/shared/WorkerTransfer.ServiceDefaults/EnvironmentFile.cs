using DotNetEnv;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>
/// Loads the configuration out of a <c>.env</c> file into the process
/// environment.
/// </summary>
/// <remarks>
/// <para><strong>Before <c>CreateBuilder</c>, not after.</strong> ASP.NET's
/// configuration builder reads the environment variables once, when it builds.
/// Whoever loads the file afterwards has loaded it and nobody reads it. That is
/// why this is a static call in the first line of every service and not a
/// registration.</para>
///
/// <para><strong>The precedence is the environment before <c>appsettings</c></strong>,
/// and that happens by itself: <c>Env.Load</c> sets process variables, and in
/// ASP.NET the environment provider stands above the JSON provider. Noelia does
/// exactly the same at its own most important place — <c>JWT_SECRET</c> beats
/// <c>JwtSettings:Secret</c>, and if both are absent the start aborts with an
/// exception that <em>names</em> the key.</para>
///
/// <para><strong>An already-set variable is never overwritten.</strong> What
/// the environment already brings with it wins: in compose and in Kubernetes it
/// comes from there, and a file in the image must never override that. The file
/// is meant for the case where a human starts one service on its own.</para>
///
/// <para><strong>If the file is missing, nothing happens</strong> — that is not
/// an error but the normal case in a container. Whatever is missing is reported
/// by whoever needs it, and reported by name.</para>
///
/// <para>Later Infisical will fill the environment. It feeds <c>.env</c>, it
/// does not replace this mechanism — so the code does not change for it.</para>
/// </remarks>
public static class EnvironmentFile
{
    /// <summary>The name of the file without an environment part.</summary>
    public const string Datei = ".env";

    /// <summary>
    /// Loads <c>.env.&lt;environment&gt;</c>, otherwise <c>.env</c>, otherwise
    /// nothing.
    /// </summary>
    /// <param name="verzeichnis">
    /// Where to look. Without a value, the working directory — that is, what a
    /// service sees at startup. The parameter exists for the test: changing the
    /// working directory is process-wide and topples suites running in
    /// parallel.
    /// </param>
    /// <returns>Which file was loaded, or <c>null</c>.</returns>
    public static string? Load(string? verzeichnis = null)
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

            // NoClobber: a variable that is already set stays. TraversePath is
            // off so that a file from a parent directory does not apply by
            // accident — which secrets a service gets must not depend on where
            // somebody started it.
            Env.Load(kandidat, new LoadOptions(setEnvVars: true, clobberExistingVars: false));

            return kandidat;
        }

        return null;
    }
}
