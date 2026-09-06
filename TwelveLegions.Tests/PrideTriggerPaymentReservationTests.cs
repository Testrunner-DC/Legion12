using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PrideTriggerPaymentReservationTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, string masterId)
    {
        var original = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = $"{masterId}傲慢跨步费用测试",
            MasterId = masterId,
            CardIds = [.. original.CardIds],
            MoraleIds = [.. original.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "pride-trigger-payment", "PRIDEPAY", seed,
            ["甲", "乙"], [deck, original], skipPreparation: true,
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
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.TemporaryMorale = 0;
            player.UsedAbilities.Clear();
        }
        game.State.ActiveDisaster = Card("S02-DS06", "pride-payment-disaster");
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

    private static L12MoraleCard Morale(string instanceId)
        => new() { CardId = "S02-04C1", InstanceId = instanceId, Tapped = false };

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == name && candidate.GetParameters().Length == args.Length);
        return method.Invoke(target, args);
    }

    private static L12Prompt OnlyPrompt(L12GameEngine game)
        => Assert.Single(game.State.PendingPrompts);

    private static CommandResult Resolve(L12GameEngine game, L12Prompt prompt, string choice)
        => game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));

    private static void ResolveAccepted(L12GameEngine game, string choice)
    {
        var result = Resolve(game, OnlyPrompt(game), choice);
        Assert.True(result.Accepted, result.Error);
    }

    private static (L12CardInstance Moved, L12CardInstance Target) QueueTsukuyomiFollowMove(L12GameEngine game)
    {
        var player = game.State.Players[0];
        var moved = Card("S02-0401", "pride-tsukuyomi-moved");
        var target = Card("S02-0402", "pride-tsukuyomi-target");
        moved.OwnerIndex = target.OwnerIndex = 0;
        player.Field[0][0] = moved;
        player.Field[0][2] = target;
        var master = Card("S02-04M1", "master-0");
        master.OwnerIndex = 0;
        Invoke(game, "QueueOrPushTriggeredEffect", 0, master, "active", "军团位移时效果", null,
            new Dictionary<string, string>
            {
                ["ability"] = "tsukuyomiFollowMove",
                ["moved"] = moved.InstanceId,
            });
        return (moved, target);
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("temporary")]
    [InlineData("tomb-guard")]
    public void TsukuyomiUsesTwoDistinctReservedResourcesAcrossBaseAndSurcharge(string baseResourceKind)
    {
        var game = Create(9100 + baseResourceKind.Length, "S02-04M1");
        var player = game.State.Players[0];
        var firstMorale = Morale("pride-first-morale");
        var secondMorale = Morale("pride-second-morale");
        L12CardInstance? guard = null;
        string baseChoice;
        switch (baseResourceKind)
        {
            case "ordinary":
                player.Morale.AddRange([firstMorale, secondMorale]);
                baseChoice = firstMorale.InstanceId;
                break;
            case "temporary":
                player.TemporaryMorale = 2;
                baseChoice = "temporary-morale:1";
                break;
            default:
                player.Morale.Add(firstMorale);
                guard = Card("S01-0212", "pride-tomb-guard");
                guard.OwnerIndex = 0;
                player.Field[1][2] = guard;
                baseChoice = guard.InstanceId;
                break;
        }
        var (_, target) = QueueTsukuyomiFollowMove(game);

        ResolveAccepted(game, "mode:use");
        var basePrompt = OnlyPrompt(game);
        Assert.Contains(baseChoice, basePrompt.ValidChoices);
        Assert.True(Resolve(game, basePrompt, baseChoice).Accepted);
        ResolveAccepted(game, target.InstanceId);
        var slotPrompt = OnlyPrompt(game);
        Assert.True(Resolve(game, slotPrompt, "0:1").Accepted);

        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Contains(game.State.EffectStack, item =>
            item.Data.GetValueOrDefault("ability") == "tsukuyomiFollowMove");
        Assert.Contains(game.State.Events, entry => entry.Type == "cost"
            && entry.Text.Contains("傲慢之罪", StringComparison.Ordinal));
        switch (baseResourceKind)
        {
            case "ordinary":
                Assert.True(firstMorale.Tapped);
                Assert.True(secondMorale.Tapped);
                break;
            case "temporary":
                Assert.Equal(0, player.TemporaryMorale);
                break;
            default:
                Assert.True(guard!.Tapped);
                Assert.True(firstMorale.Tapped);
                break;
        }

        var consumedAfterCommit = player.Morale.Count(card => card.Tapped)
            + (guard?.Tapped == true ? 1 : 0);
        var temporaryAfterCommit = player.TemporaryMorale;
        var replay = Resolve(game, slotPrompt, "0:1");
        Assert.False(replay.Accepted);
        Assert.Equal(consumedAfterCommit, player.Morale.Count(card => card.Tapped)
            + (guard?.Tapped == true ? 1 : 0));
        Assert.Equal(temporaryAfterCommit, player.TemporaryMorale);
    }

    [Fact]
    public void OneResourceCannotBeReusedForTsukuyomiBaseCostAndPrideSurcharge()
    {
        var game = Create(9110, "S02-04M1");
        var player = game.State.Players[0];
        var onlyMorale = Morale("pride-only-morale");
        player.Morale.Add(onlyMorale);
        var (_, target) = QueueTsukuyomiFollowMove(game);

        ResolveAccepted(game, "mode:use");
        ResolveAccepted(game, target.InstanceId);
        var slot = OnlyPrompt(game);
        Assert.True(Resolve(game, slot, "0:1").Accepted);

        Assert.False(onlyMorale.Tapped);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.DoesNotContain(game.State.EffectStack, item =>
            item.Data.GetValueOrDefault("ability") == "tsukuyomiFollowMove");
        Assert.DoesNotContain("active:master-0:tsukuyomiFollowMove", player.UsedAbilities);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "cost"
            && entry.Text.Contains("傲慢之罪", StringComparison.Ordinal));
    }

    [Fact]
    public void ForgedDuplicateSurchargeChoiceIsRejectedWithoutChargingAndCannotBeReplayed()
    {
        var game = Create(9120, "S02-04M1");
        var player = game.State.Players[0];
        var first = Morale("pride-duplicate-first");
        var second = Morale("pride-duplicate-second");
        var third = Morale("pride-duplicate-third");
        player.Morale.AddRange([first, second, third]);
        var (_, target) = QueueTsukuyomiFollowMove(game);

        ResolveAccepted(game, "mode:use");
        ResolveAccepted(game, first.InstanceId);
        ResolveAccepted(game, target.InstanceId);
        ResolveAccepted(game, "0:1");
        var surcharge = OnlyPrompt(game);
        Assert.DoesNotContain(first.InstanceId, surcharge.ValidChoices);
        Assert.Contains(second.InstanceId, surcharge.ValidChoices);

        var forged = Resolve(game, surcharge, first.InstanceId);
        Assert.False(forged.Accepted);
        Assert.All(player.Morale, card => Assert.False(card.Tapped));
        Assert.Equal(surcharge.PromptId, OnlyPrompt(game).PromptId);

        Assert.True(Resolve(game, surcharge, second.InstanceId).Accepted);
        Assert.True(first.Tapped);
        Assert.True(second.Tapped);
        Assert.False(third.Tapped);
        var replay = Resolve(game, surcharge, third.InstanceId);
        Assert.False(replay.Accepted);
        Assert.True(first.Tapped);
        Assert.True(second.Tapped);
        Assert.False(third.Tapped);
    }

    [Fact]
    public void KagutsuchiTargetInvalidationCommitsNeitherBaseCostNorSurcharge()
    {
        var game = Create(9130, "ST04-M1");
        var player = game.State.Players[0];
        var target = Card("ST04-03", "pride-kagutsuchi-target");
        target.OwnerIndex = 0;
        player.Field[0][0] = target;
        var first = Morale("pride-kagutsuchi-first");
        var second = Morale("pride-kagutsuchi-second");
        var third = Morale("pride-kagutsuchi-third");
        player.Morale.AddRange([first, second, third]);
        var candidate = Assert.IsType<L12TriggerCandidate>(Invoke(game,
            "BuildStarterKagutsuchiCandidate", 0, target));
        game.State.PendingTriggerStackCandidates.Add(candidate);
        Invoke(game, "AdvanceTriggerBatches");

        ResolveAccepted(game, "mode:morale");
        ResolveAccepted(game, first.InstanceId);
        var surcharge = OnlyPrompt(game);
        Assert.DoesNotContain(first.InstanceId, surcharge.ValidChoices);
        player.Field[0][0] = null;
        player.Graveyard.Add(target);
        Assert.True(Resolve(game, surcharge, second.InstanceId).Accepted);

        Assert.All(player.Morale, card => Assert.False(card.Tapped));
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceCardId == "ST04-M1");
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "cost"
            && entry.Text.Contains("傲慢之罪", StringComparison.Ordinal));
        Assert.Contains(game.State.Events, entry => entry.Type == "ability-rejected"
            && entry.Text.Contains("迦具土", StringComparison.Ordinal));

        var replay = Resolve(game, surcharge, third.InstanceId);
        Assert.False(replay.Accepted);
        Assert.All(player.Morale, card => Assert.False(card.Tapped));
    }

    [Fact]
    public void DecliningKagutsuchiReleasesBothConditionalCosts()
    {
        var game = Create(9140, "ST04-M1");
        var player = game.State.Players[0];
        var target = Card("ST04-03", "pride-kagutsuchi-decline-target");
        target.OwnerIndex = 0;
        player.Field[0][0] = target;
        var morale = Morale("pride-kagutsuchi-decline-morale");
        player.Morale.Add(morale);
        var candidate = Assert.IsType<L12TriggerCandidate>(Invoke(game,
            "BuildStarterKagutsuchiCandidate", 0, target));
        game.State.PendingTriggerStackCandidates.Add(candidate);
        Invoke(game, "AdvanceTriggerBatches");

        ResolveAccepted(game, "mode:none");

        Assert.False(morale.Tapped);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "cost"
            && entry.Text.Contains("傲慢之罪", StringComparison.Ordinal));
    }
}
