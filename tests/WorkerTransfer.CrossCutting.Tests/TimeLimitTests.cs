using System.Text.RegularExpressions;
using FluentAssertions;

namespace WorkerTransfer.CrossCutting.Tests;

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
///
/// <para><strong>And the call site is found by its CALL, not by the name of
/// the factory.</strong> This test used to look for <c>fabrik.CreateClient(</c>.
/// The English rename turned <c>fabrik</c> into <c>factory</c> service by
/// service, and every renamed call site silently dropped out of the check —
/// sixteen of thirty-four, while the test stayed green over its floor of ten.
/// The time-limit properties are found the same way: by what is assigned to
/// <c>.Timeout</c>, not by a property name — the German word
/// <c>Zeitueberschreitung</c> it once searched for already missed
/// <c>Geduld</c> and <c>Timeout</c>.</para>
/// </remarks>
public sealed class TimeLimitTests
{
    private static readonly Regex CreatesClient = new(
        @"\b\w+\.CreateClient\(", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex AssignsTimeout = new(
        @"\.Timeout = [\w.]*?\.(?<property>\w+);", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot.Find(), "src"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                       StringComparison.Ordinal)
                   && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                       StringComparison.Ordinal));

    private static List<string> CallSites() =>
        [.. Sources().Where(path => CreatesClient.IsMatch(File.ReadAllText(path)))];

    [Fact]
    public void Every_call_site_sets_a_time_limit()
    {
        var callSites = CallSites();

        callSites.Should().HaveCountGreaterThan(
            30, "otherwise this test no longer finds the call sites at all");

        foreach (var path in callSites)
        {
            File.ReadAllText(path).Should().Contain(
                ".Timeout =",
                $"'{Path.GetFileName(path)}' calls another service but sets no time "
                + "limit — then the default of a hundred seconds applies");
        }
    }

    /// <summary>
    /// And no call site waits longer than a minute.
    /// </summary>
    /// <remarks>
    /// The ceiling is deliberately wide: it is not meant to prescribe the choice
    /// between five and thirty seconds, but to prevent anybody from slipping
    /// back near the hundred seconds by accident. Checked are exactly the
    /// properties some call site assigns to <c>.Timeout</c> — the outbox's
    /// polling interval is a <c>TimeSpan</c> too, and no time limit.
    /// </remarks>
    [Fact]
    public void No_call_site_waits_longer_than_a_minute()
    {
        var sources = Sources().Select(File.ReadAllText).ToList();

        var timeLimits = sources
            .SelectMany(text => AssignsTimeout.Matches(text))
            .Select(hit => hit.Groups["property"].Value)
            .ToHashSet(StringComparer.Ordinal);

        timeLimits.Should().NotBeEmpty("otherwise no call site assigns a time limit at all");

        var declaration = new Regex(
            @"TimeSpan (?<property>\w+) \{ get; set; \} = TimeSpan\.From(?<unit>Seconds|Minutes)\((?<value>[\d.]+)\)",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        var found = 0;

        foreach (var text in sources)
        {
            foreach (Match hit in declaration.Matches(text))
            {
                if (!timeLimits.Contains(hit.Groups["property"].Value))
                {
                    continue;
                }

                found++;
                var value = double.Parse(hit.Groups["value"].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                var span = hit.Groups["unit"].Value == "Minutes"
                    ? TimeSpan.FromMinutes(value)
                    : TimeSpan.FromSeconds(value);

                span.Should().BeLessThanOrEqualTo(
                    TimeSpan.FromMinutes(1),
                    $"'{hit.Groups["property"].Value}' waits too long");
            }
        }

        found.Should().BeGreaterThan(30, "otherwise this test checks nothing");
    }
}
