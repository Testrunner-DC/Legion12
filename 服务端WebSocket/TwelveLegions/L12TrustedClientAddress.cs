using System.Net;
using Microsoft.AspNetCore.Http;

namespace TwelveLegions.Server;

/// <summary>Resolve only the deployed local-Nginx → optional Cloudflare proxy chain.</summary>
internal static class L12TrustedClientAddress
{
    // Cloudflare's published proxy networks, checked 2026-09-07. No request-controlled
    // additions; unknown proxies stop traversal. Review these during infrastructure changes.
    // https://www.cloudflare.com/ips-v4/ and https://www.cloudflare.com/ips-v6/
    private static readonly IPNetwork[] CloudflareNetworks = new[]
    {
        "173.245.48.0/20", "103.21.244.0/22", "103.22.200.0/22", "103.31.4.0/22",
        "141.101.64.0/18", "108.162.192.0/18", "190.93.240.0/20", "188.114.96.0/20",
        "197.234.240.0/22", "198.41.128.0/17", "162.158.0.0/15", "104.16.0.0/13",
        "104.24.0.0/14", "172.64.0.0/13", "131.0.72.0/22", "2400:cb00::/32",
        "2606:4700::/32", "2803:f800::/32", "2405:b500::/32", "2405:8100::/32",
        "2a06:98c0::/29", "2c0f:f248::/32",
    }.Select(IPNetwork.Parse).ToArray();

    internal static IPAddress? Resolve(HttpContext context)
    {
        var peer = Normalize(context.Connection.RemoteIpAddress);
        // Internet clients cannot make themselves a trusted proxy through headers.
        if (peer is null || !IPAddress.IsLoopback(peer)) return peer;
        var header = context.Request.Headers["X-Forwarded-For"].ToString();
        if (header.Length is 0 or > 2048) return peer;
        var hops = header.Split(',', StringSplitOptions.TrimEntries);
        if (hops.Length > 16) return peer;
        // Nginx appends its real socket peer on the right. Discard any forged prefix
        // once the first untrusted address is reached. CF-Connecting-IP is not trusted.
        for (var i = hops.Length - 1; i >= 0; i--)
        {
            if (hops[i].Contains('%') || !IPAddress.TryParse(hops[i], out var parsed)) return peer;
            var address = Normalize(parsed)!;
            if (IPAddress.IsLoopback(address)) return peer;
            if (!CloudflareNetworks.Any(network => network.Contains(address))) return address;
        }
        return peer; // Incomplete chain: never manufacture an end-user identity.
    }

    private static IPAddress? Normalize(IPAddress? address)
        => address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4() : address;
}
