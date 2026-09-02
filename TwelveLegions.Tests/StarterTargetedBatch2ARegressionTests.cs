using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class StarterTargetedBatch2ARegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
        => new(Catalog, "starter-targeted-2a", "STARTER2A", seed, ["甲", "乙"], [0, 1],
            skipPreparation: true);

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
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
        };
    }

    private static void Queue(L12GameEngine game, L12CardInstance source)
        => Invoke(game, "QueueOrPushTriggeredEffect", 0, source, "enter", "【登场时】效果", null,
            new Dictionary<string, string>());

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().Name, name);
        return method.Invoke(target, args);
    }

    private static L12Prompt Prompt(L12GameEngine game) => Assert.Single(game.State.PendingPrompts);

    private static void Choose(L12GameEngine game, string choice)
    {
        var prompt = Prompt(game);
        Assert.True(game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
    }

    private static void PassResponses(L12GameEngine game, int maximum = 12)
    {
        var guard = 0;
        while (game.State.PendingPrompts.FirstOrDefault() is { Kind: "response" } prompt && guard++ < maximum)
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
    }

    private static void HoldOpponentResponseWindow(L12GameEngine game)
    {
        var opponent = game.State.Players[1];
        var counter = Card("S01-0016", "starter2a-counter");
        counter.Hidden = true;
        opponent.Field[1][0] = counter;
        opponent.Hand.Add(Card("S01-0002", "starter2a-counter-hand"));
    }

    private static int EventIndex(L12GameEngine game, Func<L12ActionEvent, bool> predicate)
        => game.State.Events.FindIndex(entry => predicate(entry));

    private static void AssertNaturalUniquePrompt(L12Prompt prompt)
    {
        string[] forbidden = ["预先声明", "预先选择", "公开区域", "私密区域", "结算模式"];
        Assert.DoesNotContain(forbidden, word => prompt.Text.Contains(word, StringComparison.Ordinal));
        Assert.Equal(prompt.ValidChoices.Count,
            prompt.ValidChoices.Select(choice => prompt.ChoiceLabels.GetValueOrDefault(choice, choice))
                .Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Batch2AUsesSharedVerifiedProgramsAndStructuredCatalog()
    {
        var expected = new (string CardId, string Trigger)[]
        {
            ("ST01-03", "enter"),
            ("ST02-01", "continuous"), ("ST02-01", "enter"),
            ("ST02-03", "enter"), ("ST02-06", "enter"), ("ST03-03", "enter"),
        };

        foreach (var (cardId, trigger) in expected)
        {
            Assert.NotNull(L12VerifiedAtomicPrograms.Find(cardId, trigger));
            Assert.Contains(Catalog.AtomicEffects.Find(cardId)!.Abilities,
                ability => ability.Trigger == trigger && ability.MigrationStatus == "verified"
                    && !ability.HasLegacyFallback);
        }
    }

    [Fact]
    public void StarterTargetedEntryPoolScanStaysExplicit()
    {
        var scanned = Catalog.Cards.Values
            .Where(card => card.Id.StartsWith("ST", StringComparison.Ordinal)
                && card.CardType == "legion"
                && card.Effect?.Contains("登场时", StringComparison.Ordinal) == true
                && new[] { "选择", "手牌/牌库", "从手牌", "加入手牌", "转为活跃", "兵力-" }
                    .Any(marker => card.Effect.Contains(marker, StringComparison.Ordinal)))
            .Select(card => card.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([
            "ST01-03", "ST02-01", "ST02-03", "ST02-06", "ST03-03",
            "ST05-07", "ST06-01", "ST06-03", "ST06-04", "ST06-07",
        ], scanned);
        Assert.All(new[] { "ST01-03", "ST02-01", "ST02-03", "ST02-06", "ST03-03" },
            cardId => Assert.NotNull(L12VerifiedAtomicPrograms.Find(cardId, "enter")));
    }

    [Fact]
    public void XiaoHePrepaysReturnedMoraleThenSummonsDeclaredHanXin()
    {
        var game = Create(20101);
        var player = game.State.Players[0];
        var xiaoHe = Card("ST01-03", "xiaohe");
        var hanXin = Card("S01-0104", "hanxin");
        player.Field[0][0] = xiaoHe;
        player.Hand.Add(hanXin);
        player.Morale.Add(new L12MoraleCard { CardId = "S01-01C1", InstanceId = "xiaohe-morale" });

        Queue(game, xiaoHe);
        AssertNaturalUniquePrompt(Prompt(game));
        Choose(game, "mode:use");
        Choose(game, "xiaohe-morale");
        Choose(game, hanXin.InstanceId);
        Choose(game, "0:1");

        Assert.Empty(player.Morale);
        Assert.True(EventIndex(game, entry => entry.Type == "cost" && entry.Text.Contains("萧何", StringComparison.Ordinal))
            < EventIndex(game, entry => entry.Type == "stack-push" && entry.Text.Contains("萧何", StringComparison.Ordinal)));
        PassResponses(game);

        Assert.Same(hanXin, player.Field[0][1]);
        Assert.False(hanXin.Tapped);
        Assert.DoesNotContain(hanXin, player.Hand);
    }

    [Fact]
    public void KhufuDiscardsGuardBeforeStackAndHasSummonTurnCounterProtection()
    {
        var game = Create(20102);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var khufu = Card("ST02-01", "khufu");
        var guard = Card("S01-0212", "guard");
        var enemy = Card("S01-0102", "enemy");
        player.Field[0][0] = khufu;
        player.Field[0][1] = guard;
        opponent.Field[0][0] = enemy;
        khufu.SummonRound = game.State.Round;

        Assert.True(L12StructuredCardRules.HasSummonTurnCounterTacticProtection(khufu, game.State.Round));
        Queue(game, khufu);
        Choose(game, "mode:use");
        Choose(game, guard.InstanceId);
        Choose(game, enemy.InstanceId);

        Assert.Null(player.Field[0][1]);
        Assert.Contains(guard, player.Graveyard);
        Assert.True(EventIndex(game, entry => entry.Type == "cost" && entry.Text.Contains("胡夫", StringComparison.Ordinal))
            < EventIndex(game, entry => entry.Type == "stack-push" && entry.Text.Contains("胡夫", StringComparison.Ordinal)));
        PassResponses(game);

        Assert.Equal(enemy.BaseTroops - 4000, enemy.Troops);
    }

    [Fact]
    public void SnakeCharmerLibraryChoiceIsControllerOnlyUntilCobraEnters()
    {
        var game = Create(20103);
        var player = game.State.Players[0];
        var charmer = Card("ST02-03", "charmer");
        var cobra = Card("ST02-09", "secret-cobra");
        player.Field[0][0] = charmer;
        player.Library.Add(cobra);
        HoldOpponentResponseWindow(game);

        Queue(game, charmer);
        Choose(game, "mode:use");
        var privateChoice = Prompt(game);
        Assert.Equal("card", privateChoice.Kind);
        Assert.Equal("沙漠眼镜蛇", privateChoice.Data[cobra.InstanceId]);
        var opponentView = JsonSerializer.Serialize(game.SnapshotFor(1));
        Assert.DoesNotContain(cobra.InstanceId, opponentView, StringComparison.Ordinal);
        Assert.DoesNotContain("沙漠眼镜蛇", opponentView, StringComparison.Ordinal);

        Choose(game, cobra.InstanceId);
        Choose(game, "1:1");
        Assert.Contains(game.State.EffectStack, item => item.SourceCardId == "ST02-03");
        Assert.DoesNotContain(cobra.InstanceId, JsonSerializer.Serialize(game.SnapshotFor(1)), StringComparison.Ordinal);
        PassResponses(game);

        Assert.Same(cobra, player.Field[1][1]);
        Assert.False(cobra.Tapped);
    }

    [Fact]
    public void GeorgeInvalidFinalTargetDoesNotPayDeclaredFieldCostOrEnterStack()
    {
        var game = Create(20104);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var george = Card("ST02-06", "george");
        var cost = Card("S01-0102", "george-cost");
        var enemy = Card("S01-0103", "george-enemy");
        player.Field[0][0] = george;
        player.Field[0][1] = cost;
        opponent.Field[0][0] = enemy;

        Queue(game, george);
        Choose(game, "mode:use");
        Choose(game, cost.InstanceId);
        opponent.Field[0][0] = null;
        Choose(game, enemy.InstanceId);

        Assert.Same(cost, player.Field[0][1]);
        Assert.DoesNotContain(cost, player.Graveyard);
        Assert.Empty(game.State.EffectStack);
        Assert.Contains(game.State.Events, entry => entry.Type == "ability-rejected"
            && entry.Text.Contains("未弃置军团", StringComparison.Ordinal));
    }

    [Fact]
    public void GeorgeMayDiscardHimselfAndStillResolveFromTheTriggerSnapshot()
    {
        var game = Create(20106);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var george = Card("ST02-06", "george-self-cost");
        var enemy = Card("S01-0103", "george-success-enemy");
        player.Field[0][0] = george;
        opponent.Field[0][0] = enemy;

        Queue(game, george);
        Choose(game, "mode:use");
        Choose(game, george.InstanceId);
        Choose(game, enemy.InstanceId);
        PassResponses(game);

        Assert.Null(player.Field[0][0]);
        Assert.Contains(george, player.Graveyard);
        Assert.Equal(enemy.BaseTroops - 2000, enemy.Troops);
    }

    [Fact]
    public void FreydisPrepaysHandDiscardAndRevealsDeclaredAsgardRecovery()
    {
        var game = Create(20105);
        var player = game.State.Players[0];
        var freydis = Card("ST03-03", "freydis");
        var handCost = Card("S01-0002", "freydis-cost");
        var recover = Card("S01-0302", "freydis-recover");
        player.Field[0][0] = freydis;
        player.Hand.Add(handCost);
        player.Graveyard.Add(recover);

        Queue(game, freydis);
        Choose(game, "mode:use");
        Choose(game, handCost.InstanceId);
        Choose(game, recover.InstanceId);

        Assert.Contains(handCost, player.Graveyard);
        Assert.True(EventIndex(game, entry => entry.Type == "cost" && entry.Text.Contains("弗蕾迪斯", StringComparison.Ordinal))
            < EventIndex(game, entry => entry.Type == "stack-push" && entry.Text.Contains("弗蕾迪斯", StringComparison.Ordinal)));
        PassResponses(game);

        Assert.Contains(recover, player.Hand);
        Assert.DoesNotContain(recover, player.Graveyard);
        Assert.Contains(game.State.Events, entry => entry.Text.Contains("展示", StringComparison.Ordinal)
            && entry.Text.Contains(recover.Name, StringComparison.Ordinal));
    }
}
