using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

// 「静默失败可见性」：效果失败必须在玩家侧有原因，而对局事实本身不能变。
// 用例 A 钉住 BUG-20261118-f8bd01fb 的选择层（阿伊是否可选自己）；
// 用例 B 钉住共享记录器 RecordTargetSettlementFailure 的原因契约。
public sealed class SilentEffectFailureReasonTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "silent-failure-reason", "SILENT-FAILURE", seed,
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

    private static L12CardInstance Card(string cardId, string instanceId, string? cardType = null, int? troops = null)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = cardType ?? definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = troops ?? definition.Troops ?? 1000,
            Troops = troops ?? definition.Troops ?? 1000,
            SummonRound = -1,
        };
    }

    private static L12MoraleCard Morale(string instanceId)
        => new() { CardId = "S01-01C1", InstanceId = instanceId };

    private static void AttackMaster(L12GameEngine game, L12CardInstance attacker)
    {
        game.State.Players[0].Field[0][0] = attacker;
        var result = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(result.Accepted, result.Error);
    }

    private static void Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
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

    [Fact]
    [Trait("L12Evidence", "card:S01-0208")]
    [Trait("L12Evidence", "bug:BUG-20261118-f8bd01fb")]
    [Trait("L12Evidence", "entry:ayi-attack-may-target-herself")]
    public void AyFrontlineAttackMayDeclareHerselfAndKeepsHerOwnTroopsModifier()
    {
        var game = Create(9311);
        var player = game.State.Players[0];
        var source = Card("S01-0208", "ayi-self-source");
        var morale = Morale("ayi-self-morale");
        player.Morale.Add(morale);

        // 卡文是「我方前排1张兵力不高于2000的军团」，没有「其他」——阿伊印制兵力 2000，她自己是合法目标。
        AttackMaster(game, source);
        Resolve(game, "mode:use");
        Resolve(game, source.InstanceId);
        PassResponses(game);

        Assert.True(morale.Tapped);
        Assert.NotEmpty(source.TimedModifiers);
        Assert.Contains(game.State.Events,
            entry => entry.Type == "troops-modifier" && entry.Text.Contains("+2000", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0208")]
    [Trait("L12Evidence", "entry:settled-target-failure-carries-player-reason")]
    public void FailedDeclaredTargetSettlementCarriesAPlayerFacingReason()
    {
        var game = Create(9312);
        var player = game.State.Players[0];
        var source = Card("S01-0208", "ayi-reason-source");
        var target = Card("S01-0001", "ayi-reason-target", troops: 2000);
        var morale = Morale("ayi-reason-morale");
        player.Field[0][1] = target;
        player.Morale.Add(morale);

        AttackMaster(game, source);
        Resolve(game, "mode:use");
        Resolve(game, target.InstanceId);

        // 已付费已声明、逆结算后目标不再是军团：失败成立，但必须带上玩家可读的原因。
        player.Field[0][1] = Card(target.CardId, target.InstanceId, cardType: "artifact", troops: 2000);
        PassResponses(game);

        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Cards.Any(card => card.InstanceId == source.InstanceId));
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == source.InstanceId)
            && entry.PlayerLogSemantic is { OutcomeLabel: not null }
            && entry.PlayerLogSemantic!.OutcomeLabel.Contains("原因：", StringComparison.Ordinal));
    }
}
