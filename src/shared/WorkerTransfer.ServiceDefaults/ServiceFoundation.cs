using Noelia.Abstractions.Hosting;
using Microsoft.Extensions.Logging;
using Noelia.Infrastructure.Builder;
using Noelia.Infrastructure.Builder.Modules;
using Noelia.Infrastructure.Extensions;
using Noelia.Infrastructure.Security;
using Noelia.Infrastructure.Security.Identity;
using Noelia.Infrastructure.Security.InputSanitization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Noelia.Dashboard;
using Noelia.Infrastructure.Sovereignty;
using WorkerTransfer.ServiceDefaults.Checks;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>What a service states about its use of a model.</summary>
/// <remarks>
/// <para>A declaration, not an observation, and it is made per service in its
/// <c>Program.cs</c>. Neither value is 0, so a forgotten argument is refused
/// at startup rather than read as "no".</para>
/// </remarks>
public enum ArtificialIntelligence
{
    /// <summary>The service calls no model and reads no person's AI access.</summary>
    NotUsed = 1,

    /// <summary>
    /// The service calls a model or holds a person's AI access. Which endpoint
    /// is decided per person at run time, so none is named.
    /// </summary>
    Used = 2,
}

/// <summary>
/// The one call every service makes, and the one that decides nothing a service
/// has to decide for itself.
/// </summary>
/// <remarks>
/// <para>What is here is here because eleven services answering it eleven ways
/// would be eleven chances to answer it wrong: how a token is verified, what a
/// failure looks like on the wire, in which order the pipeline runs. What is
/// <em>not</em> here is everything that is a decision — which database, which
/// repositories, whether there is an outbox. Those live in each service's own
/// <c>Add&lt;Service&gt;Infrastructure()</c>, where a reader can see them.</para>
///
/// <para><strong>Since Girder 4 the default is a default.</strong> Before
/// that, <c>AddSharedInfrastructure</c> took a lambda, and that lambda
/// <em>replaced</em> the default instead of adding to it. We listed five
/// modules and got five — and did not notice what went missing with it:
/// <strong>Serilog, Swagger, CORS, the JSON fine-tuning and the
/// <c>HttpContextAccessor</c></strong>. None of them had been deselected;
/// nobody had ever selected them.</para>
///
/// <para>The proof that we were using nothing of the new builder was the
/// version jump itself: 3.0.1 to 4.0.1 cost <em>zero</em> source changes.
/// Whoever uses nothing notices nothing either.</para>
///
/// <para><c>UseDefaults()</c> is a call, not an automatism — whoever leaves it
/// out gets nothing, the same shape as EF without a provider. And
/// <c>Without(module, reason)</c> does not compile without a reason, which is
/// why every deviation here carries its reason in the source rather than in a
/// header.</para>
/// </remarks>
public static class ServiceFoundation
{
    /// <summary>
    /// Noelia's default, plus the acting principal — and no issuing.
    /// </summary>
    /// <remarks>
    /// <para><c>Principal</c> does not stand in the default, because it demands
    /// a decision Noelia must not take. We take it the same way for all eleven:
    /// every service reads the acting principal out of the verified token,
    /// because <c>Capacity</c> stands on it (ADR-0017).</para>
    ///
    /// <para><strong>Only whoever has the private key can issue</strong>
    /// (<see cref="ResolveSigningKeys"/>), and identity-service alone gets it.
    /// Since ADR-0046 that is no longer a convention but a property of the key:
    /// a second issuer would be a second place that can create an acting
    /// principal, and nothing further down could tell the two apart.</para>
    /// </remarks>
    /// <param name="services">The container.</param>
    /// <param name="configuration">The service's configuration.</param>
    /// <param name="environment">Where it runs.</param>
    /// <param name="serviceName">What it calls itself in logs and telemetry.</param>
    /// <param name="artificialIntelligence">What this service states about its
    /// use of a model. Required: see <see cref="ArtificialIntelligence"/>.</param>
    /// <param name="additional">Modules only this service needs.</param>
    public static IServiceCollection AddWorkerTransferDefaults(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string serviceName,
        ArtificialIntelligence artificialIntelligence,
        Action<NoeliaBuilder>? additional = null)
    {
        // `default(ArtificialIntelligence)` is 0 and names neither value: a
        // forgotten argument must not arrive as a silent "no".
        if (!Enum.IsDefined(artificialIntelligence))
        {
            throw new ArgumentOutOfRangeException(
                nameof(artificialIntelligence),
                "Every service states its model use: ArtificialIntelligence.Used "
                + "or ArtificialIntelligence.NotUsed.");
        }

        // The input sanitization does NOT look into JSON bodies. Measured, not
        // assumed.
        //
        // It runs before everything else and cannot name a field — a request it
        // refuses never reached the domain validator. That turns a 422 saying
        // WHICH field is wrong into a bare 400 "malicious input". Measured on
        // five cases in three services: `javascript:alert(1)` and
        // `data:text/html,<script>` as a portfolio link, the same two on the
        // employer profile, and `../../etc/passwd` as a GitHub login name. All
        // five are still refused — the answer just tells the person in the form
        // less than it used to.
        //
        // That is bearable here and not elsewhere: our whole write surface is
        // validated domain layer with RFC 9457 responses including field names,
        // we build no SQL out of strings (EF with parameters), and the output
        // escapes through React. So the edge filter here conceals better
        // validators instead of covering anything.
        //
        // Query string and the two address headers stay checked — that is the
        // surface where a value arrives without a domain validator.
        services.Configure<InputSanitizationOptions>(
            options => options.InspectJsonBodies = false);

        // The token comes out of the `Authorization` header OR out of the
        // `access` cookie, and both carriers are needed: service-to-service and
        // CLI send the header, the browser never sees the httpOnly token and can
        // only send it back as a cookie.
        //
        // This stood eleven times over, word for word in every service. But it
        // is not a decision of a service, it is one of the platform — and eleven
        // places for the same statement are eleven chances to forget it at the
        // twelfth service. Then every switch in the browser would come back with
        // a 401 although the sign-in looks as though it worked.
        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options => options.AlsoFromTheCookie());

        // A refusal by a policy SHORT-CIRCUITS the answer and does not throw —
        // `ProblemDetailsMiddleware` never sees it. Without this line it falls
        // out as a bare 401/403 with an empty body, without a correlation id,
        // and the interface would get two shapes for the same thing.
        //
        // It stood in identity-service for as long as that was the only service
        // with policies. Since the company permissions hang in four services it
        // belongs here: one shape across all services.
        //
        // In a service WITHOUT policies it costs nothing: the authorization
        // middleware calls this handler only for endpoints that carry
        // authorization metadata.
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, RejectionShape>();

        // THE EVIDENCE HANGS HERE, because this is the one call every service
        // makes (ADR-0044).
        //
        // The reason already stands in this file, one heading further up: "We
        // listed five modules and got five — and did not notice what went
        // missing with it. None of them had been deselected; nobody had ever
        // selected them." The promises of this tree have exactly that illness in
        // three further places: they are written down and not executable.
        //
        // And a test cannot cure it. `Adr0022Tests` and `DraftBoundaryTests`
        // check the TREE; a person's AI access stands in `AiAccessV1` —
        // provider, address, model and key per person at run time —, and the
        // destinations of the egress boundary stand in the environment. Where
        // this container speaks tonight, only it knows itself.
        services.AddEvidence(configuration);

        return services.AddNoelia(configuration, environment, serviceName, noelia =>
        {
            noelia
                .UseDefaults()
                .Use(NoeliaModule.Principal)

                // ONLY WHOEVER HAS THE PRIVATE KEY CAN ISSUE.
                // A shared HS256 secret does not know this difference: any one
                // of the fifteen processes could mint a token for any human
                // being, and a copied configuration sheet would be every account
                // on every service.
                .UseJwt(jwt => ResolveSigningKeys(jwt, configuration))

                // A SIGN-OUT HAS TO REACH ALL FOURTEEN, not only identity:
                // the others verify by signature and would honour the old
                // token until it expires. See `Revocation`.
                .UseTokenRevocationStore(configuration, environment)

                // THE EGRESS BOUNDARY, AND WHERE IT KNOWS ITS HOSTS FROM.
                //
                // `AddSovereignPlatform` bundles four things: the egress
                // boundary, the masking in the log, the sovereignty report and
                // an audit trail.
                //
                // The audit trail stays ON. For a domain row it does not satisfy
                // ADR-0012 (that is what `EfAuditTrail` is for) — but the
                // dashboard access is not one: it has no transaction, and
                // without a sink the dashboard refuses with a 503.
                //
                // MEASURED BEFORE THIS STOOD HERE (`EgressTests`): the guard
                // REFUSES instead of logging, and a container name does NOT fall
                // under "loopback and RFC1918" — `consent-service` is not an
                // address that can be read as an IP. Adopted naively that would
                // have been the failure of every service-to-service call, and in
                // the stack rather than in the tests.
                //
                // The hosts therefore come out of the CONFIGURATION and not out
                // of a list here: `Consent__BaseUrl`, `Jobs__BaseUrl`,
                // `Lookup__*`, `Erasure__BaseUrls__*` stand in the environment
                // anyway, and what a service calls it has already said there. A
                // second list would be the one to go stale first — and nobody
                // would notice it going stale, because the call is then simply
                // refused.
                //
                // What does NOT stand in the configuration must not go out
                // either. That is exactly the purpose: a telemetry package with
                // a built-in default address, an SDK that phones home.
                //
                // THE MODEL USE IS DECLARED, because it cannot be observed here.
                // Noelia recognises a model endpoint by its host name, and the
                // endpoints that matter here stand per person in `AiAccessV1` —
                // `openai_compatible` on `localhost` included. Since 7.0.0 an
                // undeclared service reports "not determined" instead of a green
                // inventory; the declaration says yes or no, and "yes" carries
                // no endpoint, because the endpoints are not known at startup.
                // `wt.ki.anbieter` stays the complete list.
                .AddSovereignPlatform(sovereign => Declare(
                        sovereign.Allow([.. CalledHosts(configuration)]),
                        configuration)
                    .DeclareArtificialIntelligence(
                        artificialIntelligence == ArtificialIntelligence.Used))

                // THE OPERATIONS SURFACE, and it replaces seven addresses of
                // our own (ADR-0045).
                //
                // `/noelia` carries overview, composition, configuration,
                // security checks, sovereignty, AI, DUTIES, audit trail,
                // sessions, rate limits and health — together with
                // `report.json` out of THE SAME reading.
                // `WorkerTransfer.Nachweis` built that itself before this tree
                // stood on Noelia; it is deleted and does not stand beside it.
                // Two paths to one statement drift apart, and the first time
                // nobody notices.
                //
                // WITHOUT AUTHORISATION 404, not 403 — the same promise as
                // before. An operations surface whose existence you can guess is
                // already a disclosure in itself. The secret stays a piece of
                // paper of ITS OWN: whoever may trigger a mail does not have to
                // learn which AI providers this instance uses.
                .UseDashboard(dashboard => dashboard
                    .At("/noelia")
                    .VisibleTo(EvidenceSetup.IsOperator)
                    .InspectConfiguration("Consent", "Draft", "Erasure")
                    .InProduction(
                        "This instance's operations evidence — recipients, "
                        + "seam, erasure cascade — stands behind a secret of "
                        + "its own and carries no value and no name."))

                // Noelia's rate limiter stands in the default, and measured
                // against 4.0.2 it is in order (H2, measurement 1): it brakes,
                // it counts per origin, it no longer reads X-Forwarded-For, and
                // a forged "X-Forwarded-For: 127.0.0.1" does not lift it. The
                // reason for not running it here anyway has nothing to do with
                // Noelia and will not change any more:
                //
                // A service behind the gateway sees THE GATEWAY's address as the
                // origin — the same one for every caller. A brake here would
                // throw all people into one bucket: the first person to mistype
                // five times locks out the world. The brake belongs at the
                // entrance, and there, since 4.2.0, stands GIRDER'S OWN MODULE,
                // configured in `ocelot.json` — the hand-written `Bremse.cs`
                // named here before is deleted.
                .Without(
                    NoeliaModule.RateLimiting,
                    "a service behind the gateway sees only the gateway as "
                    + "the origin, so every caller as one — the brake sits at "
                    + "the entrance, in ocelot.json")

                // Does not stand in the default anyway. Named here so that the
                // decision stands in the source and not in somebody's memory.
                .Without(
                    NoeliaModule.HttpResponseCaching,
                    "an ETag is a cache at the caller, and almost everything "
                    + "here stands behind the consent gate: a 304 would go on "
                    + "showing a revoked profile. Caching itself stays ON — the "
                    + "module only provides storage, and no consent query is "
                    + "an ICacheableQuery")

                // Communication and Encryption do not stand in the default
                // anyway — named here because a reason that stands only in a
                // commit message does not exist for the next reader. Noelia's
                // own comment says it: "Silence is not a decision."
                //
                // Communication: measured in H2. The status codes are intact (a
                // 404 arrives as a 404, ONE call at the destination, not three).
                // But it demands `IEventBus`, and that is delivered by
                // Noelia.Messaging.MassTransit alone — that is, a broker we
                // deliberately do not run. On top of that `UseGateway = true`
                // sends EVERY call to the service named "gateway" by default,
                // and `EnableResponseCaching = true` stores every GET answer for
                // five minutes. And the reason that made the migration
                // attractive is settled: since 4.0.0 the correlation chain hangs
                // on every HttpClient from the factory.
                .Without(
                    NoeliaModule.Communication,
                    "demands IEventBus and with it a broker we do not run; "
                    + "since 4.0.0 we get the correlation chain without it")

                // Encryption: demands `IDataEncryptionService` AND
                // `IMasterKeyProvider`, both out of Noelia.Redis or a secret
                // store. We encrypt nothing at field level today. Whoever starts
                // doing so decides first WHERE the master key lies — and that is
                // the same question as with SecretManagement, not one line
                // here.
                .Without(
                    NoeliaModule.Encryption,
                    "nothing is encrypted at field level today; whoever starts "
                    + "doing so decides first where the master key lies")

                // ResourceAuthorization stays out (a module of its own since
                // 4.0.2): no endpoint here calls resource policies, and it
                // demands a store. `Authorization` itself is IN the default and
                // brings the PermissionPolicyProvider with it — exactly the one
                // the company permissions in identity-service hang on.
                .Without(
                    NoeliaModule.ResourceAuthorization,
                    "no endpoint here calls ResourceRead or ResourceOwner, and "
                    + "the module demands a store. Our authorization is the "
                    + "permission policies, and MembershipPermission decides "
                    + "them out of the table rather than out of claims")

                // Since 4.1.0 the blanket middleware is a module of its own,
                // separated from the policy provider — exactly the separation we
                // were missing. Before that both hung on `Authorization`, and
                // then the choice was: either nobody answers the permission
                // policies, or everything outside the enumerated surface answers
                // 401.
                //
                // `PermissionMiddleware` demands authentication for EVERY
                // request unless the endpoint carries `[AllowAnonymous]` or an
                // `IEndpointAccessPolicy` of its own declares it public.
                // Fail-closed, and right for a system without a public surface.
                // We have one: `/auth/login`, `/auth/register`,
                // `/auth/session`, the careers page under
                // `/companies/by-slug/{k}`, the health probes — and the service
                // entrances behind a secret header instead of behind a token
                // (`/notifications`, `/erasure`, `/internal/notify`).
                //
                // Measured what it costs: with it, `POST /notifications` answers
                // 401 in Noelia's envelope although the secret header is right —
                // the refusal falls before our problem document and before the
                // endpoint.
                //
                // Switching it on would mean declaring our public surface a
                // SECOND time, beside the endpoints and beside
                // `docs/routenkarte.yml`. Two lists over the same question drift
                // apart, and the map is the one that gets driven. On top of
                // that: the middleware reads permissions out of CLAIMS, and our
                // token carries none — so it could only wave through or refuse,
                // never permit.
                //
                // `Authorization` stays and is therefore NOT named here: the
                // PermissionPolicyProvider carries the company permissions.
                .Without(
                    NoeliaModule.PermissionEnforcement,
                    "our public surface stands at the endpoints and in "
                    + "docs/routenkarte.yml; declaring it here a second time "
                    + "would mean two lists over the same question. The policy "
                    + "provider (Authorization) stays");

            additional?.Invoke(noelia);
        });
    }

    /// <summary>Where this service takes its token keys from.</summary>
    /// <remarks>
    /// <para>The private key decides, not a switch in the code. It stands in
    /// the environment of identity-service alone, and so who can mint can be
    /// read in <c>docker-compose.yml</c> and in the chart — a line of code
    /// would be the same statement in a place the operator does not see.</para>
    ///
    /// <para>Without a public key this call throws and names the name. A
    /// service that instead silently refused every token would look like a
    /// broken sign-in.</para>
    /// </remarks>
    /// <param name="jwt">Noelia's key builder.</param>
    /// <param name="configuration">This service's configuration.</param>
    private static void ResolveSigningKeys(JwtBuilder jwt, IConfiguration configuration)
    {
        var id = configuration["Jwt:KeyId"];
        var publicKey = configuration["Jwt:PublicKey"];
        var privateKey = configuration["Jwt:PrivateKey"];

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(publicKey))
        {
            throw new InvalidOperationException(
                "Jwt:KeyId and Jwt:PublicKey are not set. `make env` rolls the "
                + "key pair into .env; in compose and in the chart they come from "
                + "the environment.");
        }

        if (string.IsNullOrWhiteSpace(privateKey))
        {
            jwt.VerifyOnly(publicKey, id);
            return;
        }

        jwt.Issue(privateKey, id);
    }

    /// <summary>
    /// What a service additionally needs in order to sign people in. Identity
    /// only.
    /// </summary>
    /// <remarks>
    /// Password checking and sessions — not the issuing itself: since ADR-0046
    /// that hangs on the private key and therefore on the environment. This
    /// method has a name of its own all the same, so that a search for it finds
    /// exactly one composition root.
    /// </remarks>
    /// <param name="noelia">This service's builder.</param>
    public static NoeliaBuilder AsIssuer(this NoeliaBuilder noelia)
    {
        ArgumentNullException.ThrowIfNull(noelia);

        return noelia
            .Use(NoeliaModule.PasswordHashing)
            .Use(NoeliaModule.TokenSessions);
    }

    /// <summary>The chain, and who decides its order.</summary>
    /// <remarks>
    /// <para><strong>The order belongs to Noelia.</strong> Until recently
    /// Noelia's default chain stood here transcribed, thirteen links long, with
    /// three lines left out — not out of design but out of necessity: until
    /// 4.0.2 the chain knew nothing of the module selection and aborted at
    /// startup without the omission. Since 4.1.0 it reads
    /// <c>NoeliaComposition</c>, so the call without a lambda stands here again:
    /// the same chain every other Noelia service runs, and every deselection
    /// above takes effect once instead of twice.</para>
    ///
    /// <para>Two links come afterwards, and both for a reason:</para>
    ///
    /// <para><c>UseNoeliaPrincipal()</c>, because <c>Principal</c> is not part
    /// of the default chain — it hangs on a decision, and we took that one
    /// above. It stands behind the authentication, because the acting principal
    /// is built out of the <em>verified</em> token.</para>
    ///
    /// <para><see cref="ProblemDetailsMiddleware"/> last, that is, closest to
    /// the endpoints. Noelia's own error handling stands further out and so
    /// catches only what this one passes through. One shape across all services
    /// is the reason: a caller should not have to know who answered in order to
    /// read the error.</para>
    /// </remarks>
    /// <param name="app">The application being built.</param>
    /// <param name="environment">Where it runs.</param>
    /// <param name="serviceName">What it calls itself.</param>
    public static WebApplication UseWorkerTransferDefaults(
        this WebApplication app,
        IHostEnvironment environment,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Noelia's default chain, without a lambda.
        //
        // Thirteen transcribed lines with three omissions stood here. They stood
        // there because until 4.0.2 the chain called every link unconditionally
        // and knew nothing of `Without(...)`: `UseRateLimiting()` would have
        // aborted every service at startup. So the copy was not a design but a
        // necessity — and exactly the kind of duplicate that goes silently wrong
        // at the next Noelia release: if a link is added, it is missing here, no
        // build breaks, no test falls, the chain is simply shorter.
        //
        // Since 4.1.0 the chain reads `NoeliaComposition`. Every deselection
        // above therefore takes effect here too — RateLimiting,
        // HttpResponseCaching and PermissionEnforcement fall away by themselves,
        // and their reason stands in exactly one place.
        app.UseNoelia(environment, serviceName);

        // Noelia's default chain does NOT check revocations, even with a store
        // registered — registered, present, without effect. Behind the
        // authentication, because it reads the verified token's claims.
        app.UseTokenRevocation();

        app.UseNoeliaPrincipal();
        app.UseMiddleware<ProblemDetailsMiddleware>();

        ReportComposition(app, serviceName);

        return app;
    }

    /// <summary>Enters every destination into the sovereignty directory.</summary>
    /// <remarks>
    /// <c>Allow</c> prevents the undeclared call; only <c>DeclareDependency</c>
    /// makes the destination visible in the report. Without this line the egress
    /// register is empty and the fleet view shows nothing. Declaring happens
    /// under the configuration key, so that the report says <em>what for</em> a
    /// host is called.
    /// </remarks>
    private static SovereignPlatformBuilder Declare(
        SovereignPlatformBuilder sovereign, IConfiguration configuration)
    {
        foreach (var (key, address) in CalledDestinations(configuration))
        {
            sovereign.DeclareDependency(key, address);
        }

        return sovereign;
    }

    /// <summary>Configuration key and address of every destination called.</summary>
    private static IEnumerable<(string Key, string Address)> CalledDestinations(
        IConfiguration configuration) =>
        configuration.AsEnumerable()
            .Where(entry => !IsListenAddress(entry.Key)
                              && !string.IsNullOrWhiteSpace(entry.Value)
                              && Uri.TryCreate(entry.Value, UriKind.Absolute, out var target)
                              && (target.Scheme == Uri.UriSchemeHttp
                                  || target.Scheme == Uri.UriSchemeHttps))
            .Select(entry => (entry.Key, entry.Value!))
            .DistinctBy(entry => entry.Item2, StringComparer.OrdinalIgnoreCase)
            .OrderBy(entry => entry.Key, StringComparer.Ordinal);

    /// <summary>Where this service listens — not a destination it calls.</summary>
    private static bool IsListenAddress(string key) =>
        key.Contains("URLS", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("Kestrel", StringComparison.OrdinalIgnoreCase);

    /// <summary>Every host this service calls according to its configuration.</summary>
    /// <remarks>
    /// <para><strong>A derivation, not a list.</strong> The whole configuration
    /// is read; every value that can be read as an absolute http or https
    /// address yields its host. A second list would be the one to go stale first
    /// — and nobody would notice it going stale, because a call that was not
    /// captured is simply refused by the egress boundary.</para>
    ///
    /// <para><strong>It stood somewhere else in the meantime, and the reason
    /// has fallen away.</strong> ADR-0044 pulled it into a <c>Zielkunde</c> of
    /// its own because it had two readers: the egress boundary and
    /// <c>wt.grenze.ziele</c>. With ADR-0045 Noelia's sovereignty section
    /// reports the destinations itself, the second reader is gone — and a
    /// derivation with one reader belongs where it is read.</para>
    ///
    /// <para><strong>Two destinations therefore stand in the environment that
    /// used to stand only in the source</strong> — <c>Draft__BaseUrl</c> and
    /// <c>GitHub__BaseUrl</c>. A built-in default address pointing abroad is
    /// exactly what a sovereignty report exists to surface; leaving it in the
    /// code would mean hiding it from that report.</para>
    /// </remarks>
    private static IReadOnlyList<string> CalledHosts(IConfiguration configuration) =>
    [
        .. configuration.AsEnumerable()
            .Select(entry => entry.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value =>
                Uri.TryCreate(value, UriKind.Absolute, out var address)
                && (address.Scheme == Uri.UriSchemeHttp
                    || address.Scheme == Uri.UriSchemeHttps)
                    ? address.Host
                    : null)
            .Where(host => host is { Length: > 0 })
            .Select(host => host!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(host => host, StringComparer.OrdinalIgnoreCase)
    ];

    /// <summary>Says at startup what this service runs — and what it does not, and why.</summary>
    /// <remarks>
    /// <para><strong>The intent stands in <c>ServiceFoundation.cs</c>, the
    /// reality in the container.</strong> Until 4.0.2 the two could drift apart
    /// without anybody noticing: the chain called every link unconditionally, a
    /// <c>Without(...)</c> took effect only at registration, and whether the
    /// omission really arrived could be seen only from the crash. That is
    /// exactly what <c>NoeliaComposition</c> is for, and a report nobody prints
    /// is not one.</para>
    ///
    /// <para><strong>Printed are only the number of modules in operation and
    /// every omission with its reason.</strong> With nineteen entries the full
    /// list would be the same thing twelve times over in the startup log; the
    /// omissions are what somebody decided about, and only they can deviate from
    /// the intent. Whoever wants to see everything reads
    /// <c>NoeliaComposition.Included</c> out of the container.</para>
    ///
    /// <para>No value travels into the log along the way: a module name is an
    /// enumeration, a reason a sentence we wrote ourselves.</para>
    /// </remarks>
    private static void ReportComposition(WebApplication app, string serviceName)
    {
        var composition = app.Services.GetService<NoeliaComposition>();

        if (composition is null)
        {
            // Do not throw: the report is information, not a promise. A
            // service that fails to start over a missing piece of information
            // trades a small problem for a big one.
            app.Logger.LogWarning(
                "Noelia reports no composition for {Service} — the report stays empty",
                serviceName);

            return;
        }

        // WITH NAMES, not only with a number. "19 modules in operation" reads
        // like a confirmation and is none: that a decision from above really
        // arrived can be seen only from the name. Measured on 09.09.2026, the
        // same 19 stood before and after a new `AddSovereignPlatform` line — the
        // number could not show the difference it existed to report.
        app.Logger.LogInformation(
            "Noelia for {Service}: {Included} modules in operation ({Modules}), {Excluded} left out",
            serviceName,
            composition.Included.Count,
            string.Join(", ", composition.Included),
            composition.Excluded.Count);

        foreach (var (module, reason) in composition.Excluded)
        {
            app.Logger.LogInformation(
                "Noelia module {Module} not in operation: {Reason}", module, reason);
        }
    }
}
