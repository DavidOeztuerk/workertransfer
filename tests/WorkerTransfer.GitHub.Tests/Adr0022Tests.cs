using System.Reflection;
using FluentAssertions;
using WorkerTransfer.GitHub.Contracts;
using WorkerTransfer.GitHub.Domain.Connections;

namespace WorkerTransfer.GitHub.Tests;

/// <summary>The line this service is built right up against.</summary>
/// <remarks>
/// This service was built <strong>under</strong> ADR-0022, not condemned by it
/// — the deleted package <c>worker-github</c> evaluated human beings, this
/// service expressly does the opposite. So that it stays that way, the line is
/// checked here and not merely described:
/// <list type="bullet">
/// <item>A statement about a <em>repository</em> is a fact — "80 % Go according
/// to GitHub".</item>
/// <item>A statement about a <em>human being</em> is a judgement they cannot
/// comment on — "this person can do Go".</item>
/// </list>
/// A sort key over human beings is the ADR-0022 score through the back door,
/// even if it is called "activity".
/// </remarks>
public class Adr0022Tests
{
    /// <summary>Words that announce a judgement about a human being.</summary>
    private static readonly string[] Forbidden =
        ["score", "punkt", "rank", "rang", "level", "weight", "gewicht", "ability",
         "faehigkeit", "skill", "talent", "bewert", "aktivitaet", "activity"];

    /// <summary>
    /// No public surface of this service carries such a name.
    /// </summary>
    /// <remarks>
    /// The test sweeps two assemblies: the domain, where a score would come
    /// into being, and the contract, where it would go out. Whoever needs one
    /// of the words has crossed the line or has to change the test — and that
    /// is a visible commit somebody has to justify.
    /// </remarks>
    [Fact]
    public void Nothing_here_sums_a_human_being_up_in_a_number()
    {
        var surfaces = new[] { typeof(Connection).Assembly, typeof(ConnectionV1).Assembly }
            .SelectMany(assembly => assembly.GetExportedTypes())
            .SelectMany(type => type.GetMembers(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly)
                .Select(glied => $"{type.Name}.{glied.Name}")
                .Append(type.Name))
            .Distinct()
            .ToList();

        surfaces.Should().NotBeEmpty("otherwise the test checks nothing");

        foreach (var name in surfaces)
        {
            foreach (var word in Forbidden)
            {
                name.Contains(word, StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse($"'{name}' carries '{word}' — ADR-0022");
            }
        }
    }

    /// <summary>
    /// A repository carries exactly what GitHub reported.
    /// </summary>
    /// <remarks>
    /// Eight fields, all copied, none computed. A ninth would be the place where
    /// evidence turns into a judgement.
    /// <para>
    /// <strong>The two youngest are the reason to read this test
    /// closely.</strong> <c>Topics</c> are the topics the owner set themselves —
    /// a naming, copied. <c>Languages</c> is the SET of a repository's
    /// languages, and that is the line: GitHub delivers a byte count per
    /// language with it, and out of exactly that the deleted package computed
    /// its "skill" (<c>bytes / total_bytes</c>, ADR-0022 §2). The names are a
    /// fact about a repository; the numbers would be the raw material for a
    /// statement about a human being. So they stand nowhere — see
    /// <c>HttpGitHubTests.Languages_come_as_a_set_without_bytes</c>.
    /// </para>
    /// <para>
    /// Whoever adds a field here checks first: does it come from GitHub LIKE
    /// THAT, or is it computed? Only the first may join.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_repository_carries_only_copied_fields()
    {
        typeof(Repository).GetProperties()
            .Select(property => property.Name)
            .Should().BeEquivalentTo(
                nameof(Repository.Name),
                nameof(Repository.Description),
                nameof(Repository.Language),
                nameof(Repository.Stars),
                nameof(Repository.Url),
                nameof(Repository.LastPushedAt),
                nameof(Repository.Languages),
                nameof(Repository.Topics));
    }

    /// <summary>
    /// And the connection carries no summary about the human being.
    /// </summary>
    /// <remarks>
    /// <c>LanguagesComplete</c> is the only property here that is not copied
    /// from GitHub — and it may be, because it speaks about <em>us</em> and not
    /// about the person: it says whether our fetch could get the languages for
    /// every repository. That is exactly what ADR-0022 §3 demands, and without
    /// it a shortened set would read like a complete one.
    /// <para>
    /// Whoever adds a property here answers first: does it speak about the
    /// fetch or about the human being? Nothing computed about the human being
    /// may stand here.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_connection_carries_no_summary()
    {
        typeof(Connection).GetProperties()
            .Select(property => property.Name)
            .Should().BeEquivalentTo(
                nameof(Connection.Subject),
                nameof(Connection.Login),
                nameof(Connection.Nonce),
                nameof(Connection.VerifiedAt),
                nameof(Connection.FetchedAt),
                nameof(Connection.Repositories),
                nameof(Connection.LanguagesComplete),
                nameof(Connection.IsVerified));
    }
}
