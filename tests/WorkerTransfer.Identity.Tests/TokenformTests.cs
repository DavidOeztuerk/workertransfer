using FluentAssertions;
using Girder.Core.Identity;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// Die Form des Zugriffstokens: was ein Leser darin findet — und was nicht.
/// </summary>
/// <remarks>
/// Bis Phase C trug dieses Token zwei Auspraegungen zugleich, weil noch ein
/// zweiter Dienst mitlas: <c>tenant_id</c> neben <c>tenant</c> und ein
/// <c>type</c> mit dem Wert <c>access</c>. Beide sind mit Ue-2 gefallen. Sie
/// stehen hier weiter — als Abwesenheit, siehe
/// <see cref="Die_uebergangsansprueche_sind_weg_und_bleiben_weg"/>: wer sie
/// versehentlich zurueckbringt, baut einen zweiten Ort fuer den Mandanten, und
/// zwei Orte fuer dieselbe Aussage gehen irgendwann auseinander.
/// <para>
/// Jeder verbleibende Anspruch traegt etwas, und die Auslassungen sind so
/// gewollt wie die Eintraege.
/// </para>
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
    public async Task Das_Subjekt_steht_unter_sub()
    {
        var payload = await IssueAsync(Capacity.AsSelf.Instance);

        payload.Claim("sub").Should().Be(Anna.ToString());
    }

    [Fact]
    public async Task Wer_fuer_eine_Firma_handelt_traegt_sie_unter_tenant()
    {
        var payload = await IssueAsync(new Capacity.ForCompany(Firma));

        payload.Claim("tenant").Should().Be(Firma.ToString());
    }

    [Fact]
    public async Task Wer_als_Person_handelt_traegt_gar_keinen_Mandanten()
    {
        var payload = await IssueAsync(Capacity.AsSelf.Instance);

        payload.Has("tenant").Should().BeFalse("ein leerer Mandant ist kein Mandant");
    }

    /// <summary>
    /// Die beiden Ansprueche aus der Uebergangszeit, jetzt als Verbot.
    /// </summary>
    /// <remarks>
    /// <c>tenant_id</c> war die Schreibweise des Vorgaengerdienstes und stand
    /// neben <c>tenant</c> — zwei Namen fuer dieselbe Firma. <c>type</c> sagte
    /// <c>access</c>, weil der Vorgaenger sonst nicht validierte; hier
    /// unterscheidet die Lebensdauer die beiden Tokenarten, und ein Anspruch,
    /// den niemand liest, ist bloss eine Aussage mehr, die falsch werden kann.
    /// <para>
    /// Geprueft wird in BEIDEN Handlungsformen: <c>tenant_id</c> entstand nur
    /// bei einer Firma, <c>type</c> immer. Nur eine Form zu pruefen liesse
    /// genau die andere Rueckkehr durch.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("tenant_id")]
    [InlineData("type")]
    public async Task Die_uebergangsansprueche_sind_weg_und_bleiben_weg(string anspruch)
    {
        var alsPerson = await IssueAsync(Capacity.AsSelf.Instance);
        var fuerFirma = await IssueAsync(new Capacity.ForCompany(Firma));

        alsPerson.Has(anspruch).Should().BeFalse();
        fuerFirma.Has(anspruch).Should().BeFalse();
    }

    /// <summary>
    /// Kein Versehen: die Rollenpruefungen lesen aus
    /// <c>user_tenant_memberships</c>, nie aus dem Token, der Anspruch war also
    /// nie massgeblich. Er liesse sich auch gar nicht als Liste schreiben —
    /// siehe <c>bugs/customclaims-kann-keine-liste-ausdruecken.md</c>.
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
