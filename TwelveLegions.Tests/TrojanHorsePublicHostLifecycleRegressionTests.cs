using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class TrojanHorsePublicHostLifecycleRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(
        Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void CoveredTrojanHorseDoesNotDebuffOrKillOwnersLegionsAndSurvivesV2Reconnect()
    {
        var game = Create(95600);
        var owner = game.State.Players[0];
        var subject = Card("S02-0005", "covered-horse-owner-subject");
        subject.OwnerIndex = owner.PlayerIndex;
        owner.Field[0][0] = subject;
        var horse = Card("S02-0523", "covered-horse-no-aura");
        owner.Hand.Clear();
        owner.Hand.Add(horse);
        AddMorale(owner, 2);

        var set = game.Handle(owner.PlayerIndex,
            new L12Command("playCard", horse.InstanceId, Row: 1, Slot: 1));

        Assert.True(set.Accepted, set.Error);
        Assert.True(horse.Hidden);
        Assert.Same(horse, owner.Field[1][1]);
        Assert.Same(subject, owner.Field[0][0]);
        Assert.Equal(1000, subject.Troops);
        Assert.DoesNotContain(subject, owner.Graveyard);
        Assert.Equal(0, owner.Morale.Count(card => !card.Tapped));

        game = RestoreV2(game);
        owner = game.State.Players[0];
        var restoredSubject = Assert.Single(owner.Field.SelectMany(row => row),
            card => card?.InstanceId == subject.InstanceId);
        var restoredHorse = Assert.Single(owner.Field.SelectMany(row => row),
            card => card?.InstanceId == horse.InstanceId);
        Assert.NotNull(restoredSubject);
        Assert.NotNull(restoredHorse);
        Assert.Equal(1000, restoredSubject.Troops);
        Assert.True(restoredHorse.Hidden);
        Assert.DoesNotContain(owner.Graveyard, card => card.InstanceId == subject.InstanceId);
    }

    [Fact]
    public void CoveredTrojanHorseDoesNotExpireAtEndOfItsSettingTurn()
    {
        var game = Create(95601);
        var owner = game.State.Players[0];
        var horse = Card("S02-0523", "covered-horse-no-expiry");
        owner.Hand.Clear();
        owner.Hand.Add(horse);
        AddMorale(owner, 2);

        var set = game.Handle(owner.PlayerIndex,
            new L12Command("playCard", horse.InstanceId, Row: 1, Slot: 1));
        Assert.True(set.Accepted, set.Error);
        Assert.True(horse.Hidden);
        Assert.True(horse.DiscardAtEndOfTurnUntilTurn < 0);
        var libraryBefore = owner.Library.Count;

        var end = game.Handle(owner.PlayerIndex, new L12Command("endTurn"));

        Assert.True(end.Accepted, end.Error);
        Assert.Same(horse, owner.Field[1][1]);
        Assert.True(horse.Hidden);
        Assert.DoesNotContain(horse, owner.Graveyard);
        Assert.Equal(libraryBefore, owner.Library.Count);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == horse.InstanceId));
    }

    [Fact]
    public void TrojanHorseCanUseEnemyBackRowDuringCorruptEarthAndOnlyDebuffsItsPublicHost()
    {
        var game = Create(95602);
        var host = game.State.Players[0];
        var owner = game.State.Players[1];
        var hostLegion = Card("S01-0002", "public-horse-host-legion");
        var ownerLegion = Card("S01-0002", "public-horse-owner-legion");
        hostLegion.OwnerIndex = host.PlayerIndex;
        ownerLegion.OwnerIndex = owner.PlayerIndex;
        hostLegion.SummonRound = ownerLegion.SummonRound = 0;
        host.Field[0][0] = hostLegion;
        owner.Field[0][0] = ownerLegion;
        var horse = Card("S02-0523", "public-horse-corrupt-earth");
        owner.Hand.Clear();
        owner.Hand.Add(horse);
        AddMorale(owner, 2);
        game.State.ActivePlayer = owner.PlayerIndex;

        var set = game.Handle(owner.PlayerIndex,
            new L12Command("playCard", horse.InstanceId, Row: 1, Slot: 0));
        Assert.True(set.Accepted, set.Error);
        Assert.True(horse.Hidden);
        Assert.Equal(0, owner.Morale.Count(card => !card.Tapped));

        var settingTurnEnd = game.Handle(owner.PlayerIndex, new L12Command("endTurn"));
        Assert.True(settingTurnEnd.Accepted, settingTurnEnd.Error);
        Assert.Same(horse, owner.Field[1][0]);
        Assert.True(horse.Hidden);
        Assert.Equal(host.PlayerIndex, game.State.ActivePlayer);

        game.State.ActiveDisaster = Card("S01-DS03", "public-horse-corrupt-earth-disaster");
        Assert.True(game.Handle(host.PlayerIndex,
            new L12Command("attack", hostLegion.InstanceId,
                Target: new L12AttackTarget("master"))).Accepted);
        PassResponses(game);
        Assert.True(game.Handle(owner.PlayerIndex,
            new L12Command("resolveDefense", CardInstanceIds: [])).Accepted);
        PassResponses(game);

        var confirm = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation");
        Assert.True(game.Handle(owner.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: confirm.PromptId, Choice: "mode:use")).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation");
        Assert.Contains("1:1", slot.ValidChoices);
        Assert.True(game.Handle(owner.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: slot.PromptId, Choice: "1:1")).Accepted);
        PassResponses(game);

        Assert.Null(owner.Field[1][0]);
        Assert.Same(horse, host.Field[1][1]);
        Assert.False(horse.Hidden);
        Assert.Equal(4000, hostLegion.Troops);
        Assert.Equal(5000, ownerLegion.Troops);
        var placementResults = game.State.Events.Count(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == horse.InstanceId)
            && entry.EffectSegmentIndex == 1 && entry.EffectSegmentCount == 1);
        Assert.Equal(1, placementResults);

        var duplicate = game.Handle(owner.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: slot.PromptId, Choice: "1:1"));
        Assert.False(duplicate.Accepted);
        Assert.Equal(1, game.State.Events.Count(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == horse.InstanceId)
            && entry.EffectSegmentIndex == 1 && entry.EffectSegmentCount == 1));

        game = RestoreV2(game);
        host = game.State.Players[0];
        owner = game.State.Players[1];
        hostLegion = Assert.Single(host.Field.SelectMany(row => row),
            card => card?.InstanceId == hostLegion.InstanceId)!;
        ownerLegion = Assert.Single(owner.Field.SelectMany(row => row),
            card => card?.InstanceId == ownerLegion.InstanceId)!;
        horse = Assert.Single(host.Field.SelectMany(row => row),
            card => card?.InstanceId == horse.InstanceId)!;
        Assert.False(horse.Hidden);
        Assert.Equal(4000, hostLegion.Troops);
        Assert.Equal(5000, ownerLegion.Troops);

        game.State.ActiveDisaster = null;
        game.State.ActivePlayer = host.PlayerIndex;
        game.State.Phase = L12Phase.Main;
        var replacement = Card("S01-0002", "public-horse-replacement");
        replacement.OwnerIndex = host.PlayerIndex;
        host.Hand.Add(replacement);
        AddMorale(host, replacement.Cost);
        foreach (var morale in host.Morale) morale.Tapped = false;
        var activeMoraleBefore = host.Morale.Count(card => !card.Tapped);
        var replace = game.Handle(host.PlayerIndex,
            new L12Command("playCard", replacement.InstanceId, Row: 1, Slot: 1));
        Assert.False(replace.Accepted);
        Assert.Same(horse, host.Field[1][1]);
        Assert.Contains(replacement, host.Hand);
        Assert.Equal(activeMoraleBefore, host.Morale.Count(card => !card.Tapped));

        var counterReplacement = Card("S01-0018", "public-horse-counter-replacement");
        counterReplacement.OwnerIndex = host.PlayerIndex;
        host.Hand.Add(counterReplacement);
        var replaceWithCounter = game.Handle(host.PlayerIndex,
            new L12Command("playCard", counterReplacement.InstanceId, Row: 1, Slot: 1));
        Assert.False(replaceWithCounter.Accepted);
        Assert.Same(horse, host.Field[1][1]);
        Assert.Contains(counterReplacement, host.Hand);
        Assert.Equal(activeMoraleBefore, host.Morale.Count(card => !card.Tapped));

        var expiryResultsBeforeHostEnd = game.State.Events.Count(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == horse.InstanceId)
            && entry.EffectSegmentCount == 2);
        var hostEnd = game.Handle(host.PlayerIndex, new L12Command("endTurn"));
        Assert.True(hostEnd.Accepted, hostEnd.Error);
        Assert.Same(horse, host.Field[1][1]);
        Assert.DoesNotContain(owner.Graveyard, card => card.InstanceId == horse.InstanceId);
        Assert.Equal(expiryResultsBeforeHostEnd, game.State.Events.Count(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == horse.InstanceId)
            && entry.EffectSegmentCount == 2));
        Assert.Equal(owner.PlayerIndex, game.State.ActivePlayer);

        var libraryBefore = owner.Library.Count;
        var end = game.Handle(owner.PlayerIndex, new L12Command("endTurn"));
        Assert.True(end.Accepted, end.Error);
        Assert.Null(host.Field[1][1]);
        Assert.Contains(owner.Graveyard, card => card.InstanceId == horse.InstanceId);
        Assert.Equal(libraryBefore - 1, owner.Library.Count);
        Assert.Equal(2, game.State.Events.Count(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == horse.InstanceId)
            && entry.EffectSegmentCount == 2));
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "trojan-public-host", "TROJAN-PUBLIC-HOST", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            disasterMode: "none");
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
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
            CannotAttack = definition.Id is "S02-0005" or "S02-0007" or "S02-0201" or "S02-0603",
        };
    }

    private static void AddMorale(L12PlayerState player, int count)
    {
        while (player.Morale.Count < count)
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

    private static L12GameEngine RestoreV2(L12GameEngine game)
    {
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        return L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
    }
}
