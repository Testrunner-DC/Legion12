using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class TriggerTargetAvailabilityRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int controller, string master = "S01-01M1")
    {
        var basis = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "B2 availability", MasterId = master, CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds], SpecialIds = [.. basis.SpecialIds],
        };
        var game = new L12GameEngine(Catalog, "b2-availability", "B2", 93021,
            ["甲", "乙"], controller == 0 ? [deck, basis] : [basis, deck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = controller;
        game.State.FirstPlayer = controller;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear(); player.Library.Clear(); player.Graveyard.Clear(); player.Removed.Clear();
            player.Morale.Clear(); player.Resolving.Clear(); player.UsedAbilities.Clear();
            player.SpecialZones.Trials.Clear(); player.SpecialZones.Runes = 0;
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string id, int controller)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            CardId = cardId, InstanceId = id, Name = definition.NameZh, OwnerIndex = controller,
            CardType = definition.CardType, IsCounterTactic = definition.IsCounterTactic,
            Faction = definition.Faction, Cost = definition.Cost ?? 0, HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits], Profession = definition.Profession,
            EffectiveProfession = definition.Profession, ImageUrl = definition.ImageUrl,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0, SummonRound = -1,
        };
    }

    private static object? Invoke(L12GameEngine game, string name, params object?[] arguments)
        => (typeof(L12GameEngine).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(name)).Invoke(game, arguments);

    private static IEnumerable<L12TriggerCandidate> Candidates(L12GameEngine game)
        => game.State.PendingTriggerBatches.SelectMany(batch => batch.Candidates)
            .Concat(game.State.PendingTriggerStackCandidates);

    private static void Resume(L12GameEngine game)
    {
        game.State.IsResolvingStack = false;
        Invoke(game, "AdvanceTriggerBatches");
    }

    private static L12Prompt Choose(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(choice, prompt.ValidChoices);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var count = 0; count < 50 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; count++)
            Choose(game, "pass");
        Assert.NotEqual("response", game.State.PendingPrompts.FirstOrDefault()?.Kind);
    }

    private static void AssertIdle(L12GameEngine game)
    {
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.EffectStack);
    }

    private static void CollectCounter(L12GameEngine game, int controller, string cardId)
    {
        var counter = Card(cardId, "counter", controller);
        counter.Hidden = true;
        counter.SetRound = 0;
        game.State.Players[controller].Field[1][0] = counter;
        game.State.ActivePlayer = 1 - controller;
        // Real post-attack collector, deliberately not QueueReaction or a synthetic candidate.
        Invoke(game, "QueueS1PostAttackReactions", 1 - controller);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void RealPostAttackCollectorKeepsSeppukuIndependentDrawWithoutAnyEnemyLegion(int controller)
    {
        var game = Create(controller);
        var drawn = Card("S01-0104", "independent-draw", controller);
        game.State.Players[controller].Library.Add(drawn);
        CollectCounter(game, controller, "S01-0420");
        Assert.Equal("seppuku-draw", Assert.Single(game.State.EffectStack).Data["atomicFlow"]);
        PassResponses(game);
        Assert.Contains(drawn, game.State.Players[controller].Hand);
        var results = game.State.Events.Where(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == "counter")).ToArray();
        Assert.Contains(results, entry => entry.EffectResultStatus == "resolved");
        Assert.Contains(results, entry => entry.EffectResultStatus == "skipped");
        AssertIdle(game);
    }

    [Theory]
    [InlineData(0, "S01-0017")]
    [InlineData(1, "S01-0017")]
    [InlineData(0, "S01-0420")]
    [InlineData(1, "S01-0420")]
    public void PostAttackTargetAppearingAfterCollectionIsChosenOnlyAtDeclaration(int controller, string cardId)
    {
        var game = Create(controller);
        game.State.IsResolvingStack = true;
        CollectCounter(game, controller, cardId);
        Assert.Single(Candidates(game));
        var target = Card("S01-0104", "new-target", 1 - controller);
        target.Tapped = true;
        game.State.Players[1 - controller].Field[0][0] = target;
        game.State.Players[controller].Library.Add(Card("S01-0104", "draw", controller));
        Resume(game);
        Choose(game, target.InstanceId);
        PassResponses(game);
        Assert.Equal(Math.Max(0, target.BaseTroops - (cardId == "S01-0017" ? 2000 : 0)), target.Troops);
        Assert.Equal(cardId == "S01-0420" ? -2 : 0, target.CostModifier);
        AssertIdle(game);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void LastStandNoRestedTargetsStillDeclaresItsIndependentAllBranch(int controller)
    {
        var game = Create(controller);
        CollectCounter(game, controller, "S01-0017");
        Assert.Equal(["mode:all", "skip"], Assert.Single(game.State.PendingPrompts).ValidChoices);
        Choose(game, "mode:all");
        PassResponses(game);
        AssertIdle(game);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void NewlyRetainedTargetlessCounterOrderDoesNotRevealCoveredIdentityToOpponentOrSpectator(int controller)
    {
        var game = Create(controller);
        game.State.ActivePlayer = 1 - controller;
        for (var slot = 0; slot < 2; slot++)
        {
            var counter = Card(slot == 0 ? "S01-0017" : "S01-0420", $"hidden-counter-{slot}", controller);
            counter.Hidden = true;
            counter.SetRound = 0;
            game.State.Players[controller].Field[1][slot] = counter;
        }
        Invoke(game, "QueueS1PostAttackReactions", 1 - controller);
        Assert.Equal("trigger-order", Assert.Single(game.State.PendingPrompts).Kind);
        Assert.Equal(2, Candidates(game).Count());
        foreach (var snapshot in new[] { game.SnapshotFor(1 - controller), game.SnapshotForSpectator() })
        {
            var json = JsonSerializer.Serialize(snapshot);
            Assert.DoesNotContain("S01-0017", json);
            Assert.DoesNotContain("S01-0420", json);
            Assert.Empty(snapshot.Prompts);
            Assert.Null(snapshot.WaitingPrompt);
        }
        Assert.Contains("S01-0420", JsonSerializer.Serialize(game.SnapshotFor(controller)));
        Assert.Contains("S01-0420", JsonSerializer.Serialize(game.SnapshotForGm(controller)));
        Assert.Contains("S01-0420", JsonSerializer.Serialize(game.SnapshotForReferee()));
        Assert.Empty(game.SnapshotForReferee().Prompts);
    }

    private static void CollectDeath(L12GameEngine game, int controller, bool ranged)
    {
        var defeated = new L12CardInstance
        {
            CardId = "b2-vanilla", InstanceId = "defeated", Name = "阵亡军团", CardType = "legion",
            OwnerIndex = controller, Faction = "taiyangcheng", Cost = 2, LastKnownWasRanged = ranged,
        };
        var deaths = new (int Controller, L12CardInstance Card, L12CardInstance SourceSnapshot)[]
            { (controller, defeated, defeated) };
        Invoke(game, "QueueSimultaneousDeathTriggers", (object)deaths);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ArtemisDeathWithoutMoraleRetainsCandidateAndUsesLaterExactMorale(int controller)
    {
        var game = Create(controller, "S02-05M1");
        game.State.IsResolvingStack = true;
        CollectDeath(game, controller, ranged: true);
        Assert.Equal("artemisDeathFlip", Assert.Single(Candidates(game)).Data["ability"]);
        var morale = new L12MoraleCard { CardId = "S02-05C1", InstanceId = "later-morale" };
        game.State.Players[controller].Morale.Add(morale);
        Resume(game);
        Choose(game, "mode:use");
        Choose(game, morale.InstanceId);
        PassResponses(game);
        Assert.True(morale.IsGodPower);
        AssertIdle(game);
    }

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(1, true, true)]
    [InlineData(0, false, true)]
    [InlineData(1, false, false)]
    public void SimultaneousXiaotianDeathActuallyGeneratesMoraleBeforeArtemisDeclaration(
        int controller, bool moraleFirst, bool restore)
    {
        var game = Create(controller, "S02-05M1");
        var player = game.State.Players[controller];
        player.MoraleDeck.Clear();
        player.MoraleDeck.Add(new L12MoraleCard { CardId = "S02-05C1", InstanceId = "generated-morale" });
        var dog = Card("S02-01S1", "fallen-dog", controller);
        var ranged = new L12CardInstance
        {
            CardId = "b2-ranged", InstanceId = "fallen-ranged", Name = "远程军团", CardType = "legion",
            Faction = "universal", OwnerIndex = controller, LastKnownWasRanged = true,
        };
        var deaths = new (int Controller, L12CardInstance Card, L12CardInstance SourceSnapshot)[]
            { (controller, dog, dog), (controller, ranged, ranged) };
        Invoke(game, "QueueSimultaneousDeathTriggers", (object)deaths);
        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-order", order.Kind);
        Assert.Equal(2, order.ValidChoices.Count);
        var candidates = Candidates(game).ToArray();
        var gain = Assert.Single(candidates, candidate => candidate.SourceInstanceId == dog.InstanceId);
        var flip = Assert.Single(candidates, candidate => candidate.Data.GetValueOrDefault("ability") == "artemisDeathFlip");
        var result = game.Handle(controller, new L12Command("resolvePrompt", PromptId: order.PromptId,
            CardInstanceIds: moraleFirst ? [flip.CandidateId, gain.CandidateId] : [gain.CandidateId, flip.CandidateId]));
        Assert.True(result.Accepted, result.Error);
        if (restore)
            game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
                game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        Choose(game, "mode:use");
        PassResponses(game);
        if (moraleFirst)
        {
            Choose(game, "mode:use");
            Choose(game, "generated-morale");
            PassResponses(game);
        }
        var morale = Assert.Single(game.State.Players[controller].Morale);
        Assert.Equal(moraleFirst, morale.IsGodPower);
        Assert.DoesNotContain(game.State.Players[controller].UsedAbilities,
            value => value.EndsWith(":pending", StringComparison.Ordinal));
        AssertIdle(game);
    }

    [Theory]
    [InlineData(0, "S02-05M1")]
    [InlineData(1, "S02-05M1")]
    [InlineData(0, "S02-02M1")]
    [InlineData(1, "S02-02M1")]
    [InlineData(0, "S01-02M3")]
    [InlineData(1, "S01-02M3")]
    public void OptionalTargetStillUnavailableAtDeclarationReleasesReservationWithoutUsingOnce(int controller, string master)
    {
        var game = Create(controller, master);
        game.State.ActivePlayer = 1 - controller;
        game.State.IsResolvingStack = true;
        if (master == "S01-02M3") Invoke(game, "QueueS1MasterDamageReaction", controller, 1 - controller, false);
        else CollectDeath(game, controller, ranged: true);
        Assert.Single(Candidates(game));
        Resume(game);
        Assert.Empty(game.State.Players[controller].UsedAbilities);
        AssertIdle(game);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PrintedDeathAndDamageConditionsAreNotRelaxedWithAvailability(int controller)
    {
        var artemis = Create(controller, "S02-05M1");
        artemis.State.IsResolvingStack = true;
        CollectDeath(artemis, controller, ranged: false);
        Assert.Empty(Candidates(artemis));
        var medjed = Create(controller, "S01-02M3");
        medjed.State.ActivePlayer = 1 - controller;
        medjed.State.IsResolvingStack = true;
        Invoke(medjed, "QueueS1MasterDamageReaction", controller, null, true);
        Assert.Empty(Candidates(medjed));
        medjed.State.ActivePlayer = controller;
        Invoke(medjed, "QueueS1MasterDamageReaction", controller, 1 - controller, true);
        Assert.Empty(Candidates(medjed));
    }

    [Theory]
    [InlineData(0, "S02-02M1", "S02-0201")]
    [InlineData(1, "S02-02M1", "S02-0201")]
    [InlineData(0, "S01-02M3", "S01-0212")]
    [InlineData(1, "S01-02M3", "S01-0212")]
    public void ResurrectionCollectorKeepsMissingGraveTargetThenUsesLaterInstanceAndSlot(
        int controller, string master, string targetCardId)
    {
        var game = Create(controller, master);
        var player = game.State.Players[controller];
        game.State.ActivePlayer = 1 - controller;
        for (var row = 0; row < 2; row++)
        for (var slot = 0; slot < 3; slot++)
            player.Field[row][slot] = Card("S01-0104", $"occupied-{row}-{slot}", controller);
        game.State.IsResolvingStack = true;
        if (master == "S01-02M3") Invoke(game, "QueueS1MasterDamageReaction", controller, 1 - controller, false);
        else CollectDeath(game, controller, ranged: false);
        Assert.Single(Candidates(game));
        var chosen = Card(targetCardId, "chosen", controller);
        player.Graveyard.Add(chosen);
        player.Graveyard.Add(Card(targetCardId, "same-name-not-chosen", controller));
        player.Field[0][2] = null;
        Resume(game);
        Choose(game, chosen.InstanceId);
        if (Assert.Single(game.State.PendingPrompts).ValidChoices.Contains($"battlefield:{controller}"))
            Choose(game, $"battlefield:{controller}");
        Choose(game, "0:2");
        PassResponses(game);
        Assert.Equal(chosen.InstanceId, player.Field[0][2]?.InstanceId);
        Assert.False(player.Field[0][2]!.Tapped);
        Assert.Contains(player.Graveyard, card => card.InstanceId == "same-name-not-chosen");
        AssertIdle(game);
    }

    private static void CollectXiaotian(L12GameEngine game, int controller)
        => Invoke(game, "QueueS2MasterMoraleReturnTriggers", controller,
            Card("S01-01M1", $"master-{controller}", controller), 4);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void XiaotianFullFrontAndExistingDogAreDeferredUntilActualDeclaration(int controller)
    {
        var game = Create(controller);
        var player = game.State.Players[controller];
        for (var slot = 0; slot < 3; slot++)
            player.Field[0][slot] = Card(slot == 0 ? "S02-01S1" : "S01-0104", $"occupied-{slot}", controller);
        game.State.IsResolvingStack = true;
        CollectXiaotian(game, controller);
        Assert.Equal("xiaotian", Assert.Single(Candidates(game)).Data["mode"]);
        player.Field[0][0] = null;
        Resume(game);
        Choose(game, "mode:use");
        var committed = Choose(game, "0:0");
        Assert.False(game.Handle(controller, new L12Command("resolvePrompt", PromptId: committed.PromptId, Choice: "0:0")).Accepted);
        PassResponses(game);
        Assert.Equal("S02-01S1", player.Field[0][0]?.CardId);
        Assert.False(player.Field[0][0]!.Tapped);
        Assert.Single(player.Field.SelectMany(row => row), card => card?.CardId == "S02-01S1");
        AssertIdle(game);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void XiaotianDeclarationRejectsExistingOrCrossBattlefieldDuplicateInstance(int controller, bool duplicate)
    {
        var game = Create(controller);
        var existing = Card("S02-01S1", duplicate ? $"p{controller}-xiaotian" : "other-dog", controller);
        existing.Hidden = true;
        game.State.Players[duplicate ? 1 - controller : controller].Field[1][0] = existing;
        game.State.IsResolvingStack = true;
        CollectXiaotian(game, controller);
        Assert.Single(Candidates(game));
        Resume(game);
        Assert.Empty(game.State.Players[controller].UsedAbilities);
        AssertIdle(game);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void XiaotianRechecksLimitAtCommitAndSettlement(int controller, bool afterCommit)
    {
        var game = Create(controller);
        var player = game.State.Players[controller];
        CollectXiaotian(game, controller);
        Choose(game, "mode:use");
        if (afterCommit) Choose(game, "0:0");
        var existing = Card("S02-01S1", "intervening-dog", controller);
        player.Field[1][1] = existing;
        if (!afterCommit) Choose(game, "0:0");
        PassResponses(game);
        Assert.Null(player.Field[0][0]);
        Assert.Same(existing, player.Field[1][1]);
        Assert.Equal(afterCommit, player.UsedAbilities.Contains($"trigger:xiaotian-morale:{game.State.TurnSerial}"));
        AssertIdle(game);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void XiaotianSettlementCannotOverwriteOccupiedSlotOrDuplicateAnInstanceOnOtherBattlefield(
        int controller, bool duplicate)
    {
        var game = Create(controller);
        CollectXiaotian(game, controller);
        Choose(game, "mode:use");
        Choose(game, "0:0");
        game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        var blocker = Card(duplicate ? "S02-01S1" : "S01-0104",
            duplicate ? $"p{controller}-xiaotian" : "occupied-before-settlement", controller);
        var host = duplicate ? 1 - controller : controller;
        game.State.Players[host].Field[0][0] = blocker;
        PassResponses(game);
        Assert.Same(blocker, game.State.Players[host].Field[0][0]);
        Assert.Single(game.State.Players.SelectMany(player => player.Field).SelectMany(row => row),
            card => card is not null);
        Assert.Contains($"trigger:xiaotian-morale:{game.State.TurnSerial}", game.State.Players[controller].UsedAbilities);
        AssertIdle(game);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void XiaotianDeclineDoesNotConsumeOnceOrLeaveAWaitingCandidate(int controller)
    {
        var game = Create(controller);
        CollectXiaotian(game, controller);
        var declined = Choose(game, "mode:none");
        Assert.False(game.Handle(controller,
            new L12Command("resolvePrompt", PromptId: declined.PromptId, Choice: "mode:use")).Accepted);
        Assert.Empty(game.State.Players[controller].UsedAbilities);
        AssertIdle(game);
        CollectXiaotian(game, controller);
        Assert.Contains("mode:use", Assert.Single(game.State.PendingPrompts).ValidChoices);
    }

    private static void CollectRichard(L12GameEngine game, int controller, L12CardInstance richard)
    {
        game.State.Players[controller].Field[0][0] = richard;
        var result = game.Handle(controller, new L12Command("attack", richard.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(result.Accepted, result.Error);
    }

    private static void SquiresFirst(L12GameEngine game)
    {
        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-order", order.Kind);
        var candidates = Candidates(game).ToArray();
        var defense = Assert.Single(candidates, candidate => candidate.Data.GetValueOrDefault("attackPlan") == "richard-defense");
        var squires = Assert.Single(candidates, candidate => candidate.Data.GetValueOrDefault("attackPlan") == "richard-squires");
        var result = game.Handle(order.PlayerIndex, new L12Command("resolvePrompt", PromptId: order.PromptId,
            CardInstanceIds: [defense.CandidateId, squires.CandidateId]));
        Assert.True(result.Accepted, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void RichardNoAttachedSquireStillCollectsTwoCandidatesThenPaysOnlyLaterChosenInstance(int controller)
    {
        var game = Create(controller, "S02-06M1");
        var richard = Card("S02-0608", "richard", controller);
        CollectRichard(game, controller, richard);
        Assert.Equal(2, Candidates(game).Count());
        var chosen = Card("S02-0609", "chosen-squire", controller);
        var retained = Card("S02-0609", "retained-squire", controller);
        richard.AttachedCards.AddRange([chosen, retained]);
        SquiresFirst(game);
        Choose(game, "mode:use");
        Choose(game, chosen.InstanceId);
        Assert.Contains(chosen, game.State.Players[controller].Graveyard);
        Assert.Equal(retained.InstanceId, Assert.Single(richard.AttachedCards).InstanceId);
        PassResponses(game);
        Assert.Equal(richard.BaseTroops + 1000, richard.Troops);
        AssertIdle(game);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void RichardUnavailableSquiresDoNotSwallowDefenseCandidate(int controller)
    {
        var game = Create(controller, "S02-06M1");
        var richard = Card("S02-0608", "richard", controller);
        CollectRichard(game, controller, richard);
        SquiresFirst(game);
        // A decline-only optional branch must finish locally, then retain the defense item.
        Assert.Contains(game.State.EffectStack, item => item.Data.GetValueOrDefault("attackPlan") == "richard-defense");
        PassResponses(game);
        Assert.Equal(richard.BaseTroops, richard.Troops);
        AssertIdle(game);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void RobinPrintedRichardConditionRemainsACollectionCondition(int controller)
    {
        var game = Create(controller, "S02-06M1");
        var robin = Card("S02-0617", "robin", controller);
        game.State.Players[controller].Field[0][0] = robin;
        game.State.IsResolvingStack = true;
        Invoke(game, "QueueOrPushTriggeredEffect", controller, robin, "attack", "进攻时", null, null);
        Assert.Equal("robin-rune", Assert.Single(Candidates(game)).Data["attackPlan"]);
    }
}
