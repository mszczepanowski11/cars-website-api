using System.Net;
using System.Net.Sockets;

// Shared connect-time IP allowlist for any HttpClient that fetches a caller-supplied URL
// (currently the Partner feed fetcher, driven by the public "Dla firm" signup form). The check
// runs in SocketsHttpHandler's ConnectCallback - i.e. against the IP actually being connected to,
// not just the DNS name resolved earlier - so a DNS-rebinding attack (resolve to a public IP
// during a first check, then to a private one at actual connect time) can't bypass it.
public static class SsrfGuard
{
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        ConnectCallback = async (context, cancellationToken) =>
        {
            var entry = await Dns.GetHostEntryAsync(context.DnsEndPoint.Host, cancellationToken);
            var address = Array.Find(entry.AddressList, IsPublicAddress)
                ?? throw new HttpRequestException("Adres docelowy jest niedozwolony.");

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    public static bool IsPublicAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return false;
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return false;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 10) return false;                                  // 10.0.0.0/8
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;      // 172.16.0.0/12
            if (b[0] == 192 && b[1] == 168) return false;                  // 192.168.0.0/16
            if (b[0] == 169 && b[1] == 254) return false;                  // 169.254.0.0/16 link-local
            if (b[0] == 127) return false;                                 // 127.0.0.0/8
            if (b[0] == 0) return false;                                   // 0.0.0.0/8
            if (b[0] >= 224) return false;                                 // multicast/reserved
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return false;                       // fc00::/7 unique local
            return true;
        }

        return false;
    }
}
