using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

/// <summary>
/// 登场效果预先声明的“我方军团”必须在响应后仍是同一张位于我方战场的军团，
/// 并继续满足卡牌、阵营、位置与状态条件。失效对象不得按实例ID继续获得效果。
/// </summary>
public sealed class PublicEnterOwnTargetRevalidationTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "entry-own-target-revalidation", "ENTRY-OWN-TARGET", seed,
            ["甲", "乙"], [0, 1], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Phase = L12Phase.Main;
        game.State.Round = 2;
        game.State.TurnSerial = 7;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, string? cardType = null,
        string? faction = null)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = cardType ?? definition.CardType,
            Faction = faction ?? definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 1000,
            Troops = definition.Troops ?? 1000,
            SummonRound = -1,
        };
    }

    private static void PlaceSource(L12GameEngine game, L12CardInstance source)
    {
        if (source.CardType == "legion") game.State.Players[0].Field[0][0] = source;
        else game.State.Players[0].Relic = source;
    }

    private static void QueueEnter(L12GameEngine game, L12CardInstance source)
        => typeof(L12GameEngine).GetMethod("QueueOrPushTriggeredEffect", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(game, [0, source, "enter", "公开登场我方目标复验", null, new Dictionary<string, string>()]);

    private static string Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt.PromptId;
    }

    private static string Resolve(L12GameEngine game, params string[] choices)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: [.. choices]));
        Assert.True(result.Accepted, result.Error);
        return prompt.PromptId;
    }

    private static void PassResponses(L12GameEngine game, int maximum = 16)
    {
        var passed = 0;
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response" && passed++ < maximum)
        {
            var prompt = game.State.PendingPrompts[0];
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
        Assert.True(passed < maximum, "响应窗口未在限定次数内结束");
    }

    private static L12GameEngine RestoreAsV2(L12GameEngine game)
    {
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        return L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
    }

    [Theory]
    [InlineData("S01-0215", "S01-0212", 9201)] // 安卡神碑：陵墓守卫
    [InlineData("S01-0217", "S01-0201", 9202)] // 卡诺匹斯罐一：太阳城军团
    [InlineData("S01-0411", "S01-0001", 9203)] // 安倍晴明：我方军团
    [Trait("L12Evidence", "entry:public-own-legion-current-type")]
    public void DeclaredOwnLegionThatChangesToArtifactDuringResponsesReceivesNoEffect(
        string sourceId, string targetId, int seed)
    {
        var game = Create(seed);
        var source = Card(sourceId, $"own-entry-source-{sourceId}");
        var target = Card(targetId, $"own-entry-target-{sourceId}");
        PlaceSource(game, source);
        game.State.Players[0].Field[0][1] = target;

        QueueEnter(game, source);
        Resolve(game, target.InstanceId);
        Assert.Single(game.State.EffectStack);

        var transformed = Card(targetId, target.InstanceId, cardType: "artifact");
        game.State.Players[0].Field[0][1] = transformed;
        PassResponses(game);

        Assert.Empty(transformed.TimedModifiers);
        Assert.False(transformed.HasStrongAttack);
        Assert.Equal(0, transformed.ImmortalUses);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("不再符合条件", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:public-own-legion-current-faction-cost-paid")]
    public void MoziKeepsThePaidMoraleButDoesNotProtectATargetThatLosesTianting()
    {
        var game = Create(9204);
        var player = game.State.Players[0];
        var source = Card("S01-0110", "mozi-own-entry-source");
        var target = Card("S01-0101", "mozi-own-entry-target");
        PlaceSource(game, source);
        player.Field[0][1] = target;
        player.Morale.Add(new L12MoraleCard { InstanceId = "mozi-own-entry-morale", CardId = "S01-01C1" });

        QueueEnter(game, source);
        Resolve(game, "mode:use");
        Resolve(game, "mozi-own-entry-morale");
        Resolve(game, target.InstanceId);
        var transformed = Card(target.CardId, target.InstanceId, faction: "universal");
        player.Field[0][1] = transformed;
        PassResponses(game);

        Assert.Empty(player.Morale);
        Assert.Equal(0, transformed.ImmortalUses);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("天廷", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:public-own-legion-multi-target-partial")]
    public void CanopicFourKeepsTheValidTargetWhenAnotherDeclaredTargetStopsBeingALegion()
    {
        var game = Create(9205);
        var player = game.State.Players[0];
        var source = Card("S01-0220", "canopic-four-own-entry-source");
        var invalid = Card("S01-0201", "canopic-four-own-entry-invalid");
        var valid = Card("S01-0202", "canopic-four-own-entry-valid");
        PlaceSource(game, source);
        player.Field[0][0] = invalid;
        player.Field[0][1] = valid;

        QueueEnter(game, source);
        Resolve(game, invalid.InstanceId, valid.InstanceId);
        var transformed = Card(invalid.CardId, invalid.InstanceId, cardType: "artifact");
        player.Field[0][0] = transformed;
        PassResponses(game);

        Assert.Equal(0, transformed.ImmortalUses);
        Assert.Equal(1, valid.ImmortalUses);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("1个已声明对象", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:public-own-legion-delegated-trigger-current-type")]
    public void RamsesDoesNotReplayTheEnterEffectOfATargetThatIsNoLongerALegion()
    {
        var game = Create(9206);
        var player = game.State.Players[0];
        var source = Card("S01-0202", "ramses-own-entry-source");
        var target = Card("S01-0201", "ramses-own-entry-target");
        PlaceSource(game, source);
        player.Field[0][1] = target;
        game.State.Players[1].Field[0][0] = Card("S01-0001", "ramses-own-entry-enemy");

        QueueEnter(game, source);
        Resolve(game, target.InstanceId);
        player.Field[0][1] = Card(target.CardId, target.InstanceId, cardType: "artifact");
        PassResponses(game);

        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("不再符合条件", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:public-own-legion-current-threshold")]
    public void InahimeRecordsFailureWhenTheDeclaredFrontTargetExceedsTheTroopsThreshold()
    {
        var game = Create(9209);
        var player = game.State.Players[0];
        var source = Card("S01-0416", "inahime-own-entry-source");
        var target = Card("S01-0401", "inahime-own-entry-target");
        target.Troops = 5000;
        PlaceSource(game, source);
        player.Field[0][1] = target;

        QueueEnter(game, source);
        Resolve(game, target.InstanceId);
        target.Troops = 6000;
        PassResponses(game);

        Assert.Empty(target.TimedModifiers);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("不再符合条件", StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("稻姬本多小松", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:public-own-legion-ready-current-faction-cost-paid")]
    public void IiNaotoraKeepsTheDiscardedCostButDoesNotReadyATargetThatLosesGaotianyuan()
    {
        var game = Create(9207);
        var player = game.State.Players[0];
        var source = Card("S02-0402", "ii-own-entry-source");
        var target = Card("S02-0403", "ii-own-entry-target");
        var discard = Card("S01-0001", "ii-own-entry-discard");
        target.Tapped = true;
        PlaceSource(game, source);
        player.Field[0][1] = target;
        player.Hand.Add(discard);

        QueueEnter(game, source);
        Resolve(game, "mode:use");
        Resolve(game, discard.InstanceId);
        Resolve(game, target.InstanceId);
        var transformed = Card(target.CardId, target.InstanceId, faction: "universal");
        transformed.Tapped = true;
        player.Field[0][1] = transformed;
        PassResponses(game);

        Assert.Contains(discard, player.Graveyard);
        Assert.True(transformed.Tapped);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("高天原", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:public-own-legion-v2-recovery")]
    public void RestoredAbeResponseStillRejectsADeclaredTargetThatStopsBeingALegionAndRejectsRepeat()
    {
        var game = Create(9208);
        var source = Card("S01-0411", "abe-own-entry-restore-source");
        var target = Card("S01-0001", "abe-own-entry-restore-target");
        PlaceSource(game, source);
        game.State.Players[0].Field[0][1] = target;

        QueueEnter(game, source);
        Resolve(game, target.InstanceId);
        var responseId = Assert.Single(game.State.PendingPrompts).PromptId;
        game = RestoreAsV2(game);
        var transformed = Card(target.CardId, target.InstanceId, cardType: "artifact");
        game.State.Players[0].Field[0][1] = transformed;
        PassResponses(game);

        Assert.Equal(0, transformed.ImmortalUses);
        Assert.False(game.Handle(0,
            new L12Command("resolvePrompt", PromptId: responseId, Choice: "pass")).Accepted);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("不再符合条件", StringComparison.Ordinal));
    }
}
