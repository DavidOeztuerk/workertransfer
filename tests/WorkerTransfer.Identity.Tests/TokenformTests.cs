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
    /// Der VOLLSTAENDIGE Anspruchssatz — nicht nur die verbotenen.
    /// </summary>
    /// <remarks>
    /// <strong>Der Anlass ist gemessen.</strong> `CLAUDE.md` sagte, das Token
    /// trage acht Ansprueche „und nichts sonst". Am laufenden Stapel gemessen
    /// waren es NEUN: Girder legt zusaetzlich
    /// <c>http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier</c>
    /// hinein, eine Verdopplung von <c>sub</c> in der langen WS-Schreibweise.
    ///
    /// <para>
    /// Sie ist in Girder BEGRUENDET und bleibt: <c>MapInboundClaims = false</c>
    /// schaltet die Ableitung aus, und siebzehn Leser holen den Aufrufer ueber
    /// diesen Namen — zwei davon in Fremdpaketen, die diese Assembly nicht
    /// sehen. Sie fallenzulassen spart siebzig Byte und macht aus jedem dieser
    /// Leser ein stilles <c>null</c>.
    /// </para>
    ///
    /// <para>
    /// <strong>Der Fehler lag also in der Zusage, nicht im Token</strong> — und
    /// darin, dass kein Test den ganzen Satz hielt. Die Reihe unten verbot zwei
    /// Namen; einen NEUNTEN haette sie nie bemerkt. Dieser Test tut es: er
    /// vergleicht die Menge, nicht einzelne Mitglieder.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Das_Token_traegt_genau_diese_Ansprueche()
    {
        string[] erwartetAlsPerson =
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

        var alsPerson = await IssueAsync(Capacity.AsSelf.Instance);
        alsPerson.EnumerateObject().Select(feld => feld.Name)
            .Should().BeEquivalentTo(erwartetAlsPerson);

        // Fuer eine Firma kommt GENAU EINER dazu: `tenant`. Nicht `tenant_id`,
        // nicht `roles` — die Mitgliedschaft entscheidet je Anfrage (ADR-0018).
        var fuerFirma = await IssueAsync(new Capacity.ForCompany(Firma));
        fuerFirma.EnumerateObject().Select(feld => feld.Name)
            .Should().BeEquivalentTo([.. erwartetAlsPerson, "tenant"]);
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
    [InlineData("given_name")]
    [InlineData("family_name")]
    [InlineData("address")]
    [InlineData("phone")]
    [InlineData("display_name")]
    public async Task Klarname_und_Anschrift_stehen_nicht_im_Token(string anspruch)
    {
        WorkerTransfer.Contracts.Identity.Tokenform.NiemalsImToken.Should().Contain(anspruch);

        var alsPerson = await IssueAsync(Capacity.AsSelf.Instance);
        var fuerFirma = await IssueAsync(new Capacity.ForCompany(Firma));

        alsPerson.Has(anspruch).Should().BeFalse();
        fuerFirma.Has(anspruch).Should().BeFalse();
    }

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

    /// <summary>
    /// Die <strong>geschlossene</strong> Menge — nicht einzelne Namen.
    /// </summary>
    /// <remarks>
    /// <para>Jeder Test darueber prueft einen Namen, den jemand aufgeschrieben
    /// hat. Was dabei durchrutscht, ist der Anspruch, an den niemand gedacht
    /// hat — und genau das ist passiert: das Token trug drei Ansprueche mehr,
    /// als der Kommentar am Aussteller aufzaehlte, weil Girder sie
    /// unaufgefordert dazulegte. Zwei davon
    /// (<c>email_verified</c>, <c>account_status</c>) sagten IMMER dasselbe,
    /// weil sie in <c>UserClaims</c> vorbelegt waren und niemand hier sie
    /// setzte. Kein Test konnte das finden, weil keiner nach dem Ganzen
    /// fragte.</para>
    ///
    /// <para>Seit Girder 4.1.0 stehen die beiden nur noch im Token, wenn jemand
    /// sie sagt. Diese Liste ist ab jetzt die Zusage: waechst sie, faellt der
    /// Test, und jemand muss entscheiden, ob der neue Anspruch hier hingehoert.</para>
    ///
    /// <para><c>nameidentifier</c> ist die lange WS-Federation-URI und
    /// verdoppelt <c>sub</c>. Sie steht mit Absicht drin und bleibt: Girder
    /// prueft Token mit <c>MapInboundClaims = false</c>, leitet sie also nicht
    /// aus <c>sub</c> ab, und siebzehn Leser in Girder loesen den Aufrufer
    /// darueber auf.</para>
    /// </remarks>
    [Fact]
    public async Task Das_Token_traegt_genau_diese_Ansprueche_und_keinen_mehr()
    {
        var alsPerson = await IssueAsync(Capacity.AsSelf.Instance);
        var fuerFirma = await IssueAsync(new Capacity.ForCompany(Firma));

        var gemeinsam = new[]
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

        Namen(alsPerson).Should().BeEquivalentTo(
            gemeinsam,
            "wer als Person handelt, traegt keinen Mandanten");

        Namen(fuerFirma).Should().BeEquivalentTo(
            [.. gemeinsam, "tenant"],
            "die Firma ist der EINZIGE Unterschied zwischen den beiden Formen");
    }

    private static IEnumerable<string> Namen(System.Text.Json.JsonElement payload) =>
        payload.EnumerateObject().Select(eigenschaft => eigenschaft.Name);
}
