using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class MasterTriggeredUsageResetTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    public static IEnumerable<object[]> TriggeredLimits()
    {
        yield return ["S01-02M3", 2, "trigger:medjedDamageResponse"];
        yield return ["S02-02M1", 3, "s2-nephthys-scarab:5"];
        yield return ["S02-04M1", 1, "active:master-0:tsukuyomiFollowMove"];
        yield return ["S02-05M1", 1, "trigger:artemis-ranged-death:5"];
        yield return ["S02-06M1", 1, "s2-morrigan-rune:5"];
        yield return ["S02-06M2", 2, "trigger:angus-trial-rune:5"];
        yield return ["S02-06M2", 3, "trigger:angus-tactic:5"];
        yield return ["ST01-M1", 1, "trigger:starter-change:5"];
        yield return ["ST04-M1", 1, "trigger:starter-kagutsuchi:5"];
    }

    private static L12GameEngine Create(string masterId)
    {
        var basis = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition { Name = "主宰触发次数重置", MasterId = masterId,
            CardIds = [.. basis.CardIds], MoraleIds = [.. basis.MoraleIds], SpecialIds = [] };
        var game = new L12GameEngine(Catalog, "master-trigger-reset", "RESET", 91761, ["甲", "乙"], [deck, basis],
            skipPreparation: true, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0; game.State.Round = 3; game.State.TurnSerial = 5; game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.Hand.Clear(); player.Morale.Clear(); player.UsedAbilities.Clear();
        }
        game.State.Players[0].Morale.Add(new() { InstanceId = "return-cost", CardId = "S01-03C1" });
        game.State.Players[0].UsedAbilities.Add("trigger:factionZeroRecovery");
        game.State.Players[0].Relic = new() { InstanceId = "shennong", CardId = "S02-0104", Name = "神农鼎",
            CardType = "artifact", Faction = "tianting", OwnerIndex = 0 };
        return game;
    }

    [Theory]
    [MemberData(nameof(TriggeredLimits))]
    public void EveryLimitedMasterTriggerCanBeChosenAndResetAfterRecovery(string masterId, int sequence, string usedKey)
    {
        var game = Create(masterId);
        game.State.Players[0].UsedAbilities.Add(usedKey);
        if (masterId == "S02-06M1")
            Assert.Null(typeof(L12GameEngine).GetMethod("BuildMorriganEnemyDeathCandidate",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(game, [1]));
        // Seed a completed use, not a pending reservation: this tests the reset lifecycle.
        var begin = game.Handle(0, new L12Command("activateAbility", "shennong", Ability: "shennongReset"));
        Assert.True(begin.Accepted, begin.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        var choice = Assert.Single(prompt.ValidChoices, candidate => candidate != "skip");
        var text = Catalog.AtomicEffects.Find(masterId)!.Abilities.Single(ability => ability.Sequence == sequence).Text;
        Assert.Contains("回合1次", text.Replace(" ", ""));
        Assert.Equal(text, prompt.Data[choice]);
        game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        var command = new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice);
        Assert.True(game.Handle(0, command).Accepted);
        Assert.False(game.Handle(0, command).Accepted);
        Assert.Equal(5, game.State.TurnSerial);
        Assert.Equal(0, game.State.Players[0].PlayerIndex);
        var item = Assert.Single(game.State.EffectStack);
        Assert.Equal("shennongReset", item.Data.GetValueOrDefault("ability"));
        Assert.Equal(choice, item.Data.GetValueOrDefault("target"));
        PassResponses(game);
        Assert.True(!game.State.Players[0].UsedAbilities.Contains(usedKey),
            string.Join(" | ", game.State.Events.Select(entry => entry.Text)));
        Assert.True(game.State.Players[0].Relic!.Tapped);
        Assert.Empty(game.State.Players[0].Morale);
        if (masterId == "S02-06M1")
            Assert.IsType<L12TriggerCandidate>(typeof(L12GameEngine).GetMethod("BuildMorriganEnemyDeathCandidate",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(game, [1]));
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var guard = 0; guard < 20 && game.State.PendingPrompts.FirstOrDefault() is { } response; guard++)
        {
            Assert.Equal("response", response.Kind);
            Assert.True(game.Handle(response.PlayerIndex, new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);
        }
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }

    public static IEnumerable<object[]> ExceptionalPaths()
        => TriggeredLimits().SelectMany(row => new[] { "declined", "stale-before-payment", "stale-at-resolution", "negated" }
            .Select(outcome => new object[] { row[0], row[2], outcome }));

    [Theory]
    [MemberData(nameof(ExceptionalPaths))]
    public void ResetExceptionsDoNotConsumeOrRestoreTheWrongState(string masterId, string usedKey, string outcome)
    {
        var game = Create(masterId);
        var player = game.State.Players[0];
        player.UsedAbilities.UnionWith([usedKey, "unrelated-duration", usedKey + ":pending"]);
        Assert.True(game.Handle(0, new L12Command("activateAbility", "shennong", Ability: "shennongReset")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        var choice = Assert.Single(prompt.ValidChoices, candidate => candidate != "skip");
        if (outcome == "stale-before-payment") player.UsedAbilities.Remove(usedKey);
        var command = new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: outcome == "declined" ? "skip" : choice);
        Assert.True(game.Handle(0, command).Accepted);
        Assert.False(game.Handle(0, command).Accepted);
        if (outcome is "declined" or "stale-before-payment")
        {
            Assert.False(player.Relic!.Tapped);
            Assert.Single(player.Morale);
            Assert.Empty(game.State.EffectStack);
            Assert.Empty(game.State.PendingPrompts);
        }
        else
        {
            var item = Assert.Single(game.State.EffectStack);
            item.Negated = outcome == "negated";
            if (outcome == "stale-at-resolution") player.UsedAbilities.Remove(usedKey);
            game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
                game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
            player = game.State.Players[0];
            PassResponses(game);
            Assert.True(player.Relic!.Tapped);
            Assert.Empty(player.Morale);
            Assert.Equal(outcome == "negated" ? "negated" : "failed",
                Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
                    && entry.Cards.Any(card => card.InstanceId == "shennong")).EffectResultStatus);
        }
        Assert.Equal(outcome is "declined" or "negated", player.UsedAbilities.Contains(usedKey));
        Assert.Contains("unrelated-duration", player.UsedAbilities);
        Assert.Contains(usedKey + ":pending", player.UsedAbilities); // Reset does not steal reservations.
    }

    [Theory]
    [MemberData(nameof(TriggeredLimits))]
    public void PendingReservationAloneIsNotAUsedEffectAndPersistedKeysStayCompatible(string masterId, int sequence, string usedKey)
    {
        var rule = Assert.Single(L12MasterTriggeredUsageRules.All,
            rule => rule.CardId == masterId && rule.AbilitySequence == sequence);
        Assert.Equal(usedKey, rule.Key(0, 5));
        Assert.Null(L12MasterTriggeredUsageRules.Find("UNREGISTERED", rule.Ability));
        var game = Create(masterId);
        game.State.Players[0].UsedAbilities.Add(usedKey + ":pending");
        var before = game.SerializeFullState();
        Assert.False(game.Handle(0, new L12Command("activateAbility", "shennong", Ability: "shennongReset")).Accepted);
        Assert.Equal(before, game.SerializeFullState());
    }

    [Fact]
    public void TriggeredRegistryMatchesTheReviewedSegmentAndKeyInventory()
    {
        var expected = TriggeredLimits().Select(row => $"{row[0]}|{row[1]}|{row[2]}").Order();
        var actual = L12MasterTriggeredUsageRules.All.Select(rule => $"{rule.CardId}|{rule.AbilitySequence}|{rule.Key(0, 5)}").Order();
        Assert.Equal(expected, actual);
        Assert.Equal(9, L12MasterTriggeredUsageRules.All.Select(rule => rule.Ability).Distinct().Count());
    }

    [Fact]
    public void AngusTwoTriggersRemainDistinctChoicesAndOnlyTheSelectedUseIsReset()
    {
        var game = Create("S02-06M2");
        var player = game.State.Players[0];
        player.UsedAbilities.UnionWith(["trigger:angus-tactic:5", "trigger:angus-trial-rune:5"]);
        Assert.True(game.Handle(0, new L12Command("activateAbility", "shennong", Ability: "shennongReset")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(2, prompt.ValidChoices.Count(choice => choice != "skip"));
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "angusTacticTrial")).Accepted);
        PassResponses(game);
        Assert.DoesNotContain("trigger:angus-tactic:5", player.UsedAbilities);
        Assert.Contains("trigger:angus-trial-rune:5", player.UsedAbilities);
    }

    [Fact]
    public void BadReturnSelectionCanRecoverAndCancelWithoutResettingOrPaying()
    {
        var game = Create("S02-06M1");
        game.State.Players[0].UsedAbilities.Add("s2-morrigan-rune:5");
        game.State.Players[0].Morale.Add(new() { InstanceId = "different-resource", CardId = "S02-05C1", IsGodPower = true });
        Assert.True(game.Handle(0, new L12Command("activateAbility", "shennong", Ability: "shennongReset")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "morriganEnemyDeathRune")).Accepted);
        var payment = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("active-return-choice", payment.Continuation);
        var before = game.SerializeFullState();
        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: payment.PromptId, Choice: "wrong-resource")).Accepted);
        Assert.Equal(before, game.SerializeFullState());
        game = L12GameEngine.RestoreCheckpoint(Catalog, before, game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        var cancel = new L12Command("resolvePrompt", PromptId: payment.PromptId, Choice: "cancel");
        Assert.True(game.Handle(0, cancel).Accepted);
        Assert.False(game.Handle(0, cancel).Accepted);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.False(game.State.Players[0].Relic!.Tapped);
        Assert.Equal(2, game.State.Players[0].Morale.Count);
        Assert.Contains("s2-morrigan-rune:5", game.State.Players[0].UsedAbilities);
    }
}
