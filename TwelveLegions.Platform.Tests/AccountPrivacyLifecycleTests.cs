using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class AccountPrivacyLifecycleTests
{
    [Fact]
    public void AdminResetAndLogicalDeletionProtectRootAndSelfAndScrubPersonalData()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var player = store.Register("tprivaba758", "password-123");
            store.AddBug(player.Account, "personal", "details", "/me", null, null, "test");
            var context = new L12AdminAuditContext("test", "admin.accounts.status.write");

            Assert.Throws<L12SecurityPolicyException>(() =>
                store.AdminResetPassword(admin, admin.Id, "self", context, true));
            var reset = store.AdminResetPassword(admin, player.Account!.Id, "support-reset", context, true);
            Assert.True(reset.Applied);
            Assert.True(reset.Account.MustChangePassword);
            Assert.NotNull(reset.TemporaryPassword);
            Assert.Equal(32, reset.TemporaryPassword!.Length);
            Assert.NotEqual("123456", reset.TemporaryPassword);
            Assert.Null(store.AuthenticateToken(player.Token));
            Assert.False(store.Login("tprivaba758", "123456").Success);
            Assert.True(store.Login("tprivaba758", reset.TemporaryPassword).Success);
            var secondReset = store.AdminResetPassword(admin, player.Account.Id, "second-support-reset", context, true);
            Assert.NotNull(secondReset.TemporaryPassword);
            Assert.NotEqual(reset.TemporaryPassword, secondReset.TemporaryPassword);
            Assert.False(store.Login("tprivaba758", reset.TemporaryPassword).Success);
            Assert.True(store.Login("tprivaba758", secondReset.TemporaryPassword!).Success);

            var deleted = store.DeleteAccountPersonalData(admin, player.Account.Id, "user-request", context, true);
            Assert.True(deleted.Applied);
            Assert.True(deleted.Account.Deleted);
            Assert.False(store.Login("tprivaba758", secondReset.TemporaryPassword!).Success);
            Assert.DoesNotContain(store.Bugs(null), bug => bug.ReporterName == "tprivaba758");
            Assert.Equal($"deleted-{player.Account.Id}", store.Account(player.Account.Id)!.Username);
            Assert.Throws<L12SecurityPolicyException>(() =>
                store.DeleteAccountPersonalData(admin, admin.Id, "root", context, true));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MatchRecorderAnonymizesNamesDeckLabelsAndRecordedJson()
    {
        var root = TempRoot();
        try
        {
            var path = Path.Combine(root, "matches.db");
            await using var recorder = new MatchRecorder(path);
            await recorder.InitializeAsync();
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO matches(match_id,room_code,seed,player_0,player_1,deck_0,deck_1,started_utc)
                    VALUES('m1','ROOM',1,'tprivaba758','Other','Private Deck','Other Deck','2026-01-01T00:00:00Z');
                    INSERT INTO match_events(match_id,sequence,received_utc,player_index,command_json,accepted,error,revision,state_hash,state_json)
                    VALUES('m1',1,'2026-01-01T00:00:01Z',0,'{"note":"PrivacyOwner acted"}',1,NULL,1,'hash',
                    '{"Players":[{"Name":"tprivaba758"},{"Name":"Other"}]}');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            Assert.Equal(1, await recorder.AnonymizePlayerAsync("tprivaba758", "deleted-account"));
            var summary = Assert.Single(await recorder.ListMatchesAsync());
            Assert.Equal("deleted-account", summary.Player0);
            Assert.Equal("已清理牌库", summary.Deck0);
            var detail = await recorder.GetMatchAsync("m1");
            var recorded = Assert.Single(detail!.Commands);
            Assert.DoesNotContain("tprivaba758", recorded.Command.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("tprivaba758", recorded.State.GetRawText(), StringComparison.Ordinal);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-account-privacy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
