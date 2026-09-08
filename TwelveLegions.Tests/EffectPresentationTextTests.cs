using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class EffectPresentationTextTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void EveryAnimatedAtomicAbilityHasAStableEditablePresentationScene()
    {
        var catalog = Catalog;
        var scenes = catalog.AtomicEffects.All
            .SelectMany(card => card.Abilities)
            .SelectMany(ability => ability.Presentations)
            .ToArray();

        Assert.NotEmpty(scenes);
        Assert.Equal(scenes.Length, scenes.Select(scene => scene.SceneId)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(scenes, scene =>
        {
            Assert.False(string.IsNullOrWhiteSpace(scene.CardId));
            Assert.False(string.IsNullOrWhiteSpace(scene.AbilityId));
            Assert.False(string.IsNullOrWhiteSpace(scene.Trigger));
            Assert.False(string.IsNullOrWhiteSpace(scene.DefaultText));
            Assert.Equal(scene.DefaultText, scene.EffectiveText);
            Assert.False(scene.Overridden);
        });

        var tombFallback = Assert.Single(scenes, scene => scene.CardId == "S01-0204"
            && scene.Trigger == "tomb-leave-fallback");
        Assert.StartsWith("离场时", tombFallback.DefaultText);
        Assert.DoesNotContain("阵亡时", tombFallback.DefaultText);
        Assert.DoesNotContain(scenes, scene => scene.Trigger is "static" or "continuous");
    }

    [Fact]
    public void PresentationOverrideChangesOnlyEffectiveTextAndBlankOverrideFallsBackToDefault()
    {
        var original = Catalog.AtomicEffects.All
            .SelectMany(card => card.Abilities)
            .SelectMany(ability => ability.Presentations)
            .First();

        var overridden = original with { OverrideText = "  后台修订后的动画文案  " };
        Assert.Equal(original.SceneId, overridden.SceneId);
        Assert.Equal(original.DefaultText, overridden.DefaultText);
        Assert.Equal("后台修订后的动画文案", overridden.EffectiveText);
        Assert.True(overridden.Overridden);

        var blank = original with { OverrideText = "  " };
        Assert.Equal(original.DefaultText, blank.EffectiveText);
        Assert.False(blank.Overridden);
    }

    [Fact]
    public void EveryExplicitCardAnimationSceneIsAttachedToTheAtomicAdminCatalog()
    {
        var catalog = Catalog;
        var expected = new Dictionary<string, string[]>
        {
            ["S01-0007"] = ["reveal-add"],
            ["S01-0013"] = ["opponent-hand"],
            ["S01-0103"] = ["top-card"],
            ["S01-0105"] = ["search-hit"],
            ["S01-0111"] = ["attack-top-card", "death-top-card"],
            ["S01-0117"] = ["search-hit", "search-miss"],
            ["S01-0216"] = ["search-hit"],
            ["S01-0222"] = ["search-hit"],
            ["S01-02D1"] = ["top-three", "search-hit"],
            ["S01-0419"] = ["reveal-add", "search-add"],
            ["S01-0415"] = ["enter-hide"],
            ["S01-0204"] = ["tomb-leave-fallback", "tomb-fallback-transition"],
            ["S02-0008"] = ["search-hit"],
            ["S02-0012"] = ["public-disaster"],
            ["S02-0101"] = ["condition-failed-hand"],
            ["S02-0102"] = ["top-card"],
            ["S02-0103"] = ["top-card"],
            ["S02-0106"] = ["top-card"],
            ["S02-03M1"] = ["setup-hammer"],
            ["S02-0401"] = ["search-hit"],
            ["S02-0403"] = ["top-card"],
            ["S02-0404"] = ["search-hit"],
            ["S02-0405"] = ["top-five", "artifact-picked", "uesugi-picked"],
            ["S02-0501"] = ["promotion-cost-declaration", "promotion-cost-resolution"],
            ["S02-0509"] = ["attack-cost"],
            ["S02-0514"] = ["search-hit"],
            ["S02-0518"] = ["grave-hit"],
            ["S02-0521"] = ["search-hit"],
            ["S02-05M2"] = ["search-hit"],
            ["S02-0603"] = ["search-hit", "search-miss"],
            ["S02-0616"] = ["top-card"],
            ["S02-0620"] = ["search-hit"],
            ["S02-0621"] = ["search-hit"],
            ["S02-06S4"] = ["search-hit"],
            ["ST03-03"] = ["grave-hit"],
            ["ST05-06"] = ["search-hit"],
        };

        foreach (var (cardId, keys) in expected)
        {
            var card = Assert.IsType<L12AtomicCardEffect>(catalog.AtomicEffects.Find(cardId));
            var actual = card.Abilities.SelectMany(ability => ability.Presentations)
                .Select(scene => scene.Trigger).ToHashSet(StringComparer.Ordinal);
            foreach (var key in keys)
                Assert.True(actual.Contains(key),
                    $"{cardId} 缺少 {key}；能力时点：{string.Join(',', card.Abilities.Select(ability => ability.Trigger))}");
        }
    }

    [Fact]
    public void PresentationTemplatesOnlyAllowDeclaredPublicPlaceholdersAndLf()
    {
        Assert.Equal("展示〈李靖〉\n并加入手牌", L12EffectPresentationText.Validate(
            "展示〈{cardName}〉\r\n并加入手牌", ["cardName"])
            .Replace("{cardName}", "李靖", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => L12EffectPresentationText.Validate("{hiddenCard}", ["cardName"]));
        Assert.Throws<ArgumentException>(() => L12EffectPresentationText.Validate("第一行\t第二行", []));
    }

    [Fact]
    public void EmptySnapshotKeepsLegacyStateShapeAndAbilityBindingSelectsTheDeclaredScene()
    {
        var catalog = Catalog;
        var decks = new[] { catalog.DeckAt(0), catalog.DeckAt(1) };
        var game = new L12GameEngine(catalog, "empty-presentation", "PRES00", 20260908,
            ["甲", "乙"], decks, skipPreparation: true, stateFormatVersion: 2,
            effectPresentationSnapshot: []);

        Assert.Null(game.State.EffectPresentationSnapshot);
        Assert.DoesNotContain("EffectPresentationSnapshot", game.SerializeFullState(), StringComparison.Ordinal);
        Assert.Null(game.ResolveFrozenPresentation("S01-0103", "top-card",
            new Dictionary<string, string> { ["cardName"] = "测试卡" }));

        var grouped = catalog.AtomicEffects.All
            .SelectMany(card => card.Abilities.Select(ability => (Card: card, Ability: ability)))
            .GroupBy(item => (item.Card.CardId, item.Ability.Trigger))
            .First(group => group.Count(item => item.Ability.Presentations.Any(scene => scene.EventType == "effect")) >= 2);
        var selected = grouped.Select(item => item.Ability)
            .First(ability => ability.Presentations.Any(scene => scene.EventType == "effect"));
        var selectedScene = selected.Presentations.First(scene => scene.EventType == "effect");
        var source = Card(catalog, grouped.Key.CardId, "ability-source");

        Assert.Equal(selectedScene.SceneId, game.ResolveEffectPresentationSceneId(source,
            grouped.Key.Trigger, new Dictionary<string, string> { ["ability"] = selected.AbilityId },
            selectedScene.DefaultText));
    }

    [Fact]
    public void OverridesPersistAuditFreezeAndRestoreWithoutChangingAnActiveMatch()
    {
        var directory = TempDirectory("snapshot");
        var path = Path.Combine(directory, "platform.json");
        var catalog = Catalog;
        var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
        var admin = store.Login("Admin", "L12master").Account!;
        var scene = Assert.Single(catalog.AtomicEffects.Find("S01-0103")!.Abilities
            .SelectMany(ability => ability.Presentations), item => item.Trigger == "top-card");

        Assert.Empty(store.CaptureEffectPresentationSnapshot(catalog.AtomicEffects));
        store.SaveEffectPresentationOverride(admin, scene, "第一行\r\n展示〈{cardName}〉",
            new L12AdminAuditContext("presentation-save"));
        var frozen = Assert.Single(store.CaptureEffectPresentationSnapshot(catalog.AtomicEffects));
        Assert.Equal("第一行\n展示〈{cardName}〉", frozen.Text);
        var engine = new L12GameEngine(catalog, "frozen-presentation", "PRES01", 20260909,
            ["甲", "乙"], [catalog.DeckAt(0), catalog.DeckAt(1)], skipPreparation: true,
            stateFormatVersion: 2, effectPresentationSnapshot: [frozen]);
        Assert.Equal("第一行\n展示〈李靖〉", engine.ResolveFrozenPresentation("S01-0103", "top-card",
            new Dictionary<string, string> { ["cardName"] = "李靖" }));

        store.SaveEffectPresentationOverride(admin, scene, "后台后来修改的文案");
        Assert.Equal("第一行\n展示〈李靖〉", engine.ResolveFrozenPresentation("S01-0103", "top-card",
            new Dictionary<string, string> { ["cardName"] = "李靖" }));
        var restored = L12GameEngine.RestoreCheckpoint(catalog, engine.SerializeFullState(),
            engine.RandomState!.Value, engine.CardFactSignalSequence);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal("第一行\n展示〈李靖〉", restored.ResolveFrozenPresentation("S01-0103", "top-card",
            new Dictionary<string, string> { ["cardName"] = "李靖" }));

        var reloaded = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
        var effective = Assert.Single(reloaded.ApplyEffectPresentationOverrides(
            catalog.AtomicEffects.Find("S01-0103")!).Abilities.SelectMany(ability => ability.Presentations),
            item => item.SceneId == scene.SceneId);
        Assert.Equal("后台后来修改的文案", effective.EffectiveText);
        Assert.Contains(store.AdminAudit("effect-presentation"), audit => audit.Action == "save"
            && audit.Target == scene.SceneId);
        Assert.True(store.RestoreEffectPresentationDefault(admin, scene));
        Assert.Empty(store.CaptureEffectPresentationSnapshot(catalog.AtomicEffects));
    }

    [Fact]
    public void DuplicateLegacySceneRowsUseTheNewestOverrideInsteadOfBreakingReads()
    {
        var directory = TempDirectory("duplicate-row");
        var path = Path.Combine(directory, "platform.json");
        var catalog = Catalog;
        var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
        var admin = store.Login("Admin", "L12master").Account!;
        var card = catalog.AtomicEffects.Find("S01-0103")!;
        var scene = Assert.Single(card.Abilities.SelectMany(ability => ability.Presentations),
            item => item.Trigger == "top-card");
        store.SaveEffectPresentationOverride(admin, scene, "较早文案");

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var rows = root.First(item => item.Key.Equals("EffectPresentationOverrides",
            StringComparison.OrdinalIgnoreCase)).Value!.AsArray();
        var duplicate = rows[0]!.DeepClone().AsObject();
        SetJsonProperty(duplicate, "Text", "较新文案");
        SetJsonProperty(duplicate, "UpdatedAt", DateTimeOffset.UtcNow.AddMinutes(1));
        rows.Add(duplicate);
        File.WriteAllText(path, root.ToJsonString());

        var reloaded = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
        var effective = Assert.Single(reloaded.ApplyEffectPresentationOverrides(card).Abilities
            .SelectMany(ability => ability.Presentations), item => item.SceneId == scene.SceneId);
        Assert.Equal("较新文案", effective.EffectiveText);
    }

    [Fact]
    public void LongTermAnnouncementsNormalizeNewlinesButRejectOtherControlCharacters()
    {
        var directory = TempDirectory("announcement-lf");
        var path = Path.Combine(directory, "platform.json");
        var store = new L12PlatformStore(path);
        var admin = store.Login("Admin", "L12master").Account!;
        var current = store.OperationsConfig(admin);
        var announcement = new L12AnnouncementConfig("multiline", "第一行\r\n第二行\r第三行", true);
        store.ApplyOperationsConfig(admin, current.Config with { Announcements = [announcement] },
            current.Version, "验证长期公告换行", new L12AdminAuditContext("announcement-lf"));

        var persisted = Assert.Single(new L12PlatformStore(path).OperationsConfig(admin).Config.Announcements!);
        Assert.Equal("第一行\n第二行\n第三行", persisted.Content);
        var next = store.OperationsConfig(admin);
        var invalid = next.Config with
        {
            Announcements = [announcement with { Content = "第一行\t第二行" }],
        };
        Assert.Throws<L12OperationsConfigException>(() => store.ApplyOperationsConfig(admin, invalid,
            next.Version, "验证非法控制字符", new L12AdminAuditContext("announcement-tab")));
    }

    [Fact]
    public async Task PresentationAdminApiRequiresRbacAndSupportsMultilineSaveAndRestore()
    {
        var directory = TempDirectory("http-api");
        var catalog = Catalog;
        var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var player = platform.Register("tprese1234", "Password123!").Account!;
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(catalog, recorder, platform);
        await using var server = new L12WebSocketServer(manager, recorder, platform, catalog);
        await server.StartAsync(0);
        try
        {
            var endpoint = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" }.Uri;
            using var client = new HttpClient { BaseAddress = endpoint };
            var playerLogin = platform.Login(player.Username, "Password123!");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", playerLogin.Token);
            using (var forbidden = await client.GetAsync("/api/admin/effects/S01-0103"))
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

            var adminLogin = platform.Login("Admin", "L12master");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminLogin.Token);
            var detail = await client.GetFromJsonAsync<L12AtomicCardEffect>("/api/admin/effects/S01-0103");
            var scene = Assert.Single(detail!.Abilities.SelectMany(ability => ability.Presentations),
                item => item.Trigger == "top-card");
            using (var saved = await client.PutAsJsonAsync(
                       $"/api/admin/effects/S01-0103/presentations/{Uri.EscapeDataString(scene.SceneId)}",
                       new { text = "后台第一行\r\n展示〈{cardName}〉", idempotencyKey = "presentation-save-api", reason = "API 权限与多行回归" }))
            {
                Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
                var result = await saved.Content.ReadFromJsonAsync<L12EffectPresentationScene>();
                Assert.Equal("后台第一行\n展示〈{cardName}〉", result!.EffectiveText);
            }
            detail = await client.GetFromJsonAsync<L12AtomicCardEffect>("/api/admin/effects/S01-0103");
            Assert.Contains(detail!.Abilities.SelectMany(ability => ability.Presentations),
                item => item.SceneId == scene.SceneId && item.Overridden);
            using (var restored = await client.PostAsJsonAsync(
                       $"/api/admin/effects/S01-0103/presentations/{Uri.EscapeDataString(scene.SceneId)}/restore",
                       new { idempotencyKey = "presentation-restore-api", reason = "恢复默认文案回归" }))
                Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
            detail = await client.GetFromJsonAsync<L12AtomicCardEffect>("/api/admin/effects/S01-0103");
            Assert.DoesNotContain(detail!.Abilities.SelectMany(ability => ability.Presentations),
                item => item.SceneId == scene.SceneId && item.Overridden);
        }
        finally { await server.StopAsync(); }
    }

    private static L12CardInstance Card(L12Catalog catalog, string cardId, string instanceId)
    {
        var definition = catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
        };
    }

    private static void SetJsonProperty(JsonObject source, string name, object value)
    {
        var key = source.First(item => item.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Key;
        source[key] = JsonValue.Create(value);
    }

    private static string TempDirectory(string suffix)
        => Path.Combine(Path.GetTempPath(), $"l12-effect-presentation-{suffix}", Guid.NewGuid().ToString("N"));
}
