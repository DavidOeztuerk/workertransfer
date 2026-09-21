using System.Text.RegularExpressions;
using FluentAssertions;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// Every outgoing call sets a time limit.
/// </summary>
/// <remarks>
/// <para><strong>Why this is a test at all.</strong> Without a line of its own
/// the default of <c>HttpClient</c> applies: <strong>one hundred
/// seconds</strong>. That reads like "there is a time limit" and in a
/// service-to-service call is none — a hanging service hangs its caller with
/// it, and that one hangs the person in front of it. Measured, seven of fifteen
/// call sites had none, among them a <em>consent gate</em>, while every other
/// consent gate had one. That was not a design, that was forgetting.</para>
///
/// <para><strong>Over the source and not over the wiring</strong>, because that
/// is exactly the question: forgetting cannot be seen on a running application.
/// It does answer — only eventually.</para>
///
/// <para>One time limit per call site and not one for all: five seconds are
/// generous for a consent check and too little for a draft over a language
/// model. One number for both would be wrong for one of the two.</para>
///
/// <para><strong>What is searched for is the ASSIGNMENT, not the variable
/// name.</strong> <c>client.Timeout</c> stood here, and with it the promise hung
/// on what somebody calls their local variable: <c>HttpCompanyRoles</c> sets its
/// time limit on <c>klient</c> and failed as "without a time limit" although it
/// has one. A guard that goes red over correct code gets switched off the next
/// time instead of read.</para>
/// </remarks>
public sealed class ZeitlimitTests
{
    private static IEnumerable<string> Aufrufstellen() =>
        Directory.EnumerateFiles(
                Path.Combine(Postgres_Ersatz2.Repowurzel(), "src"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                       StringComparison.Ordinal)
                   && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                       StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path)
                .Contains("fabrik.CreateClient(", StringComparison.Ordinal));

    [Fact]
    public void Every_call_site_sets_a_time_limit()
    {
        var jobs = Aufrufstellen().ToList();

        jobs.Should().HaveCountGreaterThan(
            10, "sonst findet dieser Test die Aufrufstellen gar nicht mehr");

        foreach (var path in jobs)
        {
            File.ReadAllText(path).Should().Contain(
                ".Timeout =",
                $"'{Path.GetFileName(path)}' ruft einen anderen Dienst, setzt aber kein "
                + "Zeitlimit — dann gilt die Vorgabe von hundert Sekunden");
        }
    }

    /// <summary>
    /// And no call site waits longer than a minute.
    /// </summary>
    /// <remarks>
    /// The ceiling is deliberately wide: it is not meant to prescribe the choice
    /// between five and thirty seconds, but to prevent anybody from slipping
    /// back near the hundred seconds by accident.
    /// </remarks>
    [Fact]
    public void No_call_site_waits_longer_than_a_minute()
    {
        var pattern = new Regex(
            @"Zeitueberschreitung \{ get; set; \} = TimeSpan\.From(?<einheit>Seconds|Minutes)\((?<zahl>[\d.]+)\)",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        var quellen = Directory.EnumerateFiles(
                Path.Combine(Postgres_Ersatz2.Repowurzel(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        var found = 0;

        foreach (var path in quellen)
        {
            foreach (Match hit in pattern.Matches(File.ReadAllText(path)))
            {
                found++;
                var zahl = double.Parse(hit.Groups["zahl"].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                var spanne = hit.Groups["einheit"].Value == "Minutes"
                    ? TimeSpan.FromMinutes(zahl)
                    : TimeSpan.FromSeconds(zahl);

                spanne.Should().BeLessThanOrEqualTo(
                    TimeSpan.FromMinutes(1),
                    $"'{Path.GetFileName(path)}' wartet zu lange");
            }
        }

        found.Should().BeGreaterThan(10, "sonst prueft dieser Test nichts");
    }
}

/// <summary>Finds the repository root.</summary>
internal static class Postgres_Ersatz2
{
    public static string Repowurzel()
    {
        var verzeichnis = new DirectoryInfo(AppContext.BaseDirectory);

        while (verzeichnis is not null
               && !File.Exists(Path.Combine(verzeichnis.FullName, "WorkerTransfer.slnx")))
        {
            verzeichnis = verzeichnis.Parent;
        }

        return verzeichnis?.FullName
               ?? throw new InvalidOperationException("Repowurzel nicht gefunden.");
    }
}
