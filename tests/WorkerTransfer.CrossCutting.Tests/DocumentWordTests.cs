using System.Text.RegularExpressions;
using FluentAssertions;

namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>
/// The words in the three documents — checked without a running stack.
/// </summary>
/// <remarks>
/// <para><strong>Why a .NET test over a Python file.</strong> The three scope
/// sentences and the disclaimer are the sentences that stand in a
/// <em>signed</em> document and that somebody passes around. They live in the
/// generator, and that is a script — but they have to be checked <em>before</em>
/// a stack runs, because otherwise nobody checks them until somebody calls
/// <c>make nachweis</c>. One source, two readers: the script uses the
/// constants, this test reads them.</para>
///
/// <para><strong>And why not only against the generated document.</strong>
/// <c>scripts/nachweis_worte.py</c> checks what actually came out, including
/// the reservations derived from the reading — that is the sharper half and it
/// needs the stack. This one is the half that always runs. Two halves, because
/// either one alone leaves a gap.</para>
///
/// <para><strong>The disclaimer is checked the other way round.</strong> It
/// <em>must</em> contain "zertifiziert" and "Bescheinigung", because it denies
/// them. Measured: the word search hit exactly on it during the first run. So
/// what is pinned here is that the denial is present — a disclaimer somebody
/// takes it out of stands out that way.</para>
/// </remarks>
public class DocumentWordTests
{
    /// <summary>Words that make a promise nobody here may give.</summary>
    /// <remarks>
    /// The German values stay: the documents they guard are German. The English
    /// ones stand beside them because a generator that one day writes an English
    /// sentence would otherwise slip past a list that only knows German.
    /// </remarks>
    private static readonly string[] Forbidden =
    [
        "zertifi", "konform", "compliant", "certif", "bescheinig",
        "attest", "auditiert", "erfuellt art", "erfüllt art",
        "conform", "compliance", "complies", "audited", "fulfils art", "fulfills art"
    ];

    /// <summary>No scope sentence claims compliance.</summary>
    [Fact]
    public void No_scope_sentence_claims_compliance()
    {
        var sentences = ScopeSentences();

        sentences.Should().HaveCount(3, "three documents, three readers");

        foreach (var (document, sentence) in sentences)
        {
            foreach (var word in Forbidden)
            {
                sentence.Contains(word, StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse(
                        $"the scope sentence of „{document}“ carries „{word}“ — "
                        + "evidence, not compliance");
            }
        }
    }

    /// <summary>Every scope sentence says what it does NOT contain.</summary>
    /// <remarks>
    /// <strong>That is the half which makes it honest.</strong> A scope that
    /// only says what is inside gets read as if everything else were fine — and
    /// a document about an AI deployment that does not say it knows nothing
    /// about the content of the prompts promises exactly that.
    /// </remarks>
    [Fact]
    public void Every_scope_sentence_also_says_what_it_does_not_contain()
    {
        foreach (var (document, sentence) in ScopeSentences())
        {
            sentence.Should().Contain(
                "NICHT enthalten",
                $"the scope sentence of „{document}“ has to name its own limit");
        }
    }

    /// <summary>The disclaimer denies, and the denial is present.</summary>
    /// <remarks>
    /// Three statements, and every one of them is missing from the brochures
    /// this document stands against: machine-generated, no certificate, assessed
    /// by no accredited body.
    /// </remarks>
    [Fact]
    public void The_disclaimer_says_it_is_not_a_certificate()
    {
        var disclaimer = Disclaimer();

        disclaimer.Should().Contain("maschinell erzeugte technische Evidenz");
        disclaimer.Should().Contain("KEIN Zertifikat");
        disclaimer.Should().Contain("akkreditierte Stelle");
        disclaimer.Should().Contain("Art. 42/43 DSGVO");
        disclaimer.Should().Contain("EN ISO/IEC 17065");

        disclaimer.Should().Contain(
            "keine solche Stelle hat dieses Dokument",
            "without that clause, naming the accredited body reads as a hint "
            + "that one was involved");
    }

    /// <summary>The disclaimer hands on the question of adequacy.</summary>
    [Fact]
    public void The_disclaimer_hands_the_question_to_a_human()
    {
        Disclaimer().Should().Contain("entscheidet ein Mensch");

        Disclaimer().Should().NotContain(
            "nicht hochriskant",
            "the classification is not ours — the document lays the evidence "
            + "beside the question and classifies nothing");
    }

    /// <summary>The detection itself is right.</summary>
    [Theory]
    [InlineData("Eine Tatsache, nach der Art. 30 fragt.", false)]
    [InlineData("NICHT enthalten: eine Einstufung.", false)]
    [InlineData("Dieses System ist DSGVO-konform.", true)]
    [InlineData("Hiermit zertifiziert.", true)]
    [InlineData("This system conforms to the GDPR.", true)]
    [InlineData("Fulfils Art. 30 GDPR.", true)]
    public void The_word_list_matches_exactly_what_is_meant(string sentence, bool expected) =>
        Forbidden.Any(word => sentence.Contains(word, StringComparison.OrdinalIgnoreCase))
            .Should().Be(expected);

    // ----------------------------------------------------------- the plumbing

    /// <summary>The three scope sentences, read out of the generator.</summary>
    private static IReadOnlyList<(string Document, string Sentence)> ScopeSentences()
    {
        var source = Source();

        return
        [
            .. Regex.Matches(
                    source,
                    // Ends at the scope sentence without naming the key beside
                    // it: a pattern that knows its neighbourhood silently drops
                    // to zero hits when that neighbour is renamed.
                    "\"(?<name>datenschutz|ki|mitbestimmung)\":\\s*\\{.*?"
                    + "\"scope\":\\s*\\((?<sentence>.*?)\\),\\n",
                    RegexOptions.Singleline,
                    TimeSpan.FromSeconds(5))
                .Select(hit => (
                    hit.Groups["name"].Value,
                    Joined(hit.Groups["sentence"].Value)))
        ];
    }

    /// <summary>The fixed disclaimer sentence, read out of the generator.</summary>
    private static string Disclaimer()
    {
        var hit = Regex.Match(
            Source(),
            "VORBEHALT = \\((?<sentence>.*?)\\)\\n",
            RegexOptions.Singleline,
            TimeSpan.FromSeconds(5));

        hit.Success.Should().BeTrue(
            "without the disclaimer this suite checks nothing — and is then "
            + "green over a document that may no longer carry it at all");

        return Joined(hit.Groups["sentence"].Value);
    }

    /// <summary>The strung-together Python literals become one sentence.</summary>
    private static string Joined(string raw) =>
        string.Concat(
            Regex.Matches(raw, "\"(?<part>[^\"]*)\"", RegexOptions.None,
                    TimeSpan.FromSeconds(5))
                .Select(part => part.Groups["part"].Value));

    /// <summary>The generator of the documents.</summary>
    /// <remarks>
    /// The search runs upwards from the test directory, because a suite's
    /// working directory is its output directory and not the root of the tree.
    /// </remarks>
    private static string Source()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var file = Path.Combine(
                directory.FullName, "scripts", "nachweis_dokumente.py");

            if (File.Exists(file))
            {
                return File.ReadAllText(file);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "scripts/nachweis_dokumente.py not found — without it this suite "
            + "checks nothing.");
    }
}
