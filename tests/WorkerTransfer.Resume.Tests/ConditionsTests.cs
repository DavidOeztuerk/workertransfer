using System.Reflection;
using FluentAssertions;
using WorkerTransfer.Resume.Contracts;
using WorkerTransfer.Resume.Domain.Documents;
using WorkerTransfer.Resume.Infrastructure.Persistence;

namespace WorkerTransfer.Resume.Tests;

/// <summary>
/// The conditions under which an index over personal texts may exist here at
/// all (PBI-7, ADR-0043).
/// </summary>
/// <remarks>
/// <para>They stand in <em>one</em> file because together they are the
/// justification: an index over human beings' references is the one place where
/// this system allows such a thing, and it hangs on three sentences. Whoever
/// turns one of them red has not broken a test but the permission.</para>
///
/// <para>Checked on <em>types</em> instead of behaviour where possible: a
/// behaviour probe answers "does it not do that today?", a type probe demands
/// that whoever changes it writes the set down here. The same movement, but one
/// that stands out.</para>
/// </remarks>
public class ConditionsTests
{
    // ---------------------------------------------------------------------
    // CONDITION 1 — IT IS NOT SEARCHABLE
    // ---------------------------------------------------------------------

    /// <summary>
    /// <strong>The index knows no question about the word.</strong>
    /// </summary>
    /// <remarks>
    /// "Which human beings carry 'Schweißfachmann' in their documents" is exactly
    /// the search this system does not have and will not get: searchable is only
    /// what somebody typed into their profile themselves and <em>saved</em>
    /// (ADR-0033). The only question allowed is about one's own documents.
    /// <para>
    /// A set comparison and not a containment check — a fourth method then
    /// stands out instead of riding along. The same movement as in
    /// <c>TokenShapeTests</c>, where a test forbade two names and never noticed
    /// a ninth claim.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_index_knows_no_question_about_the_word() =>
        typeof(ITermStore).GetMethods().Select(member => member.Name)
            .Should().BeEquivalentTo("AllAsync", "SaveAsync", "DeleteAsync");

    /// <summary>And neither does the store behind it.</summary>
    /// <remarks>
    /// The interface above could be bypassed by somebody hanging a query
    /// directly on the implementation — public, but past the port.
    /// </remarks>
    [Fact]
    public void The_store_knows_no_third_question_either() =>
        typeof(EfTermStore).GetMethods(BindingFlags.Public | BindingFlags.Instance
                | BindingFlags.DeclaredOnly)
            .Select(member => member.Name)
            .Should().BeEquivalentTo("AllAsync", "SaveAsync", "DeleteAsync");

    // ---------------------------------------------------------------------
    // CONDITION 2 — IT HOLDS NAMES, NEVER THE WORDING
    // ---------------------------------------------------------------------

    /// <summary>
    /// <strong>The row holds no text from the reference.</strong>
    /// </summary>
    /// <remarks>
    /// The full text lives in the memory of the one call that read it. A column
    /// with the wording would be a second copy of the document — this time in the
    /// database, and therefore in every backup and in every dump somebody pulls
    /// for debugging (ADR-0035).
    /// <para>
    /// The field set written out and not a list of forbidden names: whoever adds
    /// a field writes it down here and answers the question whether it describes
    /// the human being or the document.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_index_row_holds_exactly_these_fields() =>
        typeof(TermRow).GetProperties().Select(field => field.Name)
            .Should().BeEquivalentTo(
                "Id", "SubjectId", "DocumentId", "HasText", "Terms", "ReadAt");

    /// <summary>And the same set goes out, with one field added.</summary>
    /// <remarks>
    /// The name of the document travels along, because the interface must be
    /// able to say from <em>which</em> file there was nothing to read. "Nothing
    /// found" without the file would be the silent claim that nothing stands in
    /// it (ADR-0022 §3).
    /// </remarks>
    [Fact]
    public void The_contract_carries_exactly_these_fields() =>
        typeof(DocumentTermsV1).GetProperties().Select(field => field.Name)
            .Should().BeEquivalentTo("DocumentId", "Name", "ReadAt", "HasText", "Terms");

    // ---------------------------------------------------------------------
    // CONDITION 3 — NO NUMBER ABOUT A HUMAN BEING
    // ---------------------------------------------------------------------

    /// <summary>
    /// Nothing is computed from a term.
    /// </summary>
    /// <remarks>
    /// The obvious temptation here is called "how well does this reference fit"
    /// — three words found out of five searched, and there stands the number
    /// ADR-0022 deleted. A twin of <c>Adr0022Tests</c>, over the types of this
    /// piece of work. The German words stay in the list: they are the German
    /// names such a field would get.
    /// </remarks>
    [Theory]
    [InlineData("score")]
    [InlineData("rank")]
    [InlineData("weight")]
    [InlineData("percent")]
    [InlineData("passung")]
    [InlineData("gewicht")]
    [InlineData("niveau")]
    [InlineData("anzahl")]
    public void No_field_computes_about_a_human_being(string word)
    {
        string[] names =
        [
            .. Fields(typeof(DocumentTerms)),
            .. Fields(typeof(TermRow)),
            .. Fields(typeof(DocumentTermsV1))
        ];

        names.Should().NotContain(
            name => name.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------------
    // CONDITION 4 — THE RECOGNISER CALLS NOBODY
    // ---------------------------------------------------------------------

    /// <summary>
    /// <strong>Exactly two doors lead outside — and the text recogniser is not
    /// one of them.</strong>
    /// </summary>
    /// <remarks>
    /// <para>The ledger and notification-service; both stand in
    /// <c>docker-compose.yml</c>, because the egress boundary derives its allowed
    /// hosts from the configuration.</para>
    ///
    /// <para>A text recogniser on the network would be the third — and it is the
    /// door that is most expensive to get wrong: through it a reference with
    /// employer, duration and grade would go out so that a stranger could look
    /// for words in it. A third type with an <c>IHttpClientFactory</c> is the
    /// first suspicion, and this test forces it to be written down. If its
    /// destination then did not stand in the configuration, the boundary would
    /// refuse it without logging a line, and the button would simply do nothing
    /// — that is what the cover-letter agent died of on 10.09.2026.</para>
    /// </remarks>
    [Fact]
    public void Exactly_two_doors_lead_outside() =>
        typeof(ResumeDbContext).Assembly.GetExportedTypes()
            .Where(type => type.GetConstructors().Any(constructor => constructor.GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(IHttpClientFactory))))
            .Select(type => type.Name)
            .Should().BeEquivalentTo("HttpConsentGate", "HttpNotifier");

    private static IEnumerable<string> Fields(Type type) =>
        type.GetProperties().Select(field => field.Name);
}
