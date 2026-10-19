using System.Reflection;
using FluentAssertions;
using Noelia.Core.Identity;
using WorkerTransfer.Scout.Application.Approach;
using WorkerTransfer.Scout.Application.Candidates;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Contracts;
using WorkerTransfer.Scout.Domain.Hits;
using WorkerTransfer.Scout.Domain.Searches;

namespace WorkerTransfer.Scout.Tests;

/// <summary>
/// The four conditions of ADR-0036, each as a test.
/// </summary>
/// <remarks>
/// <para>They stand in <em>one</em> file, and that is deliberate: they are the
/// four sentences for whose sake this service could be built at all. Whoever
/// turns one of them red has not broken a test but the justification of the
/// whole service.</para>
///
/// <para>Where possible, the check is made on <em>types</em> instead of
/// behaviour: a behaviour probe answers "does it not do that today?", a type
/// probe demands that whoever changes it writes the set down here. The same
/// movement, but one that stands out.</para>
/// </remarks>
public class ConditionsTests
{
    private static readonly TenantId Company = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    // ---------------------------------------------------------------------
    // CONDITION 1 — NO ORDERING BY FIT
    // ---------------------------------------------------------------------

    /// <summary>Two identical searches give the same order.</summary>
    [Fact]
    public async Task Two_identical_searches_give_the_same_order()
    {
        var (handler, search, ledger, _) = Setup();

        var three = People(3);
        search.Stock.AddRange(three.Select(subject => Profile(subject, ["Python"])));
        Release(ledger, three);

        var first = await handler.Handle(Question(), default);
        var second = await handler.Handle(Question(), default);

        first.Items.Select(hit => hit.Subject).Should().Equal(second.Items.Select(hit => hit.Subject));
    }

    /// <summary>
    /// Whoever names more of the searched words does NOT move up.
    /// </summary>
    /// <remarks>
    /// <para>The actual test of this condition, and the counter-probe lies in
    /// the data: the last of the list fulfils both searched words, the first
    /// only one. An ordering by fit would have to pull it forward — and whoever
    /// builds one in turns exactly this line red.</para>
    ///
    /// <para>The order is profile-service's (<c>updated_at DESC, id DESC</c>):
    /// stable and <em>unrelated to the matter</em>. No rank about anybody follows
    /// from it, and that is the reason to pass it through untouched.</para>
    /// </remarks>
    [Fact]
    public async Task Whoever_fulfils_more_does_not_move_up()
    {
        var (handler, search, ledger, _) = Setup();

        var few = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var many = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

        // `few` stands in front and fulfils ONE of the two searched words;
        // `many` stands behind and fulfils BOTH. An ordering by fit would have to
        // pull `many` forward — and thereby turns this line red.
        search.Stock.Add(Profile(few, ["Python", "Go"]));
        search.Stock.Add(Profile(many, ["Python", "Kubernetes"]));
        Release(ledger, [few, many]);

        var page = await handler.Handle(Question("Python", "Kubernetes"), default);

        page.Items.Select(hit => hit.Subject.Value).Should().Equal(
            [few, many],
            "the order is profile-service's — whoever names both searched words "
            + "does not stand before the one who names one because of it");

        // And the ticks say WHICH one is missing — that is the information a
        // number would hide.
        page.Items[0].Ticks.Should().Equal(
            new Tick("Python", true), new Tick("Kubernetes", false));
        page.Items[1].Ticks.Should().Equal(
            new Tick("Python", true), new Tick("Kubernetes", true));
    }

    /// <summary>The ticks stay in the order of the search too.</summary>
    /// <remarks>
    /// Ordering the ticks "fulfilled first" would be an ordering by fit in the
    /// small — and the beginning of one in the large.
    /// </remarks>
    [Fact]
    public async Task The_ticks_stand_in_the_order_of_the_search()
    {
        var (handler, search, ledger, _) = Setup();

        var subject = People(1)[0];
        search.Stock.Add(Profile(subject, ["Go"]));
        Release(ledger, [subject]);

        var page = await handler.Handle(Question("Rust", "Go"), default);

        page.Items.Should().ContainSingle()
            .Which.Ticks.Select(tick => (tick.Word, tick.Named))
            .Should().Equal(("Rust", false), ("Go", true));
    }

    // ---------------------------------------------------------------------
    // CONDITION 2 — NO NUMBER ABOUT A HUMAN BEING
    // ---------------------------------------------------------------------

    /// <summary>Words that announce a judgement about a human being.</summary>
    /// <remarks>
    /// <para>The list of ADR-0036 condition 2, plus the twins that would be the
    /// same under another name. <c>anzahl</c> and <c>count</c> stand on it, and
    /// that is why the page length in this service is called
    /// <c>PageLength</c> and not <c>Count</c> — a field that is a page size today
    /// is tomorrow the number somebody writes beside a human being.</para>
    ///
    /// <para><strong>Bilingual on purpose.</strong> The code is English since
    /// its translation, and a German-only list would wave an English
    /// <c>RatingTotal</c> through. Each English word stands beside the German one
    /// it translates: <c>point</c> (punkt), <c>count</c> and <c>total</c>
    /// (anzahl), <c>rating</c> and <c>evaluat</c> (bewert), <c>grade</c>
    /// (note). The others were English already.</para>
    /// </remarks>
    private static readonly string[] Forbidden =
    [
        "score", "punkt", "point", "rank", "rang", "weight", "gewicht", "fit", "passung",
        "percent", "prozent", "quote", "anzahl", "count", "total", "bewert", "rating",
        "evaluat", "note", "grade", "level", "aktivitaet", "activity", "match"
    ];

    /// <summary>
    /// No public surface of the domain or of the contracts carries such a name.
    /// </summary>
    /// <remarks>
    /// The twin of <c>Adr0022Tests</c> from github-service, here over the two
    /// assemblies in which a number would arise (domain) and go out
    /// (contracts). Whoever needs one of the words has crossed the line or has to
    /// change this test — and that is a visible commit somebody has to justify.
    /// </remarks>
    [Fact]
    public void Nothing_here_sums_up_a_human_being_in_a_number()
    {
        var surfaces = new[] { typeof(Hit).Assembly, typeof(HitV1).Assembly }
            .SelectMany(assembly => assembly.GetExportedTypes())
            .SelectMany(type => type.GetMembers(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly)
                .Select(member => $"{type.Name}.{member.Name}")
                .Append(type.Name))
            .Distinct()
            .ToList();

        surfaces.Should().NotBeEmpty("otherwise the test checks nothing");

        foreach (var name in surfaces)
        {
            foreach (var word in Forbidden)
            {
                name.Contains(word, StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse($"'{name}' carries '{word}' — ADR-0022, ADR-0036 condition 2");
            }
        }
    }

    /// <summary>The detection itself is right — otherwise the test would be green because it finds nothing.</summary>
    [Theory]
    [InlineData("Tick", false)]
    [InlineData("EvidenceState", false)]
    [InlineData("PageLength", false)]
    [InlineData("Passungsgrad", true)]
    [InlineData("MatchScore", true)]
    [InlineData("Trefferanzahl", true)]
    [InlineData("HitCount", true)]
    [InlineData("TotalHits", true)]
    [InlineData("SkillPoints", true)]
    [InlineData("RatingOfPerson", true)]
    [InlineData("EvaluationText", true)]
    [InlineData("GradeLabel", true)]
    public void The_detection_separates_correctly(string name, bool expected) =>
        Forbidden.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase))
            .Should().Be(expected);

    /// <summary>A hit carries a tick <em>list</em> and no sum.</summary>
    /// <remarks>
    /// The closed field set, not a forbidden list: whoever adds a field writes it
    /// here — and answers along the way whether it is copied or computed. Only the
    /// first may join.
    /// </remarks>
    [Fact]
    public void A_hit_carries_exactly_these_fields()
    {
        Fields(typeof(HitV1)).Should().BeEquivalentTo(
            "SubjectId", "Headline", "Bio", "Location", "RemoteOk",
            "Named", "Checks", "Evidence", "EvidenceState",
            // ADR-0041: three words, no number. `Reach` is the state, the other
            // two are the STATEMENTS it arose from.
            "Reach", "ReachCommute", "ReachAttendance");

        Fields(typeof(TickV1)).Should().BeEquivalentTo("Word", "Named");
    }

    /// <summary>And a page carries no total.</summary>
    /// <remarks>
    /// ADR-0026: through the difference to the page length it would reveal how
    /// many profiles are <em>not</em> released. That is exactly the information
    /// the ledger protects.
    /// </remarks>
    [Fact]
    public void A_page_carries_no_total()
    {
        Fields(typeof(HitPageV1)).Should().BeEquivalentTo("Items", "Next");
        Fields(typeof(HitPage)).Should().BeEquivalentTo("Items", "Next");
    }

    // ---------------------------------------------------------------------
    // CONDITION 3 — ONLY WHAT WAS NAMED IS SEARCHABLE
    // ---------------------------------------------------------------------

    /// <summary>A word that is only EVIDENCED finds nobody.</summary>
    /// <remarks>
    /// <para>The condition in one sentence: "kubernetes" stands as a GitHub topic
    /// on a repository of this person and in no profile. A search for it does
    /// not find them — a topic is a statement about an <em>artefact</em>, and
    /// whoever searched by it would silently turn it into one about the human
    /// being (ADR-0033).</para>
    ///
    /// <para>The counter-probe stands in the same test: the same person can very
    /// well be found through their <em>named</em> "Go", and the evidence then
    /// shows up on the hit. Without this second half the test would also be
    /// green if the search found nothing at all.</para>
    /// </remarks>
    [Fact]
    public async Task A_word_that_is_only_evidenced_finds_nobody()
    {
        var (handler, search, ledger, evidence) = Setup();

        var subject = People(1)[0];
        search.Stock.Add(Profile(subject, ["Go"]));
        Release(ledger, [subject]);
        evidence.Stock[subject] = new EvidenceSheet(
            [new Evidence("kubernetes", EvidenceKind.Topic, "infra", "https://example.test/infra")],
            EvidenceState.Complete);

        var byTheEvidence = await handler.Handle(Question("Kubernetes"), default);

        byTheEvidence.Items.Should().BeEmpty(
            "evidence is a statement about an artefact and makes nobody findable");

        var byWhatWasNamed = await handler.Handle(Question("Go"), default);

        byWhatWasNamed.Items.Should().ContainSingle()
            .Which.Evidence.Should().ContainSingle().Which.Word.Should().Be(
                "kubernetes", "FETCHED TO the hit it very well is");
    }

    /// <summary>The evidence is fetched only AFTER the release.</summary>
    /// <remarks>
    /// The order is the promise: whoever has not released is not looked up
    /// either. An evidence fetch for a hidden person would be a call that must
    /// not exist — and it would stand in the other service's log.
    /// </remarks>
    [Fact]
    public async Task Nothing_is_looked_up_about_whoever_released_nothing()
    {
        var (handler, search, ledger, evidence) = Setup();

        var visible = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
        var hidden = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

        search.Stock.Add(Profile(visible, ["Go"]));
        search.Stock.Add(Profile(hidden, ["Go"]));
        Release(ledger, [visible]);

        await handler.Handle(Question("Go"), default);

        evidence.Asked.Should().Equal(visible);
    }

    /// <summary>A silent github-service is asked once per page, not once per hit.</summary>
    /// <remarks>
    /// The fetches run one after the other on purpose, and each has its own time
    /// limit. Measured 02.10.2026: with github-service slow, fourteen hits took
    /// 30.9 s and the company looked at "Profile werden geladen…" all the while.
    /// After the first "unavailable" the rest of the page says the same — the
    /// honest state, "we do not know right now" — without waiting for it again.
    /// </remarks>
    [Fact]
    public async Task A_silent_evidence_source_is_asked_once_per_page()
    {
        var (handler, search, ledger, evidence) = Setup();

        var people = People(3);
        foreach (var subject in people)
        {
            search.Stock.Add(Profile(subject, ["Go"]));
            evidence.Stock[subject] = new EvidenceSheet([], EvidenceState.Unavailable);
        }

        Release(ledger, people);

        var page = await handler.Handle(Question("Go"), default);

        evidence.Asked.Should().ContainSingle("after the first silence nobody waits again");
        page.Items.Should().HaveCount(3)
            .And.OnlyContain(hit => hit.EvidenceState == EvidenceState.Unavailable);
    }

    /// <summary>The counter-probe: an answering source is asked for every hit.</summary>
    [Fact]
    public async Task An_answering_evidence_source_is_asked_for_every_hit()
    {
        var (handler, search, ledger, evidence) = Setup();

        var people = People(3);
        foreach (var subject in people)
        {
            search.Stock.Add(Profile(subject, ["Go"]));
        }

        Release(ledger, people);

        await handler.Handle(Question("Go"), default);

        evidence.Asked.Should().HaveCount(3);
    }

    /// <summary>The filter that goes out carries only named words.</summary>
    /// <remarks>
    /// On the type and not on the behaviour: <see cref="SearchFilter"/> has no
    /// field for a piece of evidence, so there is no way to send one along.
    /// Whoever adds one turns this line red.
    /// </remarks>
    [Fact]
    public void A_search_filter_carries_exactly_these_fields() =>
        Fields(typeof(SearchFilter)).Should().BeEquivalentTo(
            "NamedWords", "Location", "RemoteOnly");

    // ---------------------------------------------------------------------
    // CONDITION 4 — THE APPROACH IS A DRAFT
    // ---------------------------------------------------------------------

    /// <summary>The approach context carries four fields — and none of them is evidence.</summary>
    /// <remarks>
    /// <para>No name, no email address, no <c>SubjectId</c>, no employer, no
    /// resume, no market status — and <strong>no name of the searching
    /// company</strong>: a company name in the prompt would be the invitation to
    /// let the model say something about the employer that nobody has checked
    /// (ADR-0024).</para>
    ///
    /// <para>And no evidence. It is visible but not <em>named</em>; writing it
    /// into an approach would mean attributing it to the person — the step
    /// ADR-0033 reserves for the person themselves.</para>
    /// </remarks>
    [Fact]
    public void The_approach_context_carries_exactly_four_fields() =>
        Fields(typeof(ApproachSituation)).Should().BeEquivalentTo(
            ["Headline", "Named", "Searched", "Wish", "Prompt"],
            "ADR-0036 condition 4 names this set, and whoever widens it "
            + "sends something new about a person out");

    /// <summary>What really goes out carries no identifier and no address.</summary>
    [Fact]
    public async Task The_prompt_carries_nothing_about_the_person_out()
    {
        var (_, search, ledger, _) = Setup();
        var drafter = new ProbeDrafter();
        var handler = new ApproachHandler(ledger, search, drafter);

        var subject = People(1)[0];
        search.Stock.Add(Profile(subject, ["Go"]));
        Release(ledger, [subject]);

        await handler.Handle(
            new ApproachQuery(Company, new SubjectId(subject), ["Go"], "shorter"), default);

        var prompt = drafter.Last!.Prompt;

        prompt.Should().NotContain(subject.ToString());
        prompt.Should().NotContain(Company.Value.ToString());
        prompt.Should().NotContain("@");
        prompt.Should().Contain("Go");
    }

    /// <summary>Words that look like a way of writing to somebody.</summary>
    /// <remarks>
    /// <para>Bilingual on purpose: the code is English since its translation, and
    /// a German-only list would wave an English <c>ApproachSender</c> or
    /// <c>SendApproachCommand</c> through. <c>send</c> covers <c>sender</c> and
    /// <c>senden</c>, <c>mail</c> covers <c>mailer</c>; <c>postman</c> stands
    /// beside <c>postbote</c>, <c>dispatch</c> and <c>deliver</c> beside
    /// <c>versand</c>.</para>
    ///
    /// <para>No exceptions: the counter-probe ran against the translated
    /// application assembly and found no legitimate name carrying one of these
    /// words. The outbox's <c>IDelivery</c> would carry <c>deliver</c> — it lives
    /// in <c>WorkerTransfer.Outbox</c>, not here, and its only implementation in
    /// this service delivers "your profile was discovered", never an
    /// approach.</para>
    /// </remarks>
    private static readonly string[] SendingWords =
    [
        "postbote", "postman", "versand", "dispatch", "deliver", "mail", "smtp",
        "senden", "sender", "send"
    ];

    /// <summary>This service has no way of writing to anybody.</summary>
    /// <remarks>
    /// <para>The test that really carries the fourth condition — and it checks
    /// an <em>absence</em>. The application layer knows exactly one port to the
    /// outside through which something would go to a human being: the outbox
    /// with "your profile was discovered". There is no postman, no mail address,
    /// no dispatch port and no command that would send an approach off.</para>
    ///
    /// <para>Checked on the type names and not on behaviour: a behaviour test
    /// answers "does it not happen today?", this one demands that whoever builds
    /// in a dispatch path writes it down here.</para>
    /// </remarks>
    [Fact]
    public void There_is_no_way_of_writing_to_anybody()
    {
        var names = typeof(ApproachQuery).Assembly
            .GetExportedTypes()
            .SelectMany(type => type.GetMembers(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly)
                .Select(member => $"{type.Name}.{member.Name}")
                .Append(type.Name))
            .Distinct()
            .ToArray();

        names.Should().NotBeEmpty("otherwise the test checks nothing");

        foreach (var name in names)
        {
            foreach (var word in SendingWords)
            {
                name.Contains(word, StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse(
                        $"'{name}' looks like a dispatch path — the service "
                        + "drafts, it writes to nobody (ADR-0036 condition 4)");
            }
        }
    }

    /// <summary>The detection of a dispatch path is right in both languages.</summary>
    [Theory]
    [InlineData("ApproachSituation", false)]
    [InlineData("ReportDiscoveryCommand", false)]
    [InlineData("Versandweg", true)]
    [InlineData("Postbote", true)]
    [InlineData("ApproachSender", true)]
    [InlineData("SendApproachCommand", true)]
    [InlineData("IMailer", true)]
    [InlineData("DispatchApproach", true)]
    [InlineData("DeliverDraft", true)]
    public void The_dispatch_detection_separates_correctly(string name, bool expected) =>
        SendingWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase))
            .Should().Be(expected);

    /// <summary>The draft carries only the text — no <c>sent_at</c>, no recipient.</summary>
    [Fact]
    public void A_draft_carries_only_the_text() =>
        Fields(typeof(ApproachDraftV1)).Should().BeEquivalentTo("Draft");

    // ---------------------------------------------------------------------

    /// <summary>The public instance properties of a type.</summary>
    /// <remarks>
    /// Static ones stay out: <c>Rules</c> is the system prompt and no field
    /// anybody fills. <c>Prompt</c> counts, by contrast, although it is derived —
    /// it is the string that really goes out.
    /// </remarks>
    private static string[] Fields(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name)];

    private static (CandidatesHandler, ProbeSearch, ProbeLedger, ProbeEvidence) Setup()
    {
        var search = new ProbeSearch();
        var ledger = new ProbeLedger();
        var evidence = new ProbeEvidence();

        return (new CandidatesHandler(search, ledger, evidence, new ProbeJobs()), search, ledger, evidence);
    }

    private static CandidatesQuery Question(params string[] words) =>
        new(Company, SearchFilter.From(words, null, false), 20, null);

    private static Guid[] People(int howMany) =>
        [.. Enumerable.Range(1, howMany).Select(
            number => Guid.Parse($"cccccccc-0000-0000-0000-{number:D12}"))];

    private static ProfileHit Profile(Guid subject, string[] named) =>
        new(new SubjectId(subject), "Developer", "", "Berlin", true, named);

    /// <summary>The hit's field set grows by the tick — deliberately.</summary>
    /// <remarks>
    /// It already stands once further up; this line here is the reason why it
    /// stands twice: whoever adds a field writes it down. Three fields came with
    /// ADR-0041, and none of them is a number.
    /// </remarks>
    [Fact]
    public void The_availability_tick_carries_no_kilometre_figure()
    {
        var fields = Fields(typeof(Availability));

        fields.Should().BeEquivalentTo(
            ["State", "Commute", "Attendance"],
            "the distance is computed and reduced at once to a state — "
            + "it belongs in no field (ADR-0041)");

        fields.Should().NotContain(field =>
            field.Contains("Km", StringComparison.OrdinalIgnoreCase)
            || field.Contains("Distance", StringComparison.OrdinalIgnoreCase)
            || field.Contains("Entfernung", StringComparison.OrdinalIgnoreCase)
            || field.Contains("Distanz", StringComparison.OrdinalIgnoreCase));
    }

    private static void Release(ProbeLedger ledger, IEnumerable<Guid> subjects)
    {
        foreach (var subject in subjects)
        {
            ledger.Released.Add((subject, Company.Value));
        }
    }
}
