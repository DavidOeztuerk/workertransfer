using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using FluentAssertions;
using Girder.Core.Identity;
using WorkerTransfer.Identity.Infrastructure.Security;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// Translating claims while two issuers are live — and refusing loudly where
/// the obvious implementation would downgrade silently.
/// </summary>
public class UebergangsPrincipalFactoryTests
{
    private static readonly UebergangsPrincipalFactory Factory = new();

    private static ClaimsPrincipal Angemeldet(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)),
            authenticationType: "Bearer"));

    private static (string, string) Subjekt(SubjectId subject) =>
        (JwtRegisteredClaimNames.Sub, subject.ToString());

    private static (string, string) Zugriff => ("type", "access");

    [Fact]
    public void Ohne_Anmeldung_ist_niemand_da()
    {
        Factory.Create(new ClaimsPrincipal(new ClaimsIdentity())).IsAnonymous.Should().BeTrue();
        Factory.Create(null).IsAnonymous.Should().BeTrue();
    }

    [Fact]
    public void Ohne_Mandantenanspruch_handelt_jemand_als_Person()
    {
        var anna = SubjectId.New();

        var result = Factory.Create(Angemeldet(Subjekt(anna), Zugriff));

        result.Principal!.Subject.Should().Be(anna);
        result.Principal.Acting.Should().BeOfType<Capacity.AsSelf>();
    }

    [Fact]
    public void Girders_tenant_wird_gelesen()
    {
        var firma = TenantId.New();

        var result = Factory.Create(
            Angemeldet(Subjekt(SubjectId.New()), ("tenant", firma.ToString())));

        result.Principal!.Acting.Should().BeOfType<Capacity.ForCompany>()
            .Which.Tenant.Should().Be(firma);
    }

    [Fact]
    public void Pythons_tenant_id_wird_ebenso_gelesen()
    {
        var firma = TenantId.New();

        var result = Factory.Create(
            Angemeldet(Subjekt(SubjectId.New()), Zugriff, ("tenant_id", firma.ToString())));

        result.Principal!.Acting.Should().BeOfType<Capacity.ForCompany>()
            .Which.Tenant.Should().Be(firma);
    }

    /// <summary>
    /// Python writes <c>"tenant_id": null</c> for a person, and that arrives as
    /// a claim that is present, empty, and typed <c>JSON_NULL</c> — measured,
    /// not assumed. Treating "present but empty" as a refusal would turn away
    /// every person signing in, which is the ordinary case.
    /// </summary>
    [Fact]
    public void Pythons_ausdrueckliches_null_heisst_Person_und_wird_nicht_abgelehnt()
    {
        var anna = SubjectId.New();
        var identity = new ClaimsIdentity(authenticationType: "Bearer");
        identity.AddClaim(new Claim(JwtRegisteredClaimNames.Sub, anna.ToString()));
        identity.AddClaim(new Claim("tenant_id", string.Empty, JsonClaimValueTypes.JsonNull));

        var result = Factory.Create(new ClaimsPrincipal(identity));

        result.IsInvalid.Should().BeFalse();
        result.Principal!.Acting.Should().BeOfType<Capacity.AsSelf>();
    }

    public class WoHerabstufen_falsch_waere
    {
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("null")]
        [InlineData("nicht-einmal-eine-uuid")]
        [InlineData("00000000-0000-0000-0000-000000000000")]
        public void Ein_unbrauchbarer_Mandant_wird_abgelehnt_statt_zur_Person_gemacht(string wert)
        {
            var result = Factory.Create(
                Angemeldet(Subjekt(SubjectId.New()), Zugriff, ("tenant_id", wert)));

            result.IsInvalid.Should().BeTrue(
                "ein Firmen-Akteur, der lautlos zur Privatperson wird, sieht andere Daten");
            result.Principal.Should().BeNull();
        }

        [Fact]
        public void Zwei_Mandantenansprueche_die_sich_widersprechen_werden_abgelehnt()
        {
            var result = Factory.Create(Angemeldet(
                Subjekt(SubjectId.New()),
                ("tenant", TenantId.New().ToString()),
                ("tenant_id", TenantId.New().ToString())));

            result.IsInvalid.Should().BeTrue();
        }

        [Fact]
        public void Zweimal_derselbe_Mandant_ist_kein_Widerspruch()
        {
            var firma = TenantId.New();

            var result = Factory.Create(Angemeldet(
                Subjekt(SubjectId.New()),
                ("tenant", firma.ToString()),
                ("tenant_id", firma.ToString())));

            result.Principal!.Acting.Should().BeOfType<Capacity.ForCompany>()
                .Which.Tenant.Should().Be(firma);
        }
    }

    public class WeilGirderDenTypNichtPrueft
    {
        [Fact]
        public void Ein_Erneuerungstoken_gilt_hier_nicht_als_Zugriffstoken()
        {
            var result = Factory.Create(
                Angemeldet(Subjekt(SubjectId.New()), ("type", "refresh")));

            result.IsInvalid.Should().BeTrue();
        }

        [Fact]
        public void Ohne_Typanspruch_bleibt_es_bei_der_Girder_Regel()
        {
            var result = Factory.Create(Angemeldet(Subjekt(SubjectId.New())));

            result.IsInvalid.Should().BeFalse();
        }
    }

    [Fact]
    public void Ohne_brauchbares_Subjekt_wird_abgelehnt()
    {
        Factory.Create(Angemeldet(("type", "access"))).IsInvalid.Should().BeTrue();
        Factory.Create(Angemeldet((JwtRegisteredClaimNames.Sub, "keine-uuid")))
            .IsInvalid.Should().BeTrue();
    }
}
