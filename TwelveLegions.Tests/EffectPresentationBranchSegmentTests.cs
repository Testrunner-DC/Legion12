using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class EffectPresentationBranchSegmentTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData("effect-trigger")]
    [InlineData("effect-activation")]
    [InlineData("effect-response")]
    public void SegmentedAbilitiesNeverAnimateTheirWholeDeclaration(string eventType)
    {
        var catalog = Catalog;
        var game = Create(catalog, 299030);
        var checkedScenes = 0;
        foreach (var card in catalog.AtomicEffects.All)
        foreach (var ability in card.Abilities.Where(ability =>
                     ability.Presentations.Any(scene => scene.EventType == "effect" && scene.Flow is not null)))
        foreach (var whole in ability.Presentations.Where(scene => scene.EventType == "effect" && scene.Flow is null))
        {
            var source = Card(catalog, card.CardId, $"whole-{checkedScenes++}");
            Invoke(game, "AddPresentationEventById", eventType, 0, "整段声明保留审计",
                whole.SceneId, new[] { source });
            Assert.Equal("effect-announced", game.State.Events.Last().Type);
        }
        Assert.True(checkedScenes > 0);
    }

    [Fact]
    public void EveryConfiguredCompositeSegmentIsExposedByTheAtomicAdminCatalog()
    {
        var catalog = Catalog;
        var checkedSegments = 0;
        foreach (var card in catalog.AtomicEffects.All)
        foreach (var plan in L12CompositeEffectPlans.PresentationPlansForCard(card.CardId))
        for (var index = 0; index < plan.Segments.Count; index++)
        {
            var segment = plan.Segments[index];
            var matches = card.Abilities.SelectMany(ability => ability.Presentations)
                .Where(scene => scene.Flow == segment.Flow
                    && scene.SegmentIndex == index + 1
                    && scene.SegmentCount == plan.Segments.Count)
                .ToArray();
            Assert.True(matches.Length > 0,
                $"{card.CardId}/{plan.PlanId} 第{index + 1}段 {segment.Flow} 未挂接");
            Assert.All(matches, scene =>
            {
                Assert.Equal("effect", scene.EventType);
                Assert.False(string.IsNullOrWhiteSpace(scene.SceneId));
                Assert.False(string.IsNullOrWhiteSpace(scene.DefaultText));
            });
            checkedSegments++;
        }

        var plans = L12CompositeEffectPlans.AllPresentationPlans();
        Assert.Equal(plans.Sum(plan => plan.Segments.Count), checkedSegments);
        var branchFlows = L12EffectPresentationVariants.PublicBranchDefinitions
            .Select(branch => branch.Flow).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var branchCapableSegments = plans.Sum(plan => plan.Segments.Count(segment =>
            branchFlows.Contains(segment.Flow)));
        var expectedSceneCount = plans.Sum(plan => plan.Segments.Count) - branchCapableSegments
            + L12EffectPresentationVariants.PublicBranchDefinitions.Count
            + L12EffectPresentationVariants.StandaloneBranchDefinitions.Count;
        var actualSceneCount = catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .SelectMany(ability => ability.Presentations).Count(scene => scene.Flow is not null);
        Assert.Equal(expectedSceneCount, actualSceneCount);
    }

    [Fact]
    public void StarterContinuationPlansAndMordredPublicBranchesAreIncludedInTheIndependentAuditSet()
    {
        var plans = L12CompositeEffectPlans.AllPresentationPlans();
        Assert.Contains(plans, plan => plan.PlanId == "starter-aeneas-promotion"
            && plan.CardId == "ST05-01" && plan.Segments.Count == 2);
        Assert.Contains(plans, plan => plan.PlanId == "starter-athena-active"
            && plan.CardId == "ST05-M1" && plan.Segments.Count == 2);

        var catalog = Catalog;
        Assert.Equal(2, catalog.AtomicEffects.Find("ST05-01")!.Abilities
            .SelectMany(ability => ability.Presentations)
            .Count(scene => scene.Trigger.StartsWith(
                L12EffectPresentationVariants.SceneKeyPrefix("starter-aeneas-promotion"),
                StringComparison.Ordinal)));
        Assert.Equal(2, catalog.AtomicEffects.Find("ST05-M1")!.Abilities
            .SelectMany(ability => ability.Presentations)
            .Count(scene => scene.Trigger.StartsWith(
                L12EffectPresentationVariants.SceneKeyPrefix("starter-athena-active"),
                StringComparison.Ordinal)));

        var mordred = catalog.AtomicEffects.Find("ST06-04")!.Abilities
            .SelectMany(ability => ability.Presentations)
            .Where(scene => scene.Flow == "mordred-enter-choice").ToArray();
        Assert.Equal(2, mordred.Length);
        Assert.Contains(mordred, scene => scene.RequiredChoices?.GetValueOrDefault("mode") == "mode:rune");
        Assert.Contains(mordred, scene => scene.RequiredChoices?.GetValueOrDefault("mode") == "mode:charge");
        Assert.All(mordred, scene => Assert.Null(scene.SegmentIndex));

        var game = Create(catalog, 296010);
        var source = Card(catalog, "ST06-04", "mordred-presentation");
        var rune = Assert.Single(mordred, scene =>
            scene.RequiredChoices?.GetValueOrDefault("mode") == "mode:rune");
        Assert.Equal(rune.SceneId, game.ResolveEffectPresentationSceneId(source, "enter",
            new Dictionary<string, string>
            {
                ["presentationFlow"] = "mordred-enter-choice",
                ["declared:mode"] = "mode:rune",
            }, source.EffectText!));
    }

    [Fact]
    public void RealMordredChoicePublishesItsBranchAndCompletesWithoutChangingSemanticAtomicFlow()
    {
        var catalog = Catalog;
        var runeScene = Assert.Single(catalog.AtomicEffects.Find("ST06-04")!.Abilities
            .SelectMany(ability => ability.Presentations), scene => scene.Flow == "mordred-enter-choice"
                && scene.RequiredChoices?.GetValueOrDefault("mode") == "mode:rune");
        var frozen = new L12FrozenEffectPresentation(runeScene.SceneId, runeScene.CardId,
            runeScene.Trigger, "莫德雷德符文分支覆盖", []);
        var game = Create(catalog, 296012, [frozen]);
        var mordred = Card(catalog, "ST06-04", "real-mordred");
        game.State.Players[0].Field[0][0] = mordred;

        Invoke(game, "QueueOrPushTriggeredEffect", 0, mordred, "enter", "【enter】效果", null,
            new Dictionary<string, string>());
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(declaration.PlayerIndex, new L12Command("resolvePrompt",
            PromptId: declaration.PromptId, Choice: "mode:rune")).Accepted);
        var item = Assert.Single(game.State.EffectStack);
        Assert.Equal("mordred-enter-choice", item.Data["presentationFlow"]);
        Assert.False(item.Data.ContainsKey("atomicFlow"));
        Assert.Equal(runeScene.SceneId, item.Data["presentationSceneId"]);
        Assert.Contains(game.State.Events, action => action.EffectText == "莫德雷德符文分支覆盖");

        PassResponses(game);
        Assert.Equal(1, game.State.Players[0].SpecialZones.Runes);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    public void MultiSegmentAndBranchMetadataUsesOneBasedIndexesAndPublicChoiceKeys()
    {
        var catalog = Catalog;
        var camp = catalog.AtomicEffects.Find("S01-0007")!.Abilities
            .SelectMany(ability => ability.Presentations).Where(scene => scene.Flow is not null).ToArray();
        var search = Assert.Single(camp, scene => scene.Flow == "camp-search");
        var heal = Assert.Single(camp, scene => scene.Flow == "camp-heal");
        var draw = Assert.Single(camp, scene => scene.Flow == "camp-draw");
        Assert.Equal((1, 3), (search.SegmentIndex, search.SegmentCount));
        Assert.Equal((2, 3), (heal.SegmentIndex, heal.SegmentCount));
        Assert.Equal((3, 3), (draw.SegmentIndex, draw.SegmentCount));
        Assert.Equal("mode:heal", heal.RequiredChoices!["campMode"]);
        Assert.Equal("mode:draw", draw.RequiredChoices!["campMode"]);
        Assert.DoesNotContain(heal.RequiredChoices.Keys, key => key.StartsWith("declared:",
            StringComparison.OrdinalIgnoreCase));

        var volley = catalog.AtomicEffects.Find("S01-0005")!.Abilities
            .SelectMany(ability => ability.Presentations).Where(scene => scene.Flow == "volley-effect").ToArray();
        Assert.Equal(3, volley.Length);
        Assert.Equal(3, volley.Select(scene => scene.SceneId).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(volley, scene => scene.BranchLabel == "对方前排兵力-2000"
            && scene.RequiredChoices!["volleyMode"] == "mode:front");

        var json = JsonSerializer.Serialize(volley[0], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"flow\":\"volley-effect\"", json, StringComparison.Ordinal);
        Assert.Contains("\"segmentIndex\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"requiredChoices\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("declared:", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("singleTarget", json, StringComparison.OrdinalIgnoreCase);

        var legacy = catalog.AtomicEffects.Find("S01-0103")!.Abilities
            .SelectMany(ability => ability.Presentations)
            .First(scene => scene.Flow is null && scene.EventType == "effect");
        var legacyJson = JsonSerializer.Serialize(legacy, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("\"flow\"", legacyJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"segmentIndex\"", legacyJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"segmentCount\"", legacyJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"branchLabel\"", legacyJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"requiredChoices\"", legacyJson, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeSelectionUsesFinalPublicBranchAndFallsBackForMissingOrInvalidDeclarations()
    {
        var catalog = Catalog;
        var game = Create(catalog, 29601);
        var source = Card(catalog, "S01-0005", "presentation-source");
        var scenes = catalog.AtomicEffects.Find(source.CardId)!.Abilities
            .SelectMany(ability => ability.Presentations).ToArray();
        var front = Assert.Single(scenes, scene => scene.Flow == "volley-effect"
            && scene.RequiredChoices?.GetValueOrDefault("volleyMode") == "mode:front");
        var back = Assert.Single(scenes, scene => scene.Flow == "volley-effect"
            && scene.RequiredChoices?.GetValueOrDefault("volleyMode") == "mode:back");
        var oldGeneric = Assert.Single(scenes, scene => scene.Flow is null && scene.EventType == "effect");

        Assert.Equal(front.SceneId, game.ResolveEffectPresentationSceneId(source, "play",
            SegmentData("volley-effect", 0, ("volleyMode", "mode:front")), source.EffectText!));
        Assert.Equal(back.SceneId, game.ResolveEffectPresentationSceneId(source, "play",
            SegmentData("volley-effect", 0, ("volleyMode", "mode:back")), source.EffectText!));
        Assert.Equal(oldGeneric.SceneId, game.ResolveEffectPresentationSceneId(source, "play",
            SegmentData("volley-effect", 0), source.EffectText!));
        Assert.Equal(oldGeneric.SceneId, game.ResolveEffectPresentationSceneId(source, "play",
            SegmentData("volley-effect", 0, ("volleyMode", "mode:unknown")), source.EffectText!));
    }

    [Fact]
    public void CompositePlanAndTriggerKeepSharedThutmoseFlowsOnIndependentScenes()
    {
        var catalog = Catalog;
        var game = Create(catalog, 296011);
        var source = Card(catalog, "S01-0201", "thutmose-presentation");
        var attackData = SegmentData("thutmose-debuff", 0);
        attackData["compositePlan"] = "trigger:S01-0201:attack";
        var deathData = SegmentData("thutmose-debuff", 0);
        deathData["compositePlan"] = "trigger:S01-0201:death";

        var attackId = game.ResolveEffectPresentationSceneId(source, "attack", attackData, source.EffectText!);
        var deathId = game.ResolveEffectPresentationSceneId(source, "death", deathData, source.EffectText!);
        Assert.NotNull(attackId);
        Assert.NotNull(deathId);
        Assert.NotEqual(attackId, deathId);
        var scenes = catalog.AtomicEffects.Find(source.CardId)!.Abilities
            .SelectMany(ability => ability.Presentations).ToArray();
        Assert.StartsWith(L12EffectPresentationVariants.SceneKeyPrefix("trigger:S01-0201:attack"),
            Assert.Single(scenes, scene => scene.SceneId == attackId).Trigger, StringComparison.Ordinal);
        Assert.StartsWith(L12EffectPresentationVariants.SceneKeyPrefix("trigger:S01-0201:death"),
            Assert.Single(scenes, scene => scene.SceneId == deathId).Trigger, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("S01-01D1", "palace-reward-morale", 1)]
    [InlineData("S01-01D1", "palace-exchange-kill", 2)]
    [InlineData("S01-04M1", "amaterasu-debuff", 1)]
    [InlineData("S01-04M1", "amaterasu-ready", 2)]
    public void MultipleActivePlansAreAttachedToTheirDeclaredAtomicAbility(
        string cardId, string flow, int abilitySequence)
    {
        var card = Catalog.AtomicEffects.Find(cardId)!;
        var scene = Assert.Single(card.Abilities.SelectMany(ability => ability.Presentations),
            candidate => candidate.Flow == flow && candidate.SegmentIndex == 1);
        var owner = Assert.Single(card.Abilities, ability => ability.AbilityId == scene.AbilityId);
        Assert.Equal(abilitySequence, owner.Sequence);
    }

    [Fact]
    public void RealChoicePublishesTheChosenFrozenBranchAndCancellationPublishesNothing()
    {
        var catalog = Catalog;
        var front = Assert.Single(catalog.AtomicEffects.Find("S01-0005")!.Abilities
            .SelectMany(ability => ability.Presentations), scene => scene.Flow == "volley-effect"
                && scene.RequiredChoices?.GetValueOrDefault("volleyMode") == "mode:front");
        var frozen = new L12FrozenEffectPresentation(front.SceneId, front.CardId, front.Trigger,
            "前排分支覆盖\n第二行", []);
        var game = Create(catalog, 29602, [frozen]);
        var source = Card(catalog, "S01-0005", "real-volley");
        game.State.Players[0].FreeTacticCount = 1;
        game.State.Players[0].Hand.Add(source);
        game.State.Players[1].Field[0][0] = Card(catalog, "S01-0103", "real-target", owner: 1);

        Assert.True(game.Handle(0, new L12Command("playCard", source.InstanceId)).Accepted);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:front", declaration.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            Choice: "mode:front")).Accepted);

        var item = Assert.Single(game.State.EffectStack, item => item.SourceInstanceId == source.InstanceId);
        Assert.Equal("volley-effect", item.Data["atomicFlow"]);
        Assert.Equal("mode:front", item.Data["declared:volleyMode"]);
        var presentation = Assert.Single(game.State.Events, action => action.Cards.Any(card =>
            card.InstanceId == source.InstanceId) && action.EffectText == "前排分支覆盖\n第二行");
        Assert.Equal(source.EffectText, presentation.Text);
        Assert.Equal("declared", presentation.EffectResultStatus);
        Assert.Equal(front.SceneId, presentation.EffectSceneId);
        Assert.Equal(front.AbilityId, presentation.EffectAbilityId);
        Assert.Equal(front.Trigger.Split(":branch-", 2)[0], presentation.EffectSegmentId);
        Assert.Equal(front.SceneId, presentation.EffectBranchId);

        PassResponses(game);
        var resultEvent = Assert.Single(game.State.Events, action => action.Type == "effect-result"
            && action.Cards.Any(card => card.InstanceId == source.InstanceId));
        Assert.Equal("resolved", resultEvent.EffectResultStatus);
        Assert.Equal(presentation.EffectSceneId, resultEvent.EffectSceneId);
        Assert.Equal(presentation.EffectAbilityId, resultEvent.EffectAbilityId);
        Assert.Equal(presentation.EffectSegmentId, resultEvent.EffectSegmentId);
        Assert.Equal(presentation.EffectBranchId, resultEvent.EffectBranchId);
        Assert.Equal("前排分支覆盖\n第二行", resultEvent.EffectText);

        var cancelled = Create(catalog, 29603, [frozen]);
        var cancelledSource = Card(catalog, "S01-0005", "cancelled-volley");
        cancelled.State.Players[0].FreeTacticCount = 1;
        cancelled.State.Players[0].Hand.Add(cancelledSource);
        cancelled.State.Players[1].Field[0][0] = Card(catalog, "S01-0103", "cancel-target", owner: 1);
        Assert.True(cancelled.Handle(0, new L12Command("playCard", cancelledSource.InstanceId)).Accepted);
        var cancelPrompt = Assert.Single(cancelled.State.PendingPrompts);
        Assert.Contains("skip", cancelPrompt.ValidChoices);
        Assert.True(cancelled.Handle(0, new L12Command("resolvePrompt", PromptId: cancelPrompt.PromptId,
            Choice: "skip")).Accepted);
        Assert.DoesNotContain(cancelled.State.EffectStack, stack => stack.SourceInstanceId == cancelledSource.InstanceId);
        Assert.DoesNotContain(cancelled.State.Events, action => action.EffectText == "前排分支覆盖\n第二行");
        Assert.DoesNotContain(cancelled.State.Events, action => action.Type == "effect-result");
    }

    [Theory]
    [InlineData("resolved")]
    [InlineData("negated")]
    [InlineData("skipped")]
    [InlineData("failed")]
    [InlineData("declined")]
    public void StructuredSettlementPublishesExactlyOneTypedResult(string resultStatus)
    {
        var catalog = Catalog;
        var scene = Assert.Single(catalog.AtomicEffects.Find("S01-0005")!.Abilities
            .SelectMany(ability => ability.Presentations), candidate => candidate.Flow == "volley-effect"
                && candidate.RequiredChoices?.GetValueOrDefault("volleyMode") == "mode:front");
        var game = Create(catalog, 307100 + resultStatus.Length);
        var source = Card(catalog, scene.CardId, $"result-{resultStatus}");
        var item = StackItem(source, scene, resultStatus);
        game.State.EffectStack.Add(item);

        Invoke(game, "FinishStackItem", item);
        Invoke(game, "AddEffectResultEvent", item, resultStatus);

        var resultEvent = Assert.Single(game.State.Events, action => action.Type == "effect-result");
        Assert.Equal(resultStatus, resultEvent.EffectResultStatus);
        Assert.Equal(scene.SceneId, resultEvent.EffectSceneId);
        Assert.Equal(scene.AbilityId, resultEvent.EffectAbilityId);
        Assert.Equal(scene.Trigger.Split(":branch-", 2)[0], resultEvent.EffectSegmentId);
        Assert.Equal(scene.SceneId, resultEvent.EffectBranchId);
        Assert.Equal(scene.BranchLabel, resultEvent.EffectBranchLabel);
        Assert.DoesNotContain(item, game.State.EffectStack);
    }

    [Fact]
    public void PendingSettlementStatusSurvivesCheckpointRestore()
    {
        var catalog = Catalog;
        var scene = Assert.Single(catalog.AtomicEffects.Find("S01-0005")!.Abilities
            .SelectMany(ability => ability.Presentations), candidate => candidate.Flow == "volley-effect"
                && candidate.RequiredChoices?.GetValueOrDefault("volleyMode") == "mode:front");
        var game = Create(catalog, 307201, stateFormatVersion: 2);
        var source = Card(catalog, scene.CardId, "reconnect-result");
        game.State.Players[0].Resolving.Add(source);
        game.State.EffectStack.Add(StackItem(source, scene, "skipped"));

        var restored = L12GameEngine.RestoreCheckpoint(catalog, game.SerializeFullState(),
            game.RandomState!.Value, game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        var restoredItem = Assert.Single(restored.State.EffectStack);
        Assert.Equal("skipped", restoredItem.Data["effectResultStatus"]);

        Invoke(restored, "FinishStackItem", restoredItem);

        var resultEvent = Assert.Single(restored.State.Events, action => action.Type == "effect-result");
        Assert.Equal("skipped", resultEvent.EffectResultStatus);
        Assert.Equal(scene.SceneId, resultEvent.EffectSceneId);
        Assert.Equal(scene.SceneId, resultEvent.EffectBranchId);
    }

    [Fact]
    public void RealStructuredRowEffectCanResolveWithNoTargetsAsSkipped()
    {
        var catalog = Catalog;
        var game = Create(catalog, 307250);
        var source = Card(catalog, "S01-0005", "no-target-volley");
        game.State.Players[0].FreeTacticCount = 1;
        game.State.Players[0].Hand.Add(source);

        Assert.True(game.Handle(0, new L12Command("playCard", source.InstanceId)).Accepted);
        ResolveOnlyPrompt(game, "mode:front");
        Assert.Single(game.State.EffectStack);
        PassResponses(game);

        var resultEvent = Assert.Single(game.State.Events, action => action.Type == "effect-result"
            && action.Cards.Any(card => card.InstanceId == source.InstanceId));
        Assert.Equal("skipped", resultEvent.EffectResultStatus);
        Assert.Contains("没有合法处理对象", resultEvent.Text, StringComparison.Ordinal);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.PendingPrompts);
    }

    [Theory]
    [InlineData("ability-rejected", "unavailable")]
    [InlineData("effect-cancelled", "declined")]
    [InlineData("effect-negated", "negated")]
    public void PreSettlementOutcomesRemainDistinct(string eventType, string expectedStatus)
    {
        var game = Create(Catalog, 307300 + eventType.Length);
        Invoke(game, "AddEvent", eventType, 0, "结果边界", Array.Empty<L12CardInstance>());
        Assert.Equal(expectedStatus, game.State.Events.Last().EffectResultStatus);
    }

    [Fact]
    public void ActualEventsKeepAuditTextWhileOnlyVariantScenesSupplyDefaultEffectText()
    {
        var catalog = Catalog;
        var source = Card(catalog, "S01-0005", "event-copy-source");
        var branch = Assert.Single(catalog.AtomicEffects.Find(source.CardId)!.Abilities
            .SelectMany(ability => ability.Presentations), scene => scene.Flow == "volley-effect"
                && scene.RequiredChoices?.GetValueOrDefault("volleyMode") == "mode:front");
        var legacy = Assert.Single(catalog.AtomicEffects.Find(source.CardId)!.Abilities
            .SelectMany(ability => ability.Presentations), scene => scene.Flow is null
                && scene.EventType == "effect");

        var defaults = Create(catalog, 296021);
        Invoke(defaults, "AddPresentationEventById", "effect-trigger", 0, "原始审计文案",
            branch.SceneId, new[] { source });
        var defaultEvent = Assert.Single(defaults.State.Events, action => action.Text == "原始审计文案");
        Assert.Equal(branch.DefaultText, defaultEvent.EffectText);

        Invoke(defaults, "AddPresentationEventById", "effect-trigger", 0, "旧场景审计文案",
            legacy.SceneId, new[] { source });
        var legacyEvent = Assert.Single(defaults.State.Events, action => action.Text == "旧场景审计文案");
        Assert.Null(legacyEvent.EffectText);
        Assert.Equal("effect-announced", legacyEvent.Type);
        Assert.Equal("effect-trigger", defaultEvent.Type);

        var frozen = new L12FrozenEffectPresentation(branch.SceneId, branch.CardId, branch.Trigger,
            "冻结分支覆盖", []);
        var overridden = Create(catalog, 296022, [frozen]);
        Invoke(overridden, "AddPresentationEventById", "effect-trigger", 0, "覆盖时仍保留的审计文案",
            branch.SceneId, new[] { source });
        var overrideEvent = Assert.Single(overridden.State.Events,
            action => action.Text == "覆盖时仍保留的审计文案");
        Assert.Equal("冻结分支覆盖", overrideEvent.EffectText);
    }

    public static IEnumerable<object[]> ActiveResultBranches()
    {
        yield return ["S01-02M1", "isis-reward-choice", "rewardMode", "mode:draw", "mode:heal"];
        yield return ["S01-02M3", "medjed-debuff", "mode", "mode:normal", "mode:strong"];
        yield return ["S02-06C1", "otherworld-rune-use", "mode", "mode:trial", "mode:draw"];
        yield return ["S02-0603", "merlin-rune", "mode", "mode:debuff", "mode:search"];
        yield return ["ST06-09", "light-sword-active", "mode", "mode:buff", "mode:rune"];
        yield return ["S02-0604", "galahad-grail-reward", "healMode", "mode:none", "mode:heal"];
    }

    public static IEnumerable<object[]> ActiveProducerCases()
    {
        yield return ["isis", "isis-reward-choice", "rewardMode", "mode:draw"];
        yield return ["medjed", "medjed-debuff", "mode", "mode:normal"];
        yield return ["medjed-free", "medjed-debuff", "mode", "mode:strong"];
        yield return ["divinity-damage", "divinity-power", "mode", "mode:damage"];
        yield return ["rune-use", "otherworld-rune-use", "mode", "mode:draw"];
        yield return ["merlin", "merlin-rune", "mode", "mode:search"];
        yield return ["light-sword", "light-sword-active", "mode", "mode:rune"];
        yield return ["galahad", "galahad-grail-reward", "healMode", "mode:none"];
    }

    [Theory]
    [MemberData(nameof(ActiveProducerCases))]
    public void RealCommitProducersCopyOnlyTheirValidatedPublicResultChoice(string producer,
        string expectedFlow, string expectedKey, string expectedChoice)
    {
        var catalog = Catalog;
        var requiredMasterId = producer switch
        {
            "isis" => "S01-02M1",
            "medjed" or "medjed-free" => "S01-02M3",
            "divinity-damage" => "S02-05D1",
            _ => null,
        };
        var game = Create(catalog, 296300 + producer.Length, firstMasterId: requiredMasterId);
        var player = game.State.Players[0];
        object? commit;

        switch (producer)
        {
            case "isis":
            {
                var source = Card(catalog, "S01-02M1", "master-0");
                var guards = Enumerable.Range(0, 3)
                    .Select(index => Card(catalog, "S01-0212", $"isis-guard-{index}")).ToArray();
                for (var index = 0; index < guards.Length; index++) player.Field[0][index] = guards[index];
                var canopicDefinition = catalog.Cards.Values.First(definition =>
                    definition.NameZh.Contains("卡诺匹斯", StringComparison.Ordinal)
                    && definition.CardType == "artifact");
                var canopic = Card(catalog, canopicDefinition.Id, "isis-canopic");
                player.Graveyard.Add(canopic);
                var target = string.Join('|', guards.Select(card => card.InstanceId)
                    .Append(canopic.InstanceId).Append(expectedChoice));
                commit = Invoke(game, "TryCommitS1FactionActiveAbility", 0, source,
                    "isisCanopic", target, "isis-once", null, false);
                break;
            }
            case "medjed":
            {
                player.Morale.Add(new L12MoraleCard { CardId = "morale", InstanceId = "medjed-morale" });
                var source = Card(catalog, "S01-02M3", "master-0");
                var enemy = Card(catalog, "S01-0001", "medjed-enemy", owner: 1);
                game.State.Players[1].Field[0][0] = enemy;
                commit = Invoke(game, "TryCommitS1FactionActiveAbility", 0, source,
                    "medjedDebuff", $"{expectedChoice}|{enemy.InstanceId}", "medjed-once", null, false);
                break;
            }
            case "medjed-free":
            {
                var source = Card(catalog, "S01-02M3", "master-0");
                var enemy = Card(catalog, "S01-0001", "medjed-free-enemy", owner: 1);
                game.State.Players[1].Field[0][0] = enemy;
                game.State.FreeMasterActivation = new L12FreeMasterActivation
                {
                    Controller = 0, Ability = "medjedDebuff", SourceInstanceId = "faith-source",
                };
                commit = Invoke(game, "TryCommitFreeMasterActivation", 0, source,
                    "medjedDebuff", $"{expectedChoice}|{enemy.InstanceId}");
                break;
            }
            case "divinity-damage":
            {
                player.Morale.AddRange([GodPower("producer-divinity-a"), GodPower("producer-divinity-b")]);
                var source = Card(catalog, "S02-05D1", "master-0");
                var enemy = Card(catalog, "S02-0004", "producer-divinity-enemy", owner: 1);
                game.State.Players[1].Field[0][0] = enemy;
                var target = string.Join('|', Enumerable.Repeat(enemy.InstanceId, 6).Prepend(expectedChoice));
                commit = Invoke(game, "TryCommitS2RemainingAbility", 0, source,
                    "divinityPower", target, "divinity-once");
                break;
            }
            case "rune-use":
            {
                player.SpecialZones.Runes = 1;
                var source = Card(catalog, "S02-06C1", "faction-0");
                commit = Invoke(game, "TryCommitS2FactionActiveAbility", 0, source,
                    "runeUse", expectedChoice, "rune-use-once", null, null);
                break;
            }
            case "merlin":
            {
                player.SpecialZones.Runes = 1;
                var source = Card(catalog, "S02-0603", "producer-merlin");
                player.Field[0][0] = source;
                player.Library.Add(Card(catalog, "S01-0001", "merlin-library"));
                commit = Invoke(game, "TryCommitS2FactionActiveAbility", 0, source,
                    "merlinRune", expectedChoice, "merlin-once", null, null);
                break;
            }
            case "light-sword":
            {
                var source = Card(catalog, "ST06-09", "producer-light-sword");
                player.Field[0][0] = source;
                var discard = Card(catalog, "S01-0001", "light-sword-discard");
                player.Hand.Add(discard);
                commit = Invoke(game, "TryCommitStarterRemainingActiveAbility", 0, source,
                    "lightSwordActive", $"{expectedChoice}|{discard.InstanceId}", "light-sword-once");
                break;
            }
            case "galahad":
            {
                var source = Card(catalog, "S02-0604", "producer-galahad");
                player.Field[0][0] = source;
                var trial = Card(catalog, "S02-06S4", "completed-grail");
                trial.TrialCompleted = true;
                player.SpecialZones.Trials.Add(trial);
                commit = Invoke(game, "TryCommitS2FactionActiveAbility", 0, source,
                    "galahadGrailReward", expectedChoice, "galahad-once", null, null);
                break;
            }
            default:
                throw new InvalidOperationException(producer);
        }

        var result = Assert.IsType<CommandResult>(commit);
        Assert.True(result.Accepted, result.Error);
        var item = Assert.Single(game.State.EffectStack);
        Assert.Equal(expectedFlow, item.Data["presentationFlow"]);
        Assert.Equal(expectedChoice, item.Data[$"declared:{expectedKey}"]);
        Assert.False(item.Data.ContainsKey("declared:target"));
    }

    [Theory]
    [MemberData(nameof(ActiveResultBranches))]
    public void EveryAuditedActiveResultBranchPublishesItsOwnFrozenScene(string cardId,
        string flow, string declarationKey, string firstChoice, string secondChoice)
    {
        var catalog = Catalog;
        var scenes = catalog.AtomicEffects.Find(cardId)!.Abilities
            .SelectMany(ability => ability.Presentations)
            .Where(scene => scene.Flow == flow).ToArray();
        Assert.Equal(2, scenes.Length);
        Assert.Equal(2, scenes.Select(scene => scene.SceneId).Distinct(StringComparer.Ordinal).Count());

        foreach (var choice in new[] { firstChoice, secondChoice })
        {
            var scene = Assert.Single(scenes,
                candidate => candidate.RequiredChoices?.GetValueOrDefault(declarationKey) == choice);
            var frozenText = $"{flow}:{choice}:冻结覆盖";
            var frozen = new L12FrozenEffectPresentation(scene.SceneId, scene.CardId,
                scene.Trigger, frozenText, []);
            var game = Create(catalog, 296100 + choice.Length, [frozen]);
            var source = Card(catalog, cardId, $"publish-{flow}-{choice}");
            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["presentationFlow"] = flow,
                [$"declared:{declarationKey}"] = choice,
            };

            Invoke(game, "PushEffect", 0, source, "active", "主动效果审计文案", null, data);

            Assert.Contains(game.State.Events, action => action.Type == "effect-activation"
                && action.EffectText == frozenText);
        }
    }

    [Fact]
    public void RealDivinityChoicesUseCompositeRecoverySceneAndStandaloneDamageScene()
    {
        var catalog = Catalog;
        var allScenes = catalog.AtomicEffects.Find("S02-05D1")!.Abilities
            .SelectMany(ability => ability.Presentations).ToArray();
        var recoverScene = Assert.Single(allScenes,
            scene => scene.Flow == "divinity-recover" && scene.SegmentIndex == 1);
        var damageScene = Assert.Single(allScenes,
            scene => scene.Flow == "divinity-power"
                && scene.RequiredChoices?.GetValueOrDefault("mode") == "mode:damage");
        Assert.NotEqual(recoverScene.SceneId, damageScene.SceneId);

        var recoverGame = Create(catalog, 296201,
            [new(recoverScene.SceneId, recoverScene.CardId, recoverScene.Trigger,
                "诸神巅回收分段冻结文案", [])], firstMasterId: "S02-05D1");
        var recoverPlayer = recoverGame.State.Players[0];
        recoverPlayer.Morale.AddRange([
            GodPower("divinity-recover-power-a"), GodPower("divinity-recover-power-b"),
        ]);
        var recovery = Card(catalog, "S02-0502", "divinity-real-recovery");
        recoverPlayer.Graveyard.Add(recovery);

        Assert.True(recoverGame.Handle(0,
            new L12Command("activateAbility", "master-0", Ability: "divinityPower")).Accepted);
        ResolveOnlyPrompt(recoverGame, "mode:recover");
        ResolveOnlyPrompt(recoverGame, recovery.InstanceId);
        ResolveOnlyPrompt(recoverGame, "mode:none");
        var recoverItem = Assert.Single(recoverGame.State.EffectStack);
        Assert.Equal("active:S02-05D1:divinityRecover", recoverItem.Data["compositePlan"]);
        Assert.False(recoverItem.Data.ContainsKey("presentationFlow"));
        Assert.Contains(recoverGame.State.Events,
            action => action.EffectText == "诸神巅回收分段冻结文案");
        PassResponses(recoverGame);
        Assert.Contains(recovery, recoverPlayer.Hand);
        Assert.Empty(recoverGame.State.EffectStack);

        var damageGame = Create(catalog, 296202,
            [new(damageScene.SceneId, damageScene.CardId, damageScene.Trigger,
                "诸神巅伤害分支冻结文案", [])], firstMasterId: "S02-05D1");
        var damagePlayer = damageGame.State.Players[0];
        damagePlayer.Morale.AddRange([
            GodPower("divinity-damage-power-a"), GodPower("divinity-damage-power-b"),
        ]);
        var target = Card(catalog, "S02-0004", "divinity-real-damage-target", owner: 1);
        target.Troops = 10000;
        damageGame.State.Players[1].Field[0][0] = target;

        Assert.True(damageGame.Handle(0,
            new L12Command("activateAbility", "master-0", Ability: "divinityPower")).Accepted);
        ResolveOnlyPrompt(damageGame, "mode:damage");
        for (var index = 0; index < 6; index++) ResolveOnlyPrompt(damageGame, target.InstanceId);
        var damageItem = Assert.Single(damageGame.State.EffectStack);
        Assert.Equal("divinity-power", damageItem.Data["presentationFlow"]);
        Assert.False(damageItem.Data.ContainsKey("compositePlan"));
        Assert.Equal(damageScene.SceneId, damageGame.ResolveEffectPresentationSceneId(
            Card(catalog, "S02-05D1", "damage-resolution-probe"), damageItem.Trigger,
            damageItem.Data, "诸神巅伤害分支"));
        Assert.True(damageGame.State.Events.Any(
                action => action.EffectText == "诸神巅伤害分支冻结文案"),
            $"Stack data: {string.Join(", ", damageItem.Data.Select(pair => $"{pair.Key}={pair.Value}"))}"
            + Environment.NewLine + string.Join(Environment.NewLine,
                damageGame.State.Events.Select(action =>
                    $"{action.Type} | {action.Text} | EffectText={action.EffectText}")));
        PassResponses(damageGame);
        Assert.Empty(damageGame.State.EffectStack);
    }

    [Fact]
    public void OverlappingSpecificBranchConfigurationIsRejectedBeforeRuntime()
    {
        var left = new L12EffectPresentationScene("left", "C", "A", "left", "左",
            Flow: "flow", SegmentIndex: 1, SegmentCount: 1,
            RequiredChoices: Choices(("mode", "a")));
        var right = new L12EffectPresentationScene("right", "C", "A", "right", "右",
            Flow: "flow", SegmentIndex: 1, SegmentCount: 1,
            RequiredChoices: Choices(("row", "0")));

        var error = Assert.Throws<InvalidOperationException>(() =>
            L12EffectPresentationVariants.ValidateConfiguration([left, right]));
        Assert.Contains("可同时命中", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BranchOverrideUsesTheExistingSaveFreezeAndRestorePipelineByExactSceneId()
    {
        var catalog = Catalog;
        var scene = Assert.Single(catalog.AtomicEffects.Find("S02-0406")!.Abilities
            .SelectMany(ability => ability.Presentations), candidate => candidate.Flow == "tenka-effect"
                && candidate.RequiredChoices?.GetValueOrDefault("mode") == "mode:row-cost"
                && candidate.RequiredChoices?.GetValueOrDefault("row") == "row:1");
        var repeatedLoadScene = Assert.Single(Catalog.AtomicEffects.Find("S02-0406")!.Abilities
            .SelectMany(ability => ability.Presentations), candidate => candidate.Flow == "tenka-effect"
                && candidate.RequiredChoices?.GetValueOrDefault("mode") == "mode:row-cost"
                && candidate.RequiredChoices?.GetValueOrDefault("row") == "row:1");
        Assert.Equal(scene.SceneId, repeatedLoadScene.SceneId);

        var directory = Path.Combine(Path.GetTempPath(), $"l12-presentation-branch-{Guid.NewGuid():N}");
        var store = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var admin = store.Login("Admin", "L12master").Account!;
        store.SaveEffectPresentationOverride(admin, scene, "天下布武后排覆盖\r\n第二行",
            new L12AdminAuditContext("branch-save"));

        var applied = Assert.Single(store.ApplyEffectPresentationOverrides(
            catalog.AtomicEffects.Find("S02-0406")!).Abilities.SelectMany(ability => ability.Presentations),
            candidate => candidate.SceneId == scene.SceneId);
        Assert.Equal("天下布武后排覆盖\n第二行", applied.EffectiveText);
        var frozen = Assert.Single(store.CaptureEffectPresentationSnapshot(catalog.AtomicEffects),
            candidate => candidate.SceneId == scene.SceneId);
        Assert.Equal(scene.Trigger, frozen.SceneKey);
        Assert.True(store.RestoreEffectPresentationDefault(admin, scene,
            new L12AdminAuditContext("branch-restore")));
        Assert.DoesNotContain(store.CaptureEffectPresentationSnapshot(catalog.AtomicEffects),
            candidate => candidate.SceneId == scene.SceneId);
    }

    private static Dictionary<string, string> SegmentData(string flow, int zeroBasedIndex,
        params (string Key, string Value)[] choices)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["atomicFlow"] = flow,
            ["compositeSegment"] = zeroBasedIndex.ToString(),
        };
        foreach (var choice in choices) data[$"declared:{choice.Key}"] = choice.Value;
        return data;
    }

    private static IReadOnlyDictionary<string, string> Choices(params (string Key, string Value)[] choices)
        => new ReadOnlyDictionary<string, string>(choices.ToDictionary(choice => choice.Key,
            choice => choice.Value, StringComparer.OrdinalIgnoreCase));

    private static object? Invoke(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().Name, methodName);
        return method.Invoke(target, args);
    }

    private static void PassResponses(L12GameEngine game, int maximum = 16)
    {
        var count = 0;
        while (game.State.PendingPrompts.FirstOrDefault() is { Kind: "response" } prompt
               && count++ < maximum)
        {
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
        Assert.True(count < maximum, "响应窗口未在限定次数内结束");
    }

    private static void ResolveOnlyPrompt(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static L12MoraleCard GodPower(string instanceId) => new()
    {
        CardId = "S02-05C1",
        InstanceId = instanceId,
        IsGodPower = true,
    };

    private static L12GameEngine Create(L12Catalog catalog, int seed,
        IReadOnlyList<L12FrozenEffectPresentation>? snapshot = null, string? firstMasterId = null,
        int stateFormatVersion = 0)
    {
        var baseDeck = catalog.DeckAt(0);
        var firstDeck = firstMasterId is null
            ? baseDeck
            : new L12PresetDeckDefinition
            {
                Name = $"{firstMasterId}展示测试牌库",
                MasterId = firstMasterId,
                CardIds = [.. baseDeck.CardIds],
                MoraleIds = [.. baseDeck.MoraleIds],
                SpecialIds = [],
            };
        var game = new L12GameEngine(catalog, "presentation-branch", "PRESBR", seed,
            ["甲", "乙"], [firstDeck, baseDeck], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, effectPresentationSnapshot: snapshot,
            stateFormatVersion: stateFormatVersion);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.Resolving.Clear();
        }
        return game;
    }

    private static L12StackItem StackItem(L12CardInstance source, L12EffectPresentationScene scene,
        string resultStatus)
    {
        var item = new L12StackItem
        {
            StackItemId = $"stack-{source.InstanceId}",
            Controller = source.OwnerIndex ?? 0,
            SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId,
            SourceName = source.Name,
            SourceSnapshot = source.Clone(),
            Trigger = "play",
            Text = source.EffectText ?? source.Name,
        };
        item.Data["presentationSceneId"] = scene.SceneId;
        if (resultStatus == "negated") item.Negated = true;
        else if (resultStatus != "resolved") item.Data["effectResultStatus"] = resultStatus;
        return item;
    }

    private static L12CardInstance Card(L12Catalog catalog, string cardId, string instanceId, int owner = 0)
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
            Traits = [.. definition.Traits],
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            OwnerIndex = owner,
            SummonRound = -1,
        };
    }
}
