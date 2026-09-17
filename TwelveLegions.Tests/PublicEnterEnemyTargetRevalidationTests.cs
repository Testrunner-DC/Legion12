using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

/// <summary>
/// Public entry declarations name a current enemy legion, not merely an instance that happened
/// to be on the battlefield at declaration time. These fixtures intentionally change that
/// state after the response window has opened.
/// </summary>
public sealed class PublicEnterEnemyTargetRevalidationTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "entry-target-revalidation", "ENTRY-TARGET", seed,
            ["甲", "乙"], [0, 1], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear(); player.Library.Clear(); player.Graveyard.Clear(); player.Morale.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int cost = 1,
        int troops = 1000, int disasterLevel = 1, string? cardType = null)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = definition.Id, Name = definition.NameZh,
            CardType = cardType ?? definition.CardType, Faction = definition.Faction, ImageUrl = definition.ImageUrl,
            Cost = cost, EffectText = definition.Effect, Traits = [.. definition.Traits],
            Profession = definition.Profession, BaseTroops = troops, Troops = troops,
            DisasterLevel = disasterLevel,
        };
    }

    private static void QueueEnter(L12GameEngine game, L12CardInstance source)
        => typeof(L12GameEngine).GetMethod("QueueOrPushTriggeredEffect", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(game, [0, source, "enter", "公开登场目标复验", null, new Dictionary<string, string>()]);

    private static void Resolve(L12GameEngine game, string targetId)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: [targetId]));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var guard = 0; guard < 12 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; guard++)
        {
            var prompt = game.State.PendingPrompts[0];
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
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
    [InlineData("S01-0201", 9101)] // 图特摩斯三世：兵力不高于5000
    [InlineData("S01-0402", 9102)] // 织田信长：费用不高于4
    [InlineData("S01-0403", 9103)] // 上杉谦信：费用不高于X
    [InlineData("S01-0412", 9104)] // 立花誾千代：对方军团
    [Trait("L12Evidence", "entry:public-enemy-legion-current-state")]
    public void DeclaredEnemyLegionThatChangesToArtifactDuringResponsesIsNotResolved(string sourceId, int seed)
    {
        var game = Create(seed);
        var source = Card(sourceId, $"entry-source-{sourceId}");
        var target = Card("S01-0001", $"entry-target-{sourceId}");
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[0][0] = target;
        if (sourceId == "S01-0403")
            game.State.Players[1].Field[1][0] = Card("S01-0019", "entry-uesugi-counter");

        QueueEnter(game, source);
        Resolve(game, target.InstanceId);
        Assert.Single(game.State.EffectStack);

        var transformed = Card("S01-0001", target.InstanceId, cardType: "artifact");
        game.State.Players[1].Field[0][0] = transformed;
        PassResponses(game);

        Assert.Same(transformed, game.State.Players[1].Field[0][0]);
        Assert.Equal(0, transformed.CostModifier);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("不再符合条件", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:public-enemy-legion-current-threshold")]
    public void ThutmoseDoesNotKillATargetWhoseTroopsExceedTheDeclaredThresholdDuringResponses()
    {
        var game = Create(9105);
        var source = Card("S01-0201", "entry-thutmose");
        var target = Card("S01-0001", "entry-thutmose-target", troops: 5000);
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[0][0] = target;

        QueueEnter(game, source);
        Resolve(game, target.InstanceId);
        target.Troops = 5001;
        PassResponses(game);

        Assert.Same(target, game.State.Players[1].Field[0][0]);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("不再符合条件", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:public-enemy-legion-current-cost")]
    public void NobunagaDoesNotKillATargetWhoseCurrentCostExceedsFourDuringResponses()
    {
        var game = Create(9106);
        var source = Card("S01-0402", "entry-nobunaga");
        var target = Card("S01-0001", "entry-nobunaga-target", cost: 4);
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[0][0] = target;

        QueueEnter(game, source);
        Resolve(game, target.InstanceId);
        target.CostModifier = 1;
        PassResponses(game);

        Assert.Same(target, game.State.Players[1].Field[0][0]);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("不再符合条件", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:public-enemy-legion-current-dynamic-threshold")]
    public void UesugiDoesNotKillWhenTheCurrentBackRowTacticCountDropsDuringResponses()
    {
        var game = Create(9107);
        var source = Card("S01-0403", "entry-uesugi");
        var target = Card("S01-0001", "entry-uesugi-target", cost: 1);
        var counter = Card("S01-0019", "entry-uesugi-counter");
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[0][0] = target;
        game.State.Players[1].Field[1][0] = counter;

        QueueEnter(game, source);
        Resolve(game, target.InstanceId);
        game.State.Players[1].Field[1][0] = null;
        game.State.Players[1].Graveyard.Add(counter);
        PassResponses(game);

        Assert.Same(target, game.State.Players[1].Field[0][0]);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("不再符合条件", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:public-enemy-legion-multi-target-partial")]
    public void WuzetianResolvesEachDeclaredTargetAgainstItsCurrentState()
    {
        var game = Create(9108);
        var source = Card("S01-0102", "entry-wuzetian");
        var first = Card("S01-0001", "entry-wuzetian-first");
        var second = Card("S01-0002", "entry-wuzetian-second");
        first.Tapped = true;
        second.Tapped = true;
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[0].Morale.Add(new L12MoraleCard { InstanceId = "entry-wuzetian-morale", CardId = "S01-01C1" });
        game.State.Players[1].Field[0][0] = first;
        game.State.Players[1].Field[0][1] = second;

        QueueEnter(game, source);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: mode.PromptId, Choice: "mode:use")).Accepted);
        Resolve(game, "entry-wuzetian-morale");
        var targets = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: targets.PromptId,
            CardInstanceIds: [first.InstanceId, second.InstanceId])).Accepted);

        var transformed = Card("S01-0002", second.InstanceId, cardType: "artifact");
        transformed.Tapped = true;
        game.State.Players[1].Field[0][1] = transformed;
        PassResponses(game);

        Assert.Equal(game.State.Round + 1, first.CannotUntapUntilRound);
        Assert.Equal(0, transformed.CannotUntapUntilRound);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("已声明对象在逆结算后失效", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:public-enemy-legion-v2-recovery")]
    public void RestoredResponseWindowStillRevalidatesTheDeclaredTargetAndRejectsRepeatSubmission()
    {
        var game = Create(9109);
        var source = Card("S01-0402", "entry-nobunaga-restore");
        var target = Card("S01-0001", "entry-nobunaga-restore-target", cost: 4);
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[0][0] = target;

        QueueEnter(game, source);
        Resolve(game, target.InstanceId);
        var responseId = Assert.Single(game.State.PendingPrompts).PromptId;
        game = RestoreAsV2(game);
        var transformed = Card("S01-0001", target.InstanceId, cardType: "artifact");
        game.State.Players[1].Field[0][0] = transformed;
        PassResponses(game);

        Assert.Same(transformed, game.State.Players[1].Field[0][0]);
        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: responseId, Choice: "pass")).Accepted);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("不再符合条件", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:public-enemy-legion-cost-paid-after-state-change")]
    public void ClaudiaPaysHerDeclaredRuneCostButDoesNotModifyATargetThatIsNoLongerALegion()
    {
        var game = Create(9110);
        var source = Card("S02-0619", "entry-claudia");
        var target = Card("S01-0001", "entry-claudia-target");
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[0].SpecialZones.Runes = 1;
        game.State.Players[1].Field[0][0] = target;

        QueueEnter(game, source);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: mode.PromptId, Choice: "mode:use")).Accepted);
        var runeCost = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: runeCost.PromptId, Choice: "rune-count:1")).Accepted);
        Resolve(game, target.InstanceId);

        var transformed = Card("S01-0001", target.InstanceId, cardType: "artifact");
        game.State.Players[1].Field[0][0] = transformed;
        PassResponses(game);

        Assert.Equal(0, game.State.Players[0].SpecialZones.Runes);
        Assert.Equal(1000, transformed.Troops);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("不再符合条件", StringComparison.Ordinal));
    }
}
