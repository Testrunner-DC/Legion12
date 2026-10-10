using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class BackendReportBatch296ResponseTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, bool concealHiddenResponseAvailability = false)
    {
        var original = Catalog.DeckAt(0);
        var asgard = new L12PresetDeckDefinition
        {
            Name = "BATCH296 响应测试",
            MasterId = "S02-03M1",
            CardIds = [.. original.CardIds],
            MoraleIds = [.. original.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "backend-report-batch296-response", "BATCH296R", seed,
            ["甲", "乙"], [asgard, asgard], skipPreparation: true,
            autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: concealHiddenResponseAvailability,
            stateFormatVersion: 2);
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
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.Resolving.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner = 0)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            OwnerIndex = owner,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            SummonRound = -1,
        };
    }

    private static L12CardInstance SetCounter(L12GameEngine game, int playerIndex, string cardId,
        int slot, string suffix)
    {
        var card = Card(cardId, $"batch296-{suffix}", playerIndex);
        card.Hidden = true;
        card.SetRound = 0;
        game.State.Players[playerIndex].Field[1][slot] = card;
        return card;
    }

    private static L12StackItem PushEffect(L12GameEngine game, int controller, L12CardInstance source,
        string trigger, string text)
    {
        var method = typeof(L12GameEngine).GetMethod("PushEffect", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<L12StackItem>(method.Invoke(game,
            [controller, source, trigger, text, null, new Dictionary<string, string>
            {
                ["triggerEffectText"] = text,
            }]));
    }

    private static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static T InvokePrivate<T>(L12GameEngine game, string methodName, params object?[] args)
    {
        var method = typeof(L12GameEngine).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<T>(method.Invoke(game, args));
    }

    private static L12StackItem Stack(string id, int controller, L12CardInstance source,
        string trigger, string text, string? target = null)
    {
        var item = new L12StackItem
        {
            StackItemId = id,
            Controller = controller,
            SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId,
            SourceName = source.Name,
            Trigger = trigger,
            Text = text,
        };
        if (target is not null) item.Targets.Add(target);
        return item;
    }

    [Fact]
    [Trait("L12Evidence", "bug:BUG-20260908-71905c40")]
    [Trait("L12Evidence", "bug:BUG-20260908-c5d947d2")]
    public void PitfallCannotWalkThroughAbsoluteDefenseAndBothResponsesKeepTheirExactTargetsAfterRestore()
    {
        var game = Create(29661);
        var entering = Card("S01-0103", "batch296-entering");
        game.State.Players[0].Field[0][0] = entering;
        var absoluteDefense = SetCounter(game, 0, "S01-0016", 0, "absolute-defense");
        var discard = Card("S01-0003", "batch296-absolute-discard");
        game.State.Players[0].Hand.Add(discard);
        var firstPitfall = SetCounter(game, 1, "S01-0018", 0, "first-pitfall");
        var secondPitfall = SetCounter(game, 1, "S01-0018", 1, "second-pitfall");

        var entry = PushEffect(game, 0, entering, "enter", "登场时 可抽取1张牌。");
        Resolve(game, "pass");
        Assert.Contains(firstPitfall.InstanceId, Assert.Single(game.State.PendingPrompts).ValidChoices);
        Resolve(game, firstPitfall.InstanceId);

        var firstPitfallStack = Assert.Single(game.State.EffectStack,
            item => item.SourceInstanceId == firstPitfall.InstanceId);
        Assert.Equal(entry.StackItemId, Assert.Single(firstPitfallStack.Targets));
        Resolve(game, "pass"); // 本方可连续响应，明确让过后才交给对手。
        Assert.Contains(absoluteDefense.InstanceId, Assert.Single(game.State.PendingPrompts).ValidChoices);
        Resolve(game, absoluteDefense.InstanceId);
        Resolve(game, discard.InstanceId);

        var defenseStack = game.State.EffectStack[^1];
        Assert.Equal("response-negate", defenseStack.Trigger);
        Assert.Equal(firstPitfallStack.StackItemId, Assert.Single(defenseStack.Targets));
        var currentPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(defenseStack.StackItemId, currentPrompt.StackItemId);
        Assert.DoesNotContain(secondPitfall.InstanceId, currentPrompt.ValidChoices);
        Assert.Contains("〈绝对防御〉", currentPrompt.Text, StringComparison.Ordinal);
        Assert.Contains(Catalog.Cards["S01-0016"].Effect!, currentPrompt.Text, StringComparison.Ordinal);

        var restored = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState!.Value, game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        var restoredPrompt = Assert.Single(restored.State.PendingPrompts);
        Assert.Equal(currentPrompt.PromptId, restoredPrompt.PromptId);
        Assert.Equal(currentPrompt.StackItemId, restoredPrompt.StackItemId);
        Assert.DoesNotContain(secondPitfall.InstanceId, restoredPrompt.ValidChoices);
        Assert.Equal(currentPrompt.Text, restoredPrompt.Text);
    }

    [Theory]
    [InlineData("enter")]
    [InlineData("promotion-enter")]
    [Trait("L12Evidence", "card:S01-0018")]
    public void PitfallUsesOneLegionEntryFamilyForNormalAndPromotionEffects(string trigger)
    {
        var game = Create(29662);
        var legion = Card("S02-0507", $"batch296-{trigger}");
        game.State.Players[0].Field[0][0] = legion;
        var pitfall = SetCounter(game, 1, "S01-0018", 0, $"pitfall-{trigger}");

        PushEffect(game, 0, legion, trigger,
            trigger == "enter" ? "登场时 可抽取1张牌。" : "晋升登场 可抽取1张牌。");
        Resolve(game, "pass");

        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(pitfall.InstanceId, response.ValidChoices);
        Assert.Equal(game.State.EffectStack[^1].StackItemId, response.StackItemId);
    }

    [Fact]
    [Trait("L12Evidence", "entry:promotion-inherits-entry-response-family")]
    public void AmbushAlsoInheritsPromotionEntryFromTheSharedEntryTimingFamily()
    {
        var game = Create(296621);
        var promoted = Card("S02-0507", "batch296-promoted-for-ambush");
        var ambushTarget = Card("S01-0103", "batch296-promotion-ambush-target", 1);
        game.State.Players[0].Field[0][0] = promoted;
        game.State.Players[1].Field[0][0] = ambushTarget;
        var ambush = SetCounter(game, 1, "S01-0019", 0, "promotion-ambush");

        PushEffect(game, 0, promoted, "promotion-enter", "晋升登场 可抽取1张牌。");
        Resolve(game, "pass");

        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(ambush.InstanceId, response.ValidChoices);
        Assert.Equal(game.State.EffectStack[^1].StackItemId, response.StackItemId);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0018")]
    public void PitfallRejectsArtifactEntryAndDoesNotWalkThroughAnInterleavedAndvaranautEffect()
    {
        var artifactGame = Create(29663);
        var artifact = Card("S02-0305", "batch296-andvaranaut-artifact");
        artifactGame.State.Players[0].Relic = artifact;
        var artifactPitfall = SetCounter(artifactGame, 1, "S01-0018", 0, "artifact-pitfall");
        PushEffect(artifactGame, 0, artifact, "enter", "登场时 测试圣物效果。");
        Resolve(artifactGame, "pass");
        Assert.DoesNotContain(artifactPitfall.InstanceId,
            Assert.Single(artifactGame.State.PendingPrompts).ValidChoices);

        var nestedGame = Create(29664);
        var legion = Card("S01-0103", "batch296-nested-legion");
        var ring = Card("S02-0305", "batch296-nested-andvaranaut");
        nestedGame.State.Players[0].Field[0][0] = legion;
        nestedGame.State.Players[0].Relic = ring;
        var nestedPitfall = SetCounter(nestedGame, 1, "S01-0018", 0, "nested-pitfall");
        var entry = Stack("stack-1", 0, legion, "enter", "登场时 可抽取1张牌。");
        var interleaved = Stack("stack-2", 0, ring, "reaction",
            "我方主宰受到伤害时，可抽取1张牌。", entry.StackItemId);
        nestedGame.State.EffectStack.AddRange([entry, interleaved]);
        nestedGame.State.ResponseWindow = new L12ResponseWindow { PriorityPlayer = 1 };
        typeof(L12GameEngine).GetMethod("OfferResponse", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(nestedGame, null);

        var response = Assert.Single(nestedGame.State.PendingPrompts);
        Assert.Equal(interleaved.StackItemId, response.StackItemId);
        Assert.Contains(nestedPitfall.InstanceId, response.ValidChoices);
        Assert.Contains("〈安德华拉诺特〉", response.Text, StringComparison.Ordinal);
        Assert.Contains(interleaved.Text, response.Text, StringComparison.Ordinal);
        Resolve(nestedGame, nestedPitfall.InstanceId);
        Assert.Equal(entry.StackItemId, Assert.Single(nestedGame.State.EffectStack[^1].Targets));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0018")]
    [Trait("L12Evidence", "card:S01-0417")]
    public void PitfallUsesCurrentFieldLegionStateWithoutLeakingItBackToArtifactOrMasterZones()
    {
        var game = Create(296631, concealHiddenResponseAvailability: true);
        var sword = Card("S01-0417", "batch296-kusanagi-artifact");
        Assert.Equal("artifact", sword.CardType);
        game.State.Players[0].Relic = sword;
        var pitfall = SetCounter(game, 1, "S01-0018", 0, "kusanagi-artifact-pitfall");

        var entry = PushEffect(game, 0, sword, "enter", "登场时 获得草薙剑的圣物效果。");
        Assert.False(InvokePrivate<bool>(game, "CanMasterCardPoolRespondAtTiming", 1, entry, false));
        Resolve(game, "pass");

        var response = Assert.Single(game.State.PendingPrompts);
        Assert.DoesNotContain(pitfall.InstanceId, response.ValidChoices);
        Assert.Equal(entry.StackItemId, response.StackItemId);

        var transformedSwordGame = Create(296632, concealHiddenResponseAvailability: true);
        var transformedSword = Card("S01-0417", "batch296-kusanagi-legion");
        transformedSwordGame.State.Players[0].Field[0][0] = transformedSword;
        var swordPitfall = SetCounter(transformedSwordGame, 1, "S01-0018", 0, "kusanagi-legion-pitfall");
        PushEffect(transformedSwordGame, 0, transformedSword, "enter", "作为军团登场时的效果。");
        Resolve(transformedSwordGame, "pass");
        Assert.Contains(swordPitfall.InstanceId,
            Assert.Single(transformedSwordGame.State.PendingPrompts).ValidChoices);

        var masterZoneGame = Create(296633, concealHiddenResponseAvailability: true);
        var masterSource = Card("S02-01M1", "batch296-wukong-master-zone");
        var masterPitfall = SetCounter(masterZoneGame, 1, "S01-0018", 0, "wukong-master-zone-pitfall");
        PushEffect(masterZoneGame, 0, masterSource, "enter", "主宰区的提示效果。");
        Resolve(masterZoneGame, "pass");
        Assert.DoesNotContain(masterPitfall.InstanceId,
            Assert.Single(masterZoneGame.State.PendingPrompts).ValidChoices);

        var transformedMasterGame = Create(296634, concealHiddenResponseAvailability: true);
        var transformedMaster = Card("S02-01M1", "batch296-wukong-field-legion");
        transformedMaster.IsMasterLegion = true;
        transformedMasterGame.State.Players[0].Field[0][0] = transformedMaster;
        var transformedMasterPitfall = SetCounter(transformedMasterGame, 1, "S01-0018", 0,
            "wukong-field-legion-pitfall");
        PushEffect(transformedMasterGame, 0, transformedMaster, "enter", "作为军团登场时的效果。");
        Resolve(transformedMasterGame, "pass");
        Assert.Contains(transformedMasterPitfall.InstanceId,
            Assert.Single(transformedMasterGame.State.PendingPrompts).ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "ruling:R1-ambush-counter-ambush")]
    public void AmbushMayRespondToOpponentAmbushWhileAttackOnlyResponsesKeepDefenderDirection()
    {
        var game = Create(29665);
        var attackerLegion = Card("S01-0103", "batch296-ambush-owner-legion");
        game.State.Players[0].Field[0][0] = attackerLegion;
        var attackerAmbush = SetCounter(game, 0, "S01-0019", 0, "attacker-ambush");
        var attackOnly = SetCounter(game, 0, "S01-0020", 1, "attack-only-counter");
        var opponentAmbush = Card("S01-0019", "batch296-opponent-ambush", 1);
        opponentAmbush.Hidden = false;
        game.State.Players[1].Resolving.Add(opponentAmbush);
        var attack = Stack("stack-1", 0, attackerLegion, "opponent-attack", "对方进攻时");
        var ambush = Stack("stack-2", 1, opponentAmbush, "reaction", "反击战术效果", attack.StackItemId);
        game.State.EffectStack.AddRange([attack, ambush]);
        game.State.ResponseWindow = new L12ResponseWindow { PriorityPlayer = 0 };
        typeof(L12GameEngine).GetMethod("OfferResponse", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(game, null);

        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(ambush.StackItemId, response.StackItemId);
        Assert.Contains(attackerAmbush.InstanceId, response.ValidChoices);
        Assert.DoesNotContain(attackOnly.InstanceId, response.ValidChoices);
        Assert.Contains(Catalog.Cards["S01-0019"].Effect!, response.Text, StringComparison.Ordinal);

        Resolve(game, attackerAmbush.InstanceId);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Resolve(game, attackerLegion.InstanceId);
        var counterAmbush = game.State.EffectStack[^1];
        Assert.Equal("reaction", counterAmbush.Trigger);
        Assert.Equal(ambush.StackItemId, Assert.Single(counterAmbush.Targets));
    }

    [Fact]
    [Trait("L12Evidence", "entry:hidden-response-current-target")]
    public void AnonymousPoolAndActualPitfallEligibilityAgreeOnTheCurrentTargetAndRejectDisaster()
    {
        var game = Create(29666, concealHiddenResponseAvailability: true);
        var legion = Card("S01-0103", "batch296-pool-legion");
        var pitfall = SetCounter(game, 1, "S01-0018", 0, "pool-pitfall");
        game.State.Players[0].Field[0][0] = legion;
        var entry = Stack("stack-1", 0, legion, "promotion-enter", "晋升登场 可抽取1张牌。");
        var absolute = Stack("stack-2", 0, Card("S01-0016", "batch296-pool-absolute"),
            "response-negate", "无效堆叠中的效果", entry.StackItemId);

        Assert.True(InvokePrivate<bool>(game, "CanMasterCardPoolRespondAtTiming", 1, entry, false));
        Assert.False(InvokePrivate<bool>(game, "CanMasterCardPoolRespondAtTiming", 1, absolute, false));

        game.State.EffectStack.Add(entry);
        game.State.ResponseWindow = new L12ResponseWindow { PriorityPlayer = 1 };
        typeof(L12GameEngine).GetMethod("OfferResponse", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(game, null);
        Assert.Contains(pitfall.InstanceId, Assert.Single(game.State.PendingPrompts).ValidChoices);

        game.State.PendingPrompts.Clear();
        game.State.EffectStack.Add(absolute);
        game.State.ResponseWindow = new L12ResponseWindow { PriorityPlayer = 1 };
        typeof(L12GameEngine).GetMethod("OfferResponse", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(game, null);
        Assert.Contains(pitfall.InstanceId, Assert.Single(game.State.PendingPrompts).ValidChoices);

        var disaster = Stack("stack-3", 0, Card("S01-DS01", "batch296-disaster"),
            "disaster", "天地异变效果");
        Assert.False(InvokePrivate<bool>(game, "CanMasterCardPoolRespondAtTiming", 1, disaster, false));
        var nestedDisasterResponse = Stack("stack-4", 0,
            Card("S01-0019", "batch296-disaster-response"), "reaction", "反击战术效果",
            disaster.StackItemId);
        game.State.EffectStack.Clear();
        game.State.EffectStack.AddRange([disaster, nestedDisasterResponse]);
        Assert.False(InvokePrivate<bool>(game, "CanMasterCardPoolRespondAtTiming",
            1, nestedDisasterResponse, false));
    }
}
