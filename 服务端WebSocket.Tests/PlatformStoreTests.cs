using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[CollectionDefinition("Platform environment", DisableParallelization = true)]
public sealed class PlatformEnvironmentCollection;

[Collection("Platform environment")]
public sealed class PlatformStoreTests
{
    [Fact]
    public void RootAdminPasswordCanComeFromServerEnvironment()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-platform-{Guid.NewGuid():N}");
        var previous = Environment.GetEnvironmentVariable("L12_ADMIN_PASSWORD");
        try
        {
            Environment.SetEnvironmentVariable("L12_ADMIN_PASSWORD", "server-secret-123");
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            Assert.True(store.Login("Admin", "server-secret-123").Success);
            Assert.False(store.Login("Admin", "L12master").Success);
        }
        finally
        {
            Environment.SetEnvironmentVariable("L12_ADMIN_PASSWORD", previous);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RegistrationAuthenticationPasswordAndRootAdminArePersistent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-platform-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path);
            var admin = store.Login("Admin", "L12master");
            Assert.True(admin.Success);
            Assert.Equal("admin", admin.Account!.Role);

            var registered = store.Register("TestPlayer", "password-123");
            Assert.True(registered.Success);
            Assert.NotNull(store.Authenticate($"Bearer {registered.Token}"));
            Assert.False(store.Register("administrator-copy", "password-123").Success);
            Assert.True(store.ChangePassword(registered.Account!.Id, "password-123", "new-password-456").Success);
            Assert.False(store.Login("TestPlayer", "password-123").Success);
            Assert.True(store.Login("TestPlayer", "new-password-456").Success);

            var reloaded = new L12PlatformStore(path);
            Assert.True(reloaded.Login("TestPlayer", "new-password-456").Success);
            Assert.NotNull(reloaded.Authenticate($"Bearer {registered.Token}"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RegistrationSeedsAndPersistsAccountDecks()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-platform-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(root, "platform.json");
            var presets = Enumerable.Range(1, 6).Select(index => new L12PresetDeckDefinition
            {
                Name = $"阵营预组{index}", MasterId = $"M{index}", CardIds = [$"C{index}"],
                MoraleIds = [$"R{index}"], SpecialIds = [],
            }).ToArray();
            var store = new L12PlatformStore(path, presets);
            var account = store.Register("DeckOwner", "password-123").Account!;
            Assert.Equal(6, store.Decks(account.Id).Count);

            var custom = new L12PresetDeckDefinition
            {
                Name = "我的测试牌库", MasterId = "M1", CardIds = ["C1", "C1"],
                MoraleIds = ["R1"], SpecialIds = [],
            };
            store.UpsertDeck(account.Id, custom);
            Assert.Equal(2, store.Decks(account.Id).Single(deck => deck.Name == custom.Name).CardIds.Count);
            Assert.True(store.DeleteDeck(account.Id, custom.Name));

            var reloaded = new L12PlatformStore(path, presets);
            Assert.Equal(6, reloaded.Decks(account.Id).Count);
            Assert.DoesNotContain(reloaded.Decks(account.Id), deck => deck.Name == custom.Name);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void BugReportsCanBeConfirmedAssignedAndClosed()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-platform-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var account = store.Register("BugReporter", "password-123").Account;
            var report = store.AddBug(account, "Battle issue", "Steps and expected result", "/game", "ROOM01", "match-1", "test");
            Assert.Equal("new", report.Status);
            Assert.Single(report.History);
            var admin = store.Login("Admin", "L12master").Account!;
            var updated = store.UpdateBug(admin, report.Id, "confirmed", "high", "Admin", "reproduced", "已在测试沙盒复现");
            Assert.NotNull(updated);
            Assert.Equal("confirmed", updated!.Status);
            Assert.Equal("high", updated.Priority);
            Assert.Equal("Admin", updated.Assignee);
            Assert.Contains(updated.History, audit => audit.Action == "comment" && audit.ActorName == "Admin");
            Assert.Contains(updated.History, audit => audit.Action == "status" && audit.ToValue == "confirmed");
            Assert.Single(store.Bugs("confirmed"));
            Assert.Single(store.Bugs(null, "high", "admin", "match-1"));

            var reloaded = new L12PlatformStore(Path.Combine(root, "platform.json"));
            Assert.Contains(reloaded.Bugs("confirmed").Single().History, audit => audit.Action == "comment");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AdminWorkflowPersistsDraftPublishRoleAndEffectReviewAudit()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-platform-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path);
            var admin = store.Login("Admin", "L12master").Account!;
            var editor = store.Register("ContentEditor", "password-123").Account!;
            Assert.True(store.SetRole(admin, editor.Id, "editor"));

            var draft = store.SaveContentDraft(admin, "home.hero.title", "新的首页标题");
            Assert.Equal("draft", draft.Status);
            Assert.Equal(string.Empty, store.GetContent("home.hero.title"));
            var published = store.PublishContent(admin, "home.hero.title");
            Assert.Equal("published", published.Status);
            Assert.Equal("新的首页标题", store.GetContent("home.hero.title"));

            var review = store.SaveEffectReview(admin, "S01-0001", "S01-0001:A1", "confirmed", "已核对规则书");
            Assert.Equal("confirmed", review.Status);
            Assert.Contains(store.AdminAudit(), row => row.Category == "account" && row.Target == "ContentEditor");
            Assert.Contains(store.AdminAudit("content"), row => row.Action == "publish" && row.Target == "home.hero.title");
            Assert.Contains(store.AdminAudit("effect"), row => row.Comment == "已核对规则书");

            var reloaded = new L12PlatformStore(path);
            Assert.Equal("新的首页标题", reloaded.GetContent("home.hero.title"));
            Assert.Contains(reloaded.AdminAudit("effect"), row => row.Target == "S01-0001/S01-0001:A1");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ConfirmingOneAbilityDoesNotConfirmTheWholeMultiAbilityCard()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-platform-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var abilities = new[]
            {
                new L12AtomicAbility("TEST-001:A1", "TEST-001", 1, "能力一", "active", [],
                    "legacy-backed", true, "test", 1m),
                new L12AtomicAbility("TEST-001:A2", "TEST-001", 2, "能力二", "enter", [],
                    "legacy-backed", true, "test", 1m),
            };
            var effect = new L12AtomicCardEffect("TEST-001", "测试卡", "S02", "neutral", "legion", null,
                "能力一。能力二。", abilities, "legacy-backed", 0, 0, 0, []);

            store.SaveEffectReview(admin, effect.CardId, abilities[0].AbilityId, "confirmed", "只确认能力一");
            var partlyReviewed = store.ApplyEffectReviews(effect);

            Assert.Equal("human-assisted", partlyReviewed.ReviewStatus);
            Assert.Equal("confirmed", partlyReviewed.Abilities[0].ReviewStatus);
            Assert.Equal("unreviewed", partlyReviewed.Abilities[1].ReviewStatus);

            store.SaveEffectReview(admin, effect.CardId, abilities[1].AbilityId, "confirmed", "确认能力二");
            var fullyReviewed = store.ApplyEffectReviews(effect);
            Assert.Equal("confirmed", fullyReviewed.ReviewStatus);
            Assert.All(fullyReviewed.Abilities, ability => Assert.Equal("confirmed", ability.ReviewStatus));

            store.SaveEffectReview(admin, effect.CardId, abilities[1].AbilityId, "rejected", "能力二需重拆");
            var rejected = store.ApplyEffectReviews(effect);
            Assert.Equal("rejected", rejected.ReviewStatus);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LegacyOrdinalReviewMigratesOnceAndChangedAbilityRequiresReviewAgain()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-platform-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var legacy = new L12AtomicAbility("TEST-002:ability:1", "TEST-002", 1, "登场时抽1张牌。", "enter", [],
                "legacy-backed", true, "test", 1m);
            store.SaveEffectReview(admin, "TEST-002", legacy.AbilityId, "confirmed", "旧序号记录");

            var stable = L12AtomicAbilityIdentity.Assign("TEST-002", legacy, 1);
            var effect = new L12AtomicCardEffect("TEST-002", "测试卡", "S02", "neutral", "legion", null,
                legacy.Text, [stable], "legacy-backed", 0, 0, 0, []);
            Assert.Equal("confirmed", store.ApplyEffectReviews(effect).Abilities[0].ReviewStatus);

            var reordered = L12AtomicAbilityIdentity.Assign("TEST-002", legacy, 2);
            Assert.Equal("confirmed", store.ApplyEffectReviews(effect with { Abilities = [reordered] }).Abilities[0].ReviewStatus);

            var changed = L12AtomicAbilityIdentity.Assign("TEST-002", legacy with { Text = "登场时抽取2张牌。" }, 2);
            Assert.Equal("unreviewed", store.ApplyEffectReviews(effect with { Abilities = [changed] }).Abilities[0].ReviewStatus);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
