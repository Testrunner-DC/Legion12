using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class Bq20260830RegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
        => new(Catalog, "bq-20260830-01", "BQ083001", seed, ["甲", "乙"], [0, 0], skipPreparation: true);

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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            CannotAttack = definition.Id is "S02-0005" or "S02-0007" or "S02-0201" or "S02-0603",
        };
    }

    private static void AddReadyMorale(L12PlayerState player, int count)
    {
        while (player.Morale.Count(card => !card.Tapped) < count)
        {
            var morale = player.MoraleDeck[0];
            player.MoraleDeck.RemoveAt(0);
            morale.Tapped = false;
            player.Morale.Add(morale);
        }
    }

    private static void PassResponses(L12GameEngine game)
    {
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var prompt = game.State.PendingPrompts[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
    }

    private static void CompleteYangJianCycle(L12GameEngine game, params string[] preservedCards)
    {
        var cardPrompt = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "yangjian-return-card");
        var returnCard = cardPrompt.ValidChoices.First(choice => !preservedCards.Contains(choice));
        Assert.True(game.Handle(cardPrompt.PlayerIndex, new L12Command("resolvePrompt",
            PromptId: cardPrompt.PromptId, Choice: returnCard)).Accepted);
        var placePrompt = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "yangjian-return-place");
        Assert.True(game.Handle(placePrompt.PlayerIndex, new L12Command("resolvePrompt",
            PromptId: placePrompt.PromptId, Choice: "bottom")).Accepted);
    }

    private static L12Prompt DiscardFaithZealotWithHeracles(
        L12GameEngine game,
        L12CardInstance heracles,
        L12CardInstance zealot,
        int slot)
    {
        var player = game.State.Players[0];
        Assert.Contains(heracles, player.Hand);
        Assert.Contains(zealot, player.Hand);
        Assert.True(game.Handle(0, new L12Command("playCard", heracles.InstanceId, Row: 0, Slot: slot)).Accepted);
        var optional = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: optional.PromptId,
            Choice: "mode:use")).Accepted);
        PassResponses(game);
        var discard = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-olympus-draw-discard");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: discard.PromptId,
            Choice: zealot.InstanceId)).Accepted);
        PassResponses(game);
        return Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-faith-zealot");
    }

    [Fact]
    public void RamsesExcludesEveryNamesakeAndDelegatesItsSummonTurnCounterProtection()
    {
        var game = Create(68001);
        var owner = game.State.Players[0];
        var opponent = game.State.Players[1];
        var enteringRamses = Card("S01-0202", "ramses-entering");
        var otherRamses = Card("S01-0202", "ramses-other-copy");
        var delegatedLegion = Card("S01-0201", "ramses-delegated-thutmose");
        var victim = Card("S01-0002", "ramses-delegated-victim");
        var absoluteDefense = Card("S01-0016", "ramses-absolute-defense");
        var pitfall = Card("S01-0018", "ramses-pitfall");
        var discard = Card("S01-0001", "ramses-defense-discard");
        owner.Hand.Clear();
        owner.Hand.Add(enteringRamses);
        owner.Field[0][1] = otherRamses;
        owner.Field[1][1] = delegatedLegion;
        opponent.Hand.Clear();
        opponent.Hand.Add(discard);
        opponent.Field[0][0] = victim;
        absoluteDefense.Hidden = true;
        absoluteDefense.SetRound = 0;
        opponent.Field[1][0] = absoluteDefense;
        pitfall.Hidden = true;
        pitfall.SetRound = 0;
        opponent.Field[1][1] = pitfall;
        AddReadyMorale(owner, enteringRamses.Cost);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        var played = game.Handle(0, new L12Command("playCard", enteringRamses.InstanceId, Row: 0, Slot: 0));

        Assert.True(played.Accepted, played.Error);
        var ramsesPrompt = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation");
        Assert.Contains(delegatedLegion.InstanceId, ramsesPrompt.ValidChoices);
        Assert.DoesNotContain(enteringRamses.InstanceId, ramsesPrompt.ValidChoices);
        Assert.DoesNotContain(otherRamses.InstanceId, ramsesPrompt.ValidChoices);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: ramsesPrompt.PromptId,
            Choice: delegatedLegion.InstanceId)).Accepted);
        PassResponses(game);

        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response"
            && (prompt.ValidChoices.Contains(absoluteDefense.InstanceId)
                || prompt.ValidChoices.Contains(pitfall.InstanceId)));
        var delegatedPrompt = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation");
        Assert.Contains(victim.InstanceId, delegatedPrompt.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: delegatedPrompt.PromptId,
            Choice: victim.InstanceId)).Accepted);
        Assert.Contains(victim, opponent.Graveyard);
        Assert.Same(absoluteDefense, opponent.Field[1][0]);
        Assert.Same(pitfall, opponent.Field[1][1]);
    }

    [Fact]
    public void FaithZealotFreeMasterActivationStillWorksAfterTheNormalUse()
    {
        var game = Create(68002);
        var player = game.State.Players[0];
        var heracles = Card("S02-0502", "normal-then-free-heracles");
        var zealot = Card("S02-0006", "normal-then-free-zealot");
        player.Hand.Clear();
        player.Hand.AddRange([heracles, zealot]);
        AddReadyMorale(player, heracles.Cost + 1);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "drawCycle")).Accepted);
        Assert.Contains("active:master-0:drawCycle", player.UsedAbilities);
        CompleteYangJianCycle(game, heracles.InstanceId, zealot.InstanceId);

        var faith = DiscardFaithZealotWithHeracles(game, heracles, zealot, 0);
        var moraleBeforeFree = player.Morale.Select(card => (card.InstanceId, card.Tapped)).ToArray();
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: faith.PromptId,
            Choice: "drawCycle")).Accepted);
        PassResponses(game);

        Assert.Contains("active:master-0:drawCycle", player.UsedAbilities);
        Assert.Equal(moraleBeforeFree, player.Morale.Select(card => (card.InstanceId, card.Tapped)).ToArray());
        Assert.Null(game.State.FreeMasterActivation);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("信仰狂热者", StringComparison.Ordinal));
        Assert.Contains(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "yangjian-return-card");
    }

    [Fact]
    public void FaithZealotFreeMasterActivationDoesNotConsumeTheNormalUse()
    {
        var game = Create(68003);
        var player = game.State.Players[0];
        var heracles = Card("S02-0502", "free-then-normal-heracles");
        var zealot = Card("S02-0006", "free-then-normal-zealot");
        player.Hand.Clear();
        player.Hand.AddRange([heracles, zealot]);
        AddReadyMorale(player, heracles.Cost + 1);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var faith = DiscardFaithZealotWithHeracles(game, heracles, zealot, 0);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: faith.PromptId,
            Choice: "drawCycle")).Accepted);
        PassResponses(game);
        Assert.DoesNotContain("active:master-0:drawCycle", player.UsedAbilities);
        CompleteYangJianCycle(game);

        var moraleBeforeNormal = player.Morale.Count(card => !card.Tapped);
        var normal = game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "drawCycle"));

        Assert.True(normal.Accepted, normal.Error);
        Assert.Contains("active:master-0:drawCycle", player.UsedAbilities);
        Assert.Equal(moraleBeforeNormal - 1, player.Morale.Count(card => !card.Tapped));
        Assert.Contains(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "yangjian-return-card");
    }

    [Fact]
    public void ExistingKingsSwordWarnsBeforeTheSecondArthurPaysForANoop()
    {
        var game = Create(68004);
        var player = game.State.Players[0];
        var firstArthur = Card("S02-0601", "warning-first-arthur");
        var sword = Card("S02-06S2", "warning-existing-sword");
        firstArthur.AttachedCards.Add(sword);
        player.Field[0][0] = firstArthur;
        var secondArthur = Card("S02-0601", "warning-second-arthur");
        player.Hand.Add(secondArthur);
        AddReadyMorale(player, secondArthur.Cost);
        player.SpecialZones.Runes = 1;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", secondArthur.InstanceId, Row: 0, Slot: 1)).Accepted);
        var warning = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation");
        Assert.Equal("场上已存在〈王者之剑〉。若继续发动，仍会消耗1符文，但不会叠放、生成或转移〈王者之剑〉。", warning.Text);
        Assert.Contains("mode:use", warning.ValidChoices);
        Assert.Contains("mode:none", warning.ValidChoices);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: warning.PromptId,
            Choice: "mode:use")).Accepted);
        var rune = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: rune.PromptId,
            Choice: "rune-count:1")).Accepted);
        PassResponses(game);

        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Same(sword, Assert.Single(firstArthur.AttachedCards));
        Assert.Empty(secondArthur.AttachedCards);
        Assert.Single(player.Field.SelectMany(row => row).Where(card => card is not null)
            .SelectMany(card => card!.AttachedCards), card => card.CardId == "S02-06S2");
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-noop"
            && entry.Text.Contains("Limit 1", StringComparison.Ordinal));
    }

    [Fact]
    public void ExistingKingsSwordWarningCanCancelWithoutPaymentOrResult()
    {
        var game = Create(68005);
        var player = game.State.Players[0];
        var firstArthur = Card("S02-0601", "cancel-first-arthur");
        var sword = Card("S02-06S2", "cancel-existing-sword");
        firstArthur.AttachedCards.Add(sword);
        player.Field[0][0] = firstArthur;
        var secondArthur = Card("S02-0601", "cancel-second-arthur");
        player.Hand.Add(secondArthur);
        AddReadyMorale(player, secondArthur.Cost);
        player.SpecialZones.Runes = 1;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", secondArthur.InstanceId, Row: 0, Slot: 1)).Accepted);
        var warning = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation");
        var resultEventsBefore = game.State.Events.Count(entry => entry.Type is "attach" or "effect-noop");

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: warning.PromptId,
            Choice: "mode:none")).Accepted);

        Assert.Equal(1, player.SpecialZones.Runes);
        Assert.Same(sword, Assert.Single(firstArthur.AttachedCards));
        Assert.Empty(secondArthur.AttachedCards);
        Assert.Equal(resultEventsBefore,
            game.State.Events.Count(entry => entry.Type is "attach" or "effect-noop"));
    }
}
