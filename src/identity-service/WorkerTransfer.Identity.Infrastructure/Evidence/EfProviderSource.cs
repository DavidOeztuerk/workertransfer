using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults.Checks;

namespace WorkerTransfer.Identity.Infrastructure.Evidence;

/// <summary>Which AI providers the people of this instance have entered.</summary>
/// <remarks>
/// <para><strong>This is the line no test can write.</strong>
/// <c>Adr0022Tests</c> and <c>EntwurfsgrenzeTests</c> check the tree; the AI
/// access lives in <c>KiZugangV1</c> — provider, address, model and key per
/// person, at run time out of the account settings. Where this tree speaks
/// tonight stands in this table and nowhere else.</para>
///
/// <para><strong>Counted, never named.</strong> The query groups by provider
/// and address and answers with a number; <c>subject_id</c> does not leave it,
/// and the address loses everything but the host. Who uses which provider is a
/// statement about a human being and does not belong in a document somebody
/// passes around — ADR-0026 says the same for events.</para>
///
/// <para><strong>And the key is not even read.</strong> The query selects three
/// columns; <c>ai_key_encrypted</c> is not one of them. What is not fetched
/// cannot end up in a finding by accident either.</para>
///
/// <para><c>none</c> drops out: "no provider" is no recipient, and a row about
/// it would be an entry about a recipient that does not exist.</para>
/// </remarks>
/// <param name="provider">
/// The container. The context is resolved in a scope of its own: Noelia's
/// checks are singletons, the <c>DbContext</c> is scoped — taking it directly
/// would be a captive dependency, and the container then aborts at startup.
/// </param>
public sealed class EfProviderSource(IServiceProvider provider) : IProviderSource
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderRow>> ReadAsync(CancellationToken ct = default)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var groups = await context.AccountSettings
            .Where(row => row.AiProvider != "none" && row.AiBaseUrl != "")
            .GroupBy(row => new { row.AiProvider, row.AiBaseUrl })
            .Select(group => new
            {
                group.Key.AiProvider,
                group.Key.AiBaseUrl,
                People = group.Count()
            })
            .ToListAsync(ct);

        return
        [
            .. groups
                .Select(group => new
                {
                    group.AiProvider,
                    group.People,
                    Host = Uri.TryCreate(group.AiBaseUrl, UriKind.Absolute, out var target)
                        ? target.Host
                        : string.Empty
                })
                .Where(group => group.Host.Length > 0)
                .Select(group => new ProviderRow(
                    group.AiProvider, group.Host, ProviderOrigin.Person, group.People))
        ];
    }
}
