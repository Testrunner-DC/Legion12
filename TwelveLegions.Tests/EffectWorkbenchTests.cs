using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class EffectWorkbenchTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void WorkbenchUsesExistingAtomsAndPublishesOneFrozenPresentationVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-effect-workbench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var catalog = Catalog;
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var effect = catalog.AtomicEffects.Find("S01-0103")!;
            var initial = store.EffectWorkbench(effect);
            Assert.Equal("source", initial.Status);
            Assert.True(initial.Validation.Valid);
            Assert.Equal(effect.Abilities.Select(item => item.AbilityId),
                initial.Draft.Abilities.Select(item => item.AbilityId));
            Assert.All(initial.Draft.Abilities, item => Assert.False(string.IsNullOrWhiteSpace(item.StructureHash)));
            Assert.True(L12PlatformStore.EffectPresentationStyles.Count >= 4);

            var scene = Assert.Single(initial.Draft.Scenes, item => item.Text.Contains("李靖"));
            var changed = initial.Draft with
            {
                IncludedProducts = [initial.Draft.BaseProduct, "测试收录产品"],
                Scenes = initial.Draft.Scenes.Select(item => item.SceneId == scene.SceneId
                    ? item with { Text = "工作台公开〈{cardName}〉\n并加入手牌", StyleId = "reveal" }
                    : item).ToArray(),
            };
            var saved = store.SaveEffectWorkbenchDraft(admin, effect,
                new L12EffectWorkbenchSaveRequest(changed, initial.Version, "统一维护测试"));
            Assert.Equal("draft", saved.Status);
            Assert.Equal(1, saved.Version);
            var validated = store.ValidateEffectWorkbenchDraft(admin, effect,
                new L12EffectWorkbenchActionRequest(saved.Version));
            Assert.Equal("validated", validated.Status);
            var reviewed = store.ReviewEffectWorkbenchDraft(admin, effect,
                new L12EffectWorkbenchActionRequest(validated.Version, "核对现有原子结构"));
            Assert.Equal("reviewed", reviewed.Status);
            var published = store.PublishEffectWorkbenchDraft(admin, effect,
                new L12EffectWorkbenchActionRequest(reviewed.Version, "发布统一呈现"));
            Assert.Equal("published", published.Status);
            Assert.Single(published.History);
            Assert.NotNull(published.PublishedVersionId);

            var applied = store.ApplyEffectPresentationOverrides(effect);
            var appliedScene = Assert.Single(applied.Abilities.SelectMany(item => item.Presentations),
                item => item.SceneId == scene.SceneId);
            Assert.Equal("工作台公开〈{cardName}〉\n并加入手牌", appliedScene.EffectiveText);
            var snapshot = Assert.Single(store.CaptureEffectPresentationSnapshot(catalog.AtomicEffects),
                item => item.SceneId == scene.SceneId);
            Assert.Equal(appliedScene.EffectiveText, snapshot.Text);
            Assert.Contains(store.AdminAudit("effect-workbench"), item => item.Action == "publish");

            var reloaded = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var persisted = reloaded.EffectWorkbench(effect);
            Assert.Equal("published", persisted.Status);
            Assert.Single(persisted.History);
            Assert.Equal("测试收录产品", persisted.Draft.IncludedProducts[1]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void WorkbenchRejectsSemanticDriftStaleWritesAndUnreviewedPublishing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-effect-workbench-guard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var catalog = Catalog;
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var effect = catalog.AtomicEffects.Find("S01-0103")!;
            var initial = store.EffectWorkbench(effect);
            var changed = initial.Draft with
            {
                Abilities = initial.Draft.Abilities.Select((item, index) => index == 0
                    ? item with { ResolutionText = item.ResolutionText + " 未接入的新结算" }
                    : item).ToArray(),
            };
            var saved = store.SaveEffectWorkbenchDraft(admin, effect,
                new L12EffectWorkbenchSaveRequest(changed, 0));
            Assert.False(saved.Validation.Valid);
            Assert.True(saved.Validation.RequiresDevelopment);
            var validated = store.ValidateEffectWorkbenchDraft(admin, effect,
                new L12EffectWorkbenchActionRequest(saved.Version));
            Assert.Equal("needs-development", validated.Status);
            Assert.Throws<ArgumentException>(() => store.ReviewEffectWorkbenchDraft(admin, effect,
                new L12EffectWorkbenchActionRequest(saved.Version)));
            Assert.Throws<InvalidOperationException>(() => store.SaveEffectWorkbenchDraft(admin, effect,
                new L12EffectWorkbenchSaveRequest(initial.Draft, 0)));

            var clean = store.SaveEffectWorkbenchDraft(admin, effect,
                new L12EffectWorkbenchSaveRequest(initial.Draft, saved.Version));
            var checkedDraft = store.ValidateEffectWorkbenchDraft(admin, effect,
                new L12EffectWorkbenchActionRequest(clean.Version));
            Assert.Throws<InvalidOperationException>(() => store.PublishEffectWorkbenchDraft(admin, effect,
                new L12EffectWorkbenchActionRequest(checkedDraft.Version)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
