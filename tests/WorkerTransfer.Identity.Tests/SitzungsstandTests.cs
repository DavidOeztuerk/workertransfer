using FluentAssertions;
using Girder.Core.Identity;
using NSubstitute;
using WorkerTransfer.Identity.Application.Anmelden;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// The public question — "is anyone signed in?" — and the protected one.
/// </summary>
public class SitzungsstandTests
{
    private readonly ICurrentPrincipal _akteur = Substitute.For<ICurrentPrincipal>();
    private readonly IUserRepository _benutzer = Substitute.For<IUserRepository>();

    private static readonly SubjectId Anna = SubjectId.New();

    private Task<Sitzungsstand> Stand(bool erneuerungLiegtVor) =>
        new SitzungsstandHandler(_akteur, _benutzer)
            .Handle(new SitzungsstandAbfrage(erneuerungLiegtVor), CancellationToken.None);

    private void EsGibtDasKonto() =>
        _benutzer.FindByIdAsync(Anna, Arg.Any<CancellationToken>())
            .Returns(Konten.Bestehend(Anna, "anna@example.com", "$2b$12$x", AccountStatus.Active));

    [Fact]
    public async Task Angemeldet_heisst_active_und_das_Konto_liegt_bei()
    {
        _akteur.Current.Returns(Principal.Person(Anna));
        EsGibtDasKonto();

        var stand = await Stand(erneuerungLiegtVor: false);

        stand.Zustand.Should().Be("active");
        stand.Benutzer.Should().NotBeNull();
        stand.Benutzer!.Email.Should().Be("anna@example.com");
        stand.Benutzer.Firma.Should().BeNull();
    }

    [Fact]
    public async Task Wer_fuer_eine_Firma_handelt_sieht_sie_hier()
    {
        var firma = TenantId.New();
        _akteur.Current.Returns(Principal.Company(Anna, firma));
        EsGibtDasKonto();

        var stand = await Stand(erneuerungLiegtVor: false);

        stand.Benutzer!.Firma.Should().Be(firma);
    }

    /// <summary>
    /// The state this endpoint exists for: only with it can the interface call
    /// the refresh endpoint deliberately instead of on the off-chance.
    /// </summary>
    [Fact]
    public async Task Ein_Erneuerungscookie_ohne_Anmeldung_heisst_renewable()
    {
        _akteur.Current.Returns((Principal?)null);

        (await Stand(erneuerungLiegtVor: true)).Zustand.Should().Be("renewable");
    }

    [Fact]
    public async Task Gar_nichts_heisst_anonymous()
    {
        _akteur.Current.Returns((Principal?)null);

        (await Stand(erneuerungLiegtVor: false)).Zustand.Should().Be("anonymous");
    }

    /// <summary>
    /// Public, so it must reveal nothing anyone could conclude the existence of
    /// an account from. Without a sign-in it never looks anybody up.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ohne_Anmeldung_wird_kein_Konto_gesucht(bool erneuerung)
    {
        _akteur.Current.Returns((Principal?)null);

        var stand = await Stand(erneuerung);

        stand.Benutzer.Should().BeNull();
        await _benutzer.DidNotReceive().FindByIdAsync(
            Arg.Any<SubjectId>(), Arg.Any<CancellationToken>());
    }

    public class MeinKonto
    {
        private readonly ICurrentPrincipal _akteur = Substitute.For<ICurrentPrincipal>();
        private readonly IUserRepository _benutzer = Substitute.For<IUserRepository>();

        private Task<Kontoansicht?> Konto() =>
            new MeinKontoHandler(_akteur, _benutzer)
                .Handle(new MeinKontoAbfrage(), CancellationToken.None);

        /// <summary>
        /// A protected resource, so an anonymous caller gets nothing — the
        /// opposite of the public question above, and the reason the two are
        /// not one endpoint.
        /// </summary>
        [Fact]
        public async Task Ohne_Anmeldung_gibt_es_nichts()
        {
            _akteur.Current.Returns((Principal?)null);

            (await Konto()).Should().BeNull();
        }

        /// <summary>
        /// The address is not in the token — a JWT travels with every request
        /// and is no place for one — so it is read here and goes back only to
        /// the signed-in person themselves.
        /// </summary>
        [Fact]
        public async Task Die_Adresse_kommt_aus_dem_Konto_nicht_aus_dem_Token()
        {
            var anna = SubjectId.New();
            _akteur.Current.Returns(Principal.Person(anna));
            _benutzer.FindByIdAsync(anna, Arg.Any<CancellationToken>())
                .Returns(Konten.Bestehend(anna, "anna@example.com", "$2b$12$x", AccountStatus.Active));

            (await Konto())!.Email.Should().Be("anna@example.com");
        }
    }
}
