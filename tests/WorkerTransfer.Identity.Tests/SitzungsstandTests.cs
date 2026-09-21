using FluentAssertions;
using Noelia.Core.Identity;
using NSubstitute;
using WorkerTransfer.Identity.Application.Anmelden;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// The public question — "is anyone signed in?" — and the protected one.
/// </summary>
public class SessionStateTests
{
    private readonly ICurrentPrincipal _akteur = Substitute.For<ICurrentPrincipal>();
    private readonly IUserRepository _benutzer = Substitute.For<IUserRepository>();

    private static readonly SubjectId Anna = SubjectId.New();

    private Task<SessionState> State(bool refreshPresent) =>
        new SessionStateHandler(_akteur, _benutzer)
            .Handle(new SessionStateQuery(refreshPresent), CancellationToken.None);

    private void TheAccountExists() =>
        _benutzer.FindByIdAsync(Anna, Arg.Any<CancellationToken>())
            .Returns(Accounts.Existing(Anna, "anna@example.com", "$2b$12$x", AccountStatus.Active));

    [Fact]
    public async Task Signed_in_means_active_and_the_account_comes_with_it()
    {
        _akteur.Current.Returns(Principal.Person(Anna));
        TheAccountExists();

        var state = await State(refreshPresent: false);

        state.State.Should().Be("active");
        state.Account.Should().NotBeNull();
        state.Account!.Email.Should().Be("anna@example.com");
        state.Account.Company.Should().BeNull();
    }

    [Fact]
    public async Task Whoever_acts_for_a_company_sees_it_here()
    {
        var company = TenantId.New();
        _akteur.Current.Returns(Principal.Company(Anna, company));
        TheAccountExists();

        var state = await State(refreshPresent: false);

        state.Account!.Company.Should().Be(company);
    }

    /// <summary>
    /// The state this endpoint exists for: only with it can the interface call
    /// the refresh endpoint deliberately instead of on the off-chance.
    /// </summary>
    [Fact]
    public async Task A_refresh_cookie_without_a_sign_in_means_renewable()
    {
        _akteur.Current.Returns((Principal?)null);

        (await State(refreshPresent: true)).State.Should().Be("renewable");
    }

    [Fact]
    public async Task Nothing_at_all_means_anonymous()
    {
        _akteur.Current.Returns((Principal?)null);

        (await State(refreshPresent: false)).State.Should().Be("anonymous");
    }

    /// <summary>
    /// Public, so it must reveal nothing anyone could conclude the existence of
    /// an account from. Without a sign-in it never looks anybody up.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Without_a_sign_in_no_account_is_looked_up(bool refresh)
    {
        _akteur.Current.Returns((Principal?)null);

        var state = await State(refresh);

        state.Account.Should().BeNull();
        await _benutzer.DidNotReceive().FindByIdAsync(
            Arg.Any<SubjectId>(), Arg.Any<CancellationToken>());
    }

    public class MyAccount
    {
        private readonly ICurrentPrincipal _akteur = Substitute.For<ICurrentPrincipal>();
        private readonly IUserRepository _benutzer = Substitute.For<IUserRepository>();

        private Task<AccountView?> Account() =>
            new MyAccountHandler(_akteur, _benutzer)
                .Handle(new MyAccountQuery(), CancellationToken.None);

        /// <summary>
        /// A protected resource, so an anonymous caller gets nothing — the
        /// opposite of the public question above, and the reason the two are
        /// not one endpoint.
        /// </summary>
        [Fact]
        public async Task Without_a_sign_in_there_is_nothing()
        {
            _akteur.Current.Returns((Principal?)null);

            (await Account()).Should().BeNull();
        }

        /// <summary>
        /// The address is not in the token — a JWT travels with every request
        /// and is no place for one — so it is read here and goes back only to
        /// the signed-in person themselves.
        /// </summary>
        [Fact]
        public async Task The_address_comes_from_the_account_not_from_the_token()
        {
            var anna = SubjectId.New();
            _akteur.Current.Returns(Principal.Person(anna));
            _benutzer.FindByIdAsync(anna, Arg.Any<CancellationToken>())
                .Returns(Accounts.Existing(anna, "anna@example.com", "$2b$12$x", AccountStatus.Active));

            (await Account())!.Email.Should().Be("anna@example.com");
        }
    }
}
