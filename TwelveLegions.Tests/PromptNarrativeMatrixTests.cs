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

    private static void AssertPresentation(L12Prompt prompt, string title, string situation, string instruction)
    {
        var presentation = Assert.IsType<L12PromptPresentation>(prompt.Presentation);
        Assert.Equal(title, presentation.Title);
        Assert.Contains(situation, presentation.Situation, StringComparison.Ordinal);
        Assert.Contains(instruction, presentation.Instruction, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(presentation.WaitingSummary));
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
