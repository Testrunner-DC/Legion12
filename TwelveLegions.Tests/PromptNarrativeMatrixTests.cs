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
