using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Noelia.Abstractions.Compliance;
using Noelia.Abstractions.Security.Checks;
using WorkerTransfer.ServiceDefaults.Checks;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>Evidence, not compliance — and as a test.</summary>
/// <remarks>
/// <para><strong>The word list is a real risk, not a theoretical one.</strong>
/// Under Art. 42/43 GDPR only a supervisory authority or a body accredited to
/// EN ISO/IEC 17065 may certify; BSI C5 is attested only by an approved
/// assessor, SecNumCloud qualified only by ANSSI. We are none of those.
/// "Certified" without accreditation is a misleading commercial practice in the
/// EU — Directive 2005/29/EC towards consumers, Directive 2006/114/EC between
/// undertakings.</para>
///
/// <para><strong>Hence a test and not a review.</strong> A sentence somebody
/// writes into a summary two months from now does not get reviewed — it gets
/// written and published. Vanta, Drata and Secureframe write everywhere for the
/// same reason that SOC 2 is an <em>attestation</em> and not a
/// <em>certification</em>; following them is the safe road, not the timid one.</para>
/// </remarks>
public class RegulatoryReferenceTests
{
    /// <summary>Words that make a promise nobody here may give.</summary>
    /// <remarks>
    /// The values stay German because the documents this guards are German.
    /// <c>attest</c> is on the list: in German an "Attest" is every bit as much
    /// a certificate as a Certificate is, so the English escape the American
    /// vendors take does not work here.
    /// </remarks>
    public static IReadOnlyList<string> Forbidden { get; } =
    [
        "zertifi", "zertifikat", "konform", "compliant", "certif",
        "bescheinig", "attest", "auditiert", "geprüft durch", "erfüllt art",
        "dsgvo-konform"
    ];

    /// <summary>No citation settles its own article.</summary>
    /// <remarks>
    /// <strong>The test the commission asks for by name.</strong> A
    /// <c>Reader</c> left empty would be the claim that nothing remains to be
    /// decided after this finding — exactly the presumption the whole
    /// vocabulary exists against.
    /// </remarks>
    [Fact]
    public void No_citation_has_an_empty_reader()
    {
        AdditionalRegulatoryReferences.Own.Should().NotBeEmpty("otherwise this test checks nothing");

        foreach (var reference in AdditionalRegulatoryReferences.Own)
        {
            reference.Reader.Should().NotBeNullOrWhiteSpace(
                $"{reference.Citation} has to say what a human still decides "
                + "afterwards");

            // Not merely "not empty": a full stop would pass the first
            // assertion and say nothing. The shortest real sentence in the
            // collection runs well past forty characters.
            reference.Reader.Length.Should().BeGreaterThan(
                40, $"{reference.Citation}: half a sentence is not a question for a human");
        }
    }

    /// <summary>And none of them says we fulfil anything.</summary>
    [Fact]
    public void No_citation_claims_compliance()
    {
        foreach (var reference in AdditionalRegulatoryReferences.Own)
        {
            foreach (var word in Forbidden)
            {
                $"{reference.Obligation} {reference.Reader}".Contains(
                        word, StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse(
                        $"{reference.Citation} carries „{word}“ — evidence, not "
                        + "compliance");
            }
        }
    }

    /// <summary>The detection itself is right.</summary>
    /// <remarks>
    /// Otherwise the test above would be green because it searches for nothing
    /// — the same question every blocklist has to answer.
    /// </remarks>
    [Theory]
    [InlineData("Eine Tatsache, nach der Art. 30 fragt.", false)]
    [InlineData("Beleg für Art. 30 Abs. 1.", false)]
    [InlineData("Dieses System ist DSGVO-konform.", true)]
    [InlineData("Hiermit zertifiziert.", true)]
    [InlineData("Wir bescheinigen die Einhaltung.", true)]
    [InlineData("Geprüft durch eine unabhängige Stelle.", true)]
    public void The_word_list_matches_exactly_what_is_meant(string sentence, bool expected) =>
        Forbidden.Any(word => sentence.Contains(word, StringComparison.OrdinalIgnoreCase))
            .Should().Be(expected);

    /// <summary>Our five are exactly the ones Noelia does not have.</summary>
    /// <remarks>
    /// <strong>Two versions of the same article would be worse than one</strong>
    /// — the reader could then no longer say whether two findings are about one
    /// obligation or about two. This test pins that we rebuild nothing the
    /// package already ships.
    /// </remarks>
    [Fact]
    public void No_citation_of_ours_already_exists_in_Noelia()
    {
        var fromNoelia = new[]
        {
            RegulatoryReferences.GdprRecordsOfProcessing,
            RegulatoryReferences.GdprThirdCountryTransfer,
            RegulatoryReferences.GdprProcessorContract,
            RegulatoryReferences.AiActRecordKeeping,
            RegulatoryReferences.AiActDeployerDuties,
            RegulatoryReferences.AiActTransparency
        }.Select(reference => reference.Citation).ToHashSet(StringComparer.Ordinal);

        AdditionalRegulatoryReferences.Own.Should().NotBeEmpty("otherwise this test checks nothing");

        AdditionalRegulatoryReferences.Own.Select(reference => reference.Citation)
            .Should().NotIntersectWith(fromNoelia);
    }

    /// <summary>
    /// § 87 Abs. 1 Nr. 6 BetrVG is not a citation, because it cannot be one.
    /// </summary>
    /// <remarks>
    /// <strong>The gap stands visible rather than concealed.</strong>
    /// <c>RegulatoryRegime</c> knows <c>Gdpr</c>, <c>AiAct</c>, <c>Nis2</c> and
    /// <c>Dora</c> — no national labour law. Filed as
    /// <c>bugs/betrvg-hat-kein-regelwerk.md</c>; this test will stay green until
    /// a regime for it exists, and then it falls and demands that the constant
    /// become a citation.
    /// </remarks>
    [Fact]
    public void Codetermination_is_a_sentence_and_not_a_wrong_citation()
    {
        AdditionalRegulatoryReferences.WorksCouncilCodetermination.Should().Contain("§ 87 Abs. 1 Nr. 6 BetrVG");

        Enum.GetNames<RegulatoryRegime>().Should().NotContain(
            name => name.Contains("Betr", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("National", StringComparison.OrdinalIgnoreCase),
            "as soon as a regime for national labour law exists, codetermination "
            + "belongs there as a RegulatoryReference — and this test falls and "
            + "says so");
    }

    /// <summary>Annex III classifies nothing, and says so.</summary>
    /// <remarks>
    /// <strong>Never write that WorkerTransfer is not high-risk.</strong> Write
    /// which evidence exists and who answers the question. This test pins the
    /// second half.
    /// </remarks>
    [Fact]
    public void Annex_three_hands_the_question_to_a_human()
    {
        AdditionalRegulatoryReferences.AnnexThree.Reader.Should()
            .Contain("Mensch").And.Contain("juristischer Ausbildung");

        AdditionalRegulatoryReferences.AnnexThree.Reader.Should().NotContain(
            "nicht hochriskant",
            "the classification is not ours — the evidence report lays the "
            + "findings beside the question and classifies nothing");
    }

    /// <summary>Every citation is backed by at least one check.</summary>
    /// <remarks>
    /// <strong>A citation without a finding is legal advice.</strong> The
    /// collection exists so that two checks name the same article the same way
    /// — not to keep a list of obligations this system can say nothing about.
    /// Whoever adds an article without a check backing it builds exactly that.
    /// </remarks>
    [Fact]
    public void No_citation_stands_without_a_check()
    {
        var backed = Checks()
            .SelectMany(check => check.References)
            .Select(reference => reference.Citation)
            .ToHashSet(StringComparer.Ordinal);

        backed.Should().NotBeEmpty("otherwise this test checks nothing");

        AdditionalRegulatoryReferences.Own.Select(reference => reference.Citation)
            .Should().OnlyContain(citation => backed.Contains(citation));
    }

    /// <summary>And every check names only citations from the collection.</summary>
    /// <remarks>
    /// The other direction, and the more important one: a citation a check
    /// writes for itself is the second version of the same article — and then
    /// the reader can no longer say whether two findings are about one
    /// obligation or about two.
    /// </remarks>
    [Fact]
    public void No_check_invents_a_citation_of_its_own()
    {
        // BOTH are allowed: what Noelia ships and what we add. The point of
        // this test is a different one — that nobody invents an article BESIDE
        // either collection, because then two versions of the same citation
        // would stand in the same document.
        var collection = AdditionalRegulatoryReferences.Own
            .Concat(
            [
                RegulatoryReferences.GdprRecordsOfProcessing,
                RegulatoryReferences.GdprThirdCountryTransfer,
                RegulatoryReferences.GdprProcessorContract,
                RegulatoryReferences.GdprIntegrityOfProcessing,
                RegulatoryReferences.AiActRecordKeeping,
                RegulatoryReferences.AiActDeployerDuties,
                RegulatoryReferences.AiActTransparency,
                RegulatoryReferences.Nis2SupplyChain,
                RegulatoryReferences.DoraAssetIdentification,
                RegulatoryReferences.DoraThirdPartyRegister
            ])
            .ToHashSet();

        foreach (var check in Checks())
        {
            check.References.Should().OnlyContain(reference => collection.Contains(reference),
                $"{check.Id} names a citation that is not in AdditionalRegulatoryReferences.Own");
        }
    }

    /// <summary>Every check carries its own citations.</summary>
    /// <remarks>
    /// <strong>This replaces two tests that checked our own runner.</strong>
    /// Until ADR-0045 <c>Nachweislauf</c> attached a check's references to the
    /// finding when the check did not do it itself — and two tests pinned that.
    /// Noelia's <c>SecurityCheckResult</c> carries <c>References</c> directly,
    /// so every check attaches them itself, and that is what is checked.
    /// </remarks>
    [Fact]
    public async Task Every_finding_carries_its_checks_citations()
    {
        foreach (var check in Checks())
        {
            var finding = await check.RunAsync();

            finding.References.Should().BeEquivalentTo(
                check.References,
                $"{check.Id} has to attach its citations to the finding — otherwise "
                + "the obligations page stands there without them");
        }
    }

    /// <summary>Every check in this tree that carries citations.</summary>
    /// <remarks>
    /// Built without a container: <c>RevocationCheck</c> and <c>LedgerCheck</c>
    /// need one, and their citations are the same as the others'.
    /// </remarks>
    private static IReadOnlyList<ISecurityCheck> Checks() =>
    [
        new SeamCheck(typeof(object), [], "probe"),
        new NumberCheck([]),
        new ProviderCheck([]),
        ErasureCheck.WithoutSubjectRows("probe"),
        // Without a registered gate it reports NotApplicable — it carries its
        // citations anyway, and those were exactly the ones this list missed.
        new RevocationCheck<object>(
            new ServiceCollection().BuildServiceProvider(), typeof(object))
    ];
}
