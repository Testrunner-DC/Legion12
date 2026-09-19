using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

/// <summary>
/// 跨响应公开声明的位移对象必须在结算时仍为同一张公开军团，并且声明位置仍可用。
/// 已支付费用不退；多对象彼此独立时，失效对象不妨碍其余合法位移。
/// </summary>
public sealed class PublicDeclaredMovementRevalidationTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, string? firstMaster = null)
    {
        var first = Catalog.DeckAt(0);
        if (firstMaster is not null)
            first = new L12PresetDeckDefinition
            {
                Name = $"{firstMaster}公开位移复验牌库",
                MasterId = firstMaster,
                CardIds = [.. first.CardIds],
                MoraleIds = [.. first.MoraleIds],
                SpecialIds = [],
            };
        var game = new L12GameEngine(Catalog, "public-declared-movement", "PUBLIC-MOVE", seed,
            ["甲", "乙"], [first, Catalog.DeckAt(0)], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
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
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, string? cardType = null)
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
            BaseTroops = definition.Troops ?? 1000,
            Troops = definition.Troops ?? 1000,
            SummonRound = -1,
        };
    }

    private static void QueueTrigger(L12GameEngine game, int controller, L12CardInstance source,
        string trigger, Dictionary<string, string>? data = null)
    {
        var create = typeof(L12GameEngine).GetMethod("CreateTriggerCandidate",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var queue = typeof(L12GameEngine).GetMethod("QueueTriggerCandidates",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var candidate = Assert.IsType<L12TriggerCandidate>(create.Invoke(game,
            [controller, source, trigger, $"{source.Name}公开位移复验", data, source]));
        queue.Invoke(game, [(object)new[] { candidate }]);
    }

    private static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static L12Prompt ResolveCards(L12GameEngine game, params string[] choices)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: [.. choices]));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game, int maximum = 24)
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

    private static L12GameEngine RestoreAsV2(L12GameEngine game)
    {
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        return L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
    }

    private static void AssertFailedWithoutCancellation(L12GameEngine game, string sourceName)
    {
        var summary = string.Join("\n", game.State.Events.Select(entry => $"{entry.Type}: {entry.Text}"));
        Assert.True(game.State.Events.Any(entry => entry.Type == "effect-failed"
            && entry.Text.Contains(sourceName, StringComparison.Ordinal)), summary);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains(sourceName, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0206")]
    [Trait("L12Evidence", "entry:public-move-current-type")]
    public void SaladinDoesNotMoveDeclaredTombGuardAfterItBecomesAnArtifact()
    {
        var game = Create(9401);
        var player = game.State.Players[0];
        var source = Card("S01-0206", "public-move-saladin");
        var target = Card("S01-0212", "public-move-saladin-target");
        player.Field[0][2] = source;
        player.Field[0][0] = target;

        var attack = game.Handle(0, new L12Command("attack", source.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(attack.Accepted, attack.Error);
        Resolve(game, target.InstanceId);
        Resolve(game, "1:0");
        Assert.Single(game.State.EffectStack);

        var transformed = Card(target.CardId, target.InstanceId, cardType: "artifact");
        player.Field[0][0] = transformed;
        PassResponses(game);

        Assert.Same(transformed, player.Field[0][0]);
        Assert.Null(player.Field[1][0]);
        AssertFailedWithoutCancellation(game, source.Name);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0407")]
    [Trait("L12Evidence", "entry:public-move-partial-revalidation")]
    public void RyomaSkipsInvalidDeclaredObjectButMovesIndependentLegalObject()
    {
        var game = Create(9402);
        var player = game.State.Players[0];
        var source = Card("S01-0407", "public-move-ryoma");
        var invalid = Card("S01-0401", "public-move-ryoma-invalid");
        var legal = Card("S01-0402", "public-move-ryoma-legal");
        player.Field[0][1] = source;
        player.Field[0][0] = invalid;
        player.Field[1][0] = legal;

        QueueTrigger(game, 0, source, "enter");
        Resolve(game, "mode:use");
        ResolveCards(game, invalid.InstanceId, legal.InstanceId);
        Resolve(game, "0:2");
        Resolve(game, "1:2");
        Assert.Single(game.State.EffectStack);

        game = RestoreAsV2(game);
        player = game.State.Players[0];
        invalid = Assert.IsType<L12CardInstance>(player.Field[0][0]);
        legal = Assert.IsType<L12CardInstance>(player.Field[1][0]);
        var transformed = Card(invalid.CardId, invalid.InstanceId, cardType: "artifact");
        player.Field[0][0] = transformed;
        PassResponses(game);

        Assert.Same(transformed, player.Field[0][0]);
        Assert.Null(player.Field[0][2]);
        Assert.Null(player.Field[1][0]);
        Assert.Same(legal, player.Field[1][2]);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("1个已声明对象", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-04M1")]
    [Trait("L12Evidence", "entry:public-move-v2-current-type")]
    public void TsukuyomiV2KeepsPaidCostButDoesNotMoveTargetThatBecomesAnArtifact()
    {
        var game = Create(9403, "S02-04M1");
        var player = game.State.Players[0];
        var source = Card("S02-04M1", "public-move-tsukuyomi");
        var moved = Card("S02-0401", "public-move-tsukuyomi-origin");
        var target = Card("S02-0402", "public-move-tsukuyomi-target");
        player.Field[1][0] = moved;
        player.Field[0][2] = target;
        player.Morale.Add(new L12MoraleCard
        {
            CardId = "S02-04C1", InstanceId = "public-move-tsukuyomi-cost", Tapped = false,
        });

        QueueTrigger(game, 0, source, "friendly-legion-moves",
            new Dictionary<string, string> { ["ability"] = "tsukuyomiFollowMove", ["moved"] = moved.InstanceId });
        Resolve(game, "mode:use");
        Resolve(game, target.InstanceId);
        Resolve(game, "0:1");
        Assert.True(player.Morale.Single().Tapped);
        Assert.Single(game.State.EffectStack);

        game = RestoreAsV2(game);
        player = game.State.Players[0];
        var restored = Assert.IsType<L12CardInstance>(player.Field[0][2]);
        var transformed = Card(restored.CardId, restored.InstanceId, cardType: "artifact");
        player.Field[0][2] = transformed;
        PassResponses(game);

        Assert.True(player.Morale.Single().Tapped);
        Assert.Same(transformed, player.Field[0][2]);
        Assert.Null(player.Field[0][1]);
        Assert.Empty(transformed.TimedModifiers);
        AssertFailedWithoutCancellation(game, source.Name);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0206")]
    [Trait("L12Evidence", "entry:public-move-current-destination")]
    public void SaladinDoesNotOverwriteACardThatOccupiesTheDeclaredDestinationDuringResponses()
    {
        var game = Create(9404);
        var player = game.State.Players[0];
        var source = Card("S01-0206", "public-move-saladin-slot-source");
        var target = Card("S01-0212", "public-move-saladin-slot-target");
        player.Field[0][2] = source;
        player.Field[0][0] = target;

        var attack = game.Handle(0, new L12Command("attack", source.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(attack.Accepted, attack.Error);
        Resolve(game, target.InstanceId);
        Resolve(game, "1:0");
        var blocker = Card("S01-0001", "public-move-saladin-slot-blocker");
        player.Field[1][0] = blocker;
        PassResponses(game);

        Assert.Same(target, player.Field[0][0]);
        Assert.Same(blocker, player.Field[1][0]);
        AssertFailedWithoutCancellation(game, source.Name);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0206:death")]
    [Trait("L12Evidence", "entry:public-move-death-current-type")]
    public void SaladinDeathUsesTheSameMovementRevalidation()
    {
        var game = Create(9405);
        var player = game.State.Players[0];
        var source = Card("S01-0206", "public-move-saladin-death-source");
        var target = Card("S01-0212", "public-move-saladin-death-target");
        player.Graveyard.Add(source);
        player.Field[0][0] = target;

        QueueTrigger(game, 0, source, "death");
        ResolveCards(game, target.InstanceId);
        Resolve(game, "1:0");
        Assert.Single(game.State.EffectStack);

        var transformed = Card(target.CardId, target.InstanceId, cardType: "artifact");
        player.Field[0][0] = transformed;
        PassResponses(game);

        Assert.Same(transformed, player.Field[0][0]);
        Assert.Null(player.Field[1][0]);
        AssertFailedWithoutCancellation(game, source.Name);
    }
}
