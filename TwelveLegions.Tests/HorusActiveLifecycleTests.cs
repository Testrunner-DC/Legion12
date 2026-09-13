using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class HorusActiveLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var baseDeck = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "荷鲁斯主动生命周期",
            MasterId = "ST02-M1",
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "horus-active", "HORUS-ACTIVE", seed,
            ["甲", "乙"], [deck, baseDeck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            foreach (var row in player.Field) Array.Clear(row);
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = definition.Id, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction,
            ImageUrl = definition.ImageUrl, EffectText = definition.Effect,
            Traits = [.. definition.Traits], Profession = definition.Profession,
            EffectiveProfession = definition.Profession, Cost = definition.Cost ?? 0,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            OwnerIndex = 0, SummonRound = -1,
        };
    }

    private static L12Prompt Prompt(L12GameEngine game) => Assert.Single(game.State.PendingPrompts);

    private static void Choose(L12GameEngine game, string choice)
    {
        var prompt = Prompt(game);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void ChooseMany(L12GameEngine game, params string[] choices)
    {
        var prompt = Prompt(game);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                CardInstanceIds: [.. choices]));
        Assert.True(result.Accepted, result.Error);
    }

    private static (L12CardInstance Target, L12CardInstance OtherCost) BeginWithTombGuardCost(
        L12GameEngine game, string slot = "0:0")
    {
        var player = game.State.Players[0];
        var target = Card("S01-0212", $"horus-target-{game.State.TurnSerial}");
        var other = Card("S01-0212", $"horus-other-{game.State.TurnSerial}");
        target.Tapped = true;
        other.Tapped = true;
        player.Field[0][0] = target;
        player.Field[0][1] = other;

        var start = game.Handle(0,
            new L12Command("activateAbility", "master-0", Ability: "horusRevive"));
        Assert.True(start.Accepted, start.Error);
        Choose(game, "mode:tomb-guards");
        ChooseMany(game, target.InstanceId, other.InstanceId);
        Choose(game, target.InstanceId);
        Choose(game, slot);

        Assert.Contains(target, player.Graveyard);
        Assert.Contains(other, player.Graveyard);
        Assert.Single(game.State.EffectStack);
        return (target, other);
    }

    private static void PassResponses(L12GameEngine game)
    {
        var count = 0;
        while (game.State.PendingPrompts.FirstOrDefault() is { Kind: "response" } prompt && count++ < 24)
        {
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
        Assert.True(count < 24, "荷鲁斯响应窗口未在限定次数内结束");
    }

    [Fact]
    public void PrintedAbilityUsesOneStructuredSettlementScene()
    {
        var ability = Assert.Single(Catalog.AtomicEffects.Find("ST02-M1")!.Abilities);
        var scene = Assert.Single(ability.Presentations, candidate => candidate.Flow == "horus-revive");
        Assert.Equal(1, scene.SegmentIndex);
        Assert.Equal(1, scene.SegmentCount);
        Assert.Equal("将已声明的墓地1张兵力不高于2000的【太阳城】军团休整登场", scene.DefaultText);
        Assert.True(L12StructuredCardRules.TryGetStructuredAbilities("ST02-M1", out var structured));
        Assert.Contains(Assert.Single(structured).Atoms, atom => atom.Kind == L12AtomKinds.SelectTarget
            && atom.Parameters.GetValueOrDefault("filter")?.Contains("current-troops<=2000",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    [Trait("L12Evidence", "ability:horusRevive")]
    public void EligibilityAndCandidateSelectionUseCurrentTroopsNotPrintedTroops()
    {
        var eligibleGame = Create(91900);
        var eligiblePlayer = eligibleGame.State.Players[0];
        var highPrintedTarget = Card("S01-0204", "horus-current-low");
        highPrintedTarget.Troops = 2000;
        eligiblePlayer.Graveyard.Add(highPrintedTarget);
        var firstGuard = Card("S01-0212", "horus-current-guard-1");
        var secondGuard = Card("S01-0212", "horus-current-guard-2");
        eligiblePlayer.Field[0][0] = firstGuard;
        eligiblePlayer.Field[0][1] = secondGuard;

        var begin = eligibleGame.Handle(0,
            new L12Command("activateAbility", "master-0", Ability: "horusRevive"));
        Assert.True(begin.Accepted, begin.Error);
        Choose(eligibleGame, "mode:tomb-guards");
        ChooseMany(eligibleGame, firstGuard.InstanceId, secondGuard.InstanceId);
        Assert.Contains(highPrintedTarget.InstanceId, Prompt(eligibleGame).ValidChoices);

        var ineligibleGame = Create(919001);
        var ineligiblePlayer = ineligibleGame.State.Players[0];
        var lowPrintedTarget = Card("ST02-07", "horus-current-high");
        lowPrintedTarget.Troops = 3000;
        ineligiblePlayer.Graveyard.Add(lowPrintedTarget);
        ineligiblePlayer.Field[0][0] = Card("ST01-01", "horus-current-cost-1");
        ineligiblePlayer.Field[0][1] = Card("ST01-02", "horus-current-cost-2");
        ineligiblePlayer.Morale.Add(new L12MoraleCard
        {
            CardId = "ST02-C1", InstanceId = "horus-current-morale",
        });

        var rejected = ineligibleGame.Handle(0,
            new L12Command("activateAbility", "master-0", Ability: "horusRevive"));
        Assert.False(rejected.Accepted);
        Assert.Contains("兵力不高于2000", rejected.Error);
    }

    [Fact]
    [Trait("L12Evidence", "ability:horusRevive")]
    public void NegationKeepsBothPrepaidLegionCostsAndStopsRevive()
    {
        var game = Create(91901);
        var (target, other) = BeginWithTombGuardCost(game);

        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        var player = game.State.Players[0];
        Assert.Contains(target, player.Graveyard);
        Assert.Contains(other, player.Graveyard);
        Assert.DoesNotContain(player.Field.SelectMany(row => row), card => card?.InstanceId == target.InstanceId);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "negated"
            && entry.EffectSegmentIndex == 1 && entry.EffectSegmentCount == 1);
    }

    [Fact]
    [Trait("L12Evidence", "ability:horusRevive")]
    public void TargetMovedOutOfGraveDuringResponsesFailsInsteadOfSummoningFromAnotherPrivateZone()
    {
        var game = Create(91902);
        var (target, _) = BeginWithTombGuardCost(game);
        var player = game.State.Players[0];
        player.Graveyard.Remove(target);
        player.Hand.Add(target);

        PassResponses(game);

        Assert.Contains(target, player.Hand);
        Assert.DoesNotContain(player.Field.SelectMany(row => row), card => card?.InstanceId == target.InstanceId);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "failed"
            && entry.EffectSegmentIndex == 1 && entry.EffectSegmentCount == 1);
    }

    [Fact]
    [Trait("L12Evidence", "ability:horusRevive")]
    public void OccupiedDeclaredSlotDuringResponsesFailsWithoutChangingTargetOrBlocker()
    {
        var game = Create(91903);
        var (target, _) = BeginWithTombGuardCost(game);
        var player = game.State.Players[0];
        var blocker = Card("ST01-01", "horus-slot-blocker");
        player.Field[0][0] = blocker;

        PassResponses(game);

        Assert.Same(blocker, player.Field[0][0]);
        Assert.Contains(target, player.Graveyard);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "failed"
            && entry.EffectSegmentIndex == 1 && entry.EffectSegmentCount == 1);
    }

    [Fact]
    [Trait("L12Evidence", "ability:horusRevive")]
    public void FaithZealotCopyUsesTheSameStructuredResultWithoutPayingCosts()
    {
        var game = Create(91904);
        var player = game.State.Players[0];
        var target = Card("ST02-07", "horus-free-target");
        player.Graveyard.Add(target);
        game.State.FreeMasterActivation = new L12FreeMasterActivation
        {
            Controller = 0, Ability = "horusRevive", SourceInstanceId = "faith-source",
        };

        var start = game.Handle(0,
            new L12Command("activateAbility", "master-0", Ability: "horusRevive"));
        Assert.True(start.Accepted, start.Error);
        Choose(game, target.InstanceId);
        Choose(game, "0:0");
        Assert.Equal("starter-horus-active", Assert.Single(game.State.EffectStack).Data["compositePlan"]);
        PassResponses(game);

        Assert.Same(target, player.Field[0][0]);
        Assert.True(target.Tapped);
        Assert.DoesNotContain(player.UsedAbilities, key => key.Contains("horusRevive", StringComparison.Ordinal));
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.EffectSegmentIndex == 1 && entry.EffectSegmentCount == 1);
    }

    [Fact]
    [Trait("L12Evidence", "ability:horusRevive")]
    public void V2RestoreSettlesTheFrozenGraveTargetAndRejectsTheOldResponsePrompt()
    {
        var game = Create(91905);
        var (target, _) = BeginWithTombGuardCost(game);
        var oldResponse = Prompt(game);
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

        PassResponses(game);

        var restored = game.State.Players[0].Field[0][0];
        Assert.Equal(target.InstanceId, restored?.InstanceId);
        Assert.True(restored?.Tapped);
        Assert.False(game.Handle(oldResponse.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: oldResponse.PromptId, Choice: "pass")).Accepted);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.CardId == "ST02-M1"));
    }
}
