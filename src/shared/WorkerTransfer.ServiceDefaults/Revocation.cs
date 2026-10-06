using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Noelia.Abstractions.Hosting;
using Noelia.InMemory.Security;
using Noelia.Redis;
using StackExchange.Redis;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>Where a revoked token is written, so every service refuses it.</summary>
/// <remarks>
/// <para><strong>One store for all fourteen services, or a sign-out reaches one
/// of them.</strong> identity-service ends a sign-in; the other thirteen verify
/// the access token by its signature alone and honoured it until it expired —
/// up to fifteen minutes after a sign-out or an account erasure. Valkey is the
/// shared store (VISION-FRAGEN I1): BSD-licensed, wire-compatible with the
/// client Noelia uses, and Redis itself is no longer open source since 2024.</para>
///
/// <para><strong>Only the connection is registered, not
/// <c>AddRedisConnection</c>.</strong> That one also installs a distributed
/// cache, and a cache switched on as a side effect is the kind of decision this
/// tree makes per service or not at all (ADR-0013).</para>
///
/// <para><strong>Without the key the service refuses to start in Staging and
/// Production</strong> and names the key. Elsewhere — the test hosts — it keeps
/// revocations in the process: right for a single process, and a sign-out there
/// is still refused by that process.</para>
/// </remarks>
public static class Revocation
{
    /// <summary>The connection string's name: <c>ConnectionStrings__valkey</c>.</summary>
    public const string ConnectionName = "valkey";

    /// <summary>Registers the revocation store this service reads and writes.</summary>
    /// <param name="noelia">This service's composition.</param>
    /// <param name="configuration">This service's configuration.</param>
    /// <param name="environment">Where it runs.</param>
    public static NoeliaBuilder UseTokenRevocationStore(
        this NoeliaBuilder noelia,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(noelia);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var connection = configuration.GetConnectionString(ConnectionName);

        if (string.IsNullOrWhiteSpace(connection))
        {
            if (environment.IsProduction() || environment.IsStaging())
            {
                throw new InvalidOperationException(
                    $"ConnectionStrings:{ConnectionName} is not set. Without a shared store "
                    + "a sign-out reaches only identity-service, and every other service "
                    + "honours the old token until it expires.");
            }

            noelia.Services.AddInMemoryTokenRevocation();
            return noelia;
        }

        noelia.Services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(connection);

            // Start even while Valkey is still coming up; the first check then
            // fails and the request is refused rather than waved through.
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });

        // An entry must outlive the token it revokes, or the token outlives
        // its own revocation.
        return noelia.UseRedisTokenRevocation(AccessTokenLifetime(configuration));
    }

    /// <summary>How long an access token is honoured: <c>JwtSettings:ExpireMinutes</c>, 15 by default.</summary>
    public static TimeSpan AccessTokenLifetime(IConfiguration configuration) =>
        TimeSpan.FromMinutes(
            int.TryParse(configuration["JwtSettings:ExpireMinutes"], out var minutes) && minutes > 0
                ? minutes
                : 15);
}
