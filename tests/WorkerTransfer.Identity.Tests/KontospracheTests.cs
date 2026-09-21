using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.Identity.Infrastructure.Post;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Tests;

/// <summary>The language stands on the account, not on the request.</summary>
/// <remarks>
/// The suite pins three things, and all three are the reason for the column:
/// the confirmation mail follows the header the first time, the choice
/// overwrites it for good, and a mail written without a request finds the choice
/// all the same.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class AccountLanguageTests(Postgres postgres) : IAsyncLifetime
{
    private const string NotifySecret = "melde-geheimnis";
    private const string Password = "geheim-und-lang-genug";

    private WebApplicationFactory<Program> _dienst = null!;
    private readonly MailRecord _post = new();

    public async Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:identity", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:PrivateKey", TokenShape.PrivateKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Mail:WebAdresse", "http://localhost:5173");
            host.UseSetting("Notify:Geheimnis", NotifySecret);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IMailSender>(_post)));
        });

        using var scope = _dienst.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Browser(string? languageHeader = null)
    {
        var browser = _dienst.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });

        if (languageHeader is not null)
        {
            browser.DefaultRequestHeaders.Add("Accept-Language", languageHeader);
        }

        return browser;
    }

    private static string NewAddress(string subject) =>
        $"{subject}-{Guid.NewGuid():N}@firma-{Guid.NewGuid():N}.example";

    /// <summary>Registers, confirms and signs in. Answers with the browser.</summary>
    private async Task<(HttpClient Browser, string Address)> SignedInAccount(
        string? languageHeader = null)
    {
        var address = NewAddress("anna");
        var browser = Browser(languageHeader);

        await browser.PostAsJsonAsync("/auth/register", new
        {
            email = address, password = Password, display_name = "Anna"
        });

        var token = Regex.Match(
            _post.Post[^1].Text, @"token=([A-Za-z0-9_\-]+)").Groups[1].Value;
        await browser.PostAsJsonAsync("/auth/verify-email", new { token });
        await browser.PostAsJsonAsync("/auth/login", new
        {
            email = address, password = Password
        });

        return (browser, address);
    }

    /// <summary>
    /// The first time there is nothing but the header — and the mail follows it.
    /// </summary>
    /// <remarks>
    /// The confirmation mail goes out before anybody could choose a language.
    /// Without this reach for it, the very first message to a French account
    /// would be German.
    /// </remarks>
    [Theory]
    [InlineData("fr-CA,fr;q=0.9", "Merci de confirmer")]
    [InlineData("en-GB,en;q=0.9", "Please confirm")]
    [InlineData("de-DE,de;q=0.9", "Bitte bestätige")]
    public async Task The_first_mail_follows_the_header(string header, string expected)
    {
        await Browser(header).PostAsJsonAsync("/auth/register", new
        {
            email = NewAddress("erst"), password = Password, display_name = "Erst"
        });

        _post.Post[^1].Subject.Should().Contain(expected);
    }

    /// <summary>
    /// A language we do not have leads to the source language — not to a
    /// failure.
    /// </summary>
    /// <remarks>
    /// A registration must not fail because a browser announces Icelandic. And
    /// the header often names several: if a language we do have stands second,
    /// it has to win — otherwise the first unknown entry would have the effect
    /// of a choice.
    /// </remarks>
    [Theory]
    [InlineData("is-IS", "Bitte bestätige")]
    [InlineData("is-IS,fr;q=0.8", "Merci de confirmer")]
    [InlineData("*", "Bitte bestätige")]
    public async Task Anything_unknown_in_the_header_falls_back_to_the_source_language(
        string header, string expected)
    {
        await Browser(header).PostAsJsonAsync("/auth/register", new
        {
            email = NewAddress("fremd"), password = Password, display_name = "Fremd"
        });

        _post.Post[^1].Subject.Should().Contain(expected);
    }

    /// <summary>The choice stands on the account and beats the header.</summary>
    /// <remarks>
    /// The deciding case: the browser goes on saying German, the account says
    /// French. Were the header read, the choice would be without effect — and
    /// nobody would learn of it.
    /// </remarks>
    [Fact]
    public async Task The_choice_beats_the_header()
    {
        var (browser, _) = await SignedInAccount("de-DE");

        var set = await browser.PutAsJsonAsync(
            "/account/language", new { language = "fr" });
        set.StatusCode.Should().Be(HttpStatusCode.OK);

        // Derselbe Browser, weiterhin mit deutschem Header.
        await browser.PostAsJsonAsync("/auth/resend-verification", new
        {
            email = "unbekannt@example.org"
        });

        var session = await browser.GetFromJsonAsync<SessionAnswer>("/auth/session");
        session!.User!.Language.Should().Be("fr");
    }

    /// <summary>
    /// A mail without a request finds the choice all the same — that is the
    /// reason for the column.
    /// </summary>
    /// <remarks>
    /// <c>POST /internal/notify</c> comes from notification-service and carries
    /// no browser header. Were the language on the request, this mail would
    /// always be German.
    /// </remarks>
    [Fact]
    public async Task A_mail_without_a_request_finds_the_choice()
    {
        var (browser, _) = await SignedInAccount("de-DE");
        await browser.PutAsJsonAsync("/account/language", new { language = "fr" });

        var session = await browser.GetFromJsonAsync<SessionAnswer>("/auth/session");
        var subject = Guid.Parse(session!.User!.UserId);

        var melder = Browser();
        melder.DefaultRequestHeaders.Add("X-Notify-Secret", NotifySecret);
        var reported = await melder.PostAsJsonAsync(
            "/internal/notify", new { user_id = subject });

        reported.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _post.Post[^1].Subject.Should().Be("Du nouveau sur WorkerTransfer");
    }

    /// <summary>
    /// A language with no texts is refused, not silently replaced.
    /// </summary>
    /// <remarks>
    /// Important because of the counter-probe: <c>LanguageChoice.From</c>
    /// answers anything unknown with German, and that is exactly what would be
    /// wrong here. A choice that has no effect and says nothing looks to a human
    /// being like a fault in the interface.
    /// </remarks>
    [Fact]
    public async Task A_language_with_no_texts_is_refused()
    {
        var (browser, _) = await SignedInAccount();

        var response = await browser.PutAsJsonAsync(
            "/account/language", new { language = "is" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var session = await browser.GetFromJsonAsync<SessionAnswer>("/auth/session");
        session!.User!.Language.Should().Be("de");
    }

    /// <summary>Without a sign-in nobody changes a language.</summary>
    /// <remarks>
    /// There is no id in the body, and that is the real protection: one would be
    /// the only thing between a caller and somebody else's account.
    /// </remarks>
    [Fact]
    public async Task Without_a_sign_in_it_does_not_work()
    {
        var response = await Browser().PutAsJsonAsync(
            "/account/language", new { language = "fr" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Each of the three languages has each of the five texts.</summary>
    /// <remarks>
    /// The same guard as in the frontend, and for the same reason: a missing
    /// translation otherwise surfaces only once a mail is already out. It is
    /// also checked that none of the texts COPIES the German version — in
    /// delivery a copy looks like a translation.
    /// </remarks>
    [Fact]
    public void Every_language_has_every_text()
    {
        var german = Texts(AccountLanguage.De);

        foreach (var language in new[] { AccountLanguage.En, AccountLanguage.Fr })
        {
            var texts = Texts(language);

            texts.Should().HaveSameCount(german);
            texts.Should().OnlyContain(
                text => !string.IsNullOrWhiteSpace(text.Subject)
                        && !string.IsNullOrWhiteSpace(text.Text));

            for (var i = 0; i < texts.Count; i++)
            {
                texts[i].Subject.Should().NotBe(
                    german[i].Subject,
                    $"{language} should translate, not copy");
            }
        }
    }

    private static IReadOnlyList<MailText> Texts(AccountLanguage language) =>
    [
        MailTexts.Confirmation(language, "http://x/verify?token=t"),
        MailTexts.DuplicateRegistration(language),
        MailTexts.InvitationMail(language, "Acme", "http://x/invitation?token=t"),
        MailTexts.ErasureConfirmation(language),
        MailTexts.News(language, "http://x")
    ];

    private sealed record SessionAnswer(AccountAnswer? User);

    /// <summary>The answer as it REALLY looks.</summary>
    /// <remarks>
    /// The names are spelled out because the wire speaks snake_case and the
    /// comparison would otherwise go quietly wrong: <c>user_id</c> would not
    /// match a <c>UserId</c>, the field would stay null, and the test would fall
    /// with a message that says nothing about the matter.
    /// </remarks>
    private sealed record AccountAnswer(
        [property: JsonPropertyName("user_id")] string UserId,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("tenant_id")] string? TenantId,
        [property: JsonPropertyName("language")] string Language);
}
