using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WorkerTransfer.ServiceDefaults.Roles;

/// <summary>Which permissions this service gives only to an <c>admin</c>.</summary>
/// <remarks>
/// <para><strong>One list per service, not a shared one.</strong> What binds a
/// company is known only to the service the operation belongs to —
/// "publishing" is a statement to the outside world in jobs-service and nothing
/// at all elsewhere. A shared list would be the second place where the same
/// question is answered, and two of them drift apart.</para>
///
/// <para><strong>Whatever does NOT stand in this list, every member may
/// do.</strong> That is the default and it is deliberate: a permission that is
/// too narrow turns an invitation into a spectator seat, and then somebody
/// creates a second admin in order to work — and "admin" is a word in a table
/// again. A member therefore needs no policy either: the check "are you acting
/// for a company?" already stands at the endpoint.</para>
/// </remarks>
public sealed class AdminPermissions
{
    private readonly HashSet<string> _permissions;

    /// <summary>With the permissions that belong to this service.</summary>
    /// <param name="permissions">Every permission an <c>admin</c> needs.</param>
    public AdminPermissions(IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        _permissions = [.. permissions];
    }

    /// <summary>Does this permission demand an <c>admin</c>?</summary>
    /// <param name="permission">The name out of the policy.</param>
    public bool RequiresAdmin(string permission) => _permissions.Contains(permission);

    /// <summary>Every permission this service knows — for tests and reports.</summary>
    public IReadOnlyCollection<string> All => _permissions;
}

/// <summary>Registers a service's company permissions.</summary>
public static class AdminPermissionExtensions
{
    /// <summary>Noelia's prefix for policies resolved at run time.</summary>
    /// <remarks>
    /// Its <c>PermissionPolicyProvider</c> builds a policy at run time out of
    /// every policy name spelled this way. That is why none has to be
    /// registered in advance — and why it matters that the provider runs at
    /// all: without it nobody answers those names, and the framework refuses
    /// <em>every</em> request to the protected endpoint.
    /// </remarks>
    public const string Prefix = "Permission:";

    /// <summary>The policy name for a permission.</summary>
    /// <param name="permission">The permission, for example <c>jobs.publish</c>.</param>
    public static string Policy(string permission) => Prefix + permission;

    /// <summary>
    /// Wires up the role lookup and the handler that answers the named
    /// permissions out of it.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where <c>Identity:Adresse</c> comes from.</param>
    /// <param name="adminOnly">The permissions that demand an <c>admin</c>.</param>
    /// <remarks>
    /// Not in <c>AddWorkerTransferDefaults</c>, and that is the same separation
    /// as everywhere here: the MECHANISM must not be answered eleven times, the
    /// LIST is a decision and stands where a reader sees it.
    /// </remarks>
    public static IServiceCollection AddAdminPermissions(
        this IServiceCollection services,
        IConfiguration configuration,
        params string[] adminOnly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(adminOnly);

        if (adminOnly.Length == 0)
        {
            // A service that calls this line and names nothing forgot to fill
            // it — and would get a wiring that never decides anything. That
            // looks like protection.
            throw new ArgumentException(
                "Without a single permission this wiring has no effect.", nameof(adminOnly));
        }

        services.Configure<CompanyRoleSettings>(
            configuration.GetSection(CompanyRoleSettings.Section));

        services.AddHttpClient(HttpCompanyRoles.ClientName);
        services.AddSingleton(new AdminPermissions(adminOnly));
        services.TryAddScoped<ICompanyRoles, HttpCompanyRoles>();
        services.AddScoped<IAuthorizationHandler, AdminPermission>();

        return services;
    }
}
