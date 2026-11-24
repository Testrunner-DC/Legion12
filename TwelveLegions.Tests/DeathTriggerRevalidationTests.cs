using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Tests;

/// <summary>
/// 确定性复现：BUG-20261120-76745b4c（格温莉安被《神妙行军》第二段效果击杀后未触发阵亡选择）与
/// BUG-20261119-e1dc0f6b（立花誾千代支援位阵亡未触发【阵亡时】）。
/// 两条用例只使用引擎公开命令与公开状态，不修改生产代码。
/// </summary>
[Trait("L12Evidence", "bug:BUG-20261120-76745b4c+BUG-20261119-e1dc0f6b")]
public sealed class DeathTriggerRevalidationTests(ITestOutputHelper output)
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "death-trigger-revalidation", "DEATH-REVAL", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
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
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.Resolving.Clear();
            player.UsedAbilities.Clear();
            player.Hp = player.MaxHp - 1;
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner,
        int? cost = null, int? troops = null)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            OwnerIndex = owner,
            Name = definition.NameZh,
            CardType = definition.CardType,
            IsCounterTactic = definition.IsCounterTactic,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = cost ?? definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = troops ?? definition.Troops ?? 0,
            Troops = troops ?? definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }

    private static L12CardInstance PlainLegion(string instanceId, int owner, int troops, int cost = 1)
        => new()
        {
            InstanceId = instanceId,
            CardId = $"test-{instanceId}",
            OwnerIndex = owner,
            Name = instanceId,
            CardType = "legion",
            Faction = "universal",
            Cost = cost,
            BaseTroops = troops,
            Troops = troops,
            SummonRound = -1,
        };

    private static L12MoraleCard Morale(string instanceId)
        => new() { InstanceId = instanceId, CardId = "S01-01C1", Tapped = false };

    private static string Describe(L12Prompt prompt)
        => $"{prompt.Kind}/{prompt.Continuation}/{prompt.SourceCardId}#{prompt.SourceInstanceId}"
            + $"[{string.Join(',', prompt.ValidChoices)}]";

    private static void Resolve(L12GameEngine game, L12Prompt prompt, string? choice = null,
        string[]? instanceIds = null)
    {
        var command = new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice,
            CardInstanceIds: instanceIds is null ? null : [.. instanceIds],
            ActivationId: prompt.ActivationId, SourceInstanceId: prompt.SourceInstanceId,
            SourceCardId: prompt.SourceCardId, Step: prompt.Step,
            CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller);
        var result = game.Handle(prompt.PlayerIndex, command);
        Assert.True(result.Accepted, $"{result.Error} @ {Describe(prompt)}");
    }

    private static bool IsTriggerOrder(L12Prompt prompt)
        => prompt.Kind == "trigger-order" || prompt.Continuation == "trigger-batch-order";

    private static void AssertQuiet(L12GameEngine game)
    {
        Assert.Empty(game.State.PendingPrompts);
        Assert.Null(game.State.PendingDefense);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
    }

    [Theory]
    [InlineData("mode:heal")]
    [InlineData("mode:draw")]
    [Trait("L12Evidence", "entry:gwen-effect-death-by-march-second-segment")]
    public void MarchSecondSegmentKillStillOffersGwensEffectDeathChoice(string mode)
    {
        var game = Create(mode == "mode:heal" ? 767451 : 767452);
        var caster = game.State.Players[0];
        var owner = game.State.Players[1];
        var front = PlainLegion("reval-march-front", 0, 4000);
        var gwen = Card("S02-0615", "reval-gwen", 1);
        var march = Card("S01-0118", "reval-march", 0);
        caster.Field[0][0] = front;
        caster.Hand.Add(march);
        for (var i = 0; i < 5; i++) caster.Morale.Add(Morale($"reval-morale-{i}"));
        owner.Field[0][0] = gwen;
        for (var i = 0; i < 3; i++) owner.Library.Add(PlainLegion($"reval-gwen-deck-{i}", 1, 1000));
        owner.Hp = owner.MaxHp - 2;
        var hpBefore = owner.Hp;
        var handBefore = owner.Hand.Count;
        var libraryBefore = owner.Library.Count;

        var play = game.Handle(0, new L12Command("playCard", march.InstanceId));
        Assert.True(play.Accepted, play.Error);

        L12Prompt? gwenChoice = null;
        var chain = new List<string>();
        for (var step = 0; step < 80; step++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt is null) break;
            chain.Add($"{prompt.PlayerIndex}:{Describe(prompt)}");
            if (prompt.Kind == "response") { Resolve(game, prompt, "pass"); continue; }
            if (IsTriggerOrder(prompt)) { Resolve(game, prompt, instanceIds: [.. prompt.ValidChoices]); continue; }
            if (prompt.SourceCardId == "S02-0615" || prompt.SourceInstanceId == gwen.InstanceId)
            {
                gwenChoice ??= prompt;
                Resolve(game, prompt, mode);
                continue;
            }
            if (prompt.SourceCardId == "S01-0118" || prompt.SourceInstanceId == march.InstanceId)
            {
                if (prompt.ValidChoices.Contains(gwen.InstanceId))
                { Resolve(game, prompt, gwen.InstanceId); continue; }
                if (prompt.ValidChoices.Contains(front.InstanceId))
                { Resolve(game, prompt, front.InstanceId); continue; }
                if (prompt.ValidChoices.Contains("mode:use"))
                { Resolve(game, prompt, "mode:use"); continue; }
                var returned = prompt.ValidChoices.Where(choice => choice.StartsWith("reval-morale-", StringComparison.Ordinal)).ToArray();
                if (returned.Length >= 2)
                { Resolve(game, prompt, instanceIds: [returned[0], returned[1]]); continue; }
            }
            Assert.Fail($"未覆盖的提示：{Describe(prompt)}");
        }

        output.WriteLine($"提示链：{string.Join(" -> ", chain)}");
        output.WriteLine($"格温莉安阵亡选择提示：{(gwenChoice is null ? "缺失" : Describe(gwenChoice))}");
        AssertQuiet(game);
        Assert.Contains(owner.Graveyard, card => card.InstanceId == gwen.InstanceId);
        Assert.DoesNotContain(gwen, owner.Field.SelectMany(row => row));
        Assert.Contains(march, caster.Graveyard);

        Assert.True(gwenChoice is not null, "格温莉安因效果阵亡后没有出现【阵亡时】公开触发声明提示");
        Assert.Equal("S02-0615", gwenChoice!.SourceCardId);
        Assert.Contains("mode:heal", gwenChoice.ValidChoices);
        Assert.Contains("mode:draw", gwenChoice.ValidChoices);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("格温莉安因效果阵亡", StringComparison.Ordinal));

        owner = game.State.Players[1];
        Assert.Equal(mode == "mode:heal" ? hpBefore + 1 : hpBefore, owner.Hp);
        Assert.Equal(mode == "mode:draw" ? handBefore + 1 : handBefore, owner.Hand.Count);
        Assert.Equal(mode == "mode:draw" ? libraryBefore - 1 : libraryBefore, owner.Library.Count);
    }

    [Fact]
    [Trait("L12Evidence", "entry:tachibana-support-slot-combat-death")]
    public void TachibanaSupportSlotCombatDeathStillEstablishesAndSettlesItsDeathTrigger()
    {
        var game = Create(20261119);
        var attackerPlayer = game.State.Players[0];
        var defender = game.State.Players[1];
        var attacker = PlainLegion("reval-attacker", 0, 4000);
        var firstFoe = Card("S01-0001", "reval-foe-front", 0, cost: 3);
        var secondFoe = Card("S01-0003", "reval-foe-second", 0, cost: 2);
        var target = PlainLegion("reval-target", 1, 1000);
        var tachibana = Card("S01-0412", "reval-tachibana", 1);
        attackerPlayer.Field[0][0] = attacker;
        attackerPlayer.Field[0][1] = firstFoe;
        attackerPlayer.Field[0][2] = secondFoe;
        defender.Field[0][0] = target;
        defender.Field[1][0] = tachibana;

        var attack = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId)));
        Assert.True(attack.Accepted, attack.Error);
        for (var step = 0; step < 40
            && game.State.PendingDefense?.Stage != L12CombatStage.DefenseChoice; step++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt is null) break;
            if (prompt.Kind == "response") Resolve(game, prompt, "pass");
            else if (IsTriggerOrder(prompt)) Resolve(game, prompt, instanceIds: [.. prompt.ValidChoices]);
            else Assert.Fail($"进攻到防御选择之间的未覆盖提示：{Describe(prompt)}");
        }
        Assert.Equal(L12CombatStage.DefenseChoice, game.State.PendingDefense?.Stage);

        var defend = game.Handle(1, new L12Command("resolveDefense",
            SupportInstanceId: tachibana.InstanceId));
        Assert.True(defend.Accepted, defend.Error);

        var chain = new List<string>();
        for (var step = 0; step < 80; step++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt is null) break;
            chain.Add($"{prompt.PlayerIndex}:{Describe(prompt)}");
            if (prompt.Kind == "response") { Resolve(game, prompt, "pass"); continue; }
            if (IsTriggerOrder(prompt)) { Resolve(game, prompt, instanceIds: [.. prompt.ValidChoices]); continue; }
            if (prompt.SourceCardId == "S01-0412" || prompt.SourceInstanceId == tachibana.InstanceId)
            {
                if (prompt.ValidChoices.Contains("mode:use")) { Resolve(game, prompt, "mode:use"); continue; }
                if (prompt.ValidChoices.Count == 1) { Resolve(game, prompt, prompt.ValidChoices[0]); continue; }
            }
            Assert.Fail($"未覆盖的提示：{Describe(prompt)}");
        }

        output.WriteLine($"支援阵亡后提示链：{string.Join(" -> ", chain)}");
        output.WriteLine($"费用：进攻者 {attacker.CurrentCost}，军团A {firstFoe.CurrentCost}，军团B {secondFoe.CurrentCost}");
        AssertQuiet(game);
        Assert.Contains(tachibana, defender.Graveyard);
        Assert.Null(defender.Field[1][0]);
        Assert.Contains(game.State.Events, entry => entry.Type == "leave"
            && entry.Text.Contains("立花誾千代作为支援军团阵亡", StringComparison.Ordinal));
        Assert.Contains(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("立花誾千代阵亡：直到下个我方回合结束前，对方所有军团费用-1",
                StringComparison.Ordinal));

        var debuffed = new[] { attacker, firstFoe, secondFoe };
        foreach (var legion in debuffed)
            Assert.Contains(legion.TimedModifiers, modifier => modifier.Source == "立花誾千代"
                && modifier.CostDelta == -1);
        Assert.Equal(0, attacker.CurrentCost);
        Assert.Equal(2, firstFoe.CurrentCost);
        Assert.Equal(1, secondFoe.CurrentCost);
    }
}
