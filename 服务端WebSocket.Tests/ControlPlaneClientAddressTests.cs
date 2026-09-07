using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class ControlPlaneClientAddressTests
{
    [Theory]
    [InlineData("127.0.0.1", "192.0.2.10", "192.0.2.10")]
    [InlineData("::ffff:127.0.0.1", "192.0.2.10, 173.245.48.10", "192.0.2.10")]
    [InlineData("::1", "2001:db8::10, 2606:4700::1", "2001:db8::10")]
    [InlineData("127.0.0.1", "192.0.2.9, 192.0.2.10, 173.245.48.10", "192.0.2.10")]
    [InlineData("127.0.0.1", "192.0.2.9, 198.51.100.10", "198.51.100.10")]
    [InlineData("192.0.2.10", "192.0.2.9, 173.245.48.10", "192.0.2.10")]
    [InlineData("173.245.48.10", "192.0.2.9", "173.245.48.10")]
    [InlineData("::ffff:192.0.2.10", "192.0.2.9", "192.0.2.10")]
    [InlineData("127.0.0.1", "192.0.2.10, invalid", "127.0.0.1")]
    [InlineData("127.0.0.1", "192.0.2.10, 127.0.0.1", "127.0.0.1")]
    [InlineData("127.0.0.1", "173.245.48.10", "127.0.0.1")]
    [InlineData("127.0.0.1", "", "127.0.0.1")]
    public void ResolvesOnlyTrustedProxySuffix(string peer, string forwarded, string expected)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Headers["X-Forwarded-For"] = forwarded;
        context.Request.Headers["X-Real-IP"] = "192.0.2.222";
        context.Request.Headers["CF-Connecting-IP"] = "192.0.2.222";
        Assert.Equal(expected, L12TrustedClientAddress.Resolve(context)?.ToString());
    }

    [Fact]
    public void RejectsOversizedOrOverlongProxyChains()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        foreach (var header in new[] { new string('1', 2049), string.Join(',', Enumerable.Repeat("192.0.2.10", 17)) })
        {
            context.Request.Headers["X-Forwarded-For"] = header;
            Assert.Equal(IPAddress.Loopback, L12TrustedClientAddress.Resolve(context));
        }
    }

    [Fact]
    public async Task ProxyLoginFailuresDoNotLockOtherClientsButForgedPrefixesCannotEvadeLimits()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-client-address-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            await using var recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks);
            store.Register("ProxyPlayerA", "test-password-123");
            store.Register("ProxyPlayerB", "test-password-456");
            await using var server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            async Task<HttpStatusCode> Login(string name, string password, string forwarded)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
                request.Content = JsonContent.Create(new { username = name, password });
                request.Headers.Add("X-Forwarded-For", forwarded);
                request.Headers.Add("CF-Connecting-IP", "192.0.2.222"); // ignored: untrusted header
                using var response = await client.SendAsync(request);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    Assert.NotNull(response.Headers.RetryAfter);
                return response.StatusCode;
            }
            for (var i = 0; i < 5; i++)
                Assert.Equal(i < 4 ? HttpStatusCode.Unauthorized : HttpStatusCode.TooManyRequests,
                    await Login("ProxyPlayerA", "wrong", "192.0.2.10, 173.245.48.10"));
            Assert.Equal(HttpStatusCode.OK,
                await Login("ProxyPlayerB", "test-password-456", "192.0.2.11, 173.245.48.10"));
            Assert.Equal(HttpStatusCode.TooManyRequests,
                await Login("ProxyPlayerA", "test-password-123", "192.0.2.11, 173.245.48.10"));
            Assert.Equal(HttpStatusCode.TooManyRequests,
                await Login("ProxyPlayerB", "test-password-456", "192.0.2.250, 192.0.2.10, 173.245.48.10"));
            await server.StopAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }
}
