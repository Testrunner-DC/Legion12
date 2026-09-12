using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class EffectBatch294RegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed = 20260909, string? masterId = null)
    {
        var baseDeck = Catalog.DeckAt(0);
        var decks = masterId is null
            ? new[] { baseDeck, baseDeck }
            : new[]
            {
                new L12PresetDeckDefinition
                {
                    Name = "EffectBatch294",
                    MasterId = masterId,
                    CardIds = [.. baseDeck.CardIds],
                    MoraleIds = [.. baseDeck.MoraleIds],
                    SpecialIds = [],
                },
                baseDeck,
            };
        var game = new L12GameEngine(Catalog, "effect-batch-294", "EFFECT294", seed,
            ["甲", "乙"], decks, skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2);
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
            player.Morale.Clear();
            player.Resolving.Clear();
            player.SpecialZones.Trials.Clear();
            player.SpecialZones.Runes = 0;
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner = 0, int? troops = null)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            OwnerIndex = owner,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = troops ?? definition.Troops ?? 0,
            Troops = troops ?? definition.Troops ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
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
        for (var safety = 0; safety < 80 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            Resolve(game, "pass");
    }

    private static bool AdvanceTrial(L12GameEngine game, int playerIndex, L12CardInstance source)
    {
        var method = typeof(L12GameEngine).GetMethod("AdvanceTrial", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<bool>(method.Invoke(game, [playerIndex, 1, source]));
    }

    private static L12StackItem PushEffect(L12GameEngine game, int controller, L12CardInstance source,
        string trigger, string effect, IEnumerable<string>? targets = null,
        Dictionary<string, string>? data = null)
    {
        var method = typeof(L12GameEngine).GetMethod("PushEffect", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<L12StackItem>(method.Invoke(game,
            [controller, source, trigger, effect, targets, data ?? new Dictionary<string, string>
            {
                ["triggerEffectText"] = effect,
            }]));
    }

    private static L12CardInstance AddOpenTrial(L12GameEngine game, int progress = 0)
    {
        var trial = Card("S02-06S6", $"effect294-trial-{progress}");
        trial.TrialProgress = progress;
        game.State.Players[0].SpecialZones.Trials.Add(trial);
        return trial;
    }

    [Fact]
    [Trait("L12Evidence", "batch:EFFECT-20260909-294")]
    public void CatalogCarriesTheApprovedKagutsuchiAndAngusRules()
    {
        Assert.Equal(8, Catalog.Cards["ST04-M1"].Hp);
        var angus = Catalog.Cards["S02-06M2"];
        Assert.Equal("规则上，可完成的试炼数量增加1张。\n我方 回合1次 推进试炼进度时，可获得1符文。\n回合1次 当我方成功发动战术效果时，试炼+1。",
            angus.Effect);
        Assert.Equal(2, L12SpecialDeckRules.TrialCapacity(angus));
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-06M2")]
    public void AngusTrialAdvanceDeclineDoesNotConsumeButNegatedActivationDoes()
    {
        var game = Create(29401, "S02-06M2");
        var player = game.State.Players[0];
        var trial = AddOpenTrial(game);

        Assert.True(AdvanceTrial(game, 0, trial));
        Assert.Equal("pending-activation", Assert.Single(game.State.PendingPrompts).Continuation);
        Resolve(game, "mode:none");
        Assert.DoesNotContain($"trigger:angus-trial-rune:{game.State.TurnSerial}", player.UsedAbilities);
        Assert.DoesNotContain($"trigger:angus-trial-rune:{game.State.TurnSerial}:pending", player.UsedAbilities);

        Assert.True(AdvanceTrial(game, 0, trial));
        Resolve(game, "mode:use");
        Assert.Contains($"trigger:angus-trial-rune:{game.State.TurnSerial}", player.UsedAbilities);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.True(AdvanceTrial(game, 0, trial));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-06M2")]
    public void AngusTrialAdvanceRuneIsOwnTurnOnlyAndGainsExactlyOneRune()
    {
        var ownTurn = Create(29402, "S02-06M2");
        var ownTrial = AddOpenTrial(ownTurn);
        Assert.True(AdvanceTrial(ownTurn, 0, ownTrial));
        Resolve(ownTurn, "mode:use");
        PassResponses(ownTurn);
        Assert.Equal(1, ownTurn.State.Players[0].SpecialZones.Runes);

        var opponentTurn = Create(29403, "S02-06M2");
        opponentTurn.State.ActivePlayer = 1;
        var opponentTrial = AddOpenTrial(opponentTurn);
        Assert.True(AdvanceTrial(opponentTurn, 0, opponentTrial));
        Assert.Empty(opponentTurn.State.PendingPrompts);
        Assert.Empty(opponentTurn.State.EffectStack);
        Assert.Equal(0, opponentTurn.State.Players[0].SpecialZones.Runes);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0612")]
    public void ScathachUpdatesTheCurrentAttackNoLossAndPendingDefenseRestorePreservesIt()
    {
        var game = Create(29404);
        var scathach = Card("S02-0612", "effect294-scathach");
        var defender = Card("S01-0103", "effect294-defender", owner: 1, troops: 4000);
        game.State.Players[0].Field[0][0] = scathach;
        game.State.Players[1].Field[0][0] = defender;
        game.State.Players[0].SpecialZones.Runes = 1;

        var attack = game.Handle(0, new L12Command("attack", scathach.InstanceId,
            Target: new L12AttackTarget("legion", defender.InstanceId)));
        Assert.True(attack.Accepted, attack.Error);
        Resolve(game, "mode:use");
        Assert.Equal(0, game.State.Players[0].SpecialZones.Runes);
        Resolve(game, "pass");
        Resolve(game, "pass");

        var pending = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.True(pending.AttackNoLoss);
        Assert.Equal(scathach.InstanceId, pending.AttackerInstanceId);
        var pendingJson = System.Text.Json.JsonSerializer.Serialize(pending);
        var restoredPending = Assert.IsType<L12PendingDefense>(
            System.Text.Json.JsonSerializer.Deserialize<L12PendingDefense>(pendingJson));
        Assert.True(restoredPending.AttackNoLoss);
        Assert.Equal(scathach.InstanceId, restoredPending.AttackerInstanceId);
        game.State.PendingDefense = restoredPending;

        PassResponses(game);

        Assert.Null(game.State.PendingDefense);
        Assert.Contains(game.State.Events, entry => entry.Type == "combat"
            && entry.Text.Contains("进攻无损", StringComparison.Ordinal));
        var restoredScathach = Assert.Single(game.State.Players[0].Field
            .SelectMany(row => row).OfType<L12CardInstance>());
        Assert.Equal("S02-0612", restoredScathach.CardId);
        Assert.Equal(restoredScathach.BaseTroops + 2000, restoredScathach.Troops);
        Assert.Contains(game.State.Players[1].Graveyard, card => card.InstanceId == defender.InstanceId);
    }

    public static IEnumerable<object[]> PromotionEffects()
    {
        yield return ["ST05-01", "晋升登场 可查看我方牌库，选择最多2张【远程】军团活跃登场。随后重洗牌库。"];
        yield return ["S02-0501", "晋升登场 可展示手牌中1张军团并将其放回牌库顶部：击杀对方1张费用不高于展示军团其费用的军团。"];
        yield return ["S02-0503", "晋升登场 本回合可进攻对方军团。"];
        yield return ["S02-0505", "晋升登场 可选择对方1张休整的军团，使其在下个对方重置阶段无法转为活跃。"];
        yield return ["S02-0507", "晋升登场 可抽取1张牌。"];
    }

    [Theory]
    [MemberData(nameof(PromotionEffects))]
    [Trait("L12Evidence", "card:S01-0018")]
    public void PitfallCanRespondToEveryPromotionEffectAndPromptShowsOnlyThatEffect(string cardId, string effect)
    {
        var game = Create(29405);
        var promoted = Card(cardId, $"effect294-promoted-{cardId}");
        var pitfall = Card("S01-0018", $"effect294-pitfall-{cardId}", owner: 1);
        pitfall.Hidden = true;
        game.State.Players[0].Field[0][0] = promoted;
        game.State.Players[1].Field[1][0] = pitfall;

        PushEffect(game, 0, promoted, "promotion-enter", effect, ["private-target"],
            new Dictionary<string, string>
            {
                ["triggerEffectText"] = effect,
                ["privateDeclaration"] = "private-choice-must-not-leak",
            });

        var activePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal($"选择响应卡牌；可响应任意符合卡面条件的未结算效果。\n我方使用〈{promoted.Name}〉\n时点：晋升登场\n效果：{effect}\n（效果原文中的我方／对方以发动者为准）\n是否响应？", activePrompt.Text);
        Assert.DoesNotContain("private-target", activePrompt.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("private-choice-must-not-leak", activePrompt.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(promoted.EffectText!.Split('\n')[0], activePrompt.Text, StringComparison.Ordinal);

        Resolve(game, "pass");
        var defenderPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(pitfall.InstanceId, defenderPrompt.ValidChoices);
        Assert.Equal(activePrompt.Text.Replace("我方使用", "对方使用", StringComparison.Ordinal), defenderPrompt.Text);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0018")]
    public void PitfallNegatesOnlyTheChosenPromotionItemAndNormalEntryRemainsIndependent()
    {
        var game = Create(29406);
        var promoted = Card("S02-0507", "effect294-independent-promotion");
        var pitfall = Card("S01-0018", "effect294-independent-pitfall", owner: 1);
        pitfall.Hidden = true;
        game.State.Players[0].Field[0][0] = promoted;
        game.State.Players[1].Field[1][0] = pitfall;
        var normalEffect = "登场时 可抽取1张牌。";
        var normal = new L12StackItem
        {
            StackItemId = "stack-1",
            Controller = 0,
            SourceInstanceId = promoted.InstanceId,
            SourceCardId = promoted.CardId,
            SourceName = promoted.Name,
            Trigger = "enter",
            Text = normalEffect,
        };
        game.State.StackSequence = 1;
        game.State.DeferredEffectStack.Add(normal);
        var promotion = PushEffect(game, 0, promoted, "promotion-enter", "晋升登场 可抽取1张牌。");

        Resolve(game, "pass");
        Resolve(game, pitfall.InstanceId);
        var nested = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("〈落穴陷阱〉", nested.Text, StringComparison.Ordinal);
        Assert.Contains("时点：对方 军团登场时", nested.Text, StringComparison.Ordinal);
        Assert.Contains("效果：对方 军团登场时：使此军团登场效果无效。", nested.Text, StringComparison.Ordinal);
        for (var safety = 0; safety < 12
             && !(game.State.EffectStack.LastOrDefault() == normal
                  && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"); safety++)
            Resolve(game, "pass");

        Assert.True(promotion.Negated);
        Assert.False(normal.Negated);
        var result = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == pitfall.InstanceId));
        Assert.Equal("resolved", result.EffectResultStatus);
        Assert.Equal(1, result.EffectSegmentIndex);
        Assert.Equal(1, result.EffectSegmentCount);
        Assert.Same(normal, Assert.Single(game.State.EffectStack));
        Assert.Same(promoted, game.State.Players[0].Field[0][0]);
        var normalPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal($"选择响应卡牌；可响应任意符合卡面条件的未结算效果。\n我方使用〈{promoted.Name}〉\n时点：登场时\n效果：{normalEffect}\n（效果原文中的我方／对方以发动者为准）\n是否响应？", normalPrompt.Text);
    }

    public static IEnumerable<object[]> ResponseTimings()
    {
        yield return ["enter", "登场时"];
        yield return ["promotion-enter", "晋升登场"];
        yield return ["attack", "进攻时"];
        yield return ["after-attack", "进攻后"];
        yield return ["death", "阵亡时"];
        yield return ["leave", "离场时"];
        yield return ["trial-advance", "推进试炼进度时"];
        yield return ["trial-complete", "完成试炼时"];
        yield return ["trial-advance-followup", "推进试炼进度后"];
        yield return ["turn-start", "回合开始时"];
        yield return ["play", "战术效果发动时"];
        yield return ["active", "主动效果发动时"];
        yield return ["opponent-attack", "对方进攻时"];
        yield return ["after-damage", "对主宰造成伤害时"];
        yield return ["forge-ready-after-kill", "击杀后"];
        yield return ["trojan-after-attack", "对方进攻后"];
        yield return ["medjed-master-damage", "我方主宰受到伤害后"];
        yield return ["morrigan-enemy-death", "对方军团阵亡时"];
        yield return ["nephthys-own-death", "我方军团阵亡时"];
        yield return ["discard-trigger", "我方丢弃卡牌时"];
        yield return ["master-morale-return", "士气返回士气区时"];
        yield return ["morale-return", "士气返回士气区时"];
        yield return ["opponent-back-to-front", "对方军团从后排移动至前排时"];
        yield return ["prayer-private", "祈祷时"];
        yield return ["s2-after-opponent-tactic", "对方战术效果结算后"];
        yield return ["legion-attack-timing", "军团进攻时"];
        yield return ["rune-spent", "消耗符文时"];
        yield return ["wisdom-reward", "对方效果成功完成结算后"];
        yield return ["return-library-top", "返回牌库顶部时"];
        yield return ["authority-event", "军团以手牌以外的方式登场时"];
        yield return ["future-trigger", "响应效果发动时"];
    }

    [Theory]
    [MemberData(nameof(ResponseTimings))]
    [Trait("L12Evidence", "entry:all-response-prompt-details")]
    public void EveryKnownResponseTimingUsesTheDetailedPromptContract(string trigger, string timing)
    {
        var game = Create(29407);
        var source = Card("S01-0103", $"effect294-source-{trigger}");
        game.State.Players[0].Field[0][0] = source;
        const string effect = "此项独立效果正文。";
        var data = new Dictionary<string, string>
        {
            ["triggerEffectText"] = effect,
            ["privateDeclaration"] = "hidden-value",
        };
        if (trigger == "authority-event") data["eventType"] = "non-hand-entry";

        PushEffect(game, 0, source, trigger, effect, ["hidden-target"], data);

        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal($"选择响应卡牌；可响应任意符合卡面条件的未结算效果。\n我方使用〈{source.Name}〉\n时点：{timing}\n效果：{effect}\n（效果原文中的我方／对方以发动者为准）\n是否响应？", prompt.Text);
        Assert.DoesNotContain("hidden-value", prompt.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden-target", prompt.Text, StringComparison.Ordinal);
    }
}
