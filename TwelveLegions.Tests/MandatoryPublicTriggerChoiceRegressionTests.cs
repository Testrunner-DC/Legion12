using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class MandatoryPublicTriggerChoiceRegressionTests
{
    private static readonly string[] DeclineAliases = ["skip", "mode:none", "decline", "refuse", "cancel"];
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    public static IEnumerable<object[]> MandatoryMatrix()
    {
        foreach (var effect in new[] { "tsukuyomi", "thutmose", "hiromasa" })
        foreach (var controller in new[] { 0, 1 })
        foreach (var restore in new[] { false, true })
            yield return [effect, controller, restore];
    }

    public static IEnumerable<object[]> NoObjectMatrix()
    {
        foreach (var effect in new[] { "tsukuyomi", "thutmose", "hiromasa" })
        foreach (var controller in new[] { 0, 1 })
            yield return [effect, controller];
    }

    [Theory]
    [MemberData(nameof(MandatoryMatrix))]
    [Trait("L12Evidence", "entry:mandatory-public-trigger-reject-decline")]
    public void MandatoryPublicTargetRejectsEveryDeclineAliasWithoutMutation(
        string effect, int controller, bool restore)
    {
        var prepared = PrepareMandatory(effect, controller);
        var game = restore ? Restore(prepared.Game) : prepared.Game;
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", prompt.Continuation);
        Assert.Contains(prepared.SelectionId, prompt.ValidChoices);
        Assert.All(DeclineAliases, decline => Assert.DoesNotContain(decline, prompt.ValidChoices));
        Assert.All(Assert.Single(game.State.PendingActivations).SelectionSteps,
            step => Assert.Equal(L12ActivationCancellationPolicy.NotAllowed, step.CancellationPolicy));

        foreach (var decline in DeclineAliases)
        {
            var before = game.SerializeFullState();
            var rejected = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: decline));
            Assert.False(rejected.Accepted);
            Assert.Equal(before, game.SerializeFullState());
        }
    }

    [Theory]
    [MemberData(nameof(MandatoryMatrix))]
    [Trait("L12Evidence", "entry:mandatory-public-trigger-legal-resolution")]
    public void MandatoryPublicTargetSettlesForEitherControllerAndAcrossRestore(
        string effect, int controller, bool restore)
    {
        var prepared = PrepareMandatory(effect, controller);
        var game = restore ? Restore(prepared.Game) : prepared.Game;
        var prompt = Assert.Single(game.State.PendingPrompts);
        var resolved = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: prepared.SelectionId));
        Assert.True(resolved.Accepted, resolved.Error);
        PassResponses(game);

        switch (effect)
        {
            case "tsukuyomi":
                Assert.False(Assert.Single(game.State.Players[controller].Morale,
                    card => card.InstanceId == prepared.SelectionId).Tapped);
                DeclineTsukuyomiFollowIfPresent(game, controller);
                break;
            case "thutmose":
                Assert.DoesNotContain(game.State.Players[1 - controller].Field.SelectMany(row => row),
                    card => card?.InstanceId == prepared.SelectionId);
                Assert.Contains(game.State.Players[1 - controller].Graveyard,
                    card => card.InstanceId == prepared.SelectionId);
                break;
            case "hiromasa":
                var counter = Assert.Single(game.State.Players[1 - controller].Field.SelectMany(row => row),
                    card => card?.InstanceId == prepared.SelectionId);
                Assert.Equal(game.State.Round, counter!.CannotRespondUntilRound);
                break;
            default:
                throw new Xunit.Sdk.XunitException($"未知必发夹具：{effect}");
        }

        Assert.DoesNotContain(game.State.PendingActivations,
            activation => activation.SourceInstanceId == prepared.SourceId);
    }

    [Theory]
    [MemberData(nameof(NoObjectMatrix))]
    [Trait("L12Evidence", "entry:mandatory-public-trigger-no-object-natural-end")]
    public void MandatoryPublicTargetWithNoLegalObjectEndsWithoutDeadPrompt(
        string effect, int controller)
    {
        var game = effect switch
        {
            "tsukuyomi" => BeginTsukuyomi(controller, includeReadyMorale: false).Game,
            "thutmose" => BeginThutmose(controller, includeTarget: false).Game,
            "hiromasa" => BeginHiromasa(controller, includeCounter: false).Game,
            _ => throw new Xunit.Sdk.XunitException($"未知无对象夹具：{effect}"),
        };

        DrainExplicitDeclinesAndResponses(game);

        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.EffectStack);
    }

    [Theory]
    [InlineData("tsukuyomi-follow")]
    [InlineData("wuzetian-enter")]
    [InlineData("hanxin-attack")]
    [Trait("L12Evidence", "entry:optional-public-trigger-control")]
    public void ExistingOptionalPublicTriggersStillAllowTheirPrintedDecline(string control)
    {
        var game = control switch
        {
            "tsukuyomi-follow" => PrepareTsukuyomiFollowControl(),
            "wuzetian-enter" => PrepareWuzetianControl(),
            "hanxin-attack" => PrepareHanXinControl(),
            _ => throw new Xunit.Sdk.XunitException($"未知可选对照：{control}"),
        };
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", prompt.Continuation);
        Assert.Equal(["mode:none", "mode:use"], prompt.ValidChoices);
        Assert.DoesNotContain("skip", prompt.ValidChoices);

        var declined = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "mode:none"));
        Assert.True(declined.Accepted, declined.Error);
    }

    private static Prepared PrepareMandatory(string effect, int controller)
        => effect switch
        {
            "tsukuyomi" => BeginTsukuyomi(controller, includeReadyMorale: true),
            "thutmose" => BeginThutmose(controller, includeTarget: true),
            "hiromasa" => BeginHiromasa(controller, includeCounter: true),
            _ => throw new Xunit.Sdk.XunitException($"未知必发夹具：{effect}"),
        };

    private static Prepared BeginTsukuyomi(int controller, bool includeReadyMorale)
    {
        var game = Create(2026102201 + controller + (includeReadyMorale ? 0 : 10), controller, 1, "S02-04M1");
        var player = game.State.Players[controller];
        var moved = Card("S02-0401", $"mandatory-tsukuyomi-moved-{controller}", controller);
        var other = Card("S02-0402", $"mandatory-tsukuyomi-other-{controller}", controller);
        player.Field[0][0] = moved;
        player.Field[0][2] = other;
        var moraleId = $"mandatory-tsukuyomi-morale-{controller}";
        if (includeReadyMorale)
            player.Morale.Add(new L12MoraleCard { CardId = "S02-04C1", InstanceId = moraleId });
        else
            player.TemporaryMorale = 1;

        var move = game.Handle(controller, new L12Command("move", moved.InstanceId, Row: 1, Slot: 0));
        Assert.True(move.Accepted, move.Error);
        ResolveTsukuyomiOrder(game);
        return new Prepared(game, moved.InstanceId, moraleId);
    }

    private static Prepared BeginThutmose(int controller, bool includeTarget)
    {
        var game = Create(2026102221 + controller + (includeTarget ? 0 : 10), controller, 2);
        var player = game.State.Players[controller];
        var source = Card("S01-0201", $"mandatory-thutmose-source-{controller}", controller);
        var targetId = $"mandatory-thutmose-target-{controller}";
        player.Hand.Add(source);
        if (includeTarget)
            game.State.Players[1 - controller].Field[0][0] =
                Card("S01-0001", targetId, 1 - controller, troops: 5000);
        AddMorale(player, source.Cost, "S01-02C1", $"mandatory-thutmose-morale-{controller}");

        var play = game.Handle(controller, new L12Command("playCard", source.InstanceId, Row: 0, Slot: 0));
        Assert.True(play.Accepted, play.Error);
        PassResponses(game);
        return new Prepared(game, source.InstanceId, targetId);
    }

    private static Prepared BeginHiromasa(int controller, bool includeCounter)
    {
        var game = Create(2026102241 + controller + (includeCounter ? 0 : 10), controller, 1);
        var source = Card("S01-0413", $"mandatory-hiromasa-source-{controller}", controller);
        var counterId = $"mandatory-hiromasa-counter-{controller}";
        game.State.Players[controller].Field[0][0] = source;
        if (includeCounter)
        {
            var counter = Card("S01-0019", counterId, 1 - controller, cardType: "tactic", isCounterTactic: true);
            counter.Hidden = true;
            counter.SetRound = 0;
            game.State.Players[1 - controller].Field[1][1] = counter;
        }

        var attack = game.Handle(controller, new L12Command("attack", source.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(attack.Accepted, attack.Error);
        if (!includeCounter) PassResponses(game);
        return new Prepared(game, source.InstanceId, counterId);
    }

    private static L12GameEngine PrepareTsukuyomiFollowControl()
    {
        var prepared = BeginTsukuyomi(0, includeReadyMorale: true);
        var game = prepared.Game;
        var ready = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: ready.PromptId,
            Choice: prepared.SelectionId)).Accepted);
        PassResponses(game);
        return game;
    }

    private static L12GameEngine PrepareWuzetianControl()
    {
        var game = Create(2026102261, 0, 0);
        var source = Card("S01-0102", "optional-wuzetian", 0);
        var target = Card("S01-0001", "optional-wuzetian-target", 1, troops: 1000);
        target.Tapped = true;
        game.State.Players[0].Hand.Add(source);
        game.State.Players[1].Field[0][0] = target;
        AddMorale(game.State.Players[0], source.Cost, "S01-01C1", "optional-wuzetian-morale");
        var play = game.Handle(0, new L12Command("playCard", source.InstanceId, Row: 0, Slot: 0));
        Assert.True(play.Accepted, play.Error);
        PassResponses(game);
        return game;
    }

    private static L12GameEngine PrepareHanXinControl()
    {
        var game = Create(2026102262, 0, 0);
        var source = Card("S01-0104", "optional-hanxin", 0);
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[0].Morale.Add(new L12MoraleCard
        {
            CardId = "S01-01C1",
            InstanceId = "optional-hanxin-morale",
        });
        var attack = game.Handle(0, new L12Command("attack", source.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(attack.Accepted, attack.Error);
        return game;
    }

    private static void ResolveTsukuyomiOrder(L12GameEngine game)
    {
        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-order", order.Kind);
        var ready = Assert.Single(order.ValidChoices,
            id => order.Data[$"trigger:{id}"] == "friendly-front-to-back");
        var follow = Assert.Single(order.ValidChoices,
            id => order.Data[$"trigger:{id}"] == "friendly-legion-moves");
        var result = game.Handle(order.PlayerIndex, new L12Command("resolvePrompt", PromptId: order.PromptId,
            CardInstanceIds: [follow, ready]));
        Assert.True(result.Accepted, result.Error);
    }

    private static void DeclineTsukuyomiFollowIfPresent(L12GameEngine game, int controller)
    {
        if (game.State.PendingPrompts.FirstOrDefault() is not { } prompt) return;
        Assert.Equal(["mode:none", "mode:use"], prompt.ValidChoices);
        var result = game.Handle(controller,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "mode:none"));
        Assert.True(result.Accepted, result.Error);
    }

    private static void DrainExplicitDeclinesAndResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100 && game.State.PendingPrompts.Count > 0; safety++)
        {
            var prompt = game.State.PendingPrompts[0];
            string choice;
            if (prompt.Kind == "response") choice = "pass";
            else if (prompt.ValidChoices.Contains("mode:none")) choice = "mode:none";
            else throw new Xunit.Sdk.XunitException(
                $"无合法对象后遗留不可自然结束的提示：{prompt.Kind}/{prompt.Continuation}");
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
            Assert.True(result.Accepted, result.Error);
        }
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
        {
            var prompt = game.State.PendingPrompts[0];
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
    }

    private static void AddMorale(L12PlayerState player, int count, string cardId, string prefix)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard { CardId = cardId, InstanceId = $"{prefix}-{index}" });
    }

    private static L12GameEngine Create(int seed, int controller, int deckIndex, string? masterId = null)
    {
        var original = Catalog.DeckAt(deckIndex);
        var selected = masterId is null ? original : new L12PresetDeckDefinition
        {
            Name = $"Mandatory public trigger {masterId}",
            MasterId = masterId,
            CardIds = [.. original.CardIds],
            MoraleIds = [.. original.MoraleIds],
            SpecialIds = [.. original.SpecialIds],
        };
        var decks = controller == 0 ? new[] { selected, original } : new[] { original, selected };
        var game = new L12GameEngine(Catalog, "mandatory-public-trigger", $"MANDATORY-{seed}", seed,
            ["甲", "乙"], decks, skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = controller;
        game.State.FirstPlayer = controller;
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
            player.Removed.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.UsedAbilities.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner,
        int? troops = null, string? cardType = null, bool? isCounterTactic = null)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = cardId,
            Name = definition.NameZh,
            CardType = cardType ?? definition.CardType,
            IsCounterTactic = isCounterTactic ?? false,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            BaseTroops = troops ?? definition.Troops ?? 0,
            Troops = troops ?? definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            OwnerIndex = owner,
            SummonRound = -1,
        };
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private sealed record Prepared(L12GameEngine Game, string SourceId, string SelectionId);
}
