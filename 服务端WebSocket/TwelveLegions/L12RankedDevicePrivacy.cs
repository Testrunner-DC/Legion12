using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace TwelveLegions.Server;

// A signed, first-party browser installation token, not an IP or hardware fingerprint.
// Clearing cookies / using another browser can evade it; it is never proof of cheating.
internal sealed class L12RankedDevicePrivacy
{
    internal const string CookieName = "l12-ranked-browser";
    private readonly byte[] _key;

    internal L12RankedDevicePrivacy(string? secret)
        => _key = !string.IsNullOrWhiteSpace(secret) && Encoding.UTF8.GetByteCount(secret) >= 32
            ? SHA256.HashData(Encoding.UTF8.GetBytes("l12-browser-v1:" + secret))
            : RandomNumberGenerator.GetBytes(32);

    internal string Issue()
    {
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        return id + "." + Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
    }

    internal string? Read(string? cookie)
    {
        if (cookie is null || cookie.Length != 129 || cookie[64] != '.') return null;
        if (!cookie[..64].All(Uri.IsHexDigit) || !cookie[65..].All(Uri.IsHexDigit)) return null;
        var expected = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(cookie[..64]));
        if (!CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(cookie[65..]))) return null;
        return "browser-v1:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cookie[..64]))).ToLowerInvariant();
    }

    internal void EnsureCookie(HttpContext context)
    {
        if (Read(context.Request.Cookies[CookieName]) is not null) return;
        var local = context.Request.Host.Host is "localhost" or "127.0.0.1" or "[::1]";
        context.Response.Cookies.Append(CookieName, Issue(), new CookieOptions
        {
            HttpOnly = true, Secure = !local, SameSite = SameSiteMode.Lax,
            Path = "/", MaxAge = TimeSpan.FromDays(180), IsEssential = true,
        });
    }
}
