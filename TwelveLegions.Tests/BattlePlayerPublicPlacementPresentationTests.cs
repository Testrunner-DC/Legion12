using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class BattlePlayerPublicPlacementPresentationTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Game()
    {
        var game = new L12GameEngine(Catalog, "placement-log", "PLACEMENT", 93111,
            ["甲", "乙"], [0, 0], skipPreparation: true, stateFormatVersion: 2);
        game.State.Phase = L12Phase.Main;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        foreach (var player in game.State.Players)
        foreach (var row in player.Field) Array.Clear(row);
        return game;
    }

    private static L12CardInstance Card(string id, bool hidden = false,
        string name = "无名的渗透者") => new()
    {
        InstanceId = id, CardId = "S01-0004", Name = name, CardType = "legion",
        Faction = "universal", Cost = 1, BaseTroops = 3000, Troops = 3000,
        SummonRound = -1, Hidden = hidden, OwnerIndex = 0, Tapped = true,
    };

    private static L12CardInstance CatalogCard(string cardId, string instanceId)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = definition.Id, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction,
            Cost = definition.Cost ?? 0, EffectText = definition.Effect,
            Traits = [.. definition.Traits], Profession = definition.Profession,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
        };
    }

    private static void PassResponses(L12GameEngine game)
    {
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var prompt = game.State.PendingPrompts[0];
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
    }

    [Fact]
    public void CrossPlayerPlayFreezesPublicDestinationAndControlForAllRecipientsAndCheckpoint()
    {
        var game = Game();
        var card = Card("cross-player-card");
        game.State.Players[0].Hand.Clear();
        game.State.Players[0].Hand.Add(card);
        game.State.Players[0].Morale.Add(new L12MoraleCard
        {
            CardId = "S01-02C1", InstanceId = "placement-morale",
        });
        var result = game.Handle(0, new L12Command("playCard", card.InstanceId,
            Row: 0, Slot: 1, TargetPlayerIndex: 1));
        Assert.True(result.Accepted, result.Error);
        var original = Assert.Single(game.State.Events, item => item.Type == "put"
            && item.Cards.Any(candidate => candidate.InstanceId == card.InstanceId));
        var expected = new L12PlayerPublicPlacement(card.InstanceId, 0, 1, 0, 1, false);
        Assert.Equal(expected, original.PlayerPublicPlacement);
        foreach (var snapshot in new[] { game.SnapshotFor(0), game.SnapshotFor(1), game.SnapshotForSpectator() })
            Assert.Equal(expected, Assert.Single(snapshot.RecentEvents,
                item => item.Sequence == original.Sequence).PlayerPublicPlacement);
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence);
        Assert.Equal(expected, Assert.Single(restored.SnapshotFor(0).RecentEvents,
            item => item.Sequence == original.Sequence).PlayerPublicPlacement);
    }

    [Fact]
    public void ProjectionRejectsHiddenUnlinkedContradictoryOrNonPlacementFacts()
    {
        var game = Game();
        var visible = Card("visible");
        var valid = new L12PlayerPublicPlacement("visible", 0, 1, 1, 2, true,
            "until-owner-next-turn-end");
        var raw = new L12ActionEvent(1, "put", 0, "不可从文本推断位置", [visible])
        {
            PlayerPublicPlacement = valid,
        };
        Assert.Equal(valid, L12RecipientVisibility.ProjectActionEvent(game.State, raw, 1, false)
            .PlayerPublicPlacement);
        foreach (var invalid in new[]
        {
            raw with { Type = "move" },
            raw with { Cards = [Card("visible", hidden: true)] },
            raw with { Cards = [visible, Card("visible", hidden: true)] },
            raw with { Cards = [Card("other")] },
            raw with { PlayerPublicPlacement = valid with { OwnerPlayerIndex = 1 } },
            raw with { PlayerPublicPlacement = valid with { ControllerPlayerIndex = 0 } },
            raw with { PlayerPublicPlacement = valid with { Slot = 3 } },
            raw with { PlayerPublicPlacement = valid with { Tapped = false } },
            raw with { PlayerPublicPlacement = valid with { DurationCode = "private" } },
        })
            Assert.Null(L12RecipientVisibility.ProjectActionEvent(game.State, invalid, 1, false)
                .PlayerPublicPlacement);
    }

    [Fact]
    public void TrojanHorseRevealsOnlyDestinationAndOwnerRelativeDuration()
    {
        var game = Game();
        var attacker = CatalogCard("S02-0004", "attacker");
        var horse = CatalogCard("S02-0523", "trojan-horse");
        attacker.SummonRound = 0;
        game.State.Players[0].Field[0][0] = attacker;
        horse.Hidden = true;
        horse.OwnerIndex = 1;
        horse.SetRound = 1;
        game.State.Players[1].Field[1][0] = horse;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;

        var attack = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(attack.Accepted, attack.Error);
        PassResponses(game);
        var defense = game.Handle(1, new L12Command("resolveDefense", CardInstanceIds: []));
        Assert.True(defense.Accepted, defense.Error);
        PassResponses(game);
        var confirm = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation");
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: confirm.PromptId,
            Choice: "mode:use")).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation");
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: slot.PromptId,
            Choice: "1:1")).Accepted);
        PassResponses(game);

        var placed = Assert.Single(game.State.Events, item => item.Type == "put"
            && item.Cards.Any(card => card.InstanceId == horse.InstanceId));
        var expected = new L12PlayerPublicPlacement(horse.InstanceId, 1, 0, 1, 1,
            horse.Tapped, "until-owner-next-turn-end");
        Assert.Equal(expected, placed.PlayerPublicPlacement);
        Assert.False(Assert.Single(placed.Cards).Hidden);
        Assert.DoesNotContain("1:0", JsonSerializer.Serialize(placed.PlayerPublicPlacement));
        foreach (var snapshot in new[] { game.SnapshotFor(0), game.SnapshotFor(1), game.SnapshotForSpectator() })
            Assert.Equal(expected, Assert.Single(snapshot.RecentEvents,
                item => item.Sequence == placed.Sequence).PlayerPublicPlacement);
    }

    [Fact]
    public void LegacyPlacementAndTombCountEventsRemainHonestAfterJsonRoundTrip()
    {
        var legacy = new L12ActionEvent(1, "put", 0, "旧记录", [Card("old")]);
        var restored = JsonSerializer.Deserialize<L12ActionEvent>(JsonSerializer.Serialize(legacy));
        Assert.NotNull(restored);
        Assert.Null(restored.PlayerPublicPlacement);

        var game = Game();
        var tomb = Card("tomb-departure", name: "陵墓军团");
        game.State.Players[0].Field[0][0] = tomb;
        var remove = game.GetType().GetMethod("RemoveFromField",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True((bool)remove.Invoke(game, [game.State.Players[0], tomb, true,
            "公开离场", false, L12FieldLeaveKind.Discard, true, false])!);
        var count = Assert.Single(game.State.Events, item => item.Type == "continuous");
        Assert.Equal(tomb.InstanceId, count.PlayerLogSemantic?.SourceInstanceId);
        Assert.Contains("累计1张", count.PlayerLogSemantic?.OutcomeLabel);
        Assert.Equal(count.PlayerLogSemantic,
            Assert.Single(game.SnapshotFor(1).RecentEvents, item => item.Sequence == count.Sequence)
                .PlayerLogSemantic);
    }
}
