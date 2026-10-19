using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Noelia.Infrastructure.Audit;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>What proves that somebody may see the operations surface.</summary>
/// <remarks>
/// <para><strong>A piece of paper of its own, and that is a decision.</strong>
/// The internal doors have their secret (<c>Notify</c>), the erasure cascade
/// has its own (<c>Erasure</c>), and already there the rule holds: "may search
/// profiles" and "may delete everything about a person" must not be the same
/// piece of paper. <c>/noelia</c> is a third permission — it holds nothing
/// about a person, but it tells a stranger which providers this instance uses
/// and where its doors stand open. Whoever may trigger mails does not have to
/// learn that.</para>
///
/// <para><strong>Empty means the door is shut.</strong> An unset variable is
/// the doubtful case, and in doubt it is shut. Noelia's dashboard then answers
/// <strong>404</strong> rather than 403 — an operations surface whose existence
/// you can guess is already a disclosure in itself.</para>
///
/// <para><strong>The name stayed, the content did not.</strong> Until ADR-0045
/// the setup for seven hand-built <c>/nachweis/…</c> addresses stood here.
/// Noelia delivers those; what remains is the door in front of them and the
/// registration of the checks this product answers itself.</para>
/// </remarks>
public sealed class EvidenceSecret
{
    /// <summary>The section this binds to.</summary>
    public const string Section = "Evidence";

    /// <summary>The header the secret is presented in.</summary>
    /// <remarks>
    /// The name the control plane sends. A name of our own here would mean it
    /// cannot collect this fleet.
    /// </remarks>
    public const string Header = "X-Noelia-Operator";

    /// <summary>The secret. Empty means shut.</summary>
    /// <remarks>
    /// The property name IS the environment key (<c>Evidence__Secret</c>); renaming
    /// it alone leaves the value on its default, silently.
    /// </remarks>
    public string Secret { get; set; } = string.Empty;
}

/// <summary>Hangs the evidence onto the service foundation.</summary>
/// <remarks>
/// <para>What stands here stands here because it is the same for every
/// service: the door. Which checks a service answers is a decision of
/// <em>that</em> service and stands in its composition root, where a reader
/// sees it — the same line <c>AddWorkerTransferDefaults</c> draws for the
/// modules.</para>
///
/// <para><strong>Noelia's checks are singletons</strong>
/// (<c>TryAddEnumerable(ServiceDescriptor.Singleton&lt;ISecurityCheck, …&gt;)</c>).
/// Whoever registers one of their own and pulls something scoped into it builds
/// a captive dependency — a <c>DbContext</c> that lives for ever. The checks
/// here therefore take the <c>IServiceProvider</c> and open a scope of their
/// own.</para>
/// </remarks>
public static class EvidenceSetup
{
    /// <summary>The door in front of the operations surface.</summary>
    /// <param name="services">The container.</param>
    /// <param name="configuration">The service's configuration.</param>
    /// <returns>The container.</returns>
    public static IServiceCollection AddEvidence(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<EvidenceSecret>(
            configuration.GetSection(EvidenceSecret.Section));

        // The dashboard records who read it and refuses to answer when it
        // cannot. Without this line the access check reports a warning instead
        // of a pass.
        services.AddSovereignAuditTrail();

        return services;
    }

    /// <summary>May this caller see the operations surface?</summary>
    /// <remarks>
    /// <para><c>FixedTimeEquals</c> and not <c>==</c>: a comparison that stops
    /// at the first wrong character gives away through its duration how many
    /// characters were right. The same comparison as at every other internal
    /// door of this tree.</para>
    ///
    /// <para>It is <c>public</c> because Noelia's <c>VisibleTo(...)</c> takes
    /// it as a predicate — and <c>static</c>, because the predicate is handed
    /// over at composition time and must hold no state.</para>
    /// </remarks>
    /// <param name="context">The request.</param>
    /// <returns>Whether it is let in.</returns>
    public static bool IsOperator(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var secret = context.RequestServices
            .GetRequiredService<IOptions<EvidenceSecret>>().Value.Secret;

        if (string.IsNullOrEmpty(secret)
            || !context.Request.Headers.TryGetValue(
                EvidenceSecret.Header, out var presented))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented.ToString()),
            Encoding.UTF8.GetBytes(secret));
    }
}
