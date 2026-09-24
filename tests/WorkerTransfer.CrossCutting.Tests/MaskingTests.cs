using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Noelia.Core.Logging;
using Noelia.Infrastructure.Logging;
using Serilog.Events;
using Serilog.Parsing;

namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>What Noelia's masking redacts in 4.4.0 — and what it releases again.</summary>
/// <remarks>
/// <para><strong>This suite is the counter-probe to a zero measurement.</strong>
/// Nowhere in the running stack does <c>[REDACTED]</c> stand, and that has two
/// possible meanings: there is nothing to mask, or the masking does not run.
/// Without these tests the first reading would be a hope.</para>
///
/// <para><strong>4.4.0 turns the direction in two places</strong>, and both are
/// pinned here: the comparison is now <em>exact</em> rather than by substring,
/// which makes <c>SecretName</c> and <c>TokenId</c> readable again — the name of
/// a secret is not one, and without it an operator cannot say which one was
/// meant. In return <c>Username</c>, <c>Email</c> and <c>City</c> now fall under
/// it, which they did not before.</para>
///
/// <para><strong>For WorkerTransfer both are without consequence, and that is
/// read off the tree, not assumed.</strong> The promise used to rest on luck: the
/// log templates carried German names, and Noelia's list is English. The
/// templates are English now (<c>{Kind}</c>, <c>{Status}</c>, <c>{Model}</c>…),
/// so that luck is gone, and a fixed list of five names here would only prove
/// that those five survive. This suite therefore collects every property name
/// from the message templates in <c>src/</c> and asks the masking about each
/// one. Noelia's <c>LoggingBehavior</c> writes <em>shapes</em> anyway —
/// <c>Email: string(34)</c> is a length, never an address — so masking a
/// template property would take away an operator's reading and protect
/// nothing.</para>
/// </remarks>
public sealed class MaskingTests
{
    /// <summary>A structured logging call — the start of its argument list.</summary>
    private static readonly Regex LogCall = new(
        @"\.Log(Trace|Debug|Information|Warning|Error|Critical)\(",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// A template property: <c>{Name}</c>, <c>{@Name}</c>, <c>{Name:format}</c>
    /// — but not an escaped <c>{{</c>.
    /// </summary>
    private static readonly Regex Placeholder = new(
        @"(?<!\{)\{[@$]?(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:[,:][^}]*)?\}",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>An event with exactly one property, through the enricher.</summary>
    private static string Enriched(string name, string value)
    {
        var logEvent = new LogEvent(
            DateTimeOffset.UnixEpoch,
            LogEventLevel.Information,
            exception: null,
            new MessageTemplate("irrelevant", []),
            [new LogEventProperty(name, new ScalarValue(value))]);

        new DataMaskingEnricher().Enrich(logEvent, propertyFactory: null!);

        return logEvent.Properties[name] is ScalarValue { Value: string read }
            ? read
            : logEvent.Properties[name].ToString().Trim('"');
    }

    /// <summary>What is redacted from 4.4.0 on — and was visible before.</summary>
    [Theory]
    [InlineData("Username")]
    [InlineData("Email")]
    [InlineData("City")]
    public void Newly_redacted(string name)
    {
        Enriched(name, "something personal").Should().Be(DataMaskingEnricher.Mask);
    }

    /// <summary>
    /// <strong>And what 4.4.0 releases again.</strong>
    /// </summary>
    /// <remarks>
    /// In 4.3.0 the substring "secret" also hit <c>SecretName</c> and "token"
    /// also hit <c>TokenId</c>. That was the bug 4.4.0 fixes: a log in which both
    /// are redacted is one nobody can follow any more — you then know that a
    /// secret was used, but not which one.
    /// </remarks>
    [Theory]
    [InlineData("SecretName", "jwt-signing-key")]
    [InlineData("TokenId", "01a070e2-847e-78f7")]
    public void Visible_again(string name, string value)
    {
        Enriched(name, value).Should().Be(value);
    }

    /// <summary>Exact, never as a substring.</summary>
    /// <remarks>
    /// The rule behind both halves above. <c>Emails</c> is not <c>Email</c>, and
    /// <c>SubjectId</c> is not <c>Id</c> — a comparison that hits substrings
    /// eventually redacts half the log.
    /// </remarks>
    [Theory]
    [InlineData("EmailTemplate")]
    [InlineData("HomeCity")]
    [InlineData("Usernames")]
    public void A_similar_name_is_not_hit(string name)
    {
        Enriched(name, "visible").Should().Be("visible");
    }

    /// <summary>
    /// <strong>Every property name our log templates use stays readable.</strong>
    /// </summary>
    /// <remarks>
    /// The names are collected from <c>src/</c>, not listed here: a list in this
    /// file would be green over the names somebody remembered, and the next
    /// template would never be asked. Whoever names a log property <c>Email</c>
    /// makes it unreadable, and this test says so at that name.
    /// </remarks>
    [Theory]
    [MemberData(nameof(LogPropertyNames))]
    public void Every_log_property_in_src_stays_readable(string name)
    {
        DataMaskingEnricher.IsSensitiveKey(name).Should().BeFalse(
            $"'{name}' stands in a log template in src/, and masking would turn "
            + "it into [REDACTED] for every operator who reads that line");
        Enriched(name, "readable").Should().Be("readable");
    }

    /// <summary>The scan really finds the templates — otherwise the theory above checks nothing.</summary>
    [Fact]
    public void The_scan_finds_the_log_properties_of_src()
    {
        var names = PropertyNamesIn(Sources());

        names.Should().HaveCountGreaterThan(
            15, "otherwise the scan no longer finds the log calls at all");
        names.Should().Contain(
            ["Kind", "Status", "Model", "Provider"],
            "these stand in the cover-letter worker and the consent gates");
    }

    /// <summary>
    /// The scan reads what a template means: a property in a plain literal
    /// counts, an interpolated hole and an escaped brace do not.
    /// </summary>
    [Fact]
    public void The_scan_reads_templates_and_nothing_else()
    {
        const string source = """
            log.LogWarning(
                "ledger unreachable ({Kind}) after {Attempt:D2} tries, {{literal}}",
                error.GetType().Name, attempt);
            log.LogError(error, "failed for " + "{@Email}", email);
            var text = $"not a template {Interpolated}";
            logger.LogInformation($"interpolated {Hole}");
            """;

        PropertyNamesIn([source]).Should().BeEquivalentTo(["Kind", "Attempt", "Email"]);
    }

    /// <summary>The list is one, not three.</summary>
    /// <remarks>
    /// <c>SensitiveFieldNames</c> is the same source the CQRS behaviour and the
    /// HTTP middleware read as well. Three lists would agree on the day they
    /// were written, and never again.
    /// </remarks>
    [Fact]
    public void The_list_carries_the_new_names_and_not_the_old_ones()
    {
        DataMaskingEnricher.IsSensitiveKey("Email").Should().BeTrue();
        DataMaskingEnricher.IsSensitiveKey("SecretName").Should().BeFalse();
        SensitiveFieldNames.All.Should().Contain("Email");
    }

    // ----------------------------------------------------------- the plumbing

    /// <summary>Every template property name in <c>src/</c>, one row each.</summary>
    public static TheoryData<string> LogPropertyNames
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var name in PropertyNamesIn(Sources()))
            {
                data.Add(name);
            }

            return data;
        }
    }

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot.Find(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                       StringComparison.Ordinal)
                   && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                       StringComparison.Ordinal))
            .Select(File.ReadAllText);

    /// <summary>The template property names in the log calls of the given sources.</summary>
    private static SortedSet<string> PropertyNamesIn(IEnumerable<string> sources)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var source in sources)
        {
            foreach (Match call in LogCall.Matches(source))
            {
                foreach (var literal in PlainLiteralsInArguments(source, call.Index + call.Length))
                {
                    foreach (Match hit in Placeholder.Matches(literal))
                    {
                        names.Add(hit.Groups["name"].Value);
                    }
                }
            }
        }

        return names;
    }

    /// <summary>
    /// The non-interpolated string literals between an opening parenthesis
    /// and the one that closes it.
    /// </summary>
    /// <remarks>
    /// Parentheses inside a literal do not count — <c>"unreachable ({Kind})"</c>
    /// is the common case. An interpolated literal is skipped: its holes are C#
    /// expressions, not template properties.
    /// </remarks>
    private static List<string> PlainLiteralsInArguments(string source, int start)
    {
        var literals = new List<string>();
        var depth = 1;
        var index = start;

        while (depth > 0 && index < source.Length)
        {
            var character = source[index];

            if (character == '"')
            {
                var interpolated = index > 0 && source[index - 1] == '$';
                var literal = new StringBuilder();
                index++;

                while (index < source.Length && source[index] != '"')
                {
                    if (source[index] == '\\' && index + 1 < source.Length)
                    {
                        literal.Append(source, index, 2);
                        index += 2;
                        continue;
                    }

                    literal.Append(source[index]);
                    index++;
                }

                if (!interpolated)
                {
                    literals.Add(literal.ToString());
                }
            }
            else if (character == '(')
            {
                depth++;
            }
            else if (character == ')')
            {
                depth--;
            }

            index++;
        }

        return literals;
    }
}
