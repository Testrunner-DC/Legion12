using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PromptNarrativeMatrixTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [Trait("L12Evidence", "prompt-narrative:initiative-and-setup")]
    public void InitiativeAndOptionalSetupExplainTheSituationInstructionAndChoiceConsequences()
    {
        var baseDeck = Catalog.DeckAt(0);
        var setupDeck = new L12PresetDeckDefinition
        {
            Name = "提示叙事开局牌库",
            MasterId = "S02-03M1",
            CardIds = ["S02-0305", "S02-0301", .. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [.. baseDeck.SpecialIds],
        };
        var game = new L12GameEngine(Catalog, "prompt-narrative-setup", "PN-SETUP", 202609271,
            ["甲", "乙"], [setupDeck, baseDeck], skipPreparation: false, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2);

        var initiative = Assert.Single(game.State.PendingPrompts);
        AssertPresentation(initiative, "决定先后攻", "掷骰已经结束", "请选择由哪一方先攻");
        Assert.Equal("选择先攻", initiative.ChoiceLabels["first"]);
        Assert.Contains("你将成为先攻玩家", initiative.Presentation!.ChoiceConsequences["first"], StringComparison.Ordinal);
        Assert.NotSame(initiative.ChoiceLabels, initiative.Presentation.ChoiceConsequences);

        Assert.True(game.Handle(initiative.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: initiative.PromptId, Choice: "first")).Accepted);
        var ring = Assert.Single(game.State.PendingPrompts, prompt => prompt.Continuation == "setup-s2-ring");
        AssertPresentation(ring, "安德华拉诺特", "游戏开始时", "起始手牌数量改为4张");
        Assert.Equal("发动", ring.ChoiceLabels["yes"]);
        Assert.Contains("4张牌作为起始手牌", ring.Presentation!.ChoiceConsequences["yes"], StringComparison.Ordinal);
        Assert.Contains("按通常数量抽取", ring.Presentation.ChoiceConsequences["no"], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("L12Evidence", "prompt-narrative:response-and-five-view-privacy")]
    public void HiddenResponseKeepsOwnerDetailAndProjectsOnlySafeWaitingTextToFourOtherViews()
    {
        var game = CreateGame(202609272, autoPassEmptyResponses: true, concealHiddenResponseAvailability: true);
        var entering = PutCardInHand(game, 0, "S01-0103");
        var defender = game.State.Players[1];
        var ambush = Card("S01-0019", "narrative-covered-ambush", 1);
        var target = Card("S01-0103", "narrative-ambush-target", 1);
        ambush.Hidden = true;
        ambush.SetRound = 0;
        defender.Field[1][0] = ambush;
        defender.Field[0][0] = target;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0,
            new L12Command("playCard", entering.InstanceId, Row: 0, Slot: 0)).Accepted);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            Choice: "mode:use")).Accepted);

        var response = Assert.Single(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        Assert.Equal(1, response.PlayerIndex);
        Assert.Contains(ambush.InstanceId, response.ValidChoices);
        AssertPresentation(response, "响应窗口", "拥有本次响应优先权", "不响应");
        Assert.Equal("不响应", response.ChoiceLabels["pass"]);
        Assert.Contains("优先权将继续传递", response.Presentation!.ChoiceConsequences["pass"], StringComparison.Ordinal);
        Assert.NotEqual(response.ChoiceLabels[ambush.InstanceId],
            response.Presentation.ChoiceConsequences[ambush.InstanceId]);
        Assert.DoesNotContain(response.Data.Keys,
            key => key.StartsWith("__promptNarrative:", StringComparison.Ordinal));

        Assert.Single(game.SnapshotFor(1).Prompts);
        Assert.Empty(game.SnapshotFor(0).Prompts);
        Assert.Empty(game.SnapshotForSpectator().Prompts);
        Assert.Empty(game.SnapshotForReferee().Prompts);
        Assert.Single(game.SnapshotForGm(0).Prompts);

        foreach (var waitingObject in new[]
                 {
                     game.SnapshotFor(0).WaitingPrompt,
                     game.SnapshotForSpectator().WaitingPrompt,
                     game.SnapshotForReferee().WaitingPrompt,
                 })
        {
            Assert.NotNull(waitingObject);
            var waiting = JsonSerializer.SerializeToElement(waitingObject,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var summary = waiting.GetProperty("waitingSummary").GetString();
            Assert.Equal("乙 正在决定是否响应", summary);
            Assert.DoesNotContain(ambush.Name, summary!, StringComparison.Ordinal);
            Assert.DoesNotContain(ambush.InstanceId, summary, StringComparison.Ordinal);
        }
        Assert.Null(game.SnapshotForGm(0).WaitingPrompt);
    }

    [Fact]
    [Trait("L12Evidence", "prompt-narrative:resource-payment-and-return")]
    public void NonHomogeneousPaymentAndReturnExplainAmountsResourcesAndCancelConsequences()
    {
        var game = CreateCleanGame(202609273);
        var player = game.State.Players[0];
        var source = Card("S01-0103", "narrative-payment-source", 0);
        player.Hand.Add(source);
        player.TemporaryMorale = 1;
        var firstMorale = Morale(Catalog.DeckAt(0).MoraleIds[0], "narrative-morale-1");
        var secondMorale = Morale(Catalog.DeckAt(0).MoraleIds[1], "narrative-morale-2");
        player.Morale.AddRange([firstMorale, secondMorale]);

        Invoke(game, "CreateResourcePaymentPrompt", 0, 1, "play-cost", null,
            new Dictionary<string, string> { ["cardInstanceId"] = source.InstanceId }, null, 0, true);
        var payment = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("temporary-morale:1", payment.ValidChoices);
        Assert.Contains(firstMorale.InstanceId, payment.ValidChoices);
        AssertPresentation(payment, source.Name, "需要支付1份资源", "也可以取消当前操作");
        Assert.Equal("取消打出", payment.ChoiceLabels["cancel"]);
        Assert.Contains("取消整次打出", payment.Presentation!.ChoiceConsequences["cancel"], StringComparison.Ordinal);

        game.State.PendingPrompts.Clear();
        Invoke(game, "CreateReturnMoralePrompt", 0, 1, "active-return-choice", null,
            new Dictionary<string, string> { ["sourceName"] = "测试主动效果" }, false);
        var returned = Assert.Single(game.State.PendingPrompts);
        AssertPresentation(returned, "测试主动效果", "需要返还1张", "也可以选择不发动");
        Assert.Equal("不发动", returned.ChoiceLabels["cancel"]);
        Assert.Contains("不返还士气", returned.Presentation!.ChoiceConsequences["cancel"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("split-top-bottom", "分别确认要放回牌库顶部和底部")]
    [InlineData("all-bottom", "放回牌库底部")]
    [Trait("L12Evidence", "prompt-narrative:library-placement")]
    public void LibraryPlacementExplainsThePrivateCardsAndRequestedDestination(string placementMode,
        string expectedInstruction)
    {
        var game = CreateCleanGame(202609274);
        var first = Card("S01-0103", "narrative-library-1", 0);
        var second = Card("S01-0104", "narrative-library-2", 0);
        game.State.Players[0].Library.AddRange([first, second]);
        var item = StackItem("narrative-library-stack", 0, first, "主动效果");
        game.State.EffectStack.Add(item);

        Invoke(game, "CreateLibraryPlacementPrompt", item, new[] { first.InstanceId, second.InstanceId },
            "reorder-order", placementMode, "整理牌库顺序");
        var prompt = Assert.Single(game.State.PendingPrompts);
        AssertPresentation(prompt, first.Name, "2张牌", expectedInstruction);
        Assert.Contains("尚未向对手公开", prompt.Presentation!.Situation, StringComparison.Ordinal);
        Assert.Equal("甲 正在整理牌库", prompt.Presentation.WaitingSummary);
    }

    [Fact]
    [Trait("L12Evidence", "prompt-narrative:lethal-replacement-decline")]
    public void LethalReplacementExplainsCandidateOutcomeAndDeclineOutcome()
    {
        var game = CreateCleanGame(202609275);
        var player = game.State.Players[0];
        var protectedCard = Card("S01-0205", "narrative-protected", 0);
        var replacement = Card("S01-0212", "narrative-replacement", 0);
        player.Field[0][0] = protectedCard;
        player.Field[0][1] = replacement;

        var offered = Assert.IsType<bool>(Invoke(game, "TryOfferCardLethalSubstitution", player,
            protectedCard, "effect-lethal-replacement", "效果伤害"));
        Assert.True(offered);
        var prompt = Assert.Single(game.State.PendingPrompts);
        AssertPresentation(prompt, protectedCard.Name, "即将因效果伤害阵亡", "不发动");
        Assert.Equal(replacement.Name, prompt.ChoiceLabels[replacement.InstanceId]);
        Assert.Contains($"代替〈{protectedCard.Name}〉承受", prompt.Presentation!.ChoiceConsequences[replacement.InstanceId],
            StringComparison.Ordinal);
        Assert.Equal("不发动", prompt.ChoiceLabels["decline"]);
        Assert.Contains("继续结算", prompt.Presentation.ChoiceConsequences["decline"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("L12Evidence", "prompt-narrative:peace-negotiation-five-view-checkpoint")]
    public void PeaceNegotiationExplainsBothOutcomesToTheAskedOpponentAndKeepsOtherViewsSafe(
        int controller)
    {
        var game = CreateCleanGame(202609276 + controller);
        var source = Card("S01-0015", $"narrative-peace-{controller}", controller);
        var item = LegacyStackItem($"narrative-peace-stack-{controller}", controller, source,
            "play", "peace-negotiation");
        game.State.EffectStack.Add(item);

        Invoke(game, "ResolveTacticEffect", item);

        var prompt = Assert.Single(game.State.PendingPrompts);
        var askedPlayer = 1 - controller;
        Assert.Equal(askedPlayer, prompt.PlayerIndex);
        AssertPresentation(prompt, "议和谈判", "对方打出〈议和谈判〉并已先抽取1张牌",
            "同意后，对方再抽1张牌、你抽1张牌");
        Assert.Equal("同意议和", prompt.ChoiceLabels["agree"]);
        Assert.Equal("拒绝议和", prompt.ChoiceLabels["refuse"]);
        Assert.Contains("对方本次共抽取2张", prompt.Presentation!.ChoiceConsequences["agree"],
            StringComparison.Ordinal);
        Assert.Contains("你不抽牌", prompt.Presentation.ChoiceConsequences["refuse"],
            StringComparison.Ordinal);
        Assert.DoesNotContain(prompt.Data.Keys,
            key => key.StartsWith("__promptNarrative:", StringComparison.Ordinal));

        Assert.Single(game.SnapshotFor(askedPlayer).Prompts);
        Assert.Empty(game.SnapshotFor(controller).Prompts);
        Assert.Empty(game.SnapshotForSpectator().Prompts);
        Assert.Empty(game.SnapshotForReferee().Prompts);
        Assert.Single(game.SnapshotForGm(controller).Prompts);
        Assert.Null(game.SnapshotForGm(controller).WaitingPrompt);
        AssertSafeWaitingViews(game, controller, source);

        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), random,
            game.CardFactSignalSequence, game.AutoPassEmptyResponses,
            game.ConcealHiddenResponseAvailability);
        var restoredPrompt = Assert.Single(restored.State.PendingPrompts);
        AssertPresentation(restoredPrompt, "议和谈判", "请求你决定是否同意议和", "请选择同意或拒绝");
        Assert.Equal(prompt.Presentation.ChoiceConsequences,
            restoredPrompt.Presentation!.ChoiceConsequences);
        Assert.DoesNotContain(restoredPrompt.Data.Keys,
            key => key.StartsWith("__promptNarrative:", StringComparison.Ordinal));
        AssertSafeWaitingViews(restored, controller, source);
    }

    [Fact]
    [Trait("L12Evidence", "prompt-narrative:legacy-entry-card-decisions")]
    public void EntryDecisionsExplainOptionalCostsTargetCountsAndConcreteOutcomes()
    {
        var wuzetianGame = CreateCleanGame(202609278);
        AddMorale(wuzetianGame, 0, 1, "wuzetian");
        var wuzetian = Card("S01-0102", "narrative-wuzetian", 0);
        var firstLocked = Card("S01-0003", "narrative-wuzetian-target-1", 1);
        var secondLocked = Card("S01-0004", "narrative-wuzetian-target-2", 1);
        firstLocked.Tapped = true;
        secondLocked.Tapped = true;
        wuzetianGame.State.Players[1].Field[0][0] = firstLocked;
        wuzetianGame.State.Players[1].Field[1][0] = secondLocked;
        var wuzetianItem = LegacyStackItem("narrative-wuzetian-stack", 0, wuzetian,
            "enter", "武则天");
        wuzetianGame.State.EffectStack.Add(wuzetianItem);

        Invoke(wuzetianGame, "ResolveEnterEffect", wuzetianItem);

        var wuzetianPrompt = Assert.Single(wuzetianGame.State.PendingPrompts);
        Assert.Equal(0, wuzetianPrompt.MinChoose);
        Assert.Equal(2, wuzetianPrompt.MaxChoose);
        AssertPresentation(wuzetianPrompt, "武则天", "返还1张士气", "请选择0至2个合法目标");
        Assert.Contains("不选择任何目标即表示不发动", wuzetianPrompt.Presentation!.Instruction,
            StringComparison.Ordinal);
        Assert.Contains("锁定所选全部目标", wuzetianPrompt.Presentation.ChoiceConsequences[firstLocked.InstanceId],
            StringComparison.Ordinal);
        Assert.Contains("锁定所选全部目标", wuzetianPrompt.Presentation.ChoiceConsequences[secondLocked.InstanceId],
            StringComparison.Ordinal);

        var mulanGame = CreateCleanGame(202609279);
        AddMorale(mulanGame, 0, 1, "mulan");
        var mulan = Card("S01-0108", "narrative-mulan", 0);
        var mulanItem = LegacyStackItem("narrative-mulan-stack", 0, mulan, "enter", "花木兰");
        mulanGame.State.EffectStack.Add(mulanItem);

        Invoke(mulanGame, "ResolveEnterEffect", mulanItem);

        var mulanPrompt = Assert.Single(mulanGame.State.PendingPrompts);
        AssertPresentation(mulanPrompt, "花木兰", "获得冲锋并能在登场回合进攻", "还需完成士气返还");
        Assert.Equal("发动", mulanPrompt.ChoiceLabels["yes"]);
        Assert.Equal("不发动", mulanPrompt.ChoiceLabels["no"]);
        Assert.Contains("返还完成后", mulanPrompt.Presentation!.ChoiceConsequences["yes"], StringComparison.Ordinal);
        Assert.Contains("不返还士气", mulanPrompt.Presentation.ChoiceConsequences["no"], StringComparison.Ordinal);

        var inahimeGame = CreateCleanGame(202609280);
        var inahime = Card("S01-0416", "narrative-inahime", 0);
        var buffTarget = Card("S01-0402", "narrative-inahime-target", 0);
        inahimeGame.State.Players[0].Field[0][0] = inahime;
        inahimeGame.State.Players[0].Field[0][1] = buffTarget;
        var inahimeItem = LegacyStackItem("narrative-inahime-stack", 0, inahime,
            "enter", "稻姬本多小松");
        inahimeGame.State.EffectStack.Add(inahimeItem);

        Invoke(inahimeGame, "ResolveEnterEffect", inahimeItem);

        var inahimePrompt = Assert.Single(inahimeGame.State.PendingPrompts);
        AssertPresentation(inahimePrompt, "稻姬本多小松", "我方前排另一张", "本回合兵力增加1000");
        Assert.Equal(buffTarget.Name, inahimePrompt.ChoiceLabels[buffTarget.InstanceId]);
        Assert.Contains("本回合兵力增加1000",
            inahimePrompt.Presentation!.ChoiceConsequences[buffTarget.InstanceId], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("L12Evidence", "prompt-narrative:optional-and-mandatory-enemy-target")]
    public void EnemyTargetGatewayDistinguishesLubuDeclineFromKusanagiMandatoryKill()
    {
        var lubuGame = CreateCleanGame(202609281);
        AddMorale(lubuGame, 0, 2, "lubu");
        var lubu = Card("S01-0101", "narrative-lubu", 0);
        var disasterTarget = Card("S01-0104", "narrative-lubu-target", 1);
        lubuGame.State.Players[1].Field[0][0] = disasterTarget;
        var lubuItem = LegacyStackItem("narrative-lubu-stack", 0, lubu, "enter", "吕布");
        lubuGame.State.EffectStack.Add(lubuItem);

        Invoke(lubuGame, "ResolveEnterEffect", lubuItem);

        var lubuPrompt = Assert.Single(lubuGame.State.PendingPrompts);
        AssertPresentation(lubuPrompt, "吕布", "返还2张士气", "或选择“不发动”");
        Assert.Contains("skip", lubuPrompt.ValidChoices);
        Assert.Equal("不发动", lubuPrompt.ChoiceLabels["skip"]);
        Assert.Contains("不返还士气", lubuPrompt.Presentation!.ChoiceConsequences["skip"],
            StringComparison.Ordinal);
        Assert.Contains("返还完成后击杀", lubuPrompt.Presentation.ChoiceConsequences[disasterTarget.InstanceId],
            StringComparison.Ordinal);

        var kusanagiGame = CreateCleanGame(202609282);
        var kusanagi = Card("S01-0417", "narrative-kusanagi", 0);
        var lowCostTarget = Card("S01-0004", "narrative-kusanagi-target", 1);
        kusanagiGame.State.Players[1].Field[0][0] = lowCostTarget;
        var kusanagiItem = LegacyStackItem("narrative-kusanagi-stack", 0, kusanagi,
            "enter", "草薙剑");
        kusanagiGame.State.EffectStack.Add(kusanagiItem);

        Invoke(kusanagiGame, "ResolveEnterEffect", kusanagiItem);

        var kusanagiPrompt = Assert.Single(kusanagiGame.State.PendingPrompts);
        AssertPresentation(kusanagiPrompt, "草薙剑", "必须选择并击杀", "这个效果不能跳过");
        Assert.DoesNotContain("skip", kusanagiPrompt.ValidChoices);
        Assert.Contains("确认后击杀", kusanagiPrompt.Presentation!.ChoiceConsequences[lowCostTarget.InstanceId],
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "block")]
    [InlineData(1, "support")]
    [Trait("L12Evidence", "prompt-narrative:landlord-affected-player-private-choice")]
    public void LandlordCoercionExplainsDiscardAndDeclineToTheAffectedPlayerWithoutLeakingTheHand(
        int affectedPlayer, string authorityAction)
    {
        var discardCase = BeginLandlordPrompt(202609283 + affectedPlayer, affectedPlayer, authorityAction,
            "discard");
        var game = discardCase.Game;
        var prompt = discardCase.Prompt;
        var actionLabel = authorityAction == "support" ? "支援" : "抵挡";

        Assert.Equal(affectedPlayer, prompt.PlayerIndex);
        AssertPresentation(prompt, "地主的胁迫", $"本次{actionLabel}额外弃置1张", "放弃抵挡/支援");
        Assert.DoesNotContain(discardCase.Excluded.InstanceId, prompt.ValidChoices);
        Assert.Contains(discardCase.Legal.InstanceId, prompt.ValidChoices);
        Assert.Contains("decline", prompt.ValidChoices);
        Assert.Equal("放弃抵挡/支援", prompt.ChoiceLabels["decline"]);
        Assert.Contains($"本次{actionLabel}继续有效",
            prompt.Presentation!.ChoiceConsequences[discardCase.Legal.InstanceId], StringComparison.Ordinal);
        Assert.Contains($"本次{actionLabel}无效", prompt.Presentation.ChoiceConsequences["decline"],
            StringComparison.Ordinal);
        Assert.DoesNotContain(prompt.Data.Keys,
            key => key.StartsWith("__promptNarrative:", StringComparison.Ordinal));
        AssertPrivatePromptViews(game, affectedPlayer, 1 - affectedPlayer,
            discardCase.Excluded, discardCase.Legal);
        AssertLandlordWaitingViews(game, affectedPlayer, 1 - affectedPlayer);

        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), random,
            game.CardFactSignalSequence, game.AutoPassEmptyResponses,
            game.ConcealHiddenResponseAvailability);
        var restoredPrompt = Assert.Single(restored.State.PendingPrompts);
        AssertPresentation(restoredPrompt, "地主的胁迫", $"本次{actionLabel}额外弃置1张",
            "放弃抵挡/支援");
        Assert.DoesNotContain(restoredPrompt.Data.Keys,
            key => key.StartsWith("__promptNarrative:", StringComparison.Ordinal));
        AssertPrivatePromptViews(restored, affectedPlayer, 1 - affectedPlayer,
            discardCase.Excluded, discardCase.Legal);
        AssertLandlordWaitingViews(restored, affectedPlayer, 1 - affectedPlayer);

        Assert.True(Assert.IsType<bool>(Invoke(game, "ContinueS2CounterEffect", discardCase.Item,
            prompt, new List<string> { discardCase.Legal.InstanceId })));
        Assert.Contains(game.State.Players[affectedPlayer].Graveyard,
            card => card.InstanceId == discardCase.Legal.InstanceId);
        Assert.NotEqual("true", discardCase.Authority.Data.GetValueOrDefault("invalid"));

        var declineCase = BeginLandlordPrompt(202609285 + affectedPlayer, affectedPlayer, authorityAction,
            "decline");
        Assert.True(Assert.IsType<bool>(Invoke(declineCase.Game, "ContinueS2CounterEffect", declineCase.Item,
            declineCase.Prompt, new List<string> { "decline" })));
        Assert.Equal("true", declineCase.Authority.Data["invalid"]);
        Assert.Contains(declineCase.Legal, declineCase.Game.State.Players[affectedPlayer].Hand);
    }

    [Fact]
    [Trait("L12Evidence", "prompt-narrative:poison-forced-private-discard")]
    public void PoisonDiscardExplainsTheMandatoryEffectWithoutOfferingADeclineBranch()
    {
        var game = CreateCleanGame(202609287);
        const int affectedPlayer = 0;
        var first = Card("S01-0003", "narrative-poison-hand-1", affectedPlayer);
        var second = Card("S01-0004", "narrative-poison-hand-2", affectedPlayer);
        game.State.Players[affectedPlayer].Hand.AddRange([first, second]);
        var poison = Card("S02-0018", "narrative-poison", 1);
        var item = LegacyStackItem("narrative-poison-stack", 1, poison,
            "s2-reaction", "poison-discard");
        item.Data["affectedPlayer"] = affectedPlayer.ToString();
        game.State.EffectStack.Add(item);

        Invoke(game, "ResolveS2CounterEffect", item);

        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(affectedPlayer, prompt.PlayerIndex);
        AssertPresentation(prompt, "毒药发作", "强制弃牌效果正在结算", "本次没有拒绝选项");
        Assert.Equal(new[] { first.InstanceId, second.InstanceId }.Order(), prompt.ValidChoices.Order());
        Assert.DoesNotContain("decline", prompt.ValidChoices);
        Assert.DoesNotContain("skip", prompt.ValidChoices);
        Assert.Contains("完成〈毒药发作〉的强制弃牌",
            prompt.Presentation!.ChoiceConsequences[first.InstanceId], StringComparison.Ordinal);
        AssertPrivatePromptViews(game, affectedPlayer, 1, first, second);

        Assert.True(Assert.IsType<bool>(Invoke(game, "ContinueS2CounterEffect", item, prompt,
            new List<string> { first.InstanceId })));
        Assert.Contains(first, game.State.Players[affectedPlayer].Graveyard);
        Assert.Contains(second, game.State.Players[affectedPlayer].Hand);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(1, 0)]
    [Trait("L12Evidence", "prompt-narrative:court-magician-hidden-counter-boundary")]
    public void CourtMagicianUsesPositionOnlyForAnOpposingCoveredCounterAndKeepsOwnIdentityVisible(
        int controller, int targetOwner)
    {
        var magicianCase = BeginCourtMagicianPrompt(202609289 + controller * 10 + targetOwner,
            controller, targetOwner, $"{controller}-{targetOwner}");
        var game = magicianCase.Game;
        var prompt = magicianCase.Prompt;
        var target = magicianCase.Target;

        Assert.Equal(controller, prompt.PlayerIndex);
        AssertPresentation(prompt, "宫廷魔术师", "登场时效果正在结算", "选择“不发动”");
        Assert.Equal($"{game.State.Players[controller].Name} 正在选择效果对象",
            prompt.Presentation!.WaitingSummary);
        Assert.Contains(target.InstanceId, prompt.ValidChoices);
        Assert.Contains("skip", prompt.ValidChoices);
        Assert.Equal("不发动", prompt.ChoiceLabels["skip"]);
        Assert.Equal("将所选反击战术置入其所有者墓地。",
            prompt.Presentation.ChoiceConsequences[target.InstanceId]);
        Assert.Contains("结束〈宫廷魔术师〉的登场时效果",
            prompt.Presentation.ChoiceConsequences["skip"], StringComparison.Ordinal);
        Assert.DoesNotContain(prompt.Data.Keys,
            key => key.StartsWith("__promptNarrative:", StringComparison.Ordinal));

        if (targetOwner == controller)
        {
            Assert.Equal(target.Name, prompt.ChoiceLabels[target.InstanceId]);
            Assert.Equal(target.Name, prompt.Data[target.InstanceId]);
        }
        else
        {
            Assert.Equal("对方后排第2格", prompt.ChoiceLabels[target.InstanceId]);
            Assert.DoesNotContain(prompt.Data.Keys,
                key => key.Equals(target.InstanceId, StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith($"{target.InstanceId}:", StringComparison.OrdinalIgnoreCase));
            var ownerDetail = JsonSerializer.Serialize(new
            {
                prompt.ChoiceLabels,
                prompt.Data,
                prompt.Presentation,
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            foreach (var secret in new[] { target.Name, target.CardId, target.ImageUrl, target.EffectText }
                         .Where(value => !string.IsNullOrWhiteSpace(value)))
                Assert.DoesNotContain(secret!, ownerDetail, StringComparison.Ordinal);
        }

        AssertPromptBoundaryAndCheckpoint(game, controller,
            $"{game.State.Players[controller].Name} 正在选择效果对象");

        game.State.PendingPrompts.Remove(prompt);
        Invoke(game, "ContinueS2UniversalEffect", magicianCase.Item, prompt,
            new List<string> { target.InstanceId });
        Assert.Contains(game.State.Players[targetOwner].Graveyard,
            card => card.InstanceId == target.InstanceId);
        Assert.DoesNotContain(game.State.Players[targetOwner].Field[1],
            card => card?.InstanceId == target.InstanceId);

        if (controller == 0 && targetOwner == 0)
        {
            var skipCase = BeginCourtMagicianPrompt(202609299, 0, 1, "skip");
            skipCase.Game.State.PendingPrompts.Remove(skipCase.Prompt);
            Invoke(skipCase.Game, "ContinueS2UniversalEffect", skipCase.Item, skipCase.Prompt,
                new List<string> { "skip" });
            Assert.Contains(skipCase.Game.State.Players[1].Field[1],
                card => card?.InstanceId == skipCase.Target.InstanceId);
            Assert.DoesNotContain(skipCase.Game.State.Players[1].Graveyard,
                card => card.InstanceId == skipCase.Target.InstanceId);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("L12Evidence", "prompt-narrative:ring-decision-cost-search-chain")]
    public void RingExplainsTheDecisionRealDiscardCostAndPrivateSearchAcrossEveryView(int controller)
    {
        var ringCase = BeginRingPrompt(202609301 + controller, controller, "use");
        var game = ringCase.Game;
        var start = ringCase.Prompt;
        var playerName = game.State.Players[controller].Name;

        AssertPresentation(start, "万物统御之戒", "弃置1张手牌作为费用", "选择“发动”");
        Assert.Equal("发动", start.ChoiceLabels["yes"]);
        Assert.Equal("不发动", start.ChoiceLabels["no"]);
        Assert.Contains("继续选择并弃置1张手牌作为费用",
            start.Presentation!.ChoiceConsequences["yes"], StringComparison.Ordinal);
        Assert.Contains("不支付费用", start.Presentation.ChoiceConsequences["no"],
            StringComparison.Ordinal);
        AssertPromptBoundaryAndCheckpoint(game, controller, $"{playerName} 正在决定是否发动效果",
            ringCase.FirstHand, ringCase.SecondHand, ringCase.FirstLibrary, ringCase.SecondLibrary);

        game.State.PendingPrompts.Remove(start);
        Invoke(game, "ContinueS2UniversalEffect", ringCase.Item, start, new List<string> { "yes" });

        var discard = Assert.Single(game.State.PendingPrompts);
        AssertPresentation(discard, "万物统御之戒", "弃置1张手牌支付费用", "本步骤不能取消");
        Assert.Equal(new[] { ringCase.FirstHand.InstanceId, ringCase.SecondHand.InstanceId }.Order(),
            discard.ValidChoices.Order());
        Assert.DoesNotContain("skip", discard.ValidChoices);
        Assert.DoesNotContain("no", discard.ValidChoices);
        Assert.Contains("作为费用，然后进入【通用】卡牌检索",
            discard.Presentation!.ChoiceConsequences[ringCase.FirstHand.InstanceId], StringComparison.Ordinal);
        AssertPromptBoundaryAndCheckpoint(game, controller, $"{playerName} 正在支付费用",
            ringCase.FirstHand, ringCase.SecondHand);

        game.State.PendingPrompts.Remove(discard);
        Invoke(game, "ContinueS2UniversalEffect", ringCase.Item, discard,
            new List<string> { ringCase.FirstHand.InstanceId });
        Assert.Contains(ringCase.FirstHand, game.State.Players[controller].Graveyard);

        var search = Assert.Single(game.State.PendingPrompts);
        AssertPresentation(search, "万物统御之戒", "费用已经支付", "本步骤不能取消");
        Assert.Equal(new[] { ringCase.FirstLibrary.InstanceId, ringCase.SecondLibrary.InstanceId }.Order(),
            search.ValidChoices.Order());
        Assert.DoesNotContain("skip", search.ValidChoices);
        Assert.DoesNotContain("no", search.ValidChoices);
        Assert.Contains("展示", search.Presentation!.ChoiceConsequences[ringCase.FirstLibrary.InstanceId],
            StringComparison.Ordinal);
        Assert.Contains("加入手牌，然后洗牌",
            search.Presentation.ChoiceConsequences[ringCase.FirstLibrary.InstanceId], StringComparison.Ordinal);
        AssertPromptBoundaryAndCheckpoint(game, controller, $"{playerName} 正在完成卡牌选择",
            ringCase.FirstLibrary, ringCase.SecondLibrary);

        game.State.PendingPrompts.Remove(search);
        Invoke(game, "ContinueS2UniversalEffect", ringCase.Item, search,
            new List<string> { ringCase.FirstLibrary.InstanceId });
        Assert.Contains(ringCase.FirstLibrary, game.State.Players[controller].Hand);
        Assert.DoesNotContain(ringCase.FirstLibrary, game.State.Players[controller].Library);

        var declineCase = BeginRingPrompt(202609305 + controller, controller, "decline");
        var handBefore = declineCase.Game.State.Players[controller].Hand.Select(card => card.InstanceId).Order().ToArray();
        declineCase.Game.State.PendingPrompts.Remove(declineCase.Prompt);
        Invoke(declineCase.Game, "ContinueS2UniversalEffect", declineCase.Item, declineCase.Prompt,
            new List<string> { "no" });
        Assert.Empty(declineCase.Game.State.PendingPrompts);
        Assert.Equal(handBefore,
            declineCase.Game.State.Players[controller].Hand.Select(card => card.InstanceId).Order());
        Assert.DoesNotContain(declineCase.Game.State.Players[controller].Graveyard,
            card => handBefore.Contains(card.InstanceId, StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "prompt-narrative:arthur-rune-decision-limit-one")]
    public void ArthurExplainsRuneConsumptionAndTheExistingSwordNoOp(int controller, bool existingSword)
    {
        var arthurCase = BeginArthurPrompt(202609307 + controller * 10 + (existingSword ? 1 : 0),
            controller, existingSword, "use");
        var game = arthurCase.Game;
        var prompt = arthurCase.Prompt;
        var playerName = game.State.Players[controller].Name;

        AssertPresentation(prompt, "亚瑟王", existingSword ? "场上同时只能存在1张" : "消耗1符文",
            "选择“发动”");
        Assert.Equal("发动", prompt.ChoiceLabels["yes"]);
        Assert.Equal("不发动", prompt.ChoiceLabels["no"]);
        Assert.Contains("消耗1符文", prompt.Presentation!.ChoiceConsequences["yes"],
            StringComparison.Ordinal);
        Assert.Contains(existingSword ? "保持原位" : "叠放至本次登场",
            prompt.Presentation.ChoiceConsequences["yes"], StringComparison.Ordinal);
        Assert.Contains("不消耗符文", prompt.Presentation.ChoiceConsequences["no"],
            StringComparison.Ordinal);
        AssertPromptBoundaryAndCheckpoint(game, controller, $"{playerName} 正在决定是否发动效果");

        game.State.PendingPrompts.Remove(prompt);
        ContinueS2Faction(game, arthurCase.Item, prompt, "yes");
        Assert.Equal(0, game.State.Players[controller].SpecialZones.Runes);
        if (existingSword)
        {
            Assert.Empty(arthurCase.Arthur.AttachedCards);
            Assert.NotNull(arthurCase.ExistingOwner);
            Assert.Single(arthurCase.ExistingOwner!.AttachedCards,
                card => card.CardId == "S02-06S2");
        }
        else
            Assert.Single(arthurCase.Arthur.AttachedCards, card => card.CardId == "S02-06S2");

        var declineCase = BeginArthurPrompt(202609317 + controller * 10 + (existingSword ? 1 : 0),
            controller, existingSword, "decline");
        declineCase.Game.State.PendingPrompts.Remove(declineCase.Prompt);
        ContinueS2Faction(declineCase.Game, declineCase.Item, declineCase.Prompt, "no");
        Assert.Equal(1, declineCase.Game.State.Players[controller].SpecialZones.Runes);
        Assert.Empty(declineCase.Arthur.AttachedCards);
        if (existingSword)
            Assert.Single(declineCase.ExistingOwner!.AttachedCards, card => card.CardId == "S02-06S2");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("L12Evidence", "prompt-narrative:promoted-heracles-nonlethal-decision")]
    public void PromotedHeraclesExplainsThatDamageToBothMastersCannotReduceHealthBelowOne(int controller)
    {
        var damageCase = BeginPromotedHeraclesPrompt(202609327 + controller, controller, "use");
        var game = damageCase.Game;
        var prompt = damageCase.Prompt;
        game.State.Players[0].Hp = 1;
        game.State.Players[1].Hp = 4;

        AssertPresentation(prompt, "赫拉克勒斯·晋升", "非致命伤害不会令主宰的生命降至1以下",
            "选择是否发动");
        Assert.Equal("发动", prompt.ChoiceLabels["yes"]);
        Assert.Equal("不发动", prompt.ChoiceLabels["no"]);
        Assert.Contains("生命最低保留为1", prompt.Presentation!.ChoiceConsequences["yes"],
            StringComparison.Ordinal);
        Assert.Contains("双方主宰的生命不变", prompt.Presentation.ChoiceConsequences["no"],
            StringComparison.Ordinal);
        AssertPromptBoundaryAndCheckpoint(game, controller,
            $"{game.State.Players[controller].Name} 正在决定是否发动效果");

        game.State.PendingPrompts.Remove(prompt);
        ContinueS2Faction(game, damageCase.Item, prompt, "yes");
        Assert.Equal(1, game.State.Players[0].Hp);
        Assert.Equal(3, game.State.Players[1].Hp);

        var declineCase = BeginPromotedHeraclesPrompt(202609329 + controller, controller, "decline");
        declineCase.Game.State.Players[0].Hp = 3;
        declineCase.Game.State.Players[1].Hp = 4;
        declineCase.Game.State.PendingPrompts.Remove(declineCase.Prompt);
        ContinueS2Faction(declineCase.Game, declineCase.Item, declineCase.Prompt, "no");
        Assert.Equal(3, declineCase.Game.State.Players[0].Hp);
        Assert.Equal(4, declineCase.Game.State.Players[1].Hp);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("L12Evidence", "prompt-narrative:heracles-draw-then-effect-discard")]
    public void HeraclesKeepsThePostDrawDiscardAsMandatoryEffectResolutionInsteadOfCost(int controller)
    {
        var heraclesCase = BeginHeraclesPrompt(202609331 + controller, controller, "use");
        var game = heraclesCase.Game;
        var start = heraclesCase.Prompt;
        var playerName = game.State.Players[controller].Name;

        AssertPresentation(start, "赫拉克勒斯", "弃牌属于效果结算，不是发动费用", "选择是否发动");
        Assert.Equal("发动", start.ChoiceLabels["yes"]);
        Assert.Equal("不发动", start.ChoiceLabels["no"]);
        Assert.Contains("抽取2张牌，然后必须弃置1张",
            start.Presentation!.ChoiceConsequences["yes"], StringComparison.Ordinal);
        Assert.Contains("不抽牌也不弃牌", start.Presentation.ChoiceConsequences["no"],
            StringComparison.Ordinal);
        AssertPromptBoundaryAndCheckpoint(game, controller, $"{playerName} 正在决定是否发动效果",
            heraclesCase.ExistingHand, heraclesCase.DrawnA, heraclesCase.DrawnB);

        game.State.PendingPrompts.Remove(start);
        ContinueS2Faction(game, heraclesCase.Item, start, "yes");
        var discard = Assert.Single(game.State.PendingPrompts);
        AssertPresentation(discard, "赫拉克勒斯", "已经抽取2张牌", "不能拒绝或跳过");
        Assert.Equal(new[]
        {
            heraclesCase.ExistingHand.InstanceId,
            heraclesCase.DrawnA.InstanceId,
            heraclesCase.DrawnB.InstanceId,
        }.Order(), discard.ValidChoices.Order());
        Assert.DoesNotContain("skip", discard.ValidChoices);
        Assert.DoesNotContain("no", discard.ValidChoices);
        Assert.Contains("完成〈赫拉克勒斯〉的登场时效果",
            discard.Presentation!.ChoiceConsequences[heraclesCase.DrawnB.InstanceId],
            StringComparison.Ordinal);
        var discardDetail = JsonSerializer.Serialize(new
        {
            discard.Presentation,
            discard.ChoiceLabels,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("支付费用", discardDetail, StringComparison.Ordinal);
        AssertPromptBoundaryAndCheckpoint(game, controller, $"{playerName} 正在完成卡牌选择",
            heraclesCase.ExistingHand, heraclesCase.DrawnA, heraclesCase.DrawnB);

        game.State.PendingPrompts.Remove(discard);
        ContinueS2Faction(game, heraclesCase.Item, discard, heraclesCase.DrawnB.InstanceId);
        Assert.Contains(heraclesCase.DrawnB, game.State.Players[controller].Graveyard);
        Assert.Contains(heraclesCase.ExistingHand, game.State.Players[controller].Hand);
        Assert.Contains(heraclesCase.DrawnA, game.State.Players[controller].Hand);

        var declineCase = BeginHeraclesPrompt(202609335 + controller, controller, "decline");
        var handBefore = declineCase.Game.State.Players[controller].Hand.Select(card => card.InstanceId).ToArray();
        var libraryBefore = declineCase.Game.State.Players[controller].Library.Select(card => card.InstanceId).ToArray();
        declineCase.Game.State.PendingPrompts.Remove(declineCase.Prompt);
        ContinueS2Faction(declineCase.Game, declineCase.Item, declineCase.Prompt, "no");
        Assert.Empty(declineCase.Game.State.PendingPrompts);
        Assert.Equal(handBefore,
            declineCase.Game.State.Players[controller].Hand.Select(card => card.InstanceId).ToArray());
        Assert.Equal(libraryBefore,
            declineCase.Game.State.Players[controller].Library.Select(card => card.InstanceId).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("L12Evidence", "prompt-narrative:joan-optional-discard-cost")]
    public void JoanExplainsThatSelectingAHandCardPaysForMasterProtection(int controller)
    {
        var joanCase = BeginJoanPrompt(202609337 + controller, controller, "use");
        var game = joanCase.Game;
        var prompt = joanCase.Prompt;
        var playerName = game.State.Players[controller].Name;

        AssertPresentation(prompt, "圣女贞德", "弃置1张手牌支付费用", "选择“不发动”");
        Assert.Equal(new[]
        {
            joanCase.FirstHand.InstanceId,
            joanCase.SecondHand.InstanceId,
            "skip",
        }.Order(), prompt.ValidChoices.Order());
        Assert.Equal("不发动", prompt.ChoiceLabels["skip"]);
        Assert.Contains("无法被进攻", prompt.Presentation!.ChoiceConsequences[joanCase.FirstHand.InstanceId],
            StringComparison.Ordinal);
        Assert.Contains("不获得", prompt.Presentation.ChoiceConsequences["skip"], StringComparison.Ordinal);
        AssertPromptBoundaryAndCheckpoint(game, controller, $"{playerName} 正在支付费用",
            joanCase.FirstHand, joanCase.SecondHand);

        game.State.PendingPrompts.Remove(prompt);
        ContinueS2Faction(game, joanCase.Item, prompt, joanCase.FirstHand.InstanceId);
        Assert.Contains(joanCase.FirstHand, game.State.Players[controller].Graveyard);
        Assert.Equal(int.MaxValue, game.State.Players[controller].MasterCannotBeAttackedUntilTurn);
        Assert.Equal(controller,
            game.State.Players[controller].MasterCannotBeAttackedExpiresAtPlayerTurnStart);

        var declineCase = BeginJoanPrompt(202609339 + controller, controller, "decline");
        declineCase.Game.State.PendingPrompts.Remove(declineCase.Prompt);
        ContinueS2Faction(declineCase.Game, declineCase.Item, declineCase.Prompt, "skip");
        Assert.Contains(declineCase.FirstHand, declineCase.Game.State.Players[controller].Hand);
        Assert.Empty(declineCase.Game.State.Players[controller].Graveyard);
        Assert.Equal(-1, declineCase.Game.State.Players[controller].MasterCannotBeAttackedUntilTurn);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("L12Evidence", "prompt-narrative:perseus-optional-discard-cost")]
    public void PerseusExplainsTheDiscardCostAndPublicPromotionRecovery(int controller)
    {
        var perseusCase = BeginPerseusPrompt(202609341 + controller, controller, "use");
        var game = perseusCase.Game;
        var prompt = perseusCase.Prompt;
        var playerName = game.State.Players[controller].Name;

        AssertPresentation(prompt, "珀尔修斯", "弃置1张手牌支付费用", "手牌和墓地都不会改变");
        Assert.Contains(perseusCase.FirstHand.InstanceId, prompt.ValidChoices);
        Assert.Contains(perseusCase.SecondHand.InstanceId, prompt.ValidChoices);
        Assert.Contains("skip", prompt.ValidChoices);
        Assert.Contains("〈珀尔修斯·晋升〉加入手牌",
            prompt.Presentation!.ChoiceConsequences[perseusCase.FirstHand.InstanceId],
            StringComparison.Ordinal);
        Assert.Contains("不支付", prompt.Presentation.ChoiceConsequences["skip"], StringComparison.Ordinal);
        AssertPromptBoundaryAndCheckpoint(game, controller, $"{playerName} 正在支付费用",
            perseusCase.FirstHand, perseusCase.SecondHand);

        game.State.PendingPrompts.Remove(prompt);
        ContinueS2Faction(game, perseusCase.Item, prompt, perseusCase.FirstHand.InstanceId);
        Assert.Contains(perseusCase.FirstHand, game.State.Players[controller].Graveyard);
        Assert.Contains(perseusCase.Promotion, game.State.Players[controller].Hand);
        Assert.DoesNotContain(perseusCase.Promotion, game.State.Players[controller].Graveyard);

        var declineCase = BeginPerseusPrompt(202609343 + controller, controller, "decline");
        declineCase.Game.State.PendingPrompts.Remove(declineCase.Prompt);
        ContinueS2Faction(declineCase.Game, declineCase.Item, declineCase.Prompt, "skip");
        Assert.Contains(declineCase.FirstHand, declineCase.Game.State.Players[controller].Hand);
        Assert.Contains(declineCase.Promotion, declineCase.Game.State.Players[controller].Graveyard);
        Assert.DoesNotContain(declineCase.Promotion, declineCase.Game.State.Players[controller].Hand);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("L12Evidence", "prompt-narrative:iio-cost-then-declared-target")]
    public void IioNaotoraSeparatesTheDiscardCostFromTheDeclaredReadyTarget(int controller)
    {
        var iioCase = BeginIioPrompt(202609345 + controller, controller, "use");
        var game = iioCase.Game;
        var payment = iioCase.Prompt;
        var playerName = game.State.Players[controller].Name;

        AssertPresentation(payment, "井伊直虎", "必须先弃置1张手牌支付费用", "不能取消或跳过");
        Assert.Equal(new[] { iioCase.FirstHand.InstanceId, iioCase.SecondHand.InstanceId }.Order(),
            payment.ValidChoices.Order());
        Assert.DoesNotContain("skip", payment.ValidChoices);
        Assert.Contains("进入休整【高天原】军团的对象选择",
            payment.Presentation!.ChoiceConsequences[iioCase.FirstHand.InstanceId],
            StringComparison.Ordinal);
        AssertPromptBoundaryAndCheckpoint(game, controller, $"{playerName} 正在支付费用",
            iioCase.FirstHand, iioCase.SecondHand);

        game.State.PendingPrompts.Remove(payment);
        ContinueS2Faction(game, iioCase.Item, payment, iioCase.FirstHand.InstanceId);
        Assert.Contains(iioCase.FirstHand, game.State.Players[controller].Graveyard);

        var target = Assert.Single(game.State.PendingPrompts);
        AssertPresentation(target, "井伊直虎", "费用已经支付", "仍然合法的休整【高天原】军团");
        Assert.Equal(new[] { iioCase.FirstTarget.InstanceId, iioCase.SecondTarget.InstanceId }.Order(),
            target.ValidChoices.Order());
        Assert.Contains("转为活跃", target.Presentation!.ChoiceConsequences[iioCase.FirstTarget.InstanceId],
            StringComparison.Ordinal);
        var targetDetail = JsonSerializer.Serialize(new
        {
            target.Presentation,
            target.ChoiceLabels,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("支付费用", targetDetail, StringComparison.Ordinal);
        AssertPromptBoundaryAndCheckpoint(game, controller, $"{playerName} 正在选择效果对象");

        game.State.PendingPrompts.Remove(target);
        ContinueS2Faction(game, iioCase.Item, target, iioCase.FirstTarget.InstanceId);
        Invoke(game, "ResolveTopStack");
        Assert.False(iioCase.FirstTarget.Tapped);
        Assert.True(iioCase.SecondTarget.Tapped);

        var staleCase = BeginIioPrompt(202609347 + controller, controller, "stale");
        staleCase.Game.State.PendingPrompts.Remove(staleCase.Prompt);
        ContinueS2Faction(staleCase.Game, staleCase.Item, staleCase.Prompt,
            staleCase.FirstHand.InstanceId);
        var staleTargetPrompt = Assert.Single(staleCase.Game.State.PendingPrompts);
        staleCase.Game.State.Players[controller].Field[0][1] = null;
        staleCase.Game.State.PendingPrompts.Remove(staleTargetPrompt);
        ContinueS2Faction(staleCase.Game, staleCase.Item, staleTargetPrompt,
            staleCase.FirstTarget.InstanceId);
        Assert.Contains(staleCase.FirstHand, staleCase.Game.State.Players[controller].Graveyard);
        Assert.True(staleCase.SecondTarget.Tapped);
        Assert.Empty(staleCase.Game.State.PendingPrompts);
        Assert.Contains(staleCase.Game.State.Events, entry => entry.Type == "effect-failed");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("L12Evidence", "prompt-narrative:imhotep-optional-grave-recovery")]
    public void ImhotepExplainsTheSatisfiedHandConditionAndPublicGraveRecovery(int controller)
    {
        var imhotepCase = BeginImhotepPrompt(202609349 + controller, controller, "use");
        var game = imhotepCase.Game;
        var prompt = imhotepCase.Prompt;
        var playerName = game.State.Players[controller].Name;

        AssertPresentation(prompt, "伊姆何泰普", "我方手牌数量少于对方", "选择“不发动”");
        Assert.Equal(new[]
        {
            imhotepCase.FirstTarget.InstanceId,
            imhotepCase.SecondTarget.InstanceId,
            "skip",
        }.Order(), prompt.ValidChoices.Order());
        Assert.Equal("不发动", prompt.ChoiceLabels["skip"]);
        Assert.Contains("公开墓地", prompt.Presentation!.ChoiceConsequences[imhotepCase.FirstTarget.InstanceId],
            StringComparison.Ordinal);
        Assert.Contains("墓地和手牌都不改变", prompt.Presentation.ChoiceConsequences["skip"],
            StringComparison.Ordinal);
        AssertPromptBoundaryAndCheckpoint(game, controller, $"{playerName} 正在完成卡牌选择");

        game.State.PendingPrompts.Remove(prompt);
        ContinueS2Faction(game, imhotepCase.Item, prompt, imhotepCase.FirstTarget.InstanceId);
        Assert.Contains(imhotepCase.FirstTarget, game.State.Players[controller].Hand);
        Assert.DoesNotContain(imhotepCase.FirstTarget, game.State.Players[controller].Graveyard);
        Assert.Contains(imhotepCase.SecondTarget, game.State.Players[controller].Graveyard);

        var declineCase = BeginImhotepPrompt(202609351 + controller, controller, "decline");
        declineCase.Game.State.PendingPrompts.Remove(declineCase.Prompt);
        ContinueS2Faction(declineCase.Game, declineCase.Item, declineCase.Prompt, "skip");
        Assert.Contains(declineCase.FirstTarget, declineCase.Game.State.Players[controller].Graveyard);
        Assert.Contains(declineCase.SecondTarget, declineCase.Game.State.Players[controller].Graveyard);
        Assert.DoesNotContain(declineCase.FirstTarget, declineCase.Game.State.Players[controller].Hand);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("L12Evidence", "prompt-narrative:takeda-search-route-parity")]
    public void TakedaSearchUsesOneNarrativeAcrossLegacyAndPublicTriggerRoutes(int controller)
    {
        var legacyCase = BeginTakedaSearchPrompt(202609353 + controller, controller, publicTrigger: false,
            "legacy");
        var publicCase = BeginTakedaSearchPrompt(202609355 + controller, controller, publicTrigger: true,
            "public");
        var playerName = legacyCase.Game.State.Players[controller].Name;

        foreach (var prompt in new[] { legacyCase.Prompt, publicCase.Prompt })
        {
            AssertPresentation(prompt, "武田信玄", "无论是否选择，随后都会重洗牌库",
                "这一步不会跳过后续部分");
            Assert.Equal("不加入手牌", prompt.ChoiceLabels["skip"]);
            Assert.Contains("继续结算后续部分",
                prompt.Presentation!.ChoiceConsequences[legacyCase.FirstTarget.InstanceId],
                StringComparison.Ordinal);
            Assert.Contains("仍会重洗牌库", prompt.Presentation.ChoiceConsequences["skip"],
                StringComparison.Ordinal);
        }
        Assert.Equal(legacyCase.Prompt.Presentation!.Situation, publicCase.Prompt.Presentation!.Situation);
        Assert.Equal(legacyCase.Prompt.Presentation.Instruction, publicCase.Prompt.Presentation.Instruction);
        Assert.Equal(legacyCase.Prompt.Presentation.ChoiceConsequences,
            publicCase.Prompt.Presentation.ChoiceConsequences);
        AssertPromptBoundaryAndCheckpoint(legacyCase.Game, controller,
            $"{playerName} 正在完成卡牌选择", legacyCase.FirstTarget, legacyCase.SecondTarget);
        AssertPromptBoundaryAndCheckpoint(publicCase.Game, controller,
            $"{playerName} 正在完成卡牌选择", publicCase.FirstTarget, publicCase.SecondTarget);

        legacyCase.Game.State.PendingPrompts.Remove(legacyCase.Prompt);
        ContinueS2Faction(legacyCase.Game, legacyCase.Item, legacyCase.Prompt,
            legacyCase.FirstTarget.InstanceId);
        Assert.Contains(legacyCase.FirstTarget, legacyCase.Game.State.Players[controller].Hand);
        Assert.Contains(legacyCase.Game.State.Events, entry => entry.Type == "shuffle"
            && entry.PlayerIndex == controller && entry.Text.Contains("武田信玄", StringComparison.Ordinal));

        var skipCase = BeginTakedaSearchPrompt(202609357 + controller, controller, publicTrigger: true,
            "skip");
        skipCase.Game.State.PendingPrompts.Remove(skipCase.Prompt);
        ContinueS2Faction(skipCase.Game, skipCase.Item, skipCase.Prompt, "skip");
        Assert.DoesNotContain(skipCase.FirstTarget, skipCase.Game.State.Players[controller].Hand);
        Assert.Contains(skipCase.Game.State.Events, entry => entry.Type == "shuffle"
            && entry.PlayerIndex == controller);

        var staleCase = BeginTakedaSearchPrompt(202609359 + controller, controller, publicTrigger: true,
            "stale");
        staleCase.Game.State.Players[controller].Library.Remove(staleCase.FirstTarget);
        staleCase.Game.State.Players[controller].Graveyard.Add(staleCase.FirstTarget);
        staleCase.Game.State.PendingPrompts.Remove(staleCase.Prompt);
        ContinueS2Faction(staleCase.Game, staleCase.Item, staleCase.Prompt,
            staleCase.FirstTarget.InstanceId);
        Assert.Contains(staleCase.Game.State.Events, entry => entry.Type == "effect-failed");
        Assert.Contains(staleCase.Game.State.Events, entry => entry.Type == "shuffle");
        Assert.DoesNotContain(staleCase.SecondTarget, staleCase.Game.State.Players[controller].Hand);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("L12Evidence", "prompt-narrative:promoted-heracles-paid-cost-target")]
    public void PromotedHeraclesExplainsTheIrreversibleTopDeckCostAndTargetBoundary(int controller)
    {
        var heraclesCase = BeginHeraclesPromotionPrompt(202609361 + controller, controller, "success");
        var game = heraclesCase.Game;
        var payment = heraclesCase.Prompt;
        var playerName = game.State.Players[controller].Name;

        AssertPresentation(payment, "赫拉克勒斯·晋升", "支付本次效果的费用", "选择“不发动”");
        Assert.Equal("不发动", payment.ChoiceLabels["skip"]);
        Assert.Contains($"费用不高于{heraclesCase.ShownCard.CurrentCost}",
            payment.Presentation!.ChoiceConsequences[heraclesCase.ShownCard.InstanceId],
            StringComparison.Ordinal);
        Assert.Contains("不支付", payment.Presentation.ChoiceConsequences["skip"],
            StringComparison.Ordinal);
        AssertPromptBoundaryAndCheckpoint(game, controller, $"{playerName} 正在支付费用",
            heraclesCase.ShownCard, heraclesCase.OtherHand);

        game.State.PendingPrompts.Remove(payment);
        ContinueS2Faction(game, heraclesCase.Item, payment, heraclesCase.ShownCard.InstanceId);
        Assert.Same(heraclesCase.ShownCard, game.State.Players[controller].Library[0]);
        Assert.DoesNotContain(heraclesCase.ShownCard, game.State.Players[controller].Hand);

        var target = Assert.Single(game.State.PendingPrompts);
        AssertPresentation(target, "赫拉克勒斯·晋升", "费用已经支付", "请选择1张当前费用不高于");
        Assert.Contains("费用不返还且不改选",
            target.Presentation!.ChoiceConsequences[heraclesCase.FirstTarget.InstanceId],
            StringComparison.Ordinal);
        AssertPromptBoundaryAndCheckpoint(game, controller, $"{playerName} 正在选择效果对象");

        game.State.PendingPrompts.Remove(target);
        ContinueS2Faction(game, heraclesCase.Item, target, heraclesCase.FirstTarget.InstanceId);
        Assert.Contains(heraclesCase.FirstTarget, game.State.Players[1 - controller].Graveyard);
        Assert.Same(heraclesCase.ShownCard, game.State.Players[controller].Library[0]);

        var declineCase = BeginHeraclesPromotionPrompt(202609363 + controller, controller, "decline");
        declineCase.Game.State.PendingPrompts.Remove(declineCase.Prompt);
        ContinueS2Faction(declineCase.Game, declineCase.Item, declineCase.Prompt, "skip");
        Assert.Contains(declineCase.ShownCard, declineCase.Game.State.Players[controller].Hand);
        Assert.Empty(declineCase.Game.State.Players[controller].Library);

        var noTargetCase = BeginHeraclesPromotionPrompt(202609365 + controller, controller, "no-target");
        noTargetCase.Game.State.Players[1 - controller].Field[0][0] = null;
        noTargetCase.Game.State.Players[1 - controller].Field[0][1] = null;
        noTargetCase.Game.State.PendingPrompts.Remove(noTargetCase.Prompt);
        ContinueS2Faction(noTargetCase.Game, noTargetCase.Item, noTargetCase.Prompt,
            noTargetCase.ShownCard.InstanceId);
        Assert.Empty(noTargetCase.Game.State.PendingPrompts);
        Assert.Same(noTargetCase.ShownCard, noTargetCase.Game.State.Players[controller].Library[0]);

        var staleCase = BeginHeraclesPromotionPrompt(202609367 + controller, controller, "stale");
        staleCase.Game.State.PendingPrompts.Remove(staleCase.Prompt);
        ContinueS2Faction(staleCase.Game, staleCase.Item, staleCase.Prompt,
            staleCase.ShownCard.InstanceId);
        var staleTargetPrompt = Assert.Single(staleCase.Game.State.PendingPrompts);
        staleCase.Game.State.Players[1 - controller].Field[0][0] = null;
        staleCase.Game.State.PendingPrompts.Remove(staleTargetPrompt);
        ContinueS2Faction(staleCase.Game, staleCase.Item, staleTargetPrompt,
            staleCase.FirstTarget.InstanceId);
        Assert.Same(staleCase.ShownCard, staleCase.Game.State.Players[controller].Library[0]);
        Assert.NotNull(staleCase.Game.State.Players[1 - controller].Field[0][1]);
        Assert.Contains(staleCase.Game.State.Events, entry => entry.Type == "effect-failed");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("L12Evidence", "prompt-narrative:promoted-perseus-optional-lock")]
    public void PromotedPerseusExplainsTheOptionalLockAndNoReplacementBoundary(int controller)
    {
        var perseusCase = BeginPerseusPromotionPrompt(202609369 + controller, controller, "success");
        var game = perseusCase.Game;
        var prompt = perseusCase.Prompt;
        var playerName = game.State.Players[controller].Name;

        AssertPresentation(prompt, "珀尔修斯·晋升", "下个对方重置阶段无法转为活跃",
            "本效果没有额外费用");
        Assert.Equal("不发动", prompt.ChoiceLabels["skip"]);
        Assert.Contains("若结算时不再合法，则不改选",
            prompt.Presentation!.ChoiceConsequences[perseusCase.FirstTarget.InstanceId],
            StringComparison.Ordinal);
        Assert.Contains("不影响任何军团", prompt.Presentation.ChoiceConsequences["skip"],
            StringComparison.Ordinal);
        AssertPromptBoundaryAndCheckpoint(game, controller, $"{playerName} 正在选择效果对象");

        game.State.PendingPrompts.Remove(prompt);
        ContinueS2Faction(game, perseusCase.Item, prompt, perseusCase.FirstTarget.InstanceId);
        Assert.Equal(game.State.Round + 1, perseusCase.FirstTarget.CannotUntapUntilRound);
        Assert.True(perseusCase.SecondTarget.CannotUntapUntilRound < game.State.Round + 1);

        var declineCase = BeginPerseusPromotionPrompt(202609371 + controller, controller, "decline");
        declineCase.Game.State.PendingPrompts.Remove(declineCase.Prompt);
        ContinueS2Faction(declineCase.Game, declineCase.Item, declineCase.Prompt, "skip");
        Assert.True(declineCase.FirstTarget.CannotUntapUntilRound < declineCase.Game.State.Round + 1);
        Assert.True(declineCase.SecondTarget.CannotUntapUntilRound < declineCase.Game.State.Round + 1);

        var staleCase = BeginPerseusPromotionPrompt(202609373 + controller, controller, "stale");
        staleCase.Game.State.Players[1 - controller].Field[0][0] = null;
        staleCase.Game.State.PendingPrompts.Remove(staleCase.Prompt);
        ContinueS2Faction(staleCase.Game, staleCase.Item, staleCase.Prompt,
            staleCase.FirstTarget.InstanceId);
        Assert.True(staleCase.SecondTarget.CannotUntapUntilRound < staleCase.Game.State.Round + 1);
        Assert.Empty(staleCase.Game.State.PendingPrompts);
    }

    private static void AssertPresentation(L12Prompt prompt, string title, string situation, string instruction)
    {
        var presentation = Assert.IsType<L12PromptPresentation>(prompt.Presentation);
        Assert.Equal(title, presentation.Title);
        Assert.Contains(situation, presentation.Situation, StringComparison.Ordinal);
        Assert.Contains(instruction, presentation.Instruction, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(presentation.WaitingSummary));
    }

    private static void AssertSafeWaitingViews(L12GameEngine game, int waitingViewer,
        L12CardInstance source)
    {
        foreach (var waitingObject in new[]
                 {
                     game.SnapshotFor(waitingViewer).WaitingPrompt,
                     game.SnapshotForSpectator().WaitingPrompt,
                     game.SnapshotForReferee().WaitingPrompt,
                 })
        {
            Assert.NotNull(waitingObject);
            var waiting = JsonSerializer.SerializeToElement(waitingObject,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var summary = waiting.GetProperty("waitingSummary").GetString();
            Assert.False(string.IsNullOrWhiteSpace(summary));
            Assert.DoesNotContain(source.Name, summary!, StringComparison.Ordinal);
            Assert.DoesNotContain(source.InstanceId, summary, StringComparison.Ordinal);
            Assert.DoesNotContain("agree", summary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("refuse", summary, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertPrivatePromptViews(L12GameEngine game, int owner, int other,
        params L12CardInstance[] privateCards)
    {
        Assert.Single(game.SnapshotFor(owner).Prompts);
        Assert.Empty(game.SnapshotFor(other).Prompts);
        Assert.Empty(game.SnapshotForSpectator().Prompts);
        Assert.Empty(game.SnapshotForReferee().Prompts);
        Assert.Single(game.SnapshotForGm(other).Prompts);
        Assert.Null(game.SnapshotForGm(other).WaitingPrompt);

        var hiddenViews = new[]
        {
            JsonSerializer.Serialize(game.SnapshotFor(other), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            JsonSerializer.Serialize(game.SnapshotForSpectator(), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            JsonSerializer.Serialize(game.SnapshotForReferee(), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        };
        foreach (var hidden in hiddenViews)
            foreach (var card in privateCards)
                Assert.DoesNotContain(card.InstanceId, hidden, StringComparison.Ordinal);

        var gm = JsonSerializer.Serialize(game.SnapshotForGm(other),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains(privateCards[0].InstanceId, gm, StringComparison.Ordinal);
    }

    private static void AssertLandlordWaitingViews(L12GameEngine game, int owner, int other)
    {
        var expected = $"{game.State.Players[owner].Name} 正在完成卡牌选择";
        foreach (var waitingObject in new[]
                 {
                     game.SnapshotFor(other).WaitingPrompt,
                     game.SnapshotForSpectator().WaitingPrompt,
                     game.SnapshotForReferee().WaitingPrompt,
                 })
        {
            Assert.NotNull(waitingObject);
            var waiting = JsonSerializer.SerializeToElement(waitingObject,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var summary = waiting.GetProperty("waitingSummary").GetString();
            Assert.Equal(expected, summary);
            Assert.DoesNotContain("支付费用", summary!, StringComparison.Ordinal);
            Assert.DoesNotContain("弃牌", summary, StringComparison.Ordinal);
            Assert.DoesNotContain("decline", summary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("抵挡", summary, StringComparison.Ordinal);
            Assert.DoesNotContain("支援", summary, StringComparison.Ordinal);
        }
    }

    private static void AssertPromptBoundaryAndCheckpoint(L12GameEngine game, int owner,
        string expectedWaitingSummary, params L12CardInstance[] privateCards)
    {
        AssertPromptBoundary(game, owner, expectedWaitingSummary, privateCards);
        var checkpoint = game.SerializeFullState();
        Assert.DoesNotContain("__promptNarrative:", checkpoint, StringComparison.Ordinal);
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, game.AutoPassEmptyResponses,
            game.ConcealHiddenResponseAvailability);
        var originalPrompt = Assert.Single(game.State.PendingPrompts);
        var restoredPrompt = Assert.Single(restored.State.PendingPrompts);
        Assert.Equal(originalPrompt.Presentation!.Title, restoredPrompt.Presentation!.Title);
        Assert.Equal(originalPrompt.Presentation.Situation, restoredPrompt.Presentation.Situation);
        Assert.Equal(originalPrompt.Presentation.Instruction, restoredPrompt.Presentation.Instruction);
        Assert.Equal(originalPrompt.Presentation.ChoiceConsequences,
            restoredPrompt.Presentation.ChoiceConsequences);
        Assert.DoesNotContain(restoredPrompt.Data.Keys,
            key => key.StartsWith("__promptNarrative:", StringComparison.Ordinal));
        AssertPromptBoundary(restored, owner, expectedWaitingSummary, privateCards);
    }

    private static void AssertPromptBoundary(L12GameEngine game, int owner,
        string expectedWaitingSummary, IReadOnlyCollection<L12CardInstance> privateCards)
    {
        var other = 1 - owner;
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Single(game.SnapshotFor(owner).Prompts);
        Assert.Empty(game.SnapshotFor(other).Prompts);
        Assert.Empty(game.SnapshotForSpectator().Prompts);
        Assert.Empty(game.SnapshotForReferee().Prompts);
        Assert.Single(game.SnapshotForGm(other).Prompts);
        Assert.Null(game.SnapshotForGm(other).WaitingPrompt);

        foreach (var waitingObject in new[]
                 {
                     game.SnapshotFor(other).WaitingPrompt,
                     game.SnapshotForSpectator().WaitingPrompt,
                     game.SnapshotForReferee().WaitingPrompt,
                 })
        {
            Assert.NotNull(waitingObject);
            var waiting = JsonSerializer.SerializeToElement(waitingObject,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal(expectedWaitingSummary, waiting.GetProperty("waitingSummary").GetString());
        }

        var hiddenViews = new[]
        {
            JsonSerializer.Serialize(game.SnapshotFor(other), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            JsonSerializer.Serialize(game.SnapshotForSpectator(), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            JsonSerializer.Serialize(game.SnapshotForReferee(), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        };
        foreach (var hidden in hiddenViews)
            foreach (var card in privateCards)
                Assert.DoesNotContain(card.InstanceId, hidden, StringComparison.Ordinal);

        var gm = JsonSerializer.Serialize(game.SnapshotForGm(other),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        foreach (var card in privateCards.Where(card => prompt.ValidChoices.Contains(card.InstanceId)))
            Assert.Contains(card.InstanceId, gm, StringComparison.Ordinal);
    }

    private static L12GameEngine CreateGame(int seed, bool autoPassEmptyResponses,
        bool concealHiddenResponseAvailability)
        => new(Catalog, "prompt-narrative", "PN", seed, ["甲", "乙"], [0, 1], skipPreparation: true,
            autoPassEmptyResponses: autoPassEmptyResponses,
            concealHiddenResponseAvailability: concealHiddenResponseAvailability, stateFormatVersion: 2);

    private static L12GameEngine CreateCleanGame(int seed)
    {
        var game = CreateGame(seed, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        game.State.PendingPrompts.Clear();
        game.State.EffectStack.Clear();
        game.State.DeferredEffectStack.Clear();
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.TemporaryMorale = 0;
            player.UsedAbilities.Clear();
        }
        return game;
    }

    private static L12CardInstance PutCardInHand(L12GameEngine game, int playerIndex, string cardId)
    {
        var player = game.State.Players[playerIndex];
        var card = player.Hand.Concat(player.Library).First(candidate => candidate.CardId == cardId);
        player.Hand.Remove(card);
        player.Library.Remove(card);
        player.Hand.Add(card);
        while (player.MoraleDeck.Count > 0)
        {
            var morale = player.MoraleDeck[0];
            player.MoraleDeck.RemoveAt(0);
            morale.Tapped = false;
            player.Morale.Add(morale);
        }
        game.State.ActivePlayer = playerIndex;
        game.State.Phase = L12Phase.Main;
        return card;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int ownerIndex)
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
            OwnerIndex = ownerIndex,
            SummonRound = -1,
        };
    }

    private static L12StackItem StackItem(string id, int controller, L12CardInstance source, string text)
        => new()
        {
            StackItemId = id,
            Controller = controller,
            SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId,
            SourceName = source.Name,
            Trigger = "active",
            Text = text,
            SourceSnapshot = source,
        };

    private static L12StackItem LegacyStackItem(string id, int controller, L12CardInstance source,
        string trigger, string atomicFlow)
    {
        var item = new L12StackItem
        {
            StackItemId = id,
            Controller = controller,
            SourceInstanceId = source.InstanceId,
            SourceCardId = $"legacy-{source.CardId}",
            SourceName = source.Name,
            Trigger = trigger,
            Text = $"测试〈{source.Name}〉的{trigger}效果",
            SourceSnapshot = source,
        };
        item.Data["atomicFlow"] = atomicFlow;
        return item;
    }

    private static (L12GameEngine Game, L12Prompt Prompt, L12StackItem Item,
        L12StackItem Authority, L12CardInstance Excluded, L12CardInstance Legal) BeginLandlordPrompt(
        int seed, int affectedPlayer, string authorityAction, string suffix)
    {
        var game = CreateCleanGame(seed);
        var excluded = Card("S01-0003", $"narrative-landlord-excluded-{suffix}-{affectedPlayer}",
            affectedPlayer);
        var legal = Card("S01-0004", $"narrative-landlord-legal-{suffix}-{affectedPlayer}",
            affectedPlayer);
        game.State.Players[affectedPlayer].Hand.AddRange([excluded, legal]);

        var authoritySource = Card("S01-0002", $"narrative-landlord-authority-source-{suffix}-{affectedPlayer}",
            affectedPlayer);
        var authority = new L12StackItem
        {
            StackItemId = $"narrative-landlord-authority-{suffix}-{affectedPlayer}",
            Controller = affectedPlayer,
            SourceInstanceId = authoritySource.InstanceId,
            SourceCardId = authoritySource.CardId,
            SourceName = authoritySource.Name,
            Trigger = "authority-event",
            Text = authorityAction == "support" ? "支援权威事件" : "抵挡权威事件",
            SourceSnapshot = authoritySource,
        };
        authority.Data["eventType"] = "defense";
        authority.Data["action"] = authorityAction;
        authority.Data["blockIds"] = excluded.InstanceId;
        game.State.EffectStack.Add(authority);

        var landlord = Card("S02-0015", $"narrative-landlord-{suffix}-{affectedPlayer}",
            1 - affectedPlayer);
        var item = LegacyStackItem($"narrative-landlord-stack-{suffix}-{affectedPlayer}",
            1 - affectedPlayer, landlord, "s2-reaction", "landlord-coercion");
        item.Targets.Add(authority.StackItemId);
        item.Data["affectedPlayer"] = affectedPlayer.ToString();
        game.State.EffectStack.Add(item);

        Invoke(game, "ResolveS2CounterEffect", item);
        return (game, Assert.Single(game.State.PendingPrompts), item, authority, excluded, legal);
    }

    private static (L12GameEngine Game, L12Prompt Prompt, L12StackItem Item,
        L12CardInstance Target) BeginCourtMagicianPrompt(int seed, int controller, int targetOwner,
        string suffix)
    {
        var game = CreateCleanGame(seed);
        var target = Card("S02-0018", $"narrative-magician-target-{suffix}", targetOwner);
        target.Hidden = true;
        game.State.Players[targetOwner].Field[1][1] = target;
        var magician = Card("S02-0003", $"narrative-magician-{suffix}", controller);
        var item = LegacyStackItem($"narrative-magician-stack-{suffix}", controller, magician,
            "enter", "宫廷魔术师");
        game.State.EffectStack.Add(item);

        Assert.True(Assert.IsType<bool>(Invoke(game, "TryResolveS2UniversalEnter", item, magician)));
        return (game, Assert.Single(game.State.PendingPrompts), item, target);
    }

    private static (L12GameEngine Game, L12Prompt Prompt, L12StackItem Item,
        L12CardInstance FirstHand, L12CardInstance SecondHand,
        L12CardInstance FirstLibrary, L12CardInstance SecondLibrary) BeginRingPrompt(
        int seed, int controller, string suffix)
    {
        var game = CreateCleanGame(seed);
        var player = game.State.Players[controller];
        var firstHand = Card("S01-0003", $"narrative-ring-hand-1-{suffix}-{controller}", controller);
        var secondHand = Card("S01-0004", $"narrative-ring-hand-2-{suffix}-{controller}", controller);
        var firstLibrary = Card("S02-0001", $"narrative-ring-library-1-{suffix}-{controller}", controller);
        var secondLibrary = Card("S02-0003", $"narrative-ring-library-2-{suffix}-{controller}", controller);
        player.Hand.AddRange([firstHand, secondHand]);
        player.Library.AddRange([firstLibrary, secondLibrary]);
        var ring = Card("S02-0008", $"narrative-ring-{suffix}-{controller}", controller);
        player.Relic = ring;
        var item = LegacyStackItem($"narrative-ring-stack-{suffix}-{controller}", controller, ring,
            "enter", "万物统御之戒");
        game.State.EffectStack.Add(item);

        Assert.True(Assert.IsType<bool>(Invoke(game, "TryResolveS2UniversalEnter", item, ring)));
        return (game, Assert.Single(game.State.PendingPrompts), item,
            firstHand, secondHand, firstLibrary, secondLibrary);
    }

    private static (L12GameEngine Game, L12Prompt Prompt, L12StackItem Item,
        L12CardInstance Arthur, L12CardInstance? ExistingOwner) BeginArthurPrompt(
        int seed, int controller, bool existingSword, string suffix)
    {
        var game = CreateCleanGame(seed);
        var player = game.State.Players[controller];
        player.SpecialZones.Runes = 1;
        L12CardInstance? existingOwner = null;
        if (existingSword)
        {
            existingOwner = Card("S02-0601", $"narrative-existing-arthur-{suffix}-{controller}", controller);
            existingOwner.AttachedCards.Add(Card("S02-06S2",
                $"narrative-existing-sword-{suffix}-{controller}", controller));
            player.Field[0][0] = existingOwner;
        }
        var arthur = Card("S02-0601", $"narrative-arthur-{suffix}-{controller}", controller);
        player.Field[0][1] = arthur;
        var item = LegacyStackItem($"narrative-arthur-stack-{suffix}-{controller}", controller, arthur,
            "enter", "亚瑟王");
        game.State.EffectStack.Add(item);

        Assert.True(Assert.IsType<bool>(Invoke(game, "TryResolveS2FactionEnter", item, arthur)));
        return (game, Assert.Single(game.State.PendingPrompts), item, arthur, existingOwner);
    }

    private static (L12GameEngine Game, L12Prompt Prompt, L12StackItem Item)
        BeginPromotedHeraclesPrompt(int seed, int controller, string suffix)
    {
        var game = CreateCleanGame(seed);
        var heracles = Card("S02-0501", $"narrative-promoted-heracles-{suffix}-{controller}", controller);
        game.State.Players[controller].Field[0][0] = heracles;
        var item = LegacyStackItem($"narrative-promoted-heracles-stack-{suffix}-{controller}",
            controller, heracles, "enter", "赫拉克勒斯·晋升");
        game.State.EffectStack.Add(item);

        Assert.True(Assert.IsType<bool>(Invoke(game, "TryResolveS2FactionEnter", item, heracles)));
        return (game, Assert.Single(game.State.PendingPrompts), item);
    }

    private static (L12GameEngine Game, L12Prompt Prompt, L12StackItem Item,
        L12CardInstance ExistingHand, L12CardInstance DrawnA, L12CardInstance DrawnB)
        BeginHeraclesPrompt(int seed, int controller, string suffix)
    {
        var game = CreateCleanGame(seed);
        var player = game.State.Players[controller];
        var existingHand = Card("S01-0002", $"narrative-heracles-hand-{suffix}-{controller}", controller);
        var drawnA = Card("S01-0003", $"narrative-heracles-drawn-a-{suffix}-{controller}", controller);
        var drawnB = Card("S01-0004", $"narrative-heracles-drawn-b-{suffix}-{controller}", controller);
        player.Hand.Add(existingHand);
        player.Library.AddRange([drawnA, drawnB]);
        var heracles = Card("S02-0502", $"narrative-heracles-{suffix}-{controller}", controller);
        player.Field[0][0] = heracles;
        var item = LegacyStackItem($"narrative-heracles-stack-{suffix}-{controller}", controller, heracles,
            "enter", "赫拉克勒斯");
        game.State.EffectStack.Add(item);

        Assert.True(Assert.IsType<bool>(Invoke(game, "TryResolveS2FactionEnter", item, heracles)));
        return (game, Assert.Single(game.State.PendingPrompts), item, existingHand, drawnA, drawnB);
    }

    private static (L12GameEngine Game, L12Prompt Prompt, L12StackItem Item,
        L12CardInstance FirstHand, L12CardInstance SecondHand) BeginJoanPrompt(
        int seed, int controller, string suffix)
    {
        var game = CreateCleanGame(seed);
        var player = game.State.Players[controller];
        var firstHand = Card("S01-0002", $"narrative-joan-hand-1-{suffix}-{controller}", controller);
        var secondHand = Card("S01-0003", $"narrative-joan-hand-2-{suffix}-{controller}", controller);
        player.Hand.AddRange([firstHand, secondHand]);
        var joan = Card("S02-0613", $"narrative-joan-{suffix}-{controller}", controller);
        player.Field[0][0] = joan;
        var item = LegacyStackItem($"narrative-joan-stack-{suffix}-{controller}", controller, joan,
            "enter", "圣女贞德");
        game.State.EffectStack.Add(item);

        Assert.True(Assert.IsType<bool>(Invoke(game, "TryResolveS2FactionEnter", item, joan)));
        return (game, Assert.Single(game.State.PendingPrompts), item, firstHand, secondHand);
    }

    private static (L12GameEngine Game, L12Prompt Prompt, L12StackItem Item,
        L12CardInstance FirstHand, L12CardInstance SecondHand, L12CardInstance Promotion)
        BeginPerseusPrompt(int seed, int controller, string suffix)
    {
        var game = CreateCleanGame(seed);
        var player = game.State.Players[controller];
        var firstHand = Card("S01-0002", $"narrative-perseus-hand-1-{suffix}-{controller}", controller);
        var secondHand = Card("S01-0003", $"narrative-perseus-hand-2-{suffix}-{controller}", controller);
        var promotion = Card("S02-0505", $"narrative-perseus-promotion-{suffix}-{controller}", controller);
        player.Hand.AddRange([firstHand, secondHand]);
        player.Graveyard.Add(promotion);
        var perseus = Card("S02-0506", $"narrative-perseus-{suffix}-{controller}", controller);
        player.Field[0][0] = perseus;
        var item = LegacyStackItem($"narrative-perseus-stack-{suffix}-{controller}", controller, perseus,
            "enter", "珀尔修斯");
        game.State.EffectStack.Add(item);

        Assert.True(Assert.IsType<bool>(Invoke(game, "TryResolveS2FactionEnter", item, perseus)));
        return (game, Assert.Single(game.State.PendingPrompts), item,
            firstHand, secondHand, promotion);
    }

    private static (L12GameEngine Game, L12Prompt Prompt, L12StackItem Item,
        L12CardInstance FirstTarget, L12CardInstance SecondTarget) BeginImhotepPrompt(
        int seed, int controller, string suffix)
    {
        var game = CreateCleanGame(seed);
        var player = game.State.Players[controller];
        var opponent = game.State.Players[1 - controller];
        var firstTarget = Card("S01-0202", $"narrative-imhotep-target-1-{suffix}-{controller}", controller);
        var secondTarget = Card("S01-0202", $"narrative-imhotep-target-2-{suffix}-{controller}", controller);
        player.Graveyard.AddRange([firstTarget, secondTarget]);
        opponent.Hand.Add(Card("S01-0002", $"narrative-imhotep-opponent-hand-{suffix}-{controller}",
            1 - controller));
        var imhotep = Card("S02-0204", $"narrative-imhotep-{suffix}-{controller}", controller);
        player.Field[0][0] = imhotep;
        var item = LegacyStackItem($"narrative-imhotep-stack-{suffix}-{controller}", controller, imhotep,
            "enter", "伊姆何泰普");
        game.State.EffectStack.Add(item);

        Assert.True(Assert.IsType<bool>(Invoke(game, "TryResolveS2FactionEnter", item, imhotep)));
        return (game, Assert.Single(game.State.PendingPrompts), item, firstTarget, secondTarget);
    }

    private static (L12GameEngine Game, L12Prompt Prompt, L12StackItem Item,
        L12CardInstance FirstTarget, L12CardInstance SecondTarget) BeginTakedaSearchPrompt(
        int seed, int controller, bool publicTrigger, string suffix)
    {
        var game = CreateCleanGame(seed);
        var player = game.State.Players[controller];
        var firstTarget = Card("S01-0402", $"narrative-takeda-target-1-{controller}", controller);
        var secondTarget = Card("S02-0402", $"narrative-takeda-target-2-{controller}", controller);
        player.Library.AddRange([firstTarget, secondTarget]);
        var takeda = Card("S02-0401", $"narrative-takeda-{suffix}-{controller}", controller);
        player.Field[0][0] = takeda;
        var item = publicTrigger
            ? new L12StackItem
            {
                StackItemId = $"narrative-takeda-stack-{suffix}-{controller}",
                Controller = controller,
                SourceInstanceId = takeda.InstanceId,
                SourceCardId = takeda.CardId,
                SourceName = takeda.Name,
                Trigger = "enter",
                Text = "测试武田信玄的公开登场触发效果",
                SourceSnapshot = takeda,
            }
            : LegacyStackItem($"narrative-takeda-stack-{suffix}-{controller}", controller, takeda,
                "enter", "武田信玄");
        game.State.EffectStack.Add(item);

        if (publicTrigger)
        {
            item.Data["declared:mode"] = "mode:use";
            Assert.True(Assert.IsType<bool>(Invoke(game, "TryResolveBatch6JAEnterEffect", item, takeda)));
        }
        else
        {
            Assert.True(Assert.IsType<bool>(Invoke(game, "TryResolveS2FactionEnter", item, takeda)));
        }
        return (game, Assert.Single(game.State.PendingPrompts), item, firstTarget, secondTarget);
    }

    private static (L12GameEngine Game, L12Prompt Prompt, L12StackItem Item,
        L12CardInstance ShownCard, L12CardInstance OtherHand,
        L12CardInstance FirstTarget, L12CardInstance SecondTarget) BeginHeraclesPromotionPrompt(
        int seed, int controller, string suffix)
    {
        var game = CreateCleanGame(seed);
        var player = game.State.Players[controller];
        var opponent = game.State.Players[1 - controller];
        var shownCard = Card("S01-0202", $"narrative-heracles-promotion-cost-{suffix}-{controller}", controller);
        var otherHand = Card("S01-0002", $"narrative-heracles-promotion-hand-{suffix}-{controller}", controller);
        var firstTarget = Card("S01-0002", $"narrative-heracles-promotion-target-1-{suffix}-{controller}",
            1 - controller);
        var secondTarget = Card("S01-0003", $"narrative-heracles-promotion-target-2-{suffix}-{controller}",
            1 - controller);
        player.Hand.AddRange([shownCard, otherHand]);
        opponent.Field[0][0] = firstTarget;
        opponent.Field[0][1] = secondTarget;
        var heracles = Card("S02-0501", $"narrative-heracles-promotion-{suffix}-{controller}", controller);
        player.Field[0][0] = heracles;
        var item = LegacyStackItem($"narrative-heracles-promotion-stack-{suffix}-{controller}",
            controller, heracles, "promotion-enter", "赫拉克勒斯·晋升");
        game.State.EffectStack.Add(item);

        Invoke(game, "ResolveS2PromotionEnter", item);
        return (game, Assert.Single(game.State.PendingPrompts), item,
            shownCard, otherHand, firstTarget, secondTarget);
    }

    private static (L12GameEngine Game, L12Prompt Prompt, L12StackItem Item,
        L12CardInstance FirstTarget, L12CardInstance SecondTarget) BeginPerseusPromotionPrompt(
        int seed, int controller, string suffix)
    {
        var game = CreateCleanGame(seed);
        var player = game.State.Players[controller];
        var opponent = game.State.Players[1 - controller];
        var firstTarget = Card("S01-0002", $"narrative-perseus-promotion-target-1-{suffix}-{controller}",
            1 - controller);
        var secondTarget = Card("S01-0003", $"narrative-perseus-promotion-target-2-{suffix}-{controller}",
            1 - controller);
        firstTarget.Tapped = true;
        secondTarget.Tapped = true;
        opponent.Field[0][0] = firstTarget;
        opponent.Field[0][1] = secondTarget;
        var perseus = Card("S02-0505", $"narrative-perseus-promotion-{suffix}-{controller}", controller);
        player.Field[0][0] = perseus;
        var item = LegacyStackItem($"narrative-perseus-promotion-stack-{suffix}-{controller}",
            controller, perseus, "promotion-enter", "珀尔修斯·晋升");
        game.State.EffectStack.Add(item);

        Invoke(game, "ResolveS2PromotionEnter", item);
        return (game, Assert.Single(game.State.PendingPrompts), item, firstTarget, secondTarget);
    }

    private static (L12GameEngine Game, L12Prompt Prompt, L12StackItem Item,
        L12CardInstance FirstHand, L12CardInstance SecondHand,
        L12CardInstance FirstTarget, L12CardInstance SecondTarget) BeginIioPrompt(
        int seed, int controller, string suffix)
    {
        var game = CreateCleanGame(seed);
        var player = game.State.Players[controller];
        var firstHand = Card("S01-0002", $"narrative-iio-hand-1-{suffix}-{controller}", controller);
        var secondHand = Card("S01-0003", $"narrative-iio-hand-2-{suffix}-{controller}", controller);
        player.Hand.AddRange([firstHand, secondHand]);
        var iio = Card("S02-0402", $"narrative-iio-{suffix}-{controller}", controller);
        var firstTarget = Card("S02-0401", $"narrative-iio-target-1-{suffix}-{controller}", controller);
        var secondTarget = Card("S02-0403", $"narrative-iio-target-2-{suffix}-{controller}", controller);
        firstTarget.Tapped = true;
        secondTarget.Tapped = true;
        player.Field[0][0] = iio;
        player.Field[0][1] = firstTarget;
        player.Field[0][2] = secondTarget;
        var item = LegacyStackItem($"narrative-iio-stack-{suffix}-{controller}", controller, iio,
            "enter", "井伊直虎");
        game.State.EffectStack.Add(item);

        Assert.True(Assert.IsType<bool>(Invoke(game, "TryResolveS2FactionEnter", item, iio)));
        return (game, Assert.Single(game.State.PendingPrompts), item,
            firstHand, secondHand, firstTarget, secondTarget);
    }

    private static void ContinueS2Faction(L12GameEngine game, L12StackItem item, L12Prompt prompt,
        string choice)
    {
        Assert.True(Assert.IsType<bool>(Invoke(game, "TryContinueS2Faction", item, prompt,
            new List<string> { choice }, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                Choice: choice, CardInstanceIds: [choice]))));
    }

    private static void AddMorale(L12GameEngine game, int playerIndex, int count, string prefix)
    {
        var moraleIds = Catalog.DeckAt(0).MoraleIds;
        for (var index = 0; index < count; index++)
            game.State.Players[playerIndex].Morale.Add(Morale(moraleIds[index % moraleIds.Count],
                $"narrative-{prefix}-morale-{index}"));
    }

    private static L12MoraleCard Morale(string cardId, string instanceId)
        => new()
        {
            InstanceId = instanceId,
            CardId = cardId,
            Tapped = false,
        };

    private static object? Invoke(L12GameEngine game, string methodName, params object?[] arguments)
    {
        var method = typeof(L12GameEngine).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == methodName
                && candidate.GetParameters().Length == arguments.Length);
        return method.Invoke(game, arguments);
    }
}
