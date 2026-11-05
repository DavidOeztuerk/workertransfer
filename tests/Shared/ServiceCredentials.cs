using System.Runtime.CompilerServices;
using Noelia.Infrastructure.Security.ServiceTokens;

namespace WorkerTransfer.Tests.Shared;

/// <summary>
/// The service credentials of a test process, compiled into every suite whose
/// host needs them (ADR-0057).
/// </summary>
/// <remarks>
/// A composing service reads these values at startup and refuses to start
/// without them, in a test host as in operation. Set once per process instead
/// of in each of the 48 test files that build a host. They go through the
/// environment because that is where the services read them from.
/// </remarks>
internal static class ServiceCredentials
{
    /// <summary>The clients identity-service knows, each with a secret of its own.</summary>
    public static readonly IReadOnlyDictionary<string, ServiceClientSecret> Clients =
        new[]
        {
            "advisor", "applications", "assessment", "companies", "jobs", "messages",
            "notification", "profile", "resume", "scout", "transfer"
        }.ToDictionary(id => id, ServiceClientSecret.Generate);

#pragma warning disable CA2255 // A test process has no caller that could order this differently.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Install()
    {
        foreach (var (id, secret) in Clients)
        {
            Environment.SetEnvironmentVariable(
                $"NOELIA_M2M_CLIENT_{id.ToUpperInvariant()}_SECRET_HASH", secret.Hash);
        }

        Environment.SetEnvironmentVariable(
            "NOELIA_M2M_TOKEN_ENDPOINT", "http://identity-service:8001/m2m/token");
        Environment.SetEnvironmentVariable("NOELIA_M2M_CLIENT_ID", "test-caller");
        Environment.SetEnvironmentVariable(
            "NOELIA_M2M_CLIENT_SECRET", ServiceClientSecret.Generate("test-caller").Secret);
    }
}
