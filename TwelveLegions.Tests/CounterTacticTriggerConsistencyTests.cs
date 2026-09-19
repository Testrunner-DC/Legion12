using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class CounterTacticTriggerConsistencyTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed = 91901, string? firstMasterId = null)
    {
        var basis = Catalog.DeckAt(0);
        var firstDeck = firstMasterId is null ? basis : new L12PresetDeckDefinition
        {
            Name = "触发一致性主宰",
            MasterId = firstMasterId,
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "counter-trigger-consistency", "COUNTER-TRIGGER", seed,
            ["甲", "乙"], [firstDeck, basis], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 1;
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
            player.UsedAbilities.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            IsCounterTactic = definition.IsCounterTactic,
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
            SummonRound = -1,
        };
    }

    private static L12CardInstance Legion(string instanceId, string faction = "universal", int cost = 3)
        => new()
        {
            InstanceId = instanceId,
            CardId = $"test-{instanceId}",
            Name = instanceId,
            CardType = "legion",
            Faction = faction,
            Cost = cost,
            BaseTroops = 2000,
            Troops = 2000,
            SummonRound = -1,
        };

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().Name, name);
        return method.Invoke(target, args);
    }

    private static IEnumerable<L12TriggerCandidate> QueuedCandidates(L12GameEngine game)
        => game.State.PendingTriggerBatches.SelectMany(batch => batch.Candidates)
            .Concat(game.State.PendingTriggerStackCandidates);

    private static int ActiveSourceCount(L12GameEngine game, string sourceInstanceId)
        => QueuedCandidates(game).Count(candidate => candidate.SourceInstanceId == sourceInstanceId)
            + game.State.EffectStack.Count(item => item.SourceInstanceId == sourceInstanceId)
            + game.State.DeferredEffectStack.Count(item => item.SourceInstanceId == sourceInstanceId);

    [Theory]
    [InlineData("S01-0223")]
    [InlineData("S01-0320")]
    [Trait("L12Evidence", "family:counter-tactic-trigger-deduplication")]
    public void OneSetCounterTacticProducesOnlyOneCandidateForASimultaneousLeaveBatch(string counterCardId)
    {
        var game = Create(counterCardId == "S01-0223" ? 91901 : 91902);
        var player = game.State.Players[0];
        var counter = Card(counterCardId, $"counter-{counterCardId}");
        counter.Hidden = true;
        counter.SetRound = 0;
        player.Field[1][0] = counter;
        var first = Legion("simultaneous-first");
        var second = Legion("simultaneous-second");
        var deaths = new (int Controller, L12CardInstance Card, L12CardInstance SourceSnapshot)[]
        {
            (0, first, first),
            (0, second, second),
        };

        Invoke(game, "QueueSimultaneousDeathTriggers", (object)deaths);

        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Continuation == "trigger-batch-order");
        Assert.Equal(1, ActiveSourceCount(game, counter.InstanceId));
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-02M1")]
    [Trait("L12Evidence", "family:once-per-turn-trigger-reservation")]
    public void NephthysReservesOneCandidatePerBatchAndDeclineReleasesTheReservation()
    {
        var game = Create(91903, "S02-02M1");
        var player = game.State.Players[0];
        var scarab = Card("S02-0201", "nephthys-scarab");
        player.Graveyard.Add(scarab);
        var first = Legion("nephthys-first", "taiyangcheng", 2);
        var second = Legion("nephthys-second", "taiyangcheng", 2);
        var deaths = new (int Controller, L12CardInstance Card, L12CardInstance SourceSnapshot)[]
        {
            (0, first, first),
            (0, second, second),
        };

        Invoke(game, "QueueSimultaneousDeathTriggers", (object)deaths);

        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Continuation == "trigger-batch-order");
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:none", prompt.ValidChoices);
        var onceKey = L12MasterTriggeredUsageRules.Key("nephthysScarab", player.PlayerIndex, game.State.TurnSerial);
        Assert.Contains($"{onceKey}:pending", player.UsedAbilities);

        var declined = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: "mode:none"));
        Assert.True(declined.Accepted, declined.Error);
        Assert.DoesNotContain(onceKey, player.UsedAbilities);
        Assert.DoesNotContain($"{onceKey}:pending", player.UsedAbilities);
        Assert.NotNull(Invoke(game, "BuildNephthysOwnDeathCandidate", 0,
            Legion("nephthys-later", "taiyangcheng", 2)));
    }

    [Theory]
    [InlineData("S01-0223", "leave")]
    [InlineData("S01-0320", "leave")]
    [InlineData("S01-0420", "after-attack")]
    [Trait("L12Evidence", "card:S02-0003")]
    [Trait("L12Evidence", "family:counter-tactic-common-disable")]
    public void CourtMagicianStopsCounterTriggerCandidatesAtTheCommonQueue(string cardId, string trigger)
    {
        var game = Create(91910);
        var counter = Card(cardId, $"disabled-{cardId}");
        counter.Hidden = true;
        game.State.Players[0].Field[1][0] = counter;
        game.State.CounterTacticsDisabledUntilTurnSerial = int.MaxValue;
        game.State.CounterTacticsDisabledExpiresAtPlayerTurnStart = 1;
        var candidate = new L12TriggerCandidate
        {
            CandidateId = $"disabled-candidate-{cardId}",
            Controller = 0,
            SourceInstanceId = counter.InstanceId,
            SourceCardId = counter.CardId,
            SourceName = counter.Name,
            Trigger = trigger,
            Text = "反击战术触发",
            SourceSnapshot = counter,
        };

        Invoke(game, "QueueTriggerCandidates", (object)new[] { candidate });

        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.PendingPrompts);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0003")]
    [Trait("L12Evidence", "family:counter-tactic-common-disable")]
    public void CourtMagicianRechecksAQueuedCounterBeforeDeclaration()
    {
        var game = Create(91911);
        var counter = Card("S01-0223", "disabled-pending-counter");
        counter.Hidden = true;
        game.State.Players[0].Field[1][0] = counter;
        game.State.PendingTriggerStackCandidates.Add(new L12TriggerCandidate
        {
            CandidateId = "disabled-pending-candidate",
            Controller = 0,
            SourceInstanceId = counter.InstanceId,
            SourceCardId = counter.CardId,
            SourceName = counter.Name,
            Trigger = "leave",
            Text = "反击战术触发",
            SourceSnapshot = counter,
        });
        game.State.CounterTacticsDisabledUntilTurnSerial = int.MaxValue;
        game.State.CounterTacticsDisabledExpiresAtPlayerTurnStart = 1;

        Invoke(game, "AdvancePendingTriggerStackCandidates");

        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.PendingPrompts);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0003")]
    [Trait("L12Evidence", "family:counter-tactic-common-disable")]
    public void CourtMagicianBlocksSetCountersButNotHandLegionResponses()
    {
        var game = Create(91912);
        var defender = game.State.Players[1];
        var counter = Card("S01-0016", "disabled-set-counter");
        counter.Hidden = true;
        defender.Field[1][0] = counter;
        var puppet = Card("S02-0005", "available-hand-puppet");
        defender.Hand.Add(puppet);
        game.State.PendingDefense = new L12PendingDefense
        {
            AttackerPlayer = 0,
            AttackerInstanceId = "attacker",
            Target = new L12AttackTarget("master"),
            Stage = L12CombatStage.AttackerAttackTiming,
        };
        game.State.CounterTacticsDisabledUntilTurnSerial = int.MaxValue;
        game.State.CounterTacticsDisabledExpiresAtPlayerTurnStart = 1;
        var top = new L12StackItem
        {
            StackItemId = "opponent-attack",
            Controller = 0,
            SourceInstanceId = "attacker",
            SourceCardId = "test-attacker",
            SourceName = "进攻军团",
            Trigger = "opponent-attack",
            Text = "进攻",
        };

        var choices = Assert.IsType<List<string>>(Invoke(game, "LegalResponseSources", 1, top));

        Assert.DoesNotContain(counter.InstanceId, choices);
        Assert.Contains(puppet.InstanceId, choices);
    }

}
