using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AmakineTopCardLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "amakine-top-card", "AMAKINE-TOP-CARD",
            seed, ["甲", "乙"], [3, 3], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            foreach (var row in player.Field) Array.Clear(row);
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
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

    private static L12ActionEvent Result(L12GameEngine game, string sourceId)
        => Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == sourceId));

    [Fact]
    public void AmakineUsesOneStructuredSegmentWithEveryPublicPlacementBranch()
    {
        var ability = Catalog.AtomicEffects.Find("S02-0616")!.Abilities.Single(candidate => candidate.Sequence == 3);
        var scenes = ability.Presentations.Where(scene => scene.Flow == "amakine-top-card").ToArray();

        Assert.Equal(4, scenes.Length);
        foreach (var label in new[] { "加入手牌", "返回牌库顶部", "返回牌库底部", "等待处理已展示牌" })
            Assert.Contains(scenes, scene => scene.BranchLabel == label);
        Assert.All(scenes, scene =>
        {
            Assert.Equal(1, scene.SegmentIndex);
            Assert.Equal(1, scene.SegmentCount);
        });
    }

    [Fact]
    [Trait("L12Evidence", "ability:amakineTop")]
    public void EligibleTopCardChoiceRestoresFromCheckpointAndRejectsDuplicateSubmission()
    {
        var game = Create(91371);
        var player = game.State.Players[0];
        var source = Card("S02-0616", "amakine-checkpoint-source");
        var top = Card("S02-0619", "amakine-checkpoint-top");
        player.Field[0][0] = source;
        player.Library.Add(top);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "amakineTop")).Accepted);
        var response = Assert.Single(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        Assert.Contains($"休整〈{source.Name}〉", response.Data["responsePaidCostSummary"],
            StringComparison.Ordinal);
        Assert.Contains($"展示牌库顶部的〈{top.Name}〉", response.Data["responsePaidCostSummary"],
            StringComparison.Ordinal);
        Assert.Contains("效果：阿麦金：处理已展示的牌", response.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("效果：主动休整 展示", response.Text, StringComparison.Ordinal);
        Assert.Contains(game.State.Events, entry => entry.Type == "reveal"
            && entry.Cards.Any(card => card.InstanceId == top.InstanceId));
        PassResponses(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-amakine-top-place", prompt.Data["action"]);
        Assert.Equal(["hand", "top", "bottom"], prompt.ValidChoices);

        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        var restored = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: restored.PromptId,
            Choice: "hand")).Accepted);
        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: restored.PromptId,
            Choice: "hand")).Accepted);

        Assert.Contains(game.State.Players[0].Hand, card => card.InstanceId == top.InstanceId);
        var restoredSource = Assert.Single(game.State.Players[0].Field[0], card => card?.InstanceId == source.InstanceId);
        Assert.True(restoredSource!.Tapped);
        var result = Result(game, source.InstanceId);
        Assert.Equal("resolved", result.EffectResultStatus);
        Assert.Equal("加入手牌", result.EffectBranchLabel);
        Assert.Equal(1, result.EffectSegmentIndex);
        Assert.Equal(1, result.EffectSegmentCount);
    }

    [Fact]
    [Trait("L12Evidence", "ability:amakineTop")]
    public void IneligibleTopCardCanOnlyReturnToTopOrBottomAndPublishesTheActualBranch()
    {
        var game = Create(91372);
        var player = game.State.Players[0];
        var source = Card("S02-0616", "amakine-bottom-source");
        var top = Card("S02-0602", "amakine-bottom-top");
        var next = Card("S02-0003", "amakine-bottom-next");
        player.Field[0][0] = source;
        player.Library.AddRange([top, next]);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "amakineTop")).Accepted);
        PassResponses(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(["top", "bottom"], prompt.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: "bottom")).Accepted);

        Assert.Equal([next.InstanceId, top.InstanceId], player.Library.Select(card => card.InstanceId));
        var result = Result(game, source.InstanceId);
        Assert.Equal("resolved", result.EffectResultStatus);
        Assert.Equal("返回牌库底部", result.EffectBranchLabel);
    }

    [Fact]
    [Trait("L12Evidence", "ability:amakineTop")]
    public void RevealedCardMissingOrNoLongerOnlyOtherworldFailsWithoutMovingAnotherCard()
    {
        foreach (var changeTraits in new[] { false, true })
        {
            var game = Create(changeTraits ? 91374 : 91373);
            var player = game.State.Players[0];
            var source = Card("S02-0616", $"amakine-failed-source-{changeTraits}");
            var top = Card("S02-0619", $"amakine-failed-top-{changeTraits}");
            var untouched = Card("S02-0003", $"amakine-failed-untouched-{changeTraits}");
            player.Field[0][0] = source;
            player.Library.AddRange([top, untouched]);

            Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
                Ability: "amakineTop")).Accepted);
            PassResponses(game);
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (changeTraits) top.Traits.Add("圆桌骑士");
            else player.Library.Remove(top);

            Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                Choice: "hand")).Accepted);

            Assert.DoesNotContain(player.Hand, card => card.InstanceId == top.InstanceId);
            Assert.Contains(player.Library, card => card.InstanceId == untouched.InstanceId);
            Assert.Equal("failed", Result(game, source.InstanceId).EffectResultStatus);
        }
    }

    [Fact]
    [Trait("L12Evidence", "ability:amakineTop")]
    public void EmptyLibraryAtResolutionFailsAndKeepsThePaidActiveRest()
    {
        var game = Create(91375);
        var player = game.State.Players[0];
        var source = Card("S02-0616", "amakine-empty-source");
        player.Field[0][0] = source;
        player.Library.Add(Card("S02-0619", "amakine-empty-top"));

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "amakineTop")).Accepted);
        player.Library.Clear();
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Contains(game.State.Events, entry => entry.Type == "reveal"
            && entry.Cards.Any(card => card.InstanceId == "amakine-empty-top"));
        Assert.Equal("failed", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:amakineTop")]
    public void NegatedAmakineKeepsItsPaidRestAndTopCardReveal()
    {
        var game = Create(91376);
        var player = game.State.Players[0];
        var source = Card("S02-0616", "amakine-negated-source");
        var top = Card("S02-0619", "amakine-negated-top");
        player.Field[0][0] = source;
        player.Library.Add(top);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "amakineTop")).Accepted);
        var response = Assert.Single(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        Assert.Contains($"展示牌库顶部的〈{top.Name}〉", response.Text, StringComparison.Ordinal);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Single(player.Library);
        Assert.Empty(player.Hand);
        Assert.Contains(game.State.Events, entry => entry.Type == "reveal"
            && entry.Cards.Any(card => card.InstanceId == top.InstanceId));
        Assert.Equal("negated", Result(game, source.InstanceId).EffectResultStatus);
    }
}
