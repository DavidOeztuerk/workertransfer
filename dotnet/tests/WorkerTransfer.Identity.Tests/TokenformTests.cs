using FluentAssertions;
using Girder.Core.Identity;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// The shape of the transitional access token: what both worlds read out of it.
/// </summary>
/// <remarks>
/// Every claim here is load-bearing for one side or the other, and the two
/// omissions are as deliberate as the entries. See
/// <c>docs/MIGRATION-PROMPT.md</c>, "Die Tokenform entscheidet die Reihenfolge".
/// </remarks>
public class TokenformTests
{
    private static readonly SubjectId Anna = SubjectId.New();
    private static readonly TenantId Firma = TenantId.New();
    private static readonly SessionId Sitzung = SessionId.New();

    private async Task<System.Text.Json.JsonElement> IssueAsync(Capacity acting) =>
        Tokenform.Payload(
            await Tokenform.Issuer_().IssueAsync(Anna, "anna@example.com", acting, Sitzung));

    [Fact]
    public async Task Beide_Welten_finden_das_Subjekt_unter_sub()
    {
        var payload = await IssueAsync(Capacity.AsSelf.Instance);

        payload.Claim("sub").Should().Be(Anna.ToString());
    }

    [Fact]
    public async Task Python_findet_den_Mandanten_unter_tenant_id_und_Girder_unter_tenant()
    {
        var payload = await IssueAsync(new Capacity.ForCompany(Firma));

        payload.Claim("tenant").Should().Be(Firma.ToString());
        payload.Claim("tenant_id").Should().Be(Firma.ToString());
    }

    [Fact]
    public async Task Wer_als_Person_handelt_traegt_keinen_der_beiden_Mandantenansprueche()
    {
        var payload = await IssueAsync(Capacity.AsSelf.Instance);

        payload.Has("tenant").Should().BeFalse();
        payload.Has("tenant_id").Should().BeFalse("ein leerer Mandant ist kein Mandant");
    }

    [Fact]
    public async Task Python_verlangt_type_und_bekommt_access()
    {
        var payload = await IssueAsync(Capacity.AsSelf.Instance);

        payload.Claim("type").Should().Be("access");
    }

    /// <summary>
    /// Not an oversight: the role checks read from <c>user_tenant_memberships</c>,
    /// never from the token, so the claim was never authoritative. It also cannot
    /// be written as a list — see
    /// <c>bugs/customclaims-kann-keine-liste-ausdruecken.md</c>.
    /// </summary>
    [Theory]
    [InlineData("roles")]
    [InlineData("permissions")]
    public async Task Rollen_und_Berechtigungen_stehen_nicht_im_Token(string anspruch)
    {
        var payload = await IssueAsync(new Capacity.ForCompany(Firma));

        payload.Has(anspruch).Should().BeFalse();
    }

    [Fact]
    public async Task Die_Sitzung_steht_drin_damit_ein_Widerruf_ein_Geraet_benennen_kann()
    {
        var payload = await IssueAsync(Capacity.AsSelf.Instance);

        payload.Claim("session_id").Should().Be(Sitzung.ToString());
    }

    [Fact]
    public async Task Aussteller_und_Zielgruppe_kommen_von_Girder()
    {
        var payload = await IssueAsync(Capacity.AsSelf.Instance);

        payload.Claim("iss").Should().Be(Tokenform.Issuer);
        payload.Claim("aud").Should().Be(Tokenform.Audience);
    }
}
