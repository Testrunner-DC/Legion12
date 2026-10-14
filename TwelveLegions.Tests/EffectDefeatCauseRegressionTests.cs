using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

[Trait("L12Evidence", "card:S02-0615")]
public sealed class EffectDefeatCauseRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12CardInstance Card(string id, string instanceId, int owner)
    {
        var d = Catalog.Cards[id];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = id, OwnerIndex = owner, Name = d.NameZh,
            CardType = d.CardType, IsCounterTactic = d.IsCounterTactic, Faction = d.Faction,
            ImageUrl = d.ImageUrl, Cost = d.Cost ?? 0, HasPrintedCost = d.Cost.HasValue,
            EffectText = d.Effect, Traits = [.. d.Traits], Profession = d.Profession,
            EffectiveProfession = d.Profession, BaseTroops = d.Troops ?? 0, Troops = d.Troops ?? 0,
            DisasterLevel = d.DisasterLevel ?? 0, TrialValue = d.TrialValue ?? 0, SummonRound = -1,
        };
    }

    private static L12GameEngine Create(int active, int seed)
    {
        var game = new L12GameEngine(Catalog, "effect-defeat-cause", "LOCAL", seed,
            ["合成甲", "合成乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2, disasterMode: "none");
        game.State.ActivePlayer = active;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        game.State.DisasterDeck.Clear();
        game.State.PendingPrompts.Clear();
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear(); player.Graveyard.Clear(); player.Library.Clear();
            player.Morale.Clear(); player.UsedAbilities.Clear();
            player.Hp = player.MaxHp - 1;
            for (var i = 0; i < 6; i++)
                player.Library.Add(Card("S01-0003", $"deck-{player.PlayerIndex}-{i}", player.PlayerIndex));
        }
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game) => L12GameEngine.RestoreCheckpoint(Catalog,
        game.SerializeFullState(), game.RandomState!.Value, game.CardFactSignalSequence,
        autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static L12Command Typed(L12Prompt p, string? choice, List<string>? ids = null)
        => new("resolvePrompt", PromptId: p.PromptId, Choice: choice, CardInstanceIds: ids,
            ActivationId: p.ActivationId, SourceInstanceId: p.SourceInstanceId, SourceCardId: p.SourceCardId,
            Step: p.Step, CreatedRevision: p.CreatedRevision, Controller: p.Controller);

    private sealed class Witness { internal bool JingKe; internal int GwenChoices; }

    private static void Drain(ref L12GameEngine game, bool recovery, string mode, Witness witness)
    {
        for (var step = 0; step < 200; step++)
        {
            if (recovery) game = Restore(game);
            if (game.State.PendingPrompts.FirstOrDefault() is { } p)
            {
                L12Command command;
                if (p.Kind == "response") command = Typed(p, "pass");
                else if (p.Continuation == "trigger-batch-order") command = Typed(p, null, [.. p.ValidChoices]);
                else if (p.SourceCardId == "S01-0115" || p.SourceInstanceId == "jingke")
                {
                    witness.JingKe = true;
                    if (p.ValidChoices.Contains("mode:use")) command = Typed(p, "mode:use");
                    else if (p.ValidChoices.Contains("gwen")) command = Typed(p, null, ["gwen"]);
                    else command = Typed(p, null, [Assert.Single(p.ValidChoices, x => x.StartsWith("morale-", StringComparison.Ordinal))]);
                }
                else if (p.SourceCardId == "S02-0615" || p.SourceInstanceId == "gwen")
                {
                    witness.GwenChoices++;
                    Assert.Contains(mode, p.ValidChoices);
                    command = Typed(p, mode);
                }
                else throw new InvalidOperationException($"未覆盖的合成提示：{p.Kind}/{p.Continuation}/{p.SourceCardId}");
                var result = game.Handle(p.PlayerIndex, command);
                Assert.True(result.Accepted, result.Error);
            }
            else if (game.State.PendingDefense is { } defense)
            {
                Assert.Equal(L12CombatStage.DefenseChoice, defense.Stage);
                var result = game.Handle(1 - defense.AttackerPlayer, new L12Command("resolveDefense", CardInstanceIds: []));
                Assert.True(result.Accepted, result.Error);
            }
            else return;
        }
        throw new InvalidOperationException("合成阵亡链超过有界步骤");
    }

    private static void AssertIdle(L12GameEngine game)
    {
        Assert.Empty(game.State.PendingPrompts); Assert.Null(game.State.PendingDefense);
        Assert.Empty(game.State.EffectStack); Assert.Empty(game.State.DeferredEffectStack);
        Assert.Empty(game.State.PendingActivations); Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [Trait("L12Evidence", "entry:gwen-jingke-combat-effect-death-cause")]
    public void JingKeEffectKillInAnUnfinishedCombatOffersGwenChoiceExactlyOnce(int jingKeOwner, bool recovery)
    {
        var ownerIndex = 1 - jingKeOwner;
        var game = Create(ownerIndex, 143000 + jingKeOwner * 10 + (recovery ? 1 : 0));
        var owner = game.State.Players[ownerIndex];
        var enemy = game.State.Players[jingKeOwner];
        var gwen = Card("S02-0615", "gwen", ownerIndex);
        // 仅满足荆轲<=2000目标边界的合成夹具，不代表原玩家减兵来源。
        gwen.TimedModifiers.Add(new() { TroopsDelta = -1000, ExpiresAfterTurn = 999, Source = "synthetic-jingke-target-boundary" });
        gwen.Troops = 2000;
        owner.Field[1][1] = gwen;
        owner.Field[0][0] = Card("S01-0002", "attacker", ownerIndex);
        enemy.Field[0][0] = Card("S01-0115", "jingke", jingKeOwner);
        enemy.Morale.Add(new() { InstanceId = $"morale-{jingKeOwner}", CardId = "S01-01C1" });
        var hp = owner.Hp; var hand = owner.Hand.Count; var library = owner.Library.Count;
        var mode = (jingKeOwner + (recovery ? 1 : 0)) % 2 == 0 ? "mode:heal" : "mode:draw";
        var result = game.Handle(ownerIndex, new L12Command("attack", "attacker", Target: new L12AttackTarget("legion", "jingke")));
        Assert.True(result.Accepted, result.Error);
        var witness = new Witness();
        Drain(ref game, recovery, mode, witness);
        AssertIdle(game);
        Assert.True(witness.JingKe); Assert.Equal(1, witness.GwenChoices);
        owner = game.State.Players[ownerIndex]; enemy = game.State.Players[jingKeOwner];
        Assert.Single(owner.Graveyard, c => c.InstanceId == "gwen");
        Assert.Single(enemy.Graveyard, c => c.InstanceId == "jingke");
        Assert.Empty(enemy.Morale);
        Assert.Equal(mode == "mode:heal" ? hp + 1 : hp, owner.Hp);
        Assert.Equal(mode == "mode:draw" ? hand + 1 : hand, owner.Hand.Count);
        Assert.Equal(mode == "mode:draw" ? library - 1 : library, owner.Library.Count);
    }

    public static IEnumerable<object[]> CauseControls()
    {
        foreach (var owner in new[] { 0, 1 })
        foreach (var recovery in new[] { false, true })
        foreach (var control in new[] { "effect-kill", "effect-delta", "continuous-delta", "combat" })
            yield return [owner, recovery, control];
    }

    [Theory] [MemberData(nameof(CauseControls))]
    public void NumericEffectDeathsAndActualCombatKeepDistinctCausesAfterRestore(int ownerIndex, bool recovery, string control)
    {
        var enemyIndex = 1 - ownerIndex;
        var game = Create(control == "combat" ? enemyIndex : ownerIndex, 144000 + ownerIndex);
        var owner = game.State.Players[ownerIndex];
        var gwen = Card("S02-0615", "gwen", ownerIndex);
        owner.Field[control == "combat" ? 0 : 1][1] = gwen;
        var hp = owner.Hp;
        if (control == "effect-kill")
            Assert.True(game.HandleGm(new L12GmCommand("destroyCard", ownerIndex, CardInstanceId: "gwen")).Accepted);
        else if (control == "effect-delta")
        {
            // 同e2探针对照：合成既有伤后1000及效果-1000，经正常状态检查致死。
            gwen.Troops = 1000;
            gwen.TimedModifiers.Add(new() { TroopsDelta = -1000, ExpiresAfterTurn = 999, Source = "synthetic-effect-delta" });
            gwen.Troops = 0;
            Assert.True(game.HandleGm(new L12GmCommand("readyAll", ownerIndex)).Accepted);
        }
        else if (control == "continuous-delta")
        {
            gwen.Troops = 1000;
            var horse = Card("S02-0523", "trojan", enemyIndex);
            horse.Hidden = false;
            owner.Field[1][2] = horse;
            typeof(L12GameEngine).GetMethod("RecalculateContinuousTroops", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game, null);
            Assert.Equal(0, gwen.Troops); Assert.Equal(-1000, gwen.ContinuousTroopsPenalty);
            Assert.True(game.HandleGm(new L12GmCommand("readyAll", ownerIndex)).Accepted);
        }
        else
        {
            game.State.Players[enemyIndex].Field[0][0] = Card("S01-0002", "attacker", enemyIndex);
            var result = game.Handle(enemyIndex, new L12Command("attack", "attacker", Target: new L12AttackTarget("legion", "gwen")));
            Assert.True(result.Accepted, result.Error);
        }
        var witness = new Witness();
        Drain(ref game, recovery, "mode:heal", witness);
        AssertIdle(game);
        owner = game.State.Players[ownerIndex];
        Assert.Single(owner.Graveyard, c => c.InstanceId == "gwen");
        Assert.Equal(control == "combat" ? 0 : 1, witness.GwenChoices);
        Assert.Equal(control == "combat" ? hp : hp + 1, owner.Hp);
    }

    [Theory] [InlineData(0)] [InlineData(1)]
    public void ImmortalPreventsDefeatAndDoesNotInventGwenDeathTrigger(int ownerIndex)
    {
        var game = Create(ownerIndex, 145000 + ownerIndex);
        var owner = game.State.Players[ownerIndex];
        var gwen = Card("S02-0615", "gwen", ownerIndex);
        gwen.ImmortalUses = 1;
        gwen.ImmortalUntilTurn = 999;
        owner.Field[0][0] = gwen;
        var hp = owner.Hp;
        Assert.False(game.HandleGm(new L12GmCommand("destroyCard", ownerIndex, CardInstanceId: "gwen")).Accepted);
        Assert.Same(gwen, owner.Field[0][0]); Assert.Empty(owner.Graveyard);
        Assert.Equal(0, gwen.ImmortalUses); Assert.Equal(hp, owner.Hp);
        AssertIdle(game);
    }

    [Theory]
    [InlineData("CardEffect")] [InlineData("Combat")]
    public void EffectLethalReplacementCheckpointKeepsExplicitCauseAndSelectedSubstituteRetainsProtectedCard(string cause)
    {
        var game = Create(0, 146001);
        var protectedCard = Card("S01-0205", "horemheb", 0);
        game.State.Players[0].Field[0][0] = protectedCard;
        game.State.Players[0].Field[0][1] = Card("S01-0212", "guard", 0);
        var removal = typeof(L12GameEngine).GetMethod("RemoveFromFieldWithCause", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.False((bool)removal.Invoke(game, [game.State.Players[0], protectedCard, true, "合成来源保持检查",
            true, L12FieldLeaveKind.Defeat, false, false, Enum.Parse<L12DefeatCause>(cause)])!);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(cause, prompt.Data["defeatCause"]);
        game = Restore(game);
        prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(cause, prompt.Data["defeatCause"]);
        // 真正选择代替，保护牌不离场；守卫沿所记录的致死原因离场。
        var result = game.Handle(prompt.PlayerIndex, Typed(prompt, "guard"));
        Assert.True(result.Accepted, result.Error);
        Assert.Equal("horemheb", game.State.Players[0].Field[0][0]?.InstanceId);
        Assert.Single(game.State.Players[0].Graveyard, c => c.InstanceId == "guard");
    }

    [Theory]
    [InlineData(null, "被荆轲阵亡效果击杀", "CardEffect")]
    [InlineData(null, "作为支援军团阵亡", "Combat")]
    [InlineData("CardEffect", "作为支援军团阵亡", "CardEffect")]
    [InlineData("Combat", "被效果击杀", "Combat")]
    public void ReplacementCauseUsesExplicitCheckpointOrExactLegacyOriginNotCurrentCombat(string? stored, string reason, string expected)
    {
        var prompt = new L12Prompt { PromptId = "legacy-cause", PlayerIndex = 0, Kind = "optional", Text = "合成来源", ValidChoices = ["yes", "no"], Continuation = "effect-lethal-replacement", Data = new() { ["reason"] = reason } };
        if (stored is not null) prompt.Data["defeatCause"] = stored;
        var method = typeof(L12GameEngine).GetMethod("EffectLethalDefeatCause", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal(expected, method.Invoke(null, [prompt])!.ToString());
    }

    [Fact]
    public void UnknownCheckpointDefeatCauseFailsClosed()
    {
        var prompt = new L12Prompt { PromptId = "invalid-cause", PlayerIndex = 0, Kind = "optional", Text = "合成来源", ValidChoices = ["yes", "no"], Continuation = "effect-lethal-replacement", Data = new() { ["defeatCause"] = "unknown" } };
        var method = typeof(L12GameEngine).GetMethod("EffectLethalDefeatCause", BindingFlags.Static | BindingFlags.NonPublic)!;
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [prompt]));
        Assert.IsType<InvalidOperationException>(error.InnerException);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    public void UnknownRestoredAchillesCauseFailsBeforePendingCleanupOrGodPowerPayment(int ownerIndex, bool realCommand)
    {
        var game = Create(ownerIndex, 147000 + ownerIndex);
        var owner = game.State.Players[ownerIndex];
        var achilles = Card("S02-0504", "achilles", ownerIndex);
        owner.Field[0][0] = achilles;
        owner.Morale.Add(new() { InstanceId = "power", CardId = "S01-01C1", IsGodPower = true });
        var remove = typeof(L12GameEngine).GetMethod("RemoveFromField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.False((bool)remove.Invoke(game, [owner, achilles, true, "合成效果致死", true,
            L12FieldLeaveKind.Defeat, false, false])!);
        game = Restore(game);
        owner = game.State.Players[ownerIndex];
        var prompt = Assert.Single(game.State.PendingPrompts);
        prompt.Data["defeatCause"] = "unknown";
        var before = game.SerializeFullState();
        var resolve = typeof(L12GameEngine).GetMethod("ResolveEffectLethalReplacement", BindingFlags.Instance | BindingFlags.NonPublic)!;
        if (realCommand)
            Assert.Throws<InvalidOperationException>(() => game.Handle(ownerIndex, Typed(prompt, "yes")));
        else
        {
            var error = Assert.Throws<TargetInvocationException>(() => resolve.Invoke(game, [ownerIndex, prompt, "yes"]));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
        Assert.Equal(before, game.SerializeFullState());
        Assert.False(Assert.Single(owner.Morale).Tapped);
        Assert.Equal("achilles", owner.Field[0][0]?.InstanceId);
        Assert.Empty(owner.Graveyard);
    }
}
