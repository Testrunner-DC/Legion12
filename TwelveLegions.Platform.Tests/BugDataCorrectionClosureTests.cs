using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class BugDataCorrectionClosureTests
{
    [Fact]
    public async Task OriginalHttpMutationPreviewCasReplayAndReloadKeepTruthfulDataTerminal()
    {
        await using var f = await Fixture.Create();
        var original = f.Bug;
        using var preview = await f.Patch(f.Body(dryRun: true));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(f.CurrentBug()));
        var stale = f.Store.Version;
        f.Store.AddBug(f.Admin, "合成并发版本", "非业务数据", "/synthetic", null, null, "synthetic");
        using var conflict = await f.Patch(f.Body(version: stale));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var body = f.Body();
        using var applied = await f.Patch(body);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var closed = (await applied.Content.ReadFromJsonAsync<L12BugReportView>())!;
        Assert.Equal("resolved", closed.Status);
        Assert.Equal("data_corrected_verified", closed.ClosureDisposition);
        Assert.Null(closed.FixCommit);
        Assert.Null(closed.DuplicateOf);
        Assert.Equal(f.Admin.Username, closed.VerifiedBy);
        Assert.NotNull(closed.VerifiedAt);
        Assert.Equal(f.Running, closed.DeployedVersion);
        Assert.Equal(Fixture.CheckName, closed.RegressionTest);
        Assert.Equal(original.Assignee, closed.Assignee);
        Assert.Equal(original.AdminNotes, closed.AdminNotes);
        Assert.Equal(original.Priority, closed.Priority);
        Assert.Equal(f.Proof, Assert.Single(closed.History.Where(row => row.Action == "data-correction-proof")).Comment);
        foreach (var entry in original.History) Assert.Contains(closed.History, row => row == entry);
        using var replay = await f.Patch(body);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues("X-Idempotent-Replay")));
        Assert.Equal(closed.History.Count, (await replay.Content.ReadFromJsonAsync<L12BugReportView>())!.History.Count);
        var reloaded = new L12PlatformStore(f.Host.MirrorPath);
        var persisted = reloaded.Bugs(null).Single(row => row.Id == f.Bug.Id);
        Assert.Equal(closed.ClosureDisposition, persisted.ClosureDisposition);
        Assert.Equal(closed.VerifiedBy, persisted.VerifiedBy);
        Assert.Equal(closed.VerifiedAt, persisted.VerifiedAt);
        Assert.Equal(f.Proof, persisted.History.Single(row => row.Action == "data-correction-proof").Comment);
        f.AssertArtUnchanged();
    }

    [Fact]
    public async Task MissingUnknownDuplicateAndUnboundProofFactsRejectBeforeBugMutation()
    {
        await using var f = await Fixture.Create();
        var badProofs = new List<string> { "{}", " " + f.Proof, f.Proof + " ", new('x', 2001),
            f.Proof.Replace("\"schema\":1", "\"schema\":1,\"schema\":1", StringComparison.Ordinal) };
        foreach (var mutate in new Action<JsonObject>[] {
                     node => node.Remove("auditId"), node => node["verified"] = true,
                     node => node["schema"] = 2, node => node["auditId"] = Guid.NewGuid().ToString("N"),
                     node => node["bugId"] = "BUG-20261027-1234abcd",
                     node => node["alternateArtId"] = Guid.NewGuid().ToString("N"),
                     node => node["artCode"] = "WRONG-ART",
                     node => node["before"]!["baseCardId"] = "WRONG-BEFORE",
                     node => node["after"]!["displayName"] = "伪造名称",
                     node => node["after"]!["baseCardId"] = f.Before.BaseCardId,
                     node => node["localEvidence"]!["readbackReceiptSha256"] = "not-a-hash",
                     node => node["localEvidence"]!["verifiedBy"] = "forged" })
        {
            var proof = JsonNode.Parse(f.Proof)!.AsObject(); mutate(proof); badProofs.Add(proof.ToJsonString());
        }
        foreach (var proof in badProofs) await f.AssertRejected(f.Body(proof: proof));
    }

    [Fact]
    public async Task WrongDispositionEvidenceIdentityAndNormalizationRejectBeforeMutation()
    {
        await using var f = await Fixture.Create();
        foreach (var (key, value) in new (string, object?)[] {
                     ("status", "closed"), ("status", "retest"), ("fixCommit", "not-a-code-fix"),
                     ("duplicateOf", f.Bug.Id), ("regressionTest", ""),
                     ("regressionTest", " padded "), ("regressionTest", new string('x', 501)),
                     ("deployedVersion", "not-the-running-version"),
                     ("deployedVersion", " " + f.Running), ("closureDisposition", " data_corrected_verified "),
                     ("verifiedBy", "forged-client"), ("verifiedAt", DateTimeOffset.UtcNow) })
        {
            var body = f.Body(); body[key] = value; await f.AssertRejected(body);
        }
        foreach (var (token, status) in new[] { ((string?)null, HttpStatusCode.Unauthorized),
                     (f.Host.PlayerToken, HttpStatusCode.Forbidden), (f.Host.RevokedToken, HttpStatusCode.Unauthorized) })
        {
            using var denied = await f.Patch(f.Body(), token, explicitToken: true);
            Assert.Equal(status, denied.StatusCode);
            Assert.Equal(JsonSerializer.Serialize(f.Bug), JsonSerializer.Serialize(f.CurrentBug()));
        }
    }

    [Fact]
    public async Task GenuineAuditWithForbiddenAdditionalDeltaAndNewerCurrentRowAreRejected()
    {
        await using (var forbidden = await Fixture.Create(forbiddenDelta: true))
            await forbidden.AssertRejected(forbidden.Body());
        await using var f = await Fixture.Create();
        f.Store.SaveAlternateArt(f.Admin, new(f.After.Id, f.After.ArtCode, f.After.BaseCardId,
            f.After.DisplayName, f.After.MediaAssetId, false));
        await f.AssertRejected(f.Body(), assertArt: false);
    }

    [Fact]
    public async Task UnqualifiedRealAuditAndCorruptAuthoritativeLedgerCannotBecomeProof()
    {
        await using (var f = await Fixture.Create())
        {
            // This real direct-store update has no authenticated HTTP permission/method/path facts.
            f.Store.SaveAlternateArt(f.Admin, new(f.After.Id, f.After.ArtCode, f.Before.BaseCardId,
                f.Before.DisplayName, f.After.MediaAssetId));
            var unqualified = f.Store.AdminAudit("alternate-art", 1000, "succeeded")
                .First(row => row.Target == f.After.Id && row.Action == "update" && row.Permission is null);
            var proof = JsonNode.Parse(f.Proof)!.AsObject(); proof["auditId"] = unqualified.Id;
            await f.AssertRejected(f.Body(proof: proof.ToJsonString()), assertArt: false);
        }
        await using var corrupted = await Fixture.Create();
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
                   DataSource = Path.ChangeExtension(corrupted.Host.MirrorPath, ".db"), Pooling = false }.ToString()))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "UPDATE admin_audit_events SET payload_sha256=$bad WHERE id=$id";
            command.Parameters.AddWithValue("$bad", "deliberately-invalid-negative-fixture");
            command.Parameters.AddWithValue("$id", corrupted.Audit.Id);
            Assert.Equal(1, command.ExecuteNonQuery());
        }
        Assert.Throws<InvalidDataException>(() => corrupted.Store.UpdateBug(corrupted.Admin, corrupted.Bug.Id,
            "resolved", corrupted.Bug.Priority, corrupted.Bug.Assignee, corrupted.Bug.AdminNotes, corrupted.Proof,
            regressionTest: Fixture.CheckName, deployedVersion: corrupted.Running,
            closureDisposition: "data_corrected_verified"));
        Assert.Equal(JsonSerializer.Serialize(corrupted.Bug), JsonSerializer.Serialize(corrupted.CurrentBug()));
    }

    [Fact]
    public async Task TerminalMetadataEditsKeepProofAndReopenClearsOnlyVerificationNotHistory()
    {
        await using var f = await Fixture.Create();
        using var applied = await f.Patch(f.Body());
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var closed = f.CurrentBug();
        var changed = f.Store.UpdateBug(f.Admin, f.Bug.Id, "resolved", "critical", f.Bug.Assignee,
            f.Bug.AdminNotes, "ordinary later handling note")!;
        Assert.Equal(closed.VerifiedAt, changed.VerifiedAt);
        Assert.Equal(closed.VerifiedBy, changed.VerifiedBy);
        Assert.Equal(f.Proof, changed.History.Single(row => row.Action == "data-correction-proof").Comment);
        Assert.Throws<L12BugClosureValidationException>(() => f.Store.UpdateBug(f.Admin, f.Bug.Id,
            "resolved", null, f.Bug.Assignee, f.Bug.AdminNotes, regressionTest: "replace-old-proof"));
        var reopened = f.Store.UpdateBug(f.Admin, f.Bug.Id, "retest", null, f.Bug.Assignee, f.Bug.AdminNotes)!;
        Assert.Null(reopened.ClosureDisposition); Assert.Null(reopened.VerifiedBy); Assert.Null(reopened.VerifiedAt);
        Assert.Contains(reopened.History, row => row.Action == "data-correction-proof" && row.Comment == f.Proof);
    }

    [Fact]
    public void DateMetadataNormalizesEquivalentInstantsButNotChangedValuesOrArbitraryText()
    {
        var method = typeof(L12PlatformStore).GetMethod("NormalizeDataCorrectionDates", BindingFlags.NonPublic | BindingFlags.Static)!;
        JsonNode Normalize(string json) => (JsonNode)method.Invoke(null, [JsonNode.Parse(json)!])!;
        const string utc = "{\"CreatedAt\":\"2026-10-26T16:52:44.2037600+00:00\",\"Products\":[{\"UpdatedAt\":\"2026-10-26T16:52:44.2037600Z\"}],\"Artist\":\"unchanged\"}";
        const string local = "{\"CreatedAt\":\"2026-10-27T00:52:44.20376+08:00\",\"Products\":[{\"UpdatedAt\":\"2026-10-27T00:52:44.2037600+08:00\"}],\"Artist\":\"unchanged\"}";
        Assert.True(JsonNode.DeepEquals(Normalize(utc), Normalize(local)));
        Assert.False(JsonNode.DeepEquals(Normalize(utc), Normalize(local.Replace(".20376+", ".20377+", StringComparison.Ordinal))));
        Assert.False(JsonNode.DeepEquals(Normalize(utc), Normalize(local.Replace("unchanged", "changed", StringComparison.Ordinal))));
        var error = Assert.Throws<TargetInvocationException>(() => Normalize(local.Replace("+08:00", "", StringComparison.Ordinal)));
        Assert.IsType<L12BugClosureValidationException>(error.InnerException);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal const string CheckName = "synthetic-original-http-audit-and-authoritative-current-art";
        internal AdminWorkbenchTimingTests.Fixture Host { get; private set; } = null!;
        internal L12PlatformStore Store => Host.Store;
        internal L12AccountView Admin { get; private set; } = null!;
        internal L12AlternateArtView Before { get; private set; } = null!;
        internal L12AlternateArtView After { get; private set; } = null!;
        internal L12AdminAuditView Audit { get; private set; } = null!;
        internal L12BugReportView Bug { get; private set; } = null!;
        internal string Proof { get; private set; } = null!;
        internal string Running => L12RuntimeBuildVersion.Capture().ServerRelease;

        internal static async Task<Fixture> Create(bool forbiddenDelta = false)
        {
            var f = new Fixture { Host = await AdminWorkbenchTimingTests.Fixture.Create(false) };
            try
            {
                f.Admin = f.Store.AuthenticateTokenSession(f.Host.AdminToken)!.Account;
                var first = f.Store.AlternateArts().First(row => row.BuiltIn);
                var second = f.Store.AlternateArts().First(row => row.BuiltIn && row.BaseCardId != first.BaseCardId
                    && row.DisplayName != first.DisplayName);
                var media = Upload(f.Store, f.Admin);
                f.Before = f.Store.SaveAlternateArt(f.Admin, new(null, "SYNTHETIC-DATA-" + Guid.NewGuid().ToString("N"),
                    first.BaseCardId, first.DisplayName, media.Id));
                using var request = f.Host.Request(f.Host.AdminToken); request.Method = HttpMethod.Put;
                request.RequestUri = new Uri("/api/admin/alternate-arts", UriKind.Relative);
                request.Content = JsonContent.Create(new { id = f.Before.Id, artCode = f.Before.ArtCode,
                    baseCardId = second.BaseCardId, displayName = second.DisplayName,
                    mediaAssetId = f.Before.MediaAssetId, active = !forbiddenDelta, kind = "alternate-art", effect = "" });
                using var response = await f.Host.Client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var operationBytes = await response.Content.ReadAsStringAsync();
                f.After = (await response.Content.ReadFromJsonAsync<L12AlternateArtView>())!;
                f.Audit = f.Store.AdminAudit("alternate-art", 1000, "succeeded")
                    .Single(row => row.Target == f.Before.Id && row.Action == "update");
                static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
                var bug = f.Store.AddBug(f.Admin, "合成异画数据反馈", "没有历史玩家数据", "/synthetic", null, null, "synthetic");
                f.Bug = f.Store.UpdateBug(f.Admin, bug.Id, "confirmed", "high", "synthetic-owner", "preserved notes", "original note")!;
                f.Proof = JsonSerializer.Serialize(new { schema = 1, bugId = f.Bug.Id, auditId = f.Audit.Id,
                    alternateArtId = f.After.Id, artCode = f.After.ArtCode,
                    before = new { baseCardId = f.Before.BaseCardId, displayName = f.Before.DisplayName },
                    after = new { baseCardId = f.After.BaseCardId, displayName = f.After.DisplayName },
                    localEvidence = new { operationReceiptSha256 = Hash(operationBytes),
                        readbackReceiptSha256 = Hash(JsonSerializer.Serialize(f.Store.AdminAlternateArts())),
                        auditResponseSha256 = Hash(JsonSerializer.Serialize(f.Audit)) } });
                return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }

        internal Dictionary<string, object?> Body(string? proof = null, bool dryRun = false, long? version = null)
            => new() { ["status"] = "resolved", ["closureDisposition"] = "data_corrected_verified",
                ["comment"] = proof ?? Proof, ["regressionTest"] = CheckName, ["deployedVersion"] = Running,
                ["priority"] = Bug.Priority, ["assignee"] = Bug.Assignee, ["adminNotes"] = Bug.AdminNotes,
                ["dryRun"] = dryRun, ["expectedVersion"] = version ?? Store.Version,
                ["idempotencyKey"] = Guid.NewGuid().ToString("N") };

        internal async Task<HttpResponseMessage> Patch(object body, string? token = null, bool explicitToken = false)
        {
            using var request = Host.Request(explicitToken ? token : Host.AdminToken);
            request.Method = HttpMethod.Patch; request.RequestUri = new Uri("/api/admin/bugs/" + Bug.Id, UriKind.Relative);
            request.Content = JsonContent.Create(body); return await Host.Client.SendAsync(request);
        }
        internal L12BugReportView CurrentBug() => Store.Bugs(null).Single(row => row.Id == Bug.Id);
        internal void AssertArtUnchanged() => Assert.Equal(JsonSerializer.Serialize(After),
            JsonSerializer.Serialize(Store.AdminAlternateArts().Single(row => row.Id == After.Id)));
        internal async Task AssertRejected(object body, bool assertArt = true)
        {
            var before = JsonSerializer.Serialize(CurrentBug());
            using var response = await Patch(body); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(before, JsonSerializer.Serialize(CurrentBug()));
            if (assertArt) AssertArtUnchanged();
        }
        public ValueTask DisposeAsync() => Host.DisposeAsync();

        // Reuses the existing media-test format; this is not decoded-image evidence.
        private static L12SiteMediaView Upload(L12PlatformStore store, L12AccountView admin)
        {
            var p = L12PlatformStore.SiteMediaPolicies().Single(row => row.Kind == "card-art");
            return store.UploadSiteMedia(admin, new L12SiteMediaUpload("card-art", "synthetic.webp", "image/webp",
                Webp(p.DesktopWidth, p.DesktopHeight), Webp(p.DesktopWidth, p.DesktopHeight),
                Webp(p.MobileWidth, p.MobileHeight), Webp(p.ThumbnailWidth, p.ThumbnailHeight), "合成数据卡图", .5, .5));
        }
        private static byte[] Webp(int width, int height)
        {
            var bytes = new byte[30]; Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 22);
            Encoding.ASCII.GetBytes("WEBPVP8X").CopyTo(bytes, 8);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 10);
            var w = width - 1; var h = height - 1;
            bytes[24] = (byte)w; bytes[25] = (byte)(w >> 8); bytes[26] = (byte)(w >> 16);
            bytes[27] = (byte)h; bytes[28] = (byte)(h >> 8); bytes[29] = (byte)(h >> 16); return bytes;
        }
    }
}
