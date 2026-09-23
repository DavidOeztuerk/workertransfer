using FluentAssertions;
using Noelia.Core.Identity;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// The shape of the access token: what a reader finds in it — and what not.
/// </summary>
/// <remarks>
/// Until phase C this token carried two spellings at once, because a second
/// service was still reading along: <c>tenant_id</c> beside <c>tenant</c> and a
/// <c>type</c> with the value <c>access</c>. Both fell with Ue-2. They stand
/// here still — as an absence, see
/// <see cref="The_migration_claims_are_gone_and_stay_gone"/>: whoever brings
/// them back by accident builds a second place for the tenant, and two places
/// for the same statement diverge sooner or later.
/// <para>
/// Every remaining claim carries something, and the omissions are as deliberate
/// as the entries.
/// </para>
/// </remarks>
public class TokenShapeTests
{
    private static readonly SubjectId Anna = SubjectId.New();
    private static readonly TenantId Company = TenantId.New();
    private static readonly SessionId Session = SessionId.New();

    private async Task<System.Text.Json.JsonElement> IssueAsync(Capacity acting) =>
        TokenShape.Payload(
            await TokenShape.Issuer_().IssueAsync(Anna, "anna@example.com", acting, Session));

    [Fact]
    public async Task The_subject_stands_under_sub()
    {
        var payload = await IssueAsync(Capacity.AsSelf.Instance);

        payload.Claim("sub").Should().Be(Anna.ToString());
    }

    [Fact]
    public async Task Whoever_acts_for_a_company_carries_it_under_tenant()
    {
        var payload = await IssueAsync(new Capacity.ForCompany(Company));

        payload.Claim("tenant").Should().Be(Company.ToString());
    }

    [Fact]
    public async Task Whoever_acts_as_a_person_carries_no_tenant_at_all()
    {
        var payload = await IssueAsync(Capacity.AsSelf.Instance);

        payload.Has("tenant").Should().BeFalse("an empty tenant is no tenant");
    }

    /// <summary>
    /// The COMPLETE claim set — not only the forbidden ones.
    /// </summary>
    /// <remarks>
    /// <strong>The occasion is measured.</strong> `CLAUDE.md` said the token
    /// carried eight claims "and nothing else". Measured against the running
    /// stack there were NINE: Noelia additionally puts
    /// <c>http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier</c>
    /// in, a duplication of <c>sub</c> in the long WS spelling.
    ///
    /// <para>
    /// It is GROUNDED in Noelia and stays: <c>MapInboundClaims = false</c>
    /// switches the derivation off, and seventeen readers fetch the caller over
    /// this name — two of them in foreign packages that do not see this
    /// assembly. Dropping it saves seventy bytes and turns every one of those
    /// readers into a silent <c>null</c>.
    /// </para>
    ///
    /// <para>
    /// <strong>So the fault lay in the promise, not in the token</strong> — and
    /// in the fact that no test held the whole set. The suite below forbade two
    /// names; a NINTH it would never have noticed. This test does: it compares
    /// the set, not single members.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_token_carries_exactly_these_claims()
    {
        string[] expectedAsPerson =
        [
            "sub",
            "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier",
            "email",
            "jti",
            "iat",
            "exp",
            "iss",
            "aud",
            "session_id"
        ];

        var asPerson = await IssueAsync(Capacity.AsSelf.Instance);
        asPerson.EnumerateObject().Select(field => field.Name)
            .Should().BeEquivalentTo(expectedAsPerson);

        // For a company EXACTLY ONE is added: `tenant`. Not `tenant_id`, not
        // `roles` — the membership decides per request (ADR-0018).
        var forCompany = await IssueAsync(new Capacity.ForCompany(Company));
        forCompany.EnumerateObject().Select(field => field.Name)
            .Should().BeEquivalentTo([.. expectedAsPerson, "tenant"]);
    }

    /// <summary>
    /// The two claims from the migration period, now as a prohibition.
    /// </summary>
    /// <remarks>
    /// <c>tenant_id</c> was the predecessor service's spelling and stood beside
    /// <c>tenant</c> — two names for the same company. <c>type</c> said
    /// <c>access</c>, because the predecessor did not validate otherwise; here
    /// the lifetime tells the two kinds of token apart, and a claim nobody reads
    /// is merely one more statement that can become wrong.
    /// <para>
    /// It is checked in BOTH capacities: <c>tenant_id</c> came into being only
    /// with a company, <c>type</c> always. Checking only one capacity would let
    /// exactly the other return through.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("tenant_id")]
    [InlineData("type")]
    public async Task The_migration_claims_are_gone_and_stay_gone(string claim)
    {
        var asPerson = await IssueAsync(Capacity.AsSelf.Instance);
        var forCompany = await IssueAsync(new Capacity.ForCompany(Company));

        asPerson.Has(claim).Should().BeFalse();
        forCompany.Has(claim).Should().BeFalse();
    }

    /// <summary>
    /// No oversight: the role checks read from
    /// <c>user_tenant_memberships</c>, never from the token, so the claim was
    /// never authoritative. It could not be written as a list at all either:
    /// Noelia's custom claims could not express one until Girder 3.0.0, and
    /// that ticket is closed and gone from <c>bugs/</c>.
    /// </summary>
    [Theory]
    [InlineData("given_name")]
    [InlineData("family_name")]
    [InlineData("address")]
    [InlineData("phone")]
    [InlineData("display_name")]
    public async Task The_civil_name_and_the_address_are_not_in_the_token(string claim)
    {
        WorkerTransfer.Contracts.Identity.Tokenform.NiemalsImToken.Should().Contain(claim);

        var asPerson = await IssueAsync(Capacity.AsSelf.Instance);
        var forCompany = await IssueAsync(new Capacity.ForCompany(Company));

        asPerson.Has(claim).Should().BeFalse();
        forCompany.Has(claim).Should().BeFalse();
    }

    [Theory]
    [InlineData("roles")]
    [InlineData("permissions")]
    public async Task Roles_and_permissions_are_not_in_the_token(string claim)
    {
        var payload = await IssueAsync(new Capacity.ForCompany(Company));

        payload.Has(claim).Should().BeFalse();
    }

    [Fact]
    public async Task The_session_is_in_it_so_a_revocation_can_name_a_device()
    {
        var payload = await IssueAsync(Capacity.AsSelf.Instance);

        payload.Claim("session_id").Should().Be(Session.ToString());
    }

    [Fact]
    public async Task Issuer_and_audience_come_from_Noelia()
    {
        var payload = await IssueAsync(Capacity.AsSelf.Instance);

        payload.Claim("iss").Should().Be(TokenShape.Issuer);
        payload.Claim("aud").Should().Be(TokenShape.Audience);
    }

    /// <summary>
    /// The <strong>closed</strong> set — not single names.
    /// </summary>
    /// <remarks>
    /// <para>Every test above it checks a name somebody wrote down. What slips
    /// through is the claim nobody thought of — and that is exactly what
    /// happened: the token carried three claims more than the comment at the
    /// issuer listed, because Noelia put them in unasked. Two of them
    /// (<c>email_verified</c>, <c>account_status</c>) ALWAYS said the same
    /// thing, because they were pre-set in <c>UserClaims</c> and nobody here set
    /// them. No test could find that, because none asked about the whole.</para>
    ///
    /// <para>Since Girder 4.1.0 the two stand in the token only when somebody
    /// says them. This list is the promise from now on: if it grows, the test
    /// falls, and somebody has to decide whether the new claim belongs
    /// here.</para>
    ///
    /// <para><c>nameidentifier</c> is the long WS-Federation URI and duplicates
    /// <c>sub</c>. It stands there deliberately and stays: Noelia verifies
    /// tokens with <c>MapInboundClaims = false</c>, so it does not derive it
    /// from <c>sub</c>, and seventeen readers in Noelia resolve the caller
    /// through it.</para>
    /// </remarks>
    [Fact]
    public async Task The_token_carries_exactly_these_claims_and_not_one_more()
    {
        var asPerson = await IssueAsync(Capacity.AsSelf.Instance);
        var forCompany = await IssueAsync(new Capacity.ForCompany(Company));

        var common = new[]
        {
            "sub",
            "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier",
            "email",
            "jti",
            "iat",
            "session_id",
            "exp",
            "iss",
            "aud"
        };

        Names(asPerson).Should().BeEquivalentTo(
            common,
            "whoever acts as a person carries no tenant");

        Names(forCompany).Should().BeEquivalentTo(
            [.. common, "tenant"],
            "the company is the ONLY difference between the two capacities");
    }

    private static IEnumerable<string> Names(System.Text.Json.JsonElement payload) =>
        payload.EnumerateObject().Select(property => property.Name);
}
