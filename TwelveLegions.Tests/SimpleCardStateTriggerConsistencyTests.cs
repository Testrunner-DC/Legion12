using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SimpleCardStateTriggerConsistencyTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "simple-card-state-trigger", "CARD-STATE", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
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
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Library.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner = 0)
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
            SummonRound = -1,
            OwnerIndex = owner,
        };
    }

    private static object? Invoke(object target, string method, params object?[] args)
    {
        var candidate = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(info => info.Name == method && info.GetParameters().Length == args.Length);
        return candidate.Invoke(target, args);
    }

    private static void Queue(L12GameEngine game, L12CardInstance source, string trigger,
        Dictionary<string, string>? data = null)
        => Invoke(game, "QueueOrPushTriggeredEffect", 0, source, trigger,
            "单段军团状态一致性测试", null, data ?? new Dictionary<string, string>());

    private static void Choose(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void ChooseCard(L12GameEngine game, string instanceId)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: [instanceId]));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100
             && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
        {
            var prompt = game.State.PendingPrompts.First(item => item.Kind == "response");
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-card-state-trigger-spec")]
    public void ExactStateTriggersOwnOneDefinitionProgramAndSettlementScene()
    {
        var expected = new[]
        {
            ("S01-0210", 2, "enter", "ready", false, "controller-legion"),
            ("S01-0313", 3, "death", "rest", true, "opponent-legion"),
            ("S02-0002", 2, "after-kill", "ready", true, "source"),
            ("ST05-07", 1, "enter", "ready", true, "controller-legion"),
        };
        Assert.Equal(expected, L12SimpleCardStateTriggerEffects.All.Select(spec =>
            (spec.CardId, spec.AbilitySequence, spec.Trigger, spec.Operation, spec.Optional, spec.TargetScope)));

        foreach (var spec in L12SimpleCardStateTriggerEffects.All)
        {
            var ability = Catalog.AtomicEffects.Find(spec.CardId)!.Abilities
                .Single(item => item.Sequence == spec.AbilitySequence);
            var scene = Assert.Single(ability.Presentations,
                item => item.Flow == L12SingleSegmentTriggeredEffectPresentations.Flow);
            Assert.Equal(spec.SettlementText, scene.DefaultText);
            var program = Assert.IsType<L12VerifiedAtomicProgram>(
                L12VerifiedAtomicPrograms.Find(spec.CardId, spec.Trigger));
            Assert.Equal(spec.Optional, program.Atoms.Any(atom => atom.Kind == L12AtomKinds.Optional));
            Assert.Equal(spec.CandidateCondition is not null,
                program.Atoms.Any(atom => atom.Kind == L12AtomKinds.Condition));
            Assert.Equal(spec.TargetScope != L12SimpleCardStateTriggerEffects.Source,
                program.Atoms.Any(atom => atom.Kind == L12AtomKinds.SelectTarget));
            Assert.Single(program.Atoms, atom => atom.Kind == (spec.Operation == L12SimpleCardStateTriggerEffects.Ready
                ? L12AtomKinds.Ready : L12AtomKinds.Rest));
            Assert.Equal(spec.SettlementText, L12GameEngine.ResolveTriggeredEffectDisplayText(
                Card(spec.CardId, $"display-{spec.CardId}"), spec.Trigger, "旧文本"));
        }
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0210")]
    [Trait("L12Evidence", "entry:simple-card-state-trigger-required-click")]
    public void MandatoryNitocrisWithOneTargetStillRequiresClickAndUsesReadyAuthorityEvent()
    {
        var game = Create(11101);
        var source = Card("S01-0210", "nitocris-source");
        var guard = Card("S01-0212", "nitocris-only-guard");
        guard.Tapped = true;
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[0].Field[0][1] = guard;

        Queue(game, source, "enter");

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Equal([guard.InstanceId], declaration.ValidChoices);
        Assert.Empty(game.State.EffectStack);
        ChooseCard(game, guard.InstanceId);
        PassResponses(game);

        Assert.False(guard.Tapped);
        Assert.Contains(game.State.AuthorityEvents, entry => entry.Type == "effect-ready"
            && entry.TargetInstanceId == guard.InstanceId && entry.Resolved);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.CardId == "S01-0210"));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0210")]
    [Trait("L12Evidence", "entry:effect-ready-restriction-candidate")]
    public void MandatoryNitocrisSilentlySkipsWhenEveryRestedTargetCannotReadyByEffect()
    {
        var game = Create(11117);
        var source = Card("S01-0210", "nitocris-blocked-source");
        var guard = Card("S01-0212", "nitocris-blocked-guard");
        guard.Tapped = true;
        guard.CannotReadyByEffectUntilTurn = game.State.TurnSerial;
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[0].Field[0][1] = guard;

        Queue(game, source, "enter");

        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.True(guard.Tapped);
    }

    [Fact]
    [Trait("L12Evidence", "entry:effect-ready-restriction-settlement")]
    [Trait("L12Evidence", "entry:effect-ready-restriction-reconnect")]
    public void ReadyAuthorityRejectsTargetThatBecomesBlockedDuringItsResponseWindow()
    {
        var game = Create(11118);
        var source = Card("S01-0210", "ready-blocked-source");
        var guard = Card("S01-0212", "ready-blocked-target");
        guard.Tapped = true;
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[0].Field[0][1] = guard;
        Queue(game, source, "enter");
        ChooseCard(game, guard.InstanceId);
        Choose(game, "pass");
        Choose(game, "pass");
        Assert.Equal("authority-event", Assert.Single(game.State.EffectStack).Trigger);
        guard.CannotReadyByEffectUntilTurn = game.State.TurnSerial;

        game = L12GameEngine.RestoreCheckpoint(Catalog,
            game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,"),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        PassResponses(game);

        guard = game.State.Players[0].Field[0][1]!;
        Assert.True(guard.Tapped);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("无法因效果转为活跃", StringComparison.Ordinal));
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "failed"
            && entry.EffectText?.Contains("转为活跃", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData("S01-0210", "enter")]
    [InlineData("S01-0313", "death")]
    [InlineData("S02-0002", "after-kill")]
    [InlineData("ST05-07", "enter")]
    [Trait("L12Evidence", "entry:simple-card-state-trigger-no-target")]
    public void MissingConditionOrLegalTargetSilentlySkipsBeforeDeclaration(string cardId, string trigger)
    {
        var game = Create(11102 + cardId[^1]);
        var source = Card(cardId, $"no-target-{cardId}");
        if (trigger == "death") game.State.Players[0].Resolving.Add(source);
        else game.State.Players[0].Field[0][0] = source;
        if (cardId == "ST05-07") game.State.Players[0].HandDiscardedByMasterThisTurn = true;
        var data = cardId == "S02-0002"
            ? new Dictionary<string, string> { ["killed"] = "true", ["combatKillConfirmed"] = "true" }
            : null;

        Queue(game, source, trigger, data);

        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-trigger");
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0313")]
    [Trait("L12Evidence", "entry:simple-card-state-trigger-decline")]
    public void OptionalOddrCanDeclineWithoutCreatingAnEmptyStackAndRejectsDuplicateSubmit()
    {
        var game = Create(11110);
        var source = Card("S01-0313", "oddr-decline");
        var target = Card("S01-0201", "oddr-target");
        game.State.Players[0].Resolving.Add(source);
        game.State.Players[1].Field[0][0] = target;
        Queue(game, source, "death");
        var oldPrompt = Assert.Single(game.State.PendingPrompts);

        Choose(game, "mode:none");

        Assert.Empty(game.State.EffectStack);
        Assert.False(target.Tapped);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-declined"
            && entry.Cards.Any(card => card.CardId == "S01-0313"));
        Assert.False(game.Handle(oldPrompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: oldPrompt.PromptId, Choice: "mode:use")).Accepted);
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-card-state-trigger-revalidate")]
    public void DeclaredOddrTargetThatBecomesRestedBeforeReverseSettlementFailsWithoutRetargeting()
    {
        var game = Create(11111);
        var source = Card("S01-0313", "oddr-revalidate");
        var target = Card("S01-0201", "oddr-declared");
        var other = Card("S01-0202", "oddr-other");
        game.State.Players[0].Resolving.Add(source);
        game.State.Players[1].Field[0][0] = target;
        game.State.Players[1].Field[0][1] = other;
        Queue(game, source, "death");
        Choose(game, "mode:use");
        ChooseCard(game, target.InstanceId);
        target.Tapped = true;

        PassResponses(game);

        Assert.True(target.Tapped);
        Assert.False(other.Tapped);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("不再处于要求的活跃状态", StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("不再处于要求的活跃状态", StringComparison.Ordinal));
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "failed"
            && entry.Cards.Any(card => card.CardId == "S01-0313"));
    }

    [Fact]
    [Trait("L12Evidence", "card:ST05-07")]
    [Trait("L12Evidence", "entry:simple-card-state-trigger-condition-and-faction")]
    public void AntinousRequiresLockedMasterDiscardAndOnlyOffersRestedOlympusLegions()
    {
        var game = Create(11112);
        var source = Card("ST05-07", "antinous-source");
        var olympus = Card("ST05-01", "antinous-olympus");
        var otherworld = Card("ST06-02", "antinous-otherworld");
        olympus.Tapped = true;
        otherworld.Tapped = true;
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[0].Field[0][1] = olympus;
        game.State.Players[0].Field[0][2] = otherworld;

        Queue(game, source, "enter");
        Assert.Empty(game.State.PendingPrompts);
        game.State.Players[0].HandDiscardedByMasterThisTurn = true;
        Queue(game, source, "enter");
        Choose(game, "mode:use");
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(olympus.InstanceId, targetPrompt.ValidChoices);
        Assert.DoesNotContain(otherworld.InstanceId, targetPrompt.ValidChoices);
        ChooseCard(game, olympus.InstanceId);
        PassResponses(game);

        Assert.False(olympus.Tapped);
        Assert.True(otherworld.Tapped);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0002")]
    [Trait("L12Evidence", "entry:simple-card-state-trigger-once-reconnect")]
    public void AliceRequiresAuthoritativeKillAndCommitsOnceBeforeReconnectSettlement()
    {
        var game = Create(11113);
        var source = Card("S02-0002", "alice-source");
        source.Tapped = true;
        game.State.Players[0].Field[0][0] = source;
        Queue(game, source, "after-kill", new Dictionary<string, string> { ["killed"] = "true" });
        Assert.Empty(game.State.PendingPrompts);

        Queue(game, source, "after-kill", new Dictionary<string, string>
        {
            ["killed"] = "true", ["combatKillConfirmed"] = "true",
        });
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(declaration.PromptId, JsonSerializer.Serialize(game.SnapshotFor(0)),
            StringComparison.Ordinal);
        Choose(game, "mode:use");
        var onceKey = $"alice-ready:{source.InstanceId}:{game.State.TurnSerial}";
        Assert.Contains(onceKey, game.State.Players[0].UsedAbilities);
        Assert.DoesNotContain($"{onceKey}:pending", game.State.Players[0].UsedAbilities);
        var oldResponse = Assert.Single(game.State.PendingPrompts, item => item.Kind == "response");
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);

        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        PassResponses(game);

        Assert.False(game.State.Players[0].Field[0][0]!.Tapped);
        Assert.Single(game.State.AuthorityEvents, entry => entry.Type == "effect-ready"
            && entry.TargetInstanceId == source.InstanceId && entry.Resolved);
        Assert.False(game.Handle(oldResponse.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: oldResponse.PromptId, Choice: "pass")).Accepted);
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-card-state-trigger-negated")]
    public void NegatedStateTriggerDoesNotChangeTheDeclaredCard()
    {
        var game = Create(11114);
        var source = Card("S01-0313", "oddr-negated");
        var target = Card("S01-0201", "oddr-negated-target");
        game.State.Players[0].Resolving.Add(source);
        game.State.Players[1].Field[0][0] = target;
        Queue(game, source, "death");
        Choose(game, "mode:use");
        ChooseCard(game, target.InstanceId);
        Assert.Single(game.State.EffectStack).Negated = true;

        PassResponses(game);

        Assert.False(target.Tapped);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "negated"
            && entry.Cards.Any(card => card.CardId == "S01-0313"));
    }

    [Fact]
    [Trait("L12Evidence", "entry:effect-ready-authority-revalidate")]
    public void ReadyAuthorityEventRevalidatesTheRestedTargetAfterItsOwnResponseWindow()
    {
        var game = Create(11115);
        var source = Card("S01-0210", "ready-authority-source");
        var guard = Card("S01-0212", "ready-authority-target");
        guard.Tapped = true;
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[0].Field[0][1] = guard;
        Queue(game, source, "enter");
        ChooseCard(game, guard.InstanceId);

        Choose(game, "pass");
        Choose(game, "pass");
        var readyResponse = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("response", readyResponse.Kind);
        Assert.Equal("authority-event", Assert.Single(game.State.EffectStack).Trigger);
        guard.Tapped = false;
        Choose(game, "pass");
        Choose(game, "pass");

        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("已不再为休整状态", StringComparison.Ordinal));
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "failed"
            && entry.EffectText?.Contains("转为活跃", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData("field", "normal")]
    [InlineData("relic", "normal")]
    [InlineData("morale", "normal")]
    [InlineData("field", "moved")]
    [InlineData("relic", "moved")]
    [InlineData("morale", "moved")]
    [InlineData("field", "ready")]
    [InlineData("relic", "ready")]
    [InlineData("morale", "ready")]
    [InlineData("field", "negated")]
    [InlineData("relic", "negated")]
    [InlineData("morale", "negated")]
    public void ReadyAuthorityBindsOriginalZoneAcrossRecoveryAndRejectsDuplicate(string zone, string change)
    {
        var game = Create(11116);
        var player = game.State.Players[0];
        var source = Card("S01-0210", "ready-zone-source");
        var target = Card(zone == "relic" ? "S01-0417" : "S01-0212", "ready-zone-target");
        target.Troops = 3000;
        target.Tapped = true;
        var morale = new L12MoraleCard { CardId = "ST05-C1", InstanceId = "ready-zone-morale", Tapped = true };
        player.Field[0][0] = source;
        if (zone == "field") player.Field[0][1] = target;
        else if (zone == "relic") player.Relic = target;
        else player.Morale.Add(morale);

        if (zone == "morale") Invoke(game, "ReadyMoraleByEffect", 0, source, morale, "测试士气转为活跃");
        else Invoke(game, "ReadyCardByEffect", 0, source, target, "测试卡牌转为活跃", null);
        Assert.Equal(zone, Assert.Single(game.State.AuthorityEvents, e => e.Type == "effect-ready").OriginZone);
        var pending = Assert.Single(game.State.PendingPrompts);
        if (change == "moved")
        {
            if (zone == "field") { player.Field[0][1] = null; player.Relic = target; }
            else if (zone == "relic") { player.Relic = null; player.Field[0][1] = target; }
            else { player.Morale.Remove(morale); player.MoraleDeck.Add(morale); }
        }
        else if (change == "ready") { target.Tapped = false; morale.Tapped = false; }
        else if (change == "negated") Assert.Single(game.State.EffectStack).Negated = true;

        game = L12GameEngine.RestoreCheckpoint(Catalog,
            game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,"),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        var item = Assert.Single(game.State.EffectStack);
        PassResponses(game);
        player = game.State.Players[0];
        var tapped = zone == "morale"
            ? player.Morale.Concat(player.MoraleDeck).Single(c => c.InstanceId == morale.InstanceId).Tapped
            : (player.Relic?.InstanceId == target.InstanceId ? player.Relic : player.Field[0][1])!.Tapped;
        Assert.Equal(change is "moved" or "negated", tapped);
        if (change is "moved" or "ready")
        {
            Assert.Equal("failed", item.Data["effectResultStatus"]);
            Assert.Single(game.State.Events, e => e.Type == "effect-failed");
        }
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.PendingPrompts);
        Assert.False(game.Handle(pending.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: pending.PromptId, Choice: "pass")).Accepted);
    }
}
