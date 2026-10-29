using System.Net;
using System.Net.Sockets;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>Connects only to a public address — for hosts a company named, never ours.</summary>
/// <remarks>
/// <para><strong>A company's domain must not point inside.</strong> Whoever
/// resolves their domain to <c>10.0.0.5</c> would otherwise reach the services
/// in the cluster through a read we make for them — a company's feed
/// (ADR-0052) or a company's own model server (ADR-0051). The check runs in the
/// connect step, on the addresses actually used, so a second resolution cannot
/// slip past.</para>
///
/// <para>Used as a <see cref="SocketsHttpHandler.ConnectCallback"/> on the
/// named clients that reach company hosts, with automatic redirects off.</para>
/// </remarks>
public static class PublicAddress
{
    /// <summary>Connects to the first address — if every address of the host is public.</summary>
    /// <param name="context">The connection the handler wants.</param>
    /// <param name="cancellationToken">Cancels the connect.</param>
    /// <returns>The connected stream.</returns>
    /// <exception cref="HttpRequestException">The host also resolves inside.</exception>
    public static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);

        // All of them public, or none is used: a host that also resolves inside
        // is the trick, not an accident.
        if (addresses.Length == 0 || !addresses.All(IsPublic))
        {
            throw new HttpRequestException("The host does not resolve to public addresses only.");
        }

        var socket = new Socket(addresses[0].AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            await socket.ConnectAsync(new IPEndPoint(addresses[0], context.DnsEndPoint.Port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Whether an address lies outside every internal range.</summary>
    /// <param name="address">The address.</param>
    /// <returns><c>false</c> for loopback, private, link-local, carrier-grade and multicast.</returns>
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var first = address.GetAddressBytes()[0];
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
                     || (first & 0xfe) == 0xfc);
        }

        var b = address.GetAddressBytes();
        return !(b[0] == 10
                 || b[0] == 0
                 || b[0] == 127
                 || (b[0] == 172 && b[1] is >= 16 and <= 31)
                 || (b[0] == 192 && b[1] == 168)
                 || (b[0] == 169 && b[1] == 254)
                 || (b[0] == 100 && b[1] is >= 64 and <= 127)
                 || b[0] >= 224);
    }
}
