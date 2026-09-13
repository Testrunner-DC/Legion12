using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SimpleDeathMasterHealTriggerConsistencyTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "simple-death-heal", "HEAL-DEATH", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 7;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Library.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner = 0)
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            SummonRound = -1,
            OwnerIndex = owner,
        };
    }

    private static object? Invoke(object target, string method, params object?[] args)
    {
        var candidate = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(info => info.Name == method && info.GetParameters().Length == args.Length);
        return candidate.Invoke(target, args);
    }

    private static L12CardInstance QueueDeath(L12GameEngine game, string cardId,
        int controller = 0, int owner = 0)
    {
        var source = Card(cardId, $"heal-source-{cardId}-{controller}-{owner}", owner);
        game.State.Players[controller].Resolving.Add(source);
        Invoke(game, "QueueOrPushTriggeredEffect", controller, source, "death",
            "单恢复一致性测试", null, new Dictionary<string, string> { ["cause"] = "effect" });
        return source;
    }

    private static L12Prompt OnlyResponse(L12GameEngine game)
        => Assert.Single(game.State.PendingPrompts, prompt => prompt.Kind == "response");

    private static void ResolvePrompt(L12GameEngine game, L12Prompt prompt, string choice)
    {
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            ResolvePrompt(game, OnlyResponse(game), "pass");
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-death-master-heal-spec")]
    public void TwoExactSingleMasterHealDeathEffectsUseOneParameterizedProgramShape()
    {
        var expectedCardIds = new[] { "S01-0302", "S02-0613" };
        Assert.Equal(expectedCardIds, L12SimpleMasterHealTriggerEffects.All.Select(spec => spec.CardId));

        var nonSettlementKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            L12AtomKinds.Trigger,
            L12AtomKinds.Condition,
            L12AtomKinds.Optional,
            L12AtomKinds.Legacy,
            L12AtomKinds.CompositeFlow,
        };
        var catalogMatches = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.Trigger == "death")
            .Where(ability =>
            {
                var settlement = ability.Atoms.Where(atom => !nonSettlementKinds.Contains(atom.Kind)).ToArray();
                return settlement.Length == 1 && settlement[0].Kind == L12AtomKinds.HealMaster;
            })
            .Select(ability => ability.CardId)
            .OrderBy(cardId => cardId, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedCardIds, catalogMatches);

        foreach (var spec in L12SimpleMasterHealTriggerEffects.All)
        {
            var ability = Catalog.AtomicEffects.Find(spec.CardId)!.Abilities
                .Single(item => item.Sequence == spec.AbilitySequence);
            Assert.Equal("death", ability.Trigger);
            Assert.Equal(spec.SettlementText,
                Assert.Single(ability.Presentations, scene =>
                    scene.Flow == L12SingleSegmentTriggeredEffectPresentations.Flow).DefaultText);

            var program = Assert.IsType<L12VerifiedAtomicProgram>(
                L12VerifiedAtomicPrograms.Find(spec.CardId, spec.Trigger));
            Assert.Single(program.Atoms, atom => atom.Kind == L12AtomKinds.Trigger);
            var heal = Assert.Single(program.Atoms, atom => atom.Kind == L12AtomKinds.HealMaster);
            Assert.DoesNotContain(program.Atoms,
                atom => atom.Kind is not L12AtomKinds.Trigger and not L12AtomKinds.HealMaster);
            Assert.Equal(spec.Amount.ToString(), heal.Parameters["amount"]);
            Assert.Equal(spec.HealRecipient, heal.Parameters["target"]);
            Assert.Equal(spec.Reason, heal.Parameters["reason"]);
            Assert.Equal(spec.SettlementText,
                L12GameEngine.ResolveTriggeredEffectDisplayText(
                    Card(spec.CardId, $"display-{spec.CardId}"), spec.Trigger, "【阵亡时】效果"));
        }
    }

    [Theory]
    [InlineData("S01-0302", 1, 0, 1)]
    [InlineData("S02-0613", 1, 1, 1)]
    [Trait("L12Evidence", "entry:simple-death-master-heal-recipient")]
    public void MandatoryHealUsesItsDeclaredRecipientSet(
        string cardId, int controller, int expectedPlayerZeroGain, int expectedPlayerOneGain)
    {
        var game = Create(10100 + controller + cardId.Length);
        foreach (var player in game.State.Players) player.Hp = player.MaxHp - 2;
        var before = game.State.Players.Select(player => player.Hp).ToArray();
        QueueDeath(game, cardId, controller, owner: 0);

        Assert.Empty(game.State.PendingActivations);
        var trigger = Assert.Single(game.State.Events, entry => entry.Type == "effect-trigger"
            && entry.Cards.Any(card => card.CardId == cardId));
        PassResponses(game);

        Assert.Equal(before[0] + expectedPlayerZeroGain, game.State.Players[0].Hp);
        Assert.Equal(before[1] + expectedPlayerOneGain, game.State.Players[1].Hp);
        var result = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.CardId == cardId));
        Assert.Equal("resolved", result.EffectResultStatus);
        Assert.Equal(trigger.EffectSceneId, result.EffectSceneId);
        Assert.Equal(trigger.EffectText, result.EffectText);
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-death-master-heal-noop")]
    public void FullHealthMandatoryHealResolvesWithoutCreatingAChoiceOrFalseHealLog()
    {
        var game = Create(10120);
        QueueDeath(game, "S02-0613");
        Assert.Empty(game.State.PendingActivations);
        PassResponses(game);

        Assert.DoesNotContain(game.State.Events, entry => entry.Type is "heal" or "heal-prevented");
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.CardId == "S02-0613"));
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-death-master-heal-partial-prevention")]
    public void BothMasterHealAppliesIndependentlyWhenOnlyOneMasterCannotHeal()
    {
        var game = Create(10121);
        foreach (var player in game.State.Players) player.Hp = player.MaxHp - 2;
        game.State.Players[0].MasterCannotHeal = true;
        var before = game.State.Players.Select(player => player.Hp).ToArray();
        QueueDeath(game, "S02-0613", controller: 1, owner: 1);
        PassResponses(game);

        Assert.Equal(before[0], game.State.Players[0].Hp);
        Assert.Equal(before[1] + 1, game.State.Players[1].Hp);
        Assert.Contains(game.State.Events, entry => entry.Type == "heal-prevented" && entry.PlayerIndex == 0);
        Assert.Contains(game.State.Events, entry => entry.Type == "heal" && entry.PlayerIndex == 1);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.CardId == "S02-0613"));
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-death-master-heal-negated")]
    public void NegatedMandatoryHealDoesNotChangeHealth()
    {
        var game = Create(10122);
        game.State.Players[0].Hp = game.State.Players[0].MaxHp - 2;
        var before = game.State.Players[0].Hp;
        QueueDeath(game, "S01-0302");
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.Equal(before, game.State.Players[0].Hp);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "negated"
            && entry.Cards.Any(card => card.CardId == "S01-0302"));
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-death-master-heal-reconnect")]
    public void MandatoryHealRestoresDuringResponseAndRejectsTheCompletedPrompt()
    {
        var game = Create(10123);
        foreach (var player in game.State.Players) player.Hp = player.MaxHp - 2;
        var before = game.State.Players.Select(player => player.Hp).ToArray();
        QueueDeath(game, "S02-0613");
        var oldResponse = OnlyResponse(game);
        Assert.Contains(oldResponse.PromptId, JsonSerializer.Serialize(game.SnapshotFor(oldResponse.PlayerIndex)),
            StringComparison.Ordinal);
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");

        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        PassResponses(game);

        Assert.Equal(before[0] + 1, game.State.Players[0].Hp);
        Assert.Equal(before[1] + 1, game.State.Players[1].Hp);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.CardId == "S02-0613"));
        Assert.False(game.Handle(oldResponse.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: oldResponse.PromptId, Choice: "pass")).Accepted);
    }
}
