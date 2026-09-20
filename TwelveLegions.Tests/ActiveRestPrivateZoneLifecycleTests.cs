using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ActiveRestPrivateZoneLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed = 9101, string? firstMaster = null)
    {
        var decks = firstMaster is null
            ? new L12PresetDeckDefinition[] { Catalog.DeckAt(0), Catalog.DeckAt(0) }
            :
            [
                new L12PresetDeckDefinition
                {
                    Name = $"{firstMaster}主动休整私有区牌库",
                    MasterId = firstMaster,
                    CardIds = [.. Catalog.DeckAt(0).CardIds],
                    MoraleIds = [.. Catalog.DeckAt(0).MoraleIds],
                    SpecialIds = [],
                },
                Catalog.DeckAt(0),
            ];
        var game = new L12GameEngine(Catalog, "active-rest-private-zone", "ACTIVE-REST-PRIVATE", seed,
            ["甲", "乙"], decks, skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
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
            player.Removed.Clear();
            player.Resolving.Clear();
            player.ExtraRelics.Clear();
            player.Relic = null;
            player.Morale.Clear();
            player.UsedAbilities.Clear();
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
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
            OwnerIndex = owner,
        };
    }

    private static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            Resolve(game, "pass");
    }

    private static void AddReadyMorale(L12PlayerState player, int count, string prefix)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"{prefix}-{index}",
                CardId = "S01-01C1",
            });
    }

    private static L12ActionEvent Result(L12GameEngine game, string sourceId)
        => Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == sourceId));

    [Fact]
    [Trait("L12Evidence", "ability:scarabSummon")]
    [Trait("L12Evidence", "entry:private-object-explicit-selection-and-free-cancel")]
    public void GoldenScarabRequiresExplicitGraveChoiceEvenWhenUniqueAndCancelPaysNoRestCost()
    {
        var game = Create();
        var player = game.State.Players[0];
        var source = Card("S02-0205", "private-scarab-source");
        var target = Card("S02-0201", "private-scarab-target");
        var unrelated = Card("S02-0001", "private-scarab-unrelated");
        player.Relic = source;
        player.Graveyard.AddRange([target, unrelated]);

        var begin = game.Handle(0, new L12Command("activateAbility", source.InstanceId, Ability: "scarabSummon"));
        Assert.True(begin.Accepted, begin.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("grave-card", prompt.Kind);
        Assert.Contains(target.InstanceId, prompt.ValidChoices);
        Assert.DoesNotContain(unrelated.InstanceId, prompt.ValidChoices);
        Assert.Contains("skip", prompt.ValidChoices);
        var displayed = prompt.Data["displayCardIds"].Split('|', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(target.InstanceId, displayed);
        Assert.Contains(unrelated.InstanceId, displayed);
        Assert.False(source.Tapped);

        Resolve(game, "skip");

        Assert.False(source.Tapped);
        Assert.Contains(target, player.Graveyard);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "ability:scarabSummon")]
    [Trait("L12Evidence", "entry:private-object-frozen-no-substitution-v2")]
    public void GoldenScarabV2RestoreFreezesTheChosenCopyAndNeverSubstitutesAnotherCopy()
    {
        var game = Create(9102);
        var player = game.State.Players[0];
        var source = Card("S02-0205", "private-scarab-v2-source");
        var chosen = Card("S02-0201", "private-scarab-v2-chosen");
        var other = Card("S02-0201", "private-scarab-v2-other");
        player.Relic = source;
        player.Graveyard.AddRange([chosen, other]);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "scarabSummon")).Accepted);
        Resolve(game, chosen.InstanceId);
        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        player = game.State.Players[0];
        source = player.Relic!;
        chosen = Assert.Single(player.Graveyard, card => card.InstanceId == chosen.InstanceId);
        other = Assert.Single(player.Graveyard, card => card.InstanceId == other.InstanceId);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slotPrompt.PromptId,
            Choice: "0:1")).Accepted);
        Assert.True(source.Tapped);
        player.Graveyard.Remove(chosen);
        player.Removed.Add(chosen);
        PassResponses(game);

        Assert.Contains(chosen, player.Removed);
        Assert.Contains(other, player.Graveyard);
        Assert.DoesNotContain(player.Field.SelectMany(row => row), card => card?.InstanceId == other.InstanceId);
        Assert.True(source.Tapped);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("不改选其他同名卡", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "ability:cleopatraGuard")]
    [Trait("L12Evidence", "entry:active-rest-private-target-invalid-keeps-cost")]
    public void CleopatraInvalidGraveTargetKeepsRestAndMoraleCostsWithoutSubstitution()
    {
        var game = Create(9103);
        var player = game.State.Players[0];
        var source = Card("S01-0214", "private-cleopatra-source");
        var chosen = Card("S01-0212", "private-cleopatra-chosen");
        var other = Card("S01-0212", "private-cleopatra-other");
        player.Field[0][0] = source;
        player.Graveyard.AddRange([chosen, other]);
        AddReadyMorale(player, 1, "private-cleopatra-morale");

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "cleopatraGuard")).Accepted);
        Resolve(game, chosen.InstanceId);
        Resolve(game, "0:1");
        Assert.True(source.Tapped);
        Assert.True(Assert.Single(player.Morale).Tapped);
        player.Graveyard.Remove(chosen);
        player.Removed.Add(chosen);
        PassResponses(game);

        Assert.Contains(other, player.Graveyard);
        Assert.DoesNotContain(player.Field.SelectMany(row => row), card => card?.InstanceId == other.InstanceId);
        Assert.True(source.Tapped);
        Assert.True(Assert.Single(player.Morale).Tapped);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("主动休整与士气费用不返还", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "ability:hippolytaRevive")]
    [Trait("L12Evidence", "entry:active-rest-private-target-invalid-keeps-all-costs")]
    public void HippolytaInvalidGraveTargetKeepsRestMoraleAndDiscardCostsWithoutSubstitution()
    {
        var game = Create(9104);
        var player = game.State.Players[0];
        var source = Card("S02-0510", "private-hippolyta-source");
        var discard = Card("S02-0001", "private-hippolyta-discard");
        var chosen = Card("S02-0501", "private-hippolyta-chosen");
        var other = Card("S02-0501", "private-hippolyta-other");
        chosen.CostModifier = 4 - chosen.Cost;
        other.CostModifier = 4 - other.Cost;
        player.Field[0][0] = source;
        player.Hand.Add(discard);
        player.Graveyard.AddRange([chosen, other]);
        AddReadyMorale(player, 3, "private-hippolyta-morale");

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "hippolytaRevive")).Accepted);
        Resolve(game, discard.InstanceId);
        Resolve(game, chosen.InstanceId);
        Resolve(game, "0:1");
        Assert.True(source.Tapped);
        Assert.All(player.Morale, morale => Assert.True(morale.Tapped));
        Assert.Contains(discard, player.Graveyard);
        player.Graveyard.Remove(chosen);
        player.Removed.Add(chosen);
        PassResponses(game);

        Assert.Contains(other, player.Graveyard);
        Assert.DoesNotContain(player.Field.SelectMany(row => row), card => card?.InstanceId == other.InstanceId);
        Assert.True(source.Tapped);
        Assert.All(player.Morale, morale => Assert.True(morale.Tapped));
        Assert.Contains(discard, player.Graveyard);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("费用不返还", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "ability:palaceExchange")]
    [Trait("L12Evidence", "entry:independent-private-revive-failure-keeps-active-rest-cost")]
    public void PalaceReviveTargetInvalidationFailsOnlyThatSegmentAndKeepsAllCosts()
    {
        var game = Create(9105, "S01-01D1");
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        var chosen = Card("S01-0114", "private-palace-chosen");
        var other = Card("S01-0114", "private-palace-other");
        var victim = Card("S01-0003", "private-palace-victim", owner: 1);
        player.Graveyard.AddRange([chosen, other]);
        enemy.Field[0][0] = victim;
        AddReadyMorale(player, victim.CurrentCost, "private-palace-morale");

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "palaceExchange")).Accepted);
        Resolve(game, chosen.InstanceId);
        Resolve(game, "0:1");
        Resolve(game, victim.InstanceId);
        Assert.True(player.MasterTapped);
        Assert.Equal(victim.CurrentCost, player.ReturnedMoraleThisTurn);
        player.Graveyard.Remove(chosen);
        player.Removed.Add(chosen);
        PassResponses(game);

        Assert.Contains(victim, enemy.Graveyard);
        Assert.Contains(other, player.Graveyard);
        Assert.DoesNotContain(player.Field.SelectMany(row => row), card => card?.InstanceId == other.InstanceId);
        Assert.True(player.MasterTapped);
        Assert.Equal(victim.CurrentCost, player.ReturnedMoraleThisTurn);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == "master-0")
            && entry.EffectResultStatus == "failed");
    }

    [Fact]
    [Trait("L12Evidence", "ability:artifactSearch")]
    [Trait("L12Evidence", "entry:negated-private-library-keeps-active-rest-and-discard")]
    public void NegatedShanheSearchKeepsRestAndDiscardWithoutReadingTheLibrary()
    {
        var game = Create(9106);
        var player = game.State.Players[0];
        var source = Card("S01-0117", "private-shanhe-source");
        var discard = Card("S01-0101", "private-shanhe-discard");
        var top = Card("S01-0102", "private-shanhe-top");
        player.Relic = source;
        player.Hand.Add(discard);
        player.Library.Add(top);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "artifactSearch")).Accepted);
        Resolve(game, discard.InstanceId);
        Assert.True(source.Tapped);
        Assert.Contains(discard, player.Graveyard);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Contains(discard, player.Graveyard);
        Assert.Equal([top.InstanceId], player.Library.Select(card => card.InstanceId));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Equal("negated", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:merlinRune")]
    [Trait("L12Evidence", "entry:negated-private-library-keeps-active-rest-and-rune")]
    public void NegatedMerlinSearchKeepsRestAndRuneCostWithoutReadingTheLibrary()
    {
        var game = Create(9107);
        var player = game.State.Players[0];
        var source = Card("S02-0603", "private-merlin-source");
        var tactic = Card("S01-0005", "private-merlin-tactic");
        player.Field[0][0] = source;
        player.Library.Add(tactic);
        player.SpecialZones.Runes = 1;

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "merlinRune")).Accepted);
        Resolve(game, "mode:search");
        Assert.True(source.Tapped);
        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Equal([tactic.InstanceId], player.Library.Select(card => card.InstanceId));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Equal("negated", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:telemachusTopThree")]
    [Trait("L12Evidence", "entry:negated-private-top-keeps-active-rest")]
    public void NegatedTelemachusKeepsActiveRestWithoutReadingTheLibrary()
    {
        var game = Create(9108);
        var player = game.State.Players[0];
        var source = Card("ST05-06", "private-telemachus-source");
        var top = Card("ST05-03", "private-telemachus-top");
        player.Field[0][0] = source;
        player.Library.Add(top);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "telemachusTopThree")).Accepted);
        Resolve(game, "mode:use");
        Assert.True(source.Tapped);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Equal([top.InstanceId], player.Library.Select(card => card.InstanceId));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Equal("negated", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:lightSwordActive")]
    [Trait("L12Evidence", "entry:negated-private-hand-cost-keeps-active-rest")]
    public void NegatedLightSwordRuneModeKeepsRestAndDiscardWithoutGrantingRune()
    {
        var game = Create(9109);
        var player = game.State.Players[0];
        var source = Card("ST06-09", "private-light-sword-source");
        var discard = Card("ST01-05", "private-light-sword-discard");
        player.Relic = source;
        player.Hand.Add(discard);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "lightSwordActive")).Accepted);
        Resolve(game, "mode:rune");
        Resolve(game, discard.InstanceId);
        Assert.True(source.Tapped);
        Assert.Contains(discard, player.Graveyard);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Contains(discard, player.Graveyard);
        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Equal("negated", Result(game, source.InstanceId).EffectResultStatus);
    }
}
