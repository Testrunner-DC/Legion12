using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class CommittedStatisticsReadsTests
{
    [Theory]
    [InlineData("normal", false)] [InlineData("normal", true)]
    [InlineData("confirmed", false)] [InlineData("confirmed", true)]
    [InlineData("system-error", false)] [InlineData("system-error", true)]
    [InlineData("review", false)] [InlineData("review", true)]
    [InlineData("insufficient", false)] [InlineData("insufficient", true)]
    [InlineData("revoked", false)] [InlineData("revoked", true)]
    public void CommittedExclusionsMatchOriginalHeldRevokedWaivedAndInactiveAuthority(string disposition, bool held)
    {
        using var f = new RankedIdentityFixture();
        var registered = f.Store.Register("stattarget", "Password123!");
        Assert.True(registered.Success, registered.Message);
        var inactive = Assert.IsType<L12AccountView>(registered.Account);
        string[] expectedMatches = [], expectedAccounts = [];
        f.Store.ExecuteAdminTransaction(() =>
        {
            var data = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(f.Store)!;
            SetRows(data, "RankedIntegrityAudits", new[] { new { MatchId = "stats-one" }, new { MatchId = "waived-only" } });
            SetRows(data, "RankedIntegrityDecisions", new object[]
            {
                new { Id = "earlier", Revision = 1, Disposition = "confirmed", MatchIds = new[] { "stats-one" } },
                new { Id = "latest", Revision = 2, Disposition = disposition, MatchIds = new[] { "STATS-ONE" }, RevokesDecisionId = disposition == "revoked" ? "earlier" : null },
            });
            SetRows(data, "RankedHeldRewards", held ? new[] { new { MatchId = "stats-one" } } : []);
            SetRows(data, "RankedSeasonResetRepairs", new[] { new { TransitionMatchIds = new[] { "waived-only" } } });
            var accounts = (System.Collections.IEnumerable)data.GetType().GetProperty("Accounts")!.GetValue(data)!;
            var row = accounts.Cast<object>().Single(item => (string)item.GetType().GetProperty("Id")!.GetValue(item)! == inactive.Id);
            row.GetType().GetProperty("Disabled")!.SetValue(row, true);
            expectedMatches = f.Store.RankedIntegrityExcludedMatchIds().Order(StringComparer.OrdinalIgnoreCase).ToArray();
            expectedAccounts = f.Store.StatisticsExcludedAccountIds().Order(StringComparer.OrdinalIgnoreCase).ToArray();
            var staged = f.Store.ReadCommittedStatisticsExclusions();
            Assert.Equal(expectedMatches, staged.MatchIds.Order(StringComparer.OrdinalIgnoreCase));
            Assert.Equal(expectedAccounts, staged.AccountIds.Order(StringComparer.OrdinalIgnoreCase));
            RankedIdentityTestState.RequestSave(f.Store); return true;
        });
        var actual = f.Store.ReadCommittedStatisticsExclusions();
        Assert.Equal(expectedMatches, actual.MatchIds.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(expectedAccounts, actual.AccountIds.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("RankedSeasonResetRepairs")]
    [InlineData("TransitionMatchIds")]
    public void RepairableNullExclusionsCommitUnavailableUntilExistingEnsureRepairsThem(string property)
    {
        using var f = new RankedIdentityFixture();
        f.Store.ExecuteAdminTransaction(() =>
        {
            var data = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(f.Store)!;
            if (property == "RankedSeasonResetRepairs")
                data.GetType().GetProperty(property)!.SetValue(data, null);
            else SetRows(data, "RankedSeasonResetRepairs", new[] { new { TransitionMatchIds = (string[]?)null } });
            Assert.Throws<L12PlatformStorageUnavailableException>(() => f.Store.ReadCommittedStatisticsExclusions());
            RankedIdentityTestState.RequestSave(f.Store);
            return true;
        });
        Assert.Throws<L12PlatformStorageUnavailableException>(() => f.Store.ReadCommittedStatisticsExclusions());
        var reopened = new L12PlatformStore(f.Path, f.Catalog.PresetDecks, officialCards: f.Catalog.Cards);
        var view = reopened.ReadCommittedStatisticsExclusions();
        Assert.True(view.Available);
        Assert.Empty(view.MatchIds);
    }

    private static void SetRows(object data, string name, object rows)
    {
        var property = data.GetType().GetProperty(name)!;
        property.SetValue(data, JsonSerializer.Deserialize(JsonSerializer.Serialize(rows), property.PropertyType));
    }
}
