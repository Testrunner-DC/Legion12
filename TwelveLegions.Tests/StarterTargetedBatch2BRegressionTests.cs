using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class StarterTargetedBatch2BRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
        => new(Catalog, "starter-targeted-2b", "STARTER2B", seed, ["甲", "乙"], [0, 1],
            skipPreparation: true);

    private static L12CardInstance Card(string cardId, string instanceId)
    {
        var definition = Catalog.Cards[cardId];
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
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
        };
    }

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().Name, name);
        return method.Invoke(target, args);
    }

    private static void Queue(L12GameEngine game, L12CardInstance source, string trigger = "enter")
        => Invoke(game, "QueueOrPushTriggeredEffect", 0, source, trigger, $"【{trigger}】效果", null,
            new Dictionary<string, string>());

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
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: choices.ToList()));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game, int maximum = 16)
    {
        var guard = 0;
        while (game.State.PendingPrompts.FirstOrDefault() is { Kind: "response" } prompt && guard++ < maximum)
        {
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
        Assert.True(guard < maximum, "响应窗口未在限定次数内结束");
    }

    private static void HoldOpponentResponseWindow(L12GameEngine game, string suffix)
    {
        var opponent = game.State.Players[1];
        var counter = Card("S01-0016", $"batch2b-counter-{suffix}");
        counter.Hidden = true;
        opponent.Field[1][2] = counter;
        opponent.Hand.Add(Card("S01-0002", $"batch2b-counter-hand-{suffix}"));
    }

    [Fact]
    public void Batch2BUsesSharedProgramsProfessionAndTrialMetadata()
    {
        var expected = new (string CardId, string Trigger)[]
        {
            ("ST05-07", "enter"),
            ("ST06-01", "continuous"), ("ST06-01", "enter"),
            ("ST06-04", "enter"), ("ST06-04", "death"),
            ("ST06-07", "enter"),
        };

        foreach (var (cardId, trigger) in expected)
        {
            Assert.NotNull(L12VerifiedAtomicPrograms.Find(cardId, trigger));
            Assert.Contains(Catalog.AtomicEffects.Find(cardId)!.Abilities,
                ability => ability.Trigger == trigger && ability.MigrationStatus == "verified"
                    && !ability.HasLegacyFallback);
        }

        var mordred = Card("ST06-04", "metadata-mordred");
        Assert.True(L12StructuredCardRules.HasProfession(mordred, 0, "骑兵"));
        Assert.Equal(1, Card("ST06-07", "metadata-boudica").TrialValue);
    }

    [Fact]
    public void AntinousRequiresAuthoritativeMasterDiscardAndMayDecline()
    {
        var game = Create(20201);
        var player = game.State.Players[0];
        var antinous = Card("ST05-07", "antinous-condition");
        var olympus = Card("ST05-01", "antinous-rested");
        olympus.Tapped = true;
        player.Field[0][0] = antinous;
        player.Field[0][1] = olympus;

        Queue(game, antinous);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);

        var ordinaryDiscard = Card("ST01-01", "ordinary-discard");
        player.Hand.Add(ordinaryDiscard);
        Invoke(game, "MoveHandToGrave", player, ordinaryDiscard.InstanceId, true, Card("ST01-02", "legion-source"));
        Assert.False(player.HandDiscardedByMasterThisTurn);

        var masterDiscard = Card("ST01-01", "master-discard");
        player.Hand.Add(masterDiscard);
        Invoke(game, "MoveHandToGrave", player, masterDiscard.InstanceId, false, Card("ST05-M1", "master-source"));
        Assert.True(player.HandDiscardedByMasterThisTurn);

        Queue(game, antinous);
        var prompt = Prompt(game);
        Assert.Equal(["mode:none", "mode:use"], prompt.ValidChoices);
        Assert.Equal(prompt.ValidChoices.Count,
            prompt.ValidChoices.Select(choice => prompt.ChoiceLabels.GetValueOrDefault(choice, choice))
                .Distinct(StringComparer.Ordinal).Count());
        Choose(game, "mode:none");

        Assert.True(olympus.Tapped);
        Assert.Empty(game.State.EffectStack);

        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        Invoke(game, "CompleteEndTurn", 0);
        Assert.False(player.HandDiscardedByMasterThisTurn);
    }

    [Fact]
    public void AntinousReadiesOnlyTheDeclaredRestedOlympusLegion()
    {
        var game = Create(20202);
        var player = game.State.Players[0];
        var antinous = Card("ST05-07", "antinous-ready");
        var olympus = Card("ST05-01", "antinous-olympus");
        var otherworld = Card("ST06-02", "antinous-otherworld");
        olympus.Tapped = true;
        otherworld.Tapped = true;
        player.Field[0][0] = antinous;
        player.Field[0][1] = olympus;
        player.Field[0][2] = otherworld;
        player.HandDiscardedByMasterThisTurn = true;

        Queue(game, antinous);
        Choose(game, "mode:use");
        Assert.Contains(olympus.InstanceId, Prompt(game).ValidChoices);
        Assert.DoesNotContain(otherworld.InstanceId, Prompt(game).ValidChoices);
        Choose(game, olympus.InstanceId);
        PassResponses(game);

        Assert.False(olympus.Tapped);
        Assert.True(otherworld.Tapped);
    }

    [Fact]
    public void ElizabethDerivedCostAppliesOnlyFromHandWithTudorOnField()
    {
        var game = Create(20203);
        var player = game.State.Players[0];
        var elizabeth = Card("ST06-01", "elizabeth-cost");
        var tudor = Card("S02-0618", "tudor-field");

        Assert.Equal(0, L12StructuredCardRules.HandPlayCostModifier(player, elizabeth));
        player.Hand.Add(elizabeth);
        Assert.Equal(0, L12StructuredCardRules.HandPlayCostModifier(player, elizabeth));
        player.Field[0][0] = tudor;
        Assert.Equal(-2, L12StructuredCardRules.HandPlayCostModifier(player, elizabeth));
        player.Hand.Remove(elizabeth);
        player.Field[0][1] = elizabeth;
        Assert.Equal(0, L12StructuredCardRules.HandPlayCostModifier(player, elizabeth));
    }

    [Fact]
    public void ElizabethAcceptsZeroOneOrTwoMoraleAndLocksOnlyStillRestedInstances()
    {
        var zero = Create(20204);
        var zeroSource = Card("ST06-01", "elizabeth-zero");
        zero.State.Players[0].Field[0][0] = zeroSource;
        Queue(zero, zeroSource);
        PassResponses(zero);
        Assert.Empty(zero.State.PendingPrompts);
        Assert.Empty(zero.State.EffectStack);

        var game = Create(20205);
        var source = Card("ST06-01", "elizabeth-two");
        var opponent = game.State.Players[1];
        var first = new L12MoraleCard { CardId = "S01-01C1", InstanceId = "locked-first", Tapped = true };
        var second = new L12MoraleCard { CardId = "S01-01C2", InstanceId = "locked-second", Tapped = true };
        opponent.Morale.AddRange([first, second]);
        game.State.Players[0].Field[0][0] = source;
        HoldOpponentResponseWindow(game, "elizabeth");

        Queue(game, source);
        Assert.Equal(0, Prompt(game).MinChoose);
        Assert.Equal(2, Prompt(game).MaxChoose);
        ChooseMany(game, first.InstanceId, second.InstanceId);
        second.Tapped = false;
        PassResponses(game);

        Assert.Equal(game.State.Round + 1, first.CannotUntapUntilRound);
        Assert.Equal(0, second.CannotUntapUntilRound);
        first.Tapped = true;
        game.State.Round = first.CannotUntapUntilRound;
        Invoke(game, "Untap", opponent);
        Assert.True(first.Tapped);
        game.State.Round++;
        Invoke(game, "Untap", opponent);
        Assert.False(first.Tapped);

        var one = Create(20206);
        var oneSource = Card("ST06-01", "elizabeth-one");
        var only = new L12MoraleCard { CardId = "S01-01C1", InstanceId = "locked-only", Tapped = true };
        one.State.Players[0].Field[0][0] = oneSource;
        one.State.Players[1].Morale.Add(only);
        Queue(one, oneSource);
        ChooseMany(one, only.InstanceId);
        PassResponses(one);
        Assert.Equal(one.State.Round + 1, only.CannotUntapUntilRound);
    }

    [Fact]
    public void MordredEnterChoiceHasTwoUniqueMandatoryBranches()
    {
        var runeGame = Create(20207);
        var runeMordred = Card("ST06-04", "mordred-rune");
        runeGame.State.Players[0].Field[0][0] = runeMordred;
        Queue(runeGame, runeMordred);
        var runePrompt = Prompt(runeGame);
        Assert.Equal(["mode:rune", "mode:charge"], runePrompt.ValidChoices);
        Assert.DoesNotContain("skip", runePrompt.ValidChoices);
        Choose(runeGame, "mode:rune");
        PassResponses(runeGame);
        Assert.Equal(1, runeGame.State.Players[0].SpecialZones.Runes);
        Assert.False(runeMordred.HasCharge);

        var chargeGame = Create(20208);
        var chargeMordred = Card("ST06-04", "mordred-charge");
        chargeGame.State.Players[0].Field[0][0] = chargeMordred;
        Queue(chargeGame, chargeMordred);
        Choose(chargeGame, "mode:charge");
        PassResponses(chargeGame);
        Assert.True(chargeMordred.HasCharge);
        Assert.Equal(0, chargeGame.State.Players[0].SpecialZones.Runes);
    }

    [Fact]
    public void MordredDeathAllowsNoTargetAndRevalidatesCurrentTroops()
    {
        var empty = Create(20209);
        var emptyMordred = Card("ST06-04", "mordred-empty");
        empty.State.Players[0].Graveyard.Add(emptyMordred);
        Queue(empty, emptyMordred, "death");
        PassResponses(empty);
        Assert.Empty(empty.State.PendingPrompts);
        Assert.Empty(empty.State.EffectStack);

        var game = Create(20210);
        var mordred = Card("ST06-04", "mordred-death");
        var target = Card("ST01-01", "mordred-current-target");
        target.Troops = 2000;
        game.State.Players[0].Graveyard.Add(mordred);
        game.State.Players[1].Field[0][0] = target;
        HoldOpponentResponseWindow(game, "mordred");

        Queue(game, mordred, "death");
        target.Troops = 3000;
        PassResponses(game);

        Assert.Same(target, game.State.Players[1].Field[0][0]);
        Assert.DoesNotContain(target, game.State.Players[1].Graveyard);
    }

    [Fact]
    public void BoudicaGrantsOneImmortalUseUntilNextControllerTurnStart()
    {
        var game = Create(20211);
        var player = game.State.Players[0];
        var boudica = Card("ST06-07", "boudica");
        var target = Card("ST06-02", "boudica-target");
        player.Field[0][0] = boudica;
        player.Field[0][1] = target;

        Queue(game, boudica);
        Choose(game, target.InstanceId);
        PassResponses(game);

        Assert.Equal(1, target.ImmortalUses);
        Assert.Equal(int.MaxValue, target.ImmortalUntilTurn);
        Assert.Equal(0, target.ImmortalExpiresAtPlayerTurnStart);
        var firstLethal = game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: target.InstanceId));
        Assert.False(firstLethal.Accepted);
        Assert.Same(target, player.Field[0][1]);
        Assert.Equal(0, target.ImmortalUses);
        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: target.InstanceId)).Accepted);
        Assert.Contains(target, player.Graveyard);

        var expiryGame = Create(20212);
        var expiryBoudica = Card("ST06-07", "boudica-expiry");
        var expiryTarget = Card("ST06-02", "boudica-expiry-target");
        expiryGame.State.Players[0].Field[0][0] = expiryBoudica;
        expiryGame.State.Players[0].Field[0][1] = expiryTarget;
        Queue(expiryGame, expiryBoudica);
        Choose(expiryGame, expiryTarget.InstanceId);
        PassResponses(expiryGame);
        Invoke(expiryGame, "ExpireEffectsAtPlayerTurnStart", 1);
        Assert.Equal(1, expiryTarget.ImmortalUses);
        Invoke(expiryGame, "ExpireEffectsAtPlayerTurnStart", 0);
        Assert.Equal(0, expiryTarget.ImmortalUses);
        Assert.Equal(-1, expiryTarget.ImmortalUntilTurn);
    }
}
