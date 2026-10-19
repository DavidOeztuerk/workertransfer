using System.Net;
using FluentAssertions;
using Noelia.Core.Identity;
using Noelia.Infrastructure.Sovereignty;
using WorkerTransfer.Jobs.Domain.Jobs;
using WorkerTransfer.Jobs.Domain.Sources;
using WorkerTransfer.Jobs.Infrastructure.Sources;

namespace WorkerTransfer.Jobs.Tests;

/// <summary>
/// The rules a source keeps before anything is fetched (ADR-0052) — no
/// database, no network.
/// </summary>
public sealed class SourceRuleTests
{
    private static readonly TenantId Acme = new(Guid.CreateVersion7());
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    private static ExternalAdvert Advert(string title, string description = "We weld.") =>
        new("1", title, description, "Berlin", RemoteMode.None, EmploymentKind.FullTime, "https://boards.greenhouse.io/acme/jobs/1");

    [Fact]
    public void A_board_starts_unproven_with_a_code_of_its_own()
    {
        var first = JobSource.Board(Acme, SourceKind.Greenhouse, " Acme ", PublishMode.Draft, Now);
        var second = JobSource.Board(Acme, SourceKind.Greenhouse, "acme", PublishMode.Draft, Now);

        first.Key.Should().Be("acme");
        first.IsProven.Should().BeFalse("a typed board name proves nothing");
        first.Code.Should().MatchRegex("^wt-[a-z2-9]{10}$");
        first.Code.Should().NotContainAny("0", "1", "o", "l", "i");
        second.Code.Should().NotBe(first.Code, "a code another company was shown must not prove this source");
    }

    [Theory]
    [InlineData("acme/../internal")]
    [InlineData("acme.example")]
    [InlineData("-acme")]
    [InlineData("")]
    public void A_board_name_holds_only_letters_digits_and_dashes(string key)
    {
        var act = () => JobSource.Board(Acme, SourceKind.Lever, key, PublishMode.Draft, Now);

        act.Should().Throw<SourceError>();
    }

    [Fact]
    public void The_code_in_one_advert_proves_the_board_and_it_stays_proven()
    {
        var source = JobSource.Board(Acme, SourceKind.Personio, "acme", PublishMode.Draft, Now);

        source.ProveBy([Advert("Welder")], Now).Should().BeFalse();
        source.IsProven.Should().BeFalse();

        source.ProveBy([Advert("Welder"), Advert("Fitter", $"Code: {source.Code.ToUpperInvariant()}")], Now)
            .Should().BeTrue("the company may write it in any case, in any field");
        source.ProvenAt.Should().Be(Now);

        source.ProveBy([Advert("Welder")], Now.AddDays(1))
            .Should().BeTrue("the code may leave the advert again — a board does not change owners");
        source.ProvenAt.Should().Be(Now);
    }

    [Fact]
    public void Another_sources_code_proves_nothing()
    {
        var ours = JobSource.Board(Acme, SourceKind.Greenhouse, "acme", PublishMode.Draft, Now);
        var theirs = JobSource.Board(new TenantId(Guid.CreateVersion7()), SourceKind.Greenhouse, "acme", PublishMode.Draft, Now);

        theirs.ProveBy([Advert($"Welder {ours.Code}")], Now).Should().BeFalse();
    }

    [Theory]
    [InlineData("https://acme.example/jobs.json", true)]
    [InlineData("https://careers.acme.example/jobs.json", true)]
    [InlineData("http://acme.example/jobs.json", false)]
    [InlineData("https://evilacme.example/jobs.json", false)]
    [InlineData("https://acme.example.evil.test/jobs.json", false)]
    [InlineData("acme.example/jobs.json", false)]
    public void A_feed_lies_on_the_proven_domain_or_is_refused(string address, bool accepted)
    {
        var act = () => JobSource.Feed(Acme, address, "acme.example", PublishMode.Draft, Now);

        if (accepted)
        {
            var feed = act();
            feed.IsProven.Should().BeTrue("where it lies is the proof");
            feed.Code.Should().BeEmpty();
        }
        else
        {
            act.Should().Throw<SourceError>();
        }
    }

    [Fact]
    public void A_company_without_a_proven_domain_has_no_feed()
    {
        var act = () => JobSource.Feed(Acme, "https://acme.example/jobs.json", "", PublishMode.Draft, Now);

        act.Should().Throw<SourceError>().WithMessage("The company has no proven domain");
    }

    [Theory]
    [InlineData("javascript:alert(1)", "")]
    [InlineData("http://acme.example/job/1", "")]
    [InlineData("/job/1", "")]
    [InlineData("https://acme.example/job/1", "https://acme.example/job/1")]
    public void Only_an_https_link_is_kept(string url, string kept)
    {
        JobOrigin.Link(url).Should().Be(kept);
    }

    [Fact]
    public void An_id_too_long_to_keep_gives_no_origin()
    {
        var advert = new ExternalAdvert(
            new string('x', JobOrigin.MaxIdLength + 1), "Welder", "We weld.", "", RemoteMode.None, EmploymentKind.FullTime, "");

        JobOrigin.Of(Guid.CreateVersion7(), advert).Should().BeNull();
    }

    [Fact]
    public void A_mirrored_advert_gains_skills_but_keeps_its_text()
    {
        var job = Job.Import(Acme, new JobOrigin(Guid.CreateVersion7(), "1", ""), Advert("Welder"), Now);

        job.Change("Welder", "We weld.", "Berlin", "10115", RemoteMode.None, EmploymentKind.FullTime,
            SkillList.From(["MIG/MAG"]), Now);
        job.Skills.Values.Should().Equal("MIG/MAG");

        var act = () => job.Change("Senior Welder", "We weld.", "Berlin", "10115", RemoteMode.None,
            EmploymentKind.FullTime, SkillList.From(["MIG/MAG"]), Now);
        act.Should().Throw<MirroredError>("the text is changed at the source");

        job.Detach();
        act.Should().NotThrow("a detached advert is the company's own");
    }

    [Theory]
    [InlineData("10.0.0.5", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700::1111", true)]
    public void A_company_host_must_resolve_to_a_public_address(string address, bool reachable)
    {
        HttpCompanySite.IsPublic(IPAddress.Parse(address)).Should().Be(reachable);
    }

    [Fact]
    public void The_egress_boundary_learns_proven_domains_and_personio_hosts_only()
    {
        var known = new KnownDomains();
        var policy = new CompanyDomainEgress(new Strict("boards-api.greenhouse.io"), known, "jobs.personio.de");

        policy.IsAllowed(new Uri("https://boards-api.greenhouse.io/v1/boards/acme/jobs")).Should().BeTrue();
        policy.IsAllowed(new Uri("https://acme.jobs.personio.de/xml")).Should().BeTrue();
        policy.IsAllowed(new Uri("https://jobs.personio.de.evil.test/xml")).Should().BeFalse();
        policy.IsAllowed(new Uri("https://careers.acme.example/jobs.json")).Should().BeFalse("not learned yet");

        known.Remember("acme.example");

        policy.IsAllowed(new Uri("https://careers.acme.example/jobs.json")).Should().BeTrue();
        policy.IsAllowed(new Uri("http://careers.acme.example/jobs.json")).Should().BeFalse("only https");
        policy.IsAllowed(new Uri("https://evilacme.example/jobs.json")).Should().BeFalse();
        policy.DeclaredHosts.Should().Contain(["boards-api.greenhouse.io", "*.jobs.personio.de", "acme.example"]);
    }

    /// <summary>An enforcing policy that knows one host.</summary>
    private sealed class Strict(string host) : IEgressPolicy
    {
        public bool IsAllowed(Uri destination) => destination.Host == host;

        public IReadOnlyCollection<string> DeclaredHosts => [host];

        public bool IsEnforcing => true;
    }
}
