using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class Bq20260907_263RegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, bool autoPassEmptyResponses = true, string? firstMasterId = null)
    {
        L12GameEngine game;
        if (firstMasterId is null)
        {
            game = new L12GameEngine(Catalog, "bq-20260907-263", "BQ263", seed, ["甲", "乙"], [0, 1],
                skipPreparation: true, autoPassEmptyResponses: autoPassEmptyResponses,
                concealHiddenResponseAvailability: false);
        }
        else
        {
            var baseDeck = Catalog.DeckAt(0);
            var firstDeck = new L12PresetDeckDefinition
            {
                Name = $"{firstMasterId}测试牌库",
                MasterId = firstMasterId,
                CardIds = [.. baseDeck.CardIds],
                MoraleIds = [.. baseDeck.MoraleIds],
                SpecialIds = [.. baseDeck.SpecialIds],
            };
            game = new L12GameEngine(Catalog, "bq-20260907-263", "BQ263", seed, ["甲", "乙"],
                [firstDeck, baseDeck], skipPreparation: true, autoPassEmptyResponses: autoPassEmptyResponses,
                concealHiddenResponseAvailability: false);
        }

        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 7;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.UsedAbilities.Clear();
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
            HasPrintedCost = definition.Cost is not null,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == name && candidate.GetParameters().Length == args.Length);
        return method.Invoke(target, args);
    }

    private static void AddMorale(L12PlayerState player, int count)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard { CardId = "S01-03C1", InstanceId = $"bq263-morale-{index}" });
    }

    private static L12Prompt Prompt(L12GameEngine game) => Assert.Single(game.State.PendingPrompts);

    private static void Choose(L12GameEngine game, string choice)
    {
        var prompt = Prompt(game);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void ChooseMany(L12GameEngine game, params string[] choices)
    {
        var prompt = Prompt(game);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: choices.ToList()));
        Assert.True(result.Accepted, result.Error);
    }

    private static string RepresentationChoice(L12Prompt prompt, int count)
        => Assert.Single(prompt.ValidChoices, choice =>
            prompt.ChoiceLabels[choice].Contains($"视为{count}张", StringComparison.Ordinal));

    private static void PassResponses(L12GameEngine game, int maximum = 24)
    {
        var count = 0;
        while (game.State.PendingPrompts.FirstOrDefault() is { Kind: "response" } prompt && count++ < maximum)
        {
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
        Assert.True(count < maximum, "响应窗口未在限定次数内结束");
    }

    [Fact]
    public void SolarCityFactionPromptsAndPublicPresentationDoNotBorrowImmortalGiftText()
    {
        var game = Create(26301, firstMasterId: "S01-02M1");
        var player = game.State.Players[0];
        var guard = Card("S01-0212", "bq263-sun-guard");
        guard.OwnerIndex = 0;
        player.Graveyard.Add(guard);
        player.TemporaryMorale = 2;

        var begin = game.Handle(0, new L12Command("activateAbility", "faction-0", Ability: "sunGuard"));
        Assert.True(begin.Accepted, begin.Error);
        Assert.Contains("太阳城阵营效果", Prompt(game).Text, StringComparison.Ordinal);
        Assert.DoesNotContain("不朽之礼", Prompt(game).Text, StringComparison.Ordinal);
        Choose(game, guard.InstanceId);
        Assert.Contains("太阳城阵营效果", Prompt(game).Text, StringComparison.Ordinal);
        Assert.DoesNotContain("不朽之礼", Prompt(game).Text, StringComparison.Ordinal);
        Choose(game, "0:0");

        Assert.Contains(game.State.Events, actionEvent => actionEvent.Type == "effect-activation"
            && actionEvent.Text == "太阳城阵营效果：将1张<陵墓守卫>从我方墓地活跃登场。");
    }

    [Fact]
    public void RagnarEntryResponseNamesTheCompleteTriggeredEffectWithoutReminderText()
    {
        var game = Create(26302, autoPassEmptyResponses: false);
        var ragnar = Card("S01-0303", "bq263-ragnar");
        game.State.Players[0].Field[0][0] = ragnar;
        game.State.Players[0].Hp = 7;

        Invoke(game, "QueueOrPushTriggeredEffect", 0, ragnar, "enter", "【登场时】效果", null, null);

        Assert.Equal("登场时 若我方主宰血量不高于7，获得冲锋。", Assert.Single(game.State.EffectStack).Text);
        Assert.Equal(
            "是否响应堆叠顶部：〈传奇的拉格纳〉\n时点：登场时\n效果：登场时 若我方主宰血量不高于7，获得冲锋。",
            Prompt(game).Text);
    }

    [Fact]
    public void JozefEntryPresentationKeepsTheCompleteCostAndTargetClause()
    {
        var jozef = Card("ST02-06", "bq263-jozef");

        Assert.Equal("登场时 可弃置我方战场上1张军团：选择对方1张军团，本回合兵力-2000。",
            L12GameEngine.ResolveTriggeredEffectDisplayText(jozef, "enter", "【登场时】效果"));
    }

    [Fact]
    public void NonKeywordTrailingRuleParenthesesRemainInTriggeredEffectText()
    {
        var uesugi = Card("S01-0403", "bq263-uesugi");
        Assert.Equal("登场时 击杀对方1张费用不高于X的军团。（X=双方战场<反击战术>合计数量）",
            L12GameEngine.ResolveTriggeredEffectDisplayText(uesugi, "enter", "【登场时】效果"));

        var custom = new L12CardInstance
        {
            InstanceId = "bq263-parenthetical-rule",
            CardId = "TEST-PARENTHETICAL-RULE",
            Name = "括号规则测试",
            CardType = "legion",
            Faction = "neutral",
            EffectText = "登场时 获得1000兵力。（此数值按当前公开场面计算）阵亡时 抽取1张牌。",
        };
        Assert.Equal("登场时 获得1000兵力。（此数值按当前公开场面计算）",
            L12GameEngine.ResolveTriggeredEffectDisplayText(custom, "enter", "【登场时】效果"));
    }

    [Fact]
    public void ExplicitStackTextWinsAndEmptyResolvedTextFallsBackWithoutReadingHiddenCardText()
    {
        var explicitGame = Create(26303);
        var source = Card("S01-0303", "bq263-explicit-stack");
        var explicitCandidate = new L12TriggerCandidate
        {
            CandidateId = "bq263-explicit-candidate",
            Controller = 0,
            SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId,
            SourceName = source.Name,
            Trigger = "enter",
            Text = "公开候选兜底",
            SourceSnapshot = source,
            Data = new Dictionary<string, string>
            {
                ["triggerEffectText"] = "完整登场文本",
                ["stackText"] = "特殊后续段",
            },
        };
        Invoke(explicitGame, "AddTriggerCandidateToStack", explicitCandidate);
        Assert.Equal("特殊后续段", Assert.Single(explicitGame.State.EffectStack).Text);
        Assert.Contains(explicitGame.State.Events,
            actionEvent => actionEvent.Type == "effect-trigger" && actionEvent.Text == "完整登场文本");

        var fallbackGame = Create(26304);
        var hiddenSource = new L12CardInstance
        {
            InstanceId = "bq263-hidden-source",
            CardId = "TEST-HIDDEN",
            Name = "匿名候选",
            CardType = "tactic",
            Faction = "neutral",
            EffectText = "不应由空字段额外公开的隐藏全文",
        };
        var fallbackCandidate = new L12TriggerCandidate
        {
            CandidateId = "bq263-fallback-candidate",
            Controller = 0,
            SourceInstanceId = hiddenSource.InstanceId,
            SourceCardId = hiddenSource.CardId,
            SourceName = hiddenSource.Name,
            Trigger = "enter",
            Text = "公开候选兜底",
            SourceSnapshot = hiddenSource,
            Data = new Dictionary<string, string> { ["triggerEffectText"] = string.Empty },
        };
        Invoke(fallbackGame, "AddTriggerCandidateToStack", fallbackCandidate);
        Assert.Equal("公开候选兜底", Assert.Single(fallbackGame.State.EffectStack).Text);
        Assert.Contains(fallbackGame.State.Events,
            actionEvent => actionEvent.Type == "effect-trigger" && actionEvent.Text == "公开候选兜底");
        Assert.DoesNotContain(fallbackGame.State.Events,
            actionEvent => actionEvent.Text.Contains("隐藏全文", StringComparison.Ordinal));

        var followUpGame = Create(263041);
        var followUpCandidate = new L12TriggerCandidate
        {
            CandidateId = "bq263-follow-up-candidate",
            Controller = 0,
            SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId,
            SourceName = source.Name,
            Trigger = "enter",
            Text = "拉美西斯二世再次发动的【登场时】效果",
            SourceSnapshot = source,
            Data = new Dictionary<string, string> { ["triggerEffectText"] = "整张卡的登场时能力" },
        };
        Invoke(followUpGame, "AddTriggerCandidateToStack", followUpCandidate);
        Assert.Equal("拉美西斯二世再次发动的【登场时】效果",
            Assert.Single(followUpGame.State.EffectStack).Text);
    }

    [Fact]
    public void DirectPushKeepsSpecialIndependentSegmentInsteadOfWholeTriggerText()
    {
        var game = Create(263043, autoPassEmptyResponses: false);
        var source = Card("S02-06S5", "bq263-direct-special-segment");
        Invoke(game, "PushEffect", 0, source, "trial-complete",
            "芬尼亚传奇：第2个目标本回合兵力-3000", null,
            new Dictionary<string, string>
            {
                ["triggerEffectText"] = "触发 可消耗X符文，每消耗1符文可选择对方1张军团，本回合兵力-3000。",
            });

        Assert.Equal("芬尼亚传奇：第2个目标本回合兵力-3000",
            Assert.Single(game.State.EffectStack).Text);
        Assert.Equal("是否响应堆叠顶部：〈芬尼亚传奇〉\n时点：完成试炼时\n效果：芬尼亚传奇：第2个目标本回合兵力-3000",
            Prompt(game).Text);
    }

    [Fact]
    public void LegacyCombinedRepresentationTokenStillCompletesAnInFlightSequentialDeclaration()
    {
        var game = Create(263042);
        var player = game.State.Players[0];
        var first = Card("ST03-08", "bq263-legacy-first");
        var second = Card("ST03-08", "bq263-legacy-second");
        player.Graveyard.AddRange([first, second]);
        var legacy = $"grave-copies:{first.InstanceId}=1,{second.InstanceId}=3";

        var valid = L12StructuredCardRules.TryBuildNextGraveRepresentationPrompt(player, [first, second], [legacy],
            string.Empty, 4, 4, legionOnly: false, out var currentCard, out var choices,
            out var normalized, out _, out _);

        Assert.True(valid);
        Assert.Null(currentCard);
        Assert.Empty(choices);
        Assert.Equal(legacy, normalized);
    }

    [Fact]
    public void TwoGraveWarriorsAreAskedOneEntityAtATimeAndKeepTheLegacyFinalToken()
    {
        var game = Create(26305);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var hunt = Card("S01-0319", "bq263-hunt");
        var first = Card("ST03-08", "bq263-warrior-first");
        var second = Card("ST03-08", "bq263-warrior-second");
        var target = Card("S01-0103", "bq263-hunt-target");
        player.Hand.Add(hunt);
        player.Graveyard.AddRange([first, second]);
        opponent.Field[0][0] = target;
        AddMorale(player, 3);

        Assert.True(game.Handle(0, new L12Command("playCard", hunt.InstanceId)).Accepted);
        ChooseMany(game, first.InstanceId, second.InstanceId);

        var firstCount = Prompt(game);
        Assert.Equal(3, firstCount.ValidChoices.Count);
        Assert.Equal(first.InstanceId, firstCount.Data["graveRepresentationEntityId"]);
        Assert.All(firstCount.ValidChoices, choice => Assert.DoesNotContain(',', choice));
        Assert.Contains("第1/2张", firstCount.Text, StringComparison.Ordinal);
        Assert.Contains("墓地效果", firstCount.Text, StringComparison.Ordinal);
        Choose(game, RepresentationChoice(firstCount, 1));

        var secondCount = Prompt(game);
        Assert.Single(secondCount.ValidChoices);
        Assert.Equal(second.InstanceId, secondCount.Data["graveRepresentationEntityId"]);
        Assert.Contains("第2/2张", secondCount.Text, StringComparison.Ordinal);
        Assert.Contains(hunt, player.Hand);
        Assert.Equal(3, player.Morale.Count(card => !card.Tapped));
        Assert.Contains(first, player.Graveyard);
        Assert.Contains(second, player.Graveyard);
        Choose(game, RepresentationChoice(secondCount, 3));

        var activation = Assert.Single(game.State.PendingActivations);
        Assert.Equal($"grave-copies:{first.InstanceId}=1,{second.InstanceId}=3",
            Assert.Single(activation.DeclaredValues["graveEffectCopies"]));
        Assert.DoesNotContain(activation.DeclaredValues.Keys,
            key => key.Contains("grave-count-progress", StringComparison.Ordinal));
        Assert.Equal("猎杀时刻：选择击杀目标", Prompt(game).Text);
        Assert.Contains(hunt, player.Hand);
        Assert.Equal(3, player.Morale.Count(card => !card.Tapped));
        Assert.Contains(first, player.Graveyard);
        Assert.Contains(second, player.Graveyard);

        Choose(game, target.InstanceId);
        PassResponses(game);
        Assert.Equal([first.InstanceId, second.InstanceId], player.Library.Select(card => card.InstanceId));
        Assert.Contains(target, opponent.Graveyard);
    }

    [Fact]
    public void ForgedUnreachablePerEntityCountIsRejectedBeforeHuntingMomentIsCommitted()
    {
        var game = Create(26306);
        var player = game.State.Players[0];
        var hunt = Card("S01-0319", "bq263-invalid-hunt");
        var first = Card("ST03-08", "bq263-invalid-first");
        var second = Card("ST03-08", "bq263-invalid-second");
        player.Hand.Add(hunt);
        player.Graveyard.AddRange([first, second]);
        game.State.Players[1].Field[0][0] = Card("S01-0103", "bq263-invalid-target");
        AddMorale(player, 3);

        Assert.True(game.Handle(0, new L12Command("playCard", hunt.InstanceId)).Accepted);
        ChooseMany(game, first.InstanceId, second.InstanceId);
        Choose(game, RepresentationChoice(Prompt(game), 1));
        var finalCount = Prompt(game);
        Assert.Equal(RepresentationChoice(finalCount, 3), Assert.Single(finalCount.ValidChoices));
        var forged = game.Handle(finalCount.PlayerIndex, new L12Command("resolvePrompt",
            PromptId: finalCount.PromptId, Choice: $"grave-copies:{second.InstanceId}=1"));

        Assert.False(forged.Accepted);
        Assert.Single(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Contains(hunt, player.Hand);
        Assert.Equal(3, player.Morale.Count(card => !card.Tapped));
        Assert.Contains(first, player.Graveyard);
        Assert.Contains(second, player.Graveyard);
        Assert.DoesNotContain(game.State.Events, actionEvent => actionEvent.Type == "cost");
    }

    [Fact]
    public void InvalidatedLaterGraveEntityCancelsTheUncommittedHuntingMomentDeclaration()
    {
        var game = Create(26307);
        var player = game.State.Players[0];
        var hunt = Card("S01-0319", "bq263-stale-hunt");
        var first = Card("ST03-08", "bq263-stale-first");
        var second = Card("ST03-08", "bq263-stale-second");
        player.Hand.Add(hunt);
        player.Graveyard.AddRange([first, second]);
        game.State.Players[1].Field[0][0] = Card("S01-0103", "bq263-stale-target");
        AddMorale(player, 3);

        Assert.True(game.Handle(0, new L12Command("playCard", hunt.InstanceId)).Accepted);
        ChooseMany(game, first.InstanceId, second.InstanceId);
        Choose(game, RepresentationChoice(Prompt(game), 1));
        var stalePrompt = Prompt(game);
        player.Graveyard.Remove(second);
        player.Library.Add(second);
        Choose(game, RepresentationChoice(stalePrompt, 3));

        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Contains(hunt, player.Hand);
        Assert.Equal(3, player.Morale.Count(card => !card.Tapped));
        Assert.Contains(first, player.Graveyard);
        Assert.Contains(second, player.Library);
    }

    [Fact]
    public void RolloAlsoAsksEachSelectedWarriorSeparatelyAndUsesTheirExactTotal()
    {
        var game = Create(26308);
        var player = game.State.Players[0];
        var rollo = Card("S02-0302", "bq263-rollo");
        var first = Card("ST03-08", "bq263-rollo-first");
        var second = Card("ST03-08", "bq263-rollo-second");
        player.Hand.Add(rollo);
        player.Graveyard.AddRange([first, second]);
        AddMorale(player, 8);

        var begin = game.Handle(0, new L12Command("playCard", rollo.InstanceId, Row: 0, Slot: 0));
        Assert.True(begin.Accepted, begin.Error);
        ChooseMany(game, first.InstanceId, second.InstanceId);
        var firstCount = Prompt(game);
        Assert.Equal(first.InstanceId, firstCount.Data["graveRepresentationEntityId"]);
        Assert.Equal(3, firstCount.ValidChoices.Count);
        Choose(game, RepresentationChoice(firstCount, 1));
        var secondCount = Prompt(game);
        Assert.Equal(second.InstanceId, secondCount.Data["graveRepresentationEntityId"]);
        Assert.Equal(3, secondCount.ValidChoices.Count);
        Choose(game, RepresentationChoice(secondCount, 3));

        Assert.Same(rollo, player.Field[0][0]);
        Assert.Equal(6, player.Morale.Count(card => card.Tapped));
        Assert.Equal([first.InstanceId, second.InstanceId], player.Library.Select(card => card.InstanceId));
    }
}
