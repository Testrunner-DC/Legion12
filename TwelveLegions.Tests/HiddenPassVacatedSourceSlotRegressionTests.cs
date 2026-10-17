using System.Reflection;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class HiddenPassVacatedSourceSlotRegressionTests
{
    private static readonly L12Catalog Catalog =
        L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-17T00:00:00Z");

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "card:ST01-10")]
    [Trait("L12Evidence", "entry:set-source-vacated-slot")]
    public void RealOpponentAttackLetsHiddenPassDeclareItsSourceSlotAndKeepsV2Exact(
        int ownerIndex, bool restoreBeforeSlot)
    {
        var fixture = HiddenPassAttack(ownerIndex, leaveAlternativeSlot: true, legacyProtocol: false);
        var game = fixture.Game;

        var mode = AdvanceToHiddenPassDeclaration(game, fixture.Source.InstanceId);
        Assert.Contains("mode:use", mode.ValidChoices);
        Send(game, mode, "mode:use");
        Send(game, OnlyPrompt(game), fixture.Morale.InstanceId);
        Send(game, OnlyPrompt(game), fixture.Entrant.InstanceId);

        if (restoreBeforeSlot)
            game = RestoreExactV2(game);

        var slot = OnlyPrompt(game);
        Assert.Equal("slot", slot.Kind);
        Assert.Equal(ownerIndex, slot.PlayerIndex);
        Assert.Equal(ownerIndex, slot.Controller);
        Assert.Contains("0:1", slot.ValidChoices);
        Assert.Contains("1:2", slot.ValidChoices);
        var slotCommand = Resolve(slot, "1:2");
        Send(game, slotCommand);
        AssertRejectedWithoutChange(game, ownerIndex, slotCommand);

        DrainResponses(game);

        var owner = game.State.Players[ownerIndex];
        Assert.Equal(fixture.Entrant.InstanceId, owner.Field[1][2]?.InstanceId);
        Assert.DoesNotContain(owner.Hand, card => card.InstanceId == fixture.Entrant.InstanceId);
        Assert.Contains(owner.Graveyard, card => card.InstanceId == fixture.Source.InstanceId);
        Assert.Contains(owner.MoraleDeck, card => card.InstanceId == fixture.Morale.InstanceId);
        Assert.Equal(1, CountInstance(game, fixture.Source.InstanceId));
        Assert.Equal(1, CountInstance(game, fixture.Entrant.InstanceId));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("L12Evidence", "card:ST01-10")]
    [Trait("L12Evidence", "boundary:response-occupies-declared-source-slot")]
    public void HiddenPassRechecksItsVacatedSourceSlotAndNeverOverwritesAResponseOccupant(int ownerIndex)
    {
        var fixture = HiddenPassAttack(ownerIndex, leaveAlternativeSlot: false, legacyProtocol: false);
        var game = fixture.Game;

        var mode = AdvanceToHiddenPassDeclaration(game, fixture.Source.InstanceId);
        Assert.Equal(["mode:none", "mode:use"], mode.ValidChoices);
        Send(game, mode, "mode:use");
        Send(game, OnlyPrompt(game), fixture.Morale.InstanceId);
        Send(game, OnlyPrompt(game), fixture.Entrant.InstanceId);
        var slot = OnlyPrompt(game);
        Assert.Equal(["1:2", "skip"], slot.ValidChoices);
        Send(game, slot, "1:2");

        var owner = game.State.Players[ownerIndex];
        Assert.Contains(owner.Resolving, card => card.InstanceId == fixture.Source.InstanceId);
        Assert.Null(owner.Field[1][2]);
        var occupant = PlainLegion("response-slot-occupant", ownerIndex, 2000);
        owner.Field[1][2] = occupant;
        DrainResponses(game);

        Assert.Same(occupant, owner.Field[1][2]);
        Assert.Contains(owner.Hand, card => card.InstanceId == fixture.Entrant.InstanceId);
        Assert.Contains(owner.MoraleDeck, card => card.InstanceId == fixture.Morale.InstanceId);
        Assert.Contains(owner.Graveyard, card => card.InstanceId == fixture.Source.InstanceId);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("已返还的士气不返还", StringComparison.Ordinal));
        Assert.Equal(1, CountInstance(game, occupant.InstanceId));
        Assert.Equal(1, CountInstance(game, fixture.Source.InstanceId));
        Assert.Equal(1, CountInstance(game, fixture.Entrant.InstanceId));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "compatibility:legacy-v2-source-slot")]
    public void LegacyV2HiddenPassKeepsTheOccupiedSetSourceSlotUnavailable()
    {
        var fixture = HiddenPassAttack(0, leaveAlternativeSlot: false, legacyProtocol: true);
        var game = fixture.Game;

        Assert.False(game.State.SourceVacatedEntrySlotProtocolEnabled);
        DrainLegacyCombat(game, fixture.Source.InstanceId);

        var owner = game.State.Players[0];
        Assert.Same(fixture.Source, owner.Field[1][2]);
        Assert.True(fixture.Source.Hidden);
        Assert.Contains(owner.Morale, card => card.InstanceId == fixture.Morale.InstanceId);
        Assert.DoesNotContain(owner.MoraleDeck, card => card.InstanceId == fixture.Morale.InstanceId);
        Assert.Contains(owner.Hand, card => card.InstanceId == fixture.Entrant.InstanceId);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
    }

    [Theory]
    [InlineData("nonfresh")]
    [InlineData("consumed")]
    [InlineData("visible")]
    [InlineData("off-field")]
    [InlineData("foreign-battlefield")]
    [Trait("L12Evidence", "guard:fresh-set-source-slot")]
    public void VacatedSourceSlotGuardNeverBroadensAnIneligibleCandidate(string boundary)
    {
        var game = Create(930940 + boundary.Length, stateFormatVersion: 2);
        var owner = game.State.Players[0];
        var opponent = game.State.Players[1];
        var source = Card("ST01-10", $"guard-source-{boundary}", 0);
        source.Hidden = true;
        source.SetRound = 0;
        owner.Field[1][2] = source;
        FillOtherSlots(owner, except: "0:1");
        FillOtherSlots(opponent, except: "0:1");

        var trigger = boundary == "nonfresh" ? "enter" : "reaction";
        var candidate = Assert.IsType<L12TriggerCandidate>(Invoke(game, "CreateTriggerCandidate",
            0, source, trigger, "来源格守卫", null, null));
        if (boundary == "consumed")
            candidate.Data["counterTacticResponseLifecycleConsumed"] = "true";
        else if (boundary == "visible")
            source.Hidden = false;
        else if (boundary == "off-field")
        {
            owner.Field[1][2] = null;
            owner.Graveyard.Add(source);
        }

        var targetPlayerIndex = boundary == "foreign-battlefield" ? 1 : 0;
        var target = game.State.Players[targetPlayerIndex];
        var expectedOrdinarySlots = Enumerable.Range(0, 2)
            .SelectMany(row => Enumerable.Range(0, 3)
                .Where(slot => target.Field[row][slot] is null)
                .Select(slot => $"{row}:{slot}"))
            .OrderBy(choice => choice, StringComparer.OrdinalIgnoreCase).ToArray();
        var actual = Assert.IsType<List<string>>(Invoke(game,
            "FreshSetCounterEntrySlotsAfterSourceVacates", candidate, targetPlayerIndex));

        Assert.Equal(expectedOrdinarySlots,
            actual.OrderBy(choice => choice, StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("S01-0021", "S01-0208", "hand")]
    [InlineData("S01-0223", "S01-0212", "grave")]
    [Trait("L12Evidence", "family:fresh-set-counter-source-vacates")]
    public void OtherFreshSetCounterEntryPlansExposeAndCommitOnlyTheirOwnVacatedSourceSlot(
        string counterCardId, string entrantCardId, string entrantZone)
    {
        var game = Create(930920 + counterCardId.Length, stateFormatVersion: 2);
        var player = game.State.Players[0];
        var counter = Card(counterCardId, $"source-{counterCardId}", 0);
        counter.Hidden = true;
        counter.SetRound = 0;
        player.Field[1][2] = counter;
        FillOtherSlots(player, except: "1:2");
        var entrant = Card(entrantCardId, $"entrant-{counterCardId}", 0);
        if (entrantZone == "hand") player.Hand.Add(entrant);
        else player.Graveyard.Add(entrant);
        if (counterCardId == "S01-0223")
            player.Library.Add(Card("S01-0201", "gift-draw", 0));

        var candidate = Assert.IsType<L12TriggerCandidate>(Invoke(game, "CreateTriggerCandidate",
            0, counter, "reaction", "真实盖伏反击战术", null, null));
        Invoke(game, "QueueTriggerCandidates", (object)new[] { candidate });

        if (counterCardId == "S01-0223")
            Send(game, OnlyPrompt(game), "mode:use");
        Send(game, OnlyPrompt(game), entrant.InstanceId);
        var slot = OnlyPrompt(game);
        Assert.Equal(counterCardId == "S01-0223" ? ["1:2", "skip"] : ["1:2"], slot.ValidChoices);
        Assert.Equal(0, slot.PlayerIndex);
        Assert.Equal(0, slot.Controller);
        var command = Resolve(slot, "1:2");
        Send(game, command);
        AssertRejectedWithoutChange(game, 0, command);

        Assert.Contains(player.Resolving, card => card.InstanceId == counter.InstanceId);
        Assert.Null(player.Field[1][2]);
        Assert.Single(game.State.EffectStack, item => item.SourceInstanceId == counter.InstanceId);
        Assert.Equal(1, CountInstance(game, counter.InstanceId));
        Assert.Equal(1, CountInstance(game, entrant.InstanceId));
        Assert.DoesNotContain(game.State.Players[1].Field.SelectMany(row => row),
            card => card?.InstanceId == counter.InstanceId);
    }

    private static HiddenPassFixture HiddenPassAttack(int ownerIndex, bool leaveAlternativeSlot,
        bool legacyProtocol)
    {
        var game = Create(930900 + ownerIndex * 10 + (leaveAlternativeSlot ? 1 : 0), stateFormatVersion: 2);
        var attackerIndex = 1 - ownerIndex;
        game.State.ActivePlayer = attackerIndex;
        game.State.FirstPlayer = attackerIndex;
        var owner = game.State.Players[ownerIndex];
        var attackerOwner = game.State.Players[attackerIndex];
        var source = Card("ST01-10", $"hidden-pass-{ownerIndex}", ownerIndex);
        source.Hidden = true;
        source.SetRound = 0;
        owner.Field[1][2] = source;
        FillOtherSlots(owner, except: leaveAlternativeSlot ? "0:1" : "1:2");
        owner.Field[1][2] = source;
        var attacker = PlainLegion($"attacker-{ownerIndex}", attackerIndex, 1000);
        attackerOwner.Field[0][0] = attacker;
        var entrant = Card("ST01-05", $"entrant-{ownerIndex}", ownerIndex);
        owner.Hand.Add(entrant);
        var morale = new L12MoraleCard
        {
            CardId = "ST01-C1",
            InstanceId = $"morale-{ownerIndex}",
        };
        owner.Morale.Add(morale);

        if (legacyProtocol)
            game = RestoreWithoutSourceVacatedProtocol(game);

        var restoredOwner = game.State.Players[ownerIndex];
        var restoredAttacker = Assert.Single(game.State.Players[attackerIndex].Field[0],
            card => card?.InstanceId == attacker.InstanceId)!;
        var attack = game.Handle(attackerIndex, new L12Command("attack", restoredAttacker.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(attack.Accepted, attack.Error);
        return new HiddenPassFixture(game,
            Assert.Single(restoredOwner.Field[1], card => card?.InstanceId == source.InstanceId)!,
            Assert.Single(restoredOwner.Hand, card => card.InstanceId == entrant.InstanceId),
            Assert.Single(restoredOwner.Morale, card => card.InstanceId == morale.InstanceId));
    }

    private static L12Prompt AdvanceToHiddenPassDeclaration(L12GameEngine game, string sourceInstanceId)
    {
        for (var safety = 0; safety < 120; safety++)
        {
            if (game.State.PendingPrompts.FirstOrDefault() is { } prompt)
            {
                if (prompt.SourceInstanceId == sourceInstanceId
                    || prompt.ActivationId is not null && game.State.PendingActivations.Any(activation =>
                        activation.ActivationId == prompt.ActivationId
                        && activation.SourceInstanceId == sourceInstanceId))
                    return prompt;
                if (prompt.Kind == "response")
                    Send(game, prompt, "pass");
                else if (prompt.ValidChoices.Contains("mode:none"))
                    Send(game, prompt, "mode:none");
                else
                    throw new Xunit.Sdk.XunitException(
                        $"Unexpected prompt before Hidden Pass: {prompt.Kind}/{prompt.Continuation}");
                continue;
            }
            if (game.State.PendingDefense is { Stage: L12CombatStage.DefenseChoice } defense)
            {
                var result = game.Handle(1 - defense.AttackerPlayer,
                    new L12Command("resolveDefense", CardInstanceIds: []));
                Assert.True(result.Accepted, result.Error);
                continue;
            }
        }
        throw new Xunit.Sdk.XunitException("Real attack did not reach the Hidden Pass declaration.");
    }

    private static void DrainResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 120; safety++)
        {
            if (game.State.PendingPrompts.FirstOrDefault() is { } prompt)
            {
                if (prompt.Kind == "response")
                    Send(game, prompt, "pass");
                else if (prompt.ValidChoices.Contains("mode:none"))
                    Send(game, prompt, "mode:none");
                else
                    throw new Xunit.Sdk.XunitException(
                        $"Unexpected prompt while draining: {prompt.Kind}/{prompt.Continuation}");
                continue;
            }
            if (game.State.PendingDefense is { Stage: L12CombatStage.DefenseChoice } defense)
            {
                var result = game.Handle(1 - defense.AttackerPlayer,
                    new L12Command("resolveDefense", CardInstanceIds: []));
                Assert.True(result.Accepted, result.Error);
                continue;
            }
            if (game.State.PendingDefense is null && game.State.EffectStack.Count == 0
                && game.State.DeferredEffectStack.Count == 0) return;
        }
        throw new Xunit.Sdk.XunitException("Authority queues did not settle within the safety bound.");
    }

    private static void DrainLegacyCombat(L12GameEngine game, string sourceInstanceId)
    {
        for (var safety = 0; safety < 120; safety++)
        {
            if (game.State.PendingPrompts.FirstOrDefault() is { } prompt)
            {
                if (prompt.SourceInstanceId == sourceInstanceId
                    || prompt.ActivationId is not null && game.State.PendingActivations.Any(activation =>
                        activation.ActivationId == prompt.ActivationId
                        && activation.SourceInstanceId == sourceInstanceId))
                {
                    Assert.Equal(["mode:none"], prompt.ValidChoices);
                    Send(game, prompt, "mode:none");
                }
                else if (prompt.Kind == "response")
                    Send(game, prompt, "pass");
                else if (prompt.ValidChoices.Contains("mode:none"))
                    Send(game, prompt, "mode:none");
                else
                    throw new Xunit.Sdk.XunitException(
                        $"Unexpected legacy prompt: {prompt.Kind}/{prompt.Continuation}");
                continue;
            }
            if (game.State.PendingDefense is { Stage: L12CombatStage.DefenseChoice } defense)
            {
                var result = game.Handle(1 - defense.AttackerPlayer,
                    new L12Command("resolveDefense", CardInstanceIds: []));
                Assert.True(result.Accepted, result.Error);
                continue;
            }
            if (game.State.PendingDefense is null && game.State.EffectStack.Count == 0
                && game.State.DeferredEffectStack.Count == 0) return;
        }
        throw new Xunit.Sdk.XunitException("Legacy combat did not settle within the safety bound.");
    }

    private static L12GameEngine Create(int seed, int stateFormatVersion)
    {
        var basis = Catalog.DeckAt(0);
        var game = new L12GameEngine(Catalog, "vacated-counter-source-slot", "VACATED-SLOT", seed,
            ["甲", "乙"], [basis, basis], skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: stateFormatVersion, utcNow: () => Now);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 5;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Removed.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.UsedAbilities.Clear();
            player.Relic = null;
            player.ExtraRelics.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int ownerIndex)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            CardId = definition.Id,
            InstanceId = instanceId,
            OwnerIndex = ownerIndex,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            SummonRound = -1,
        };
    }

    private static L12CardInstance PlainLegion(string instanceId, int ownerIndex, int troops) => new()
    {
        CardId = $"test-{instanceId}",
        InstanceId = instanceId,
        OwnerIndex = ownerIndex,
        Name = instanceId,
        CardType = "legion",
        Faction = "universal",
        Cost = 1,
        HasPrintedCost = true,
        BaseTroops = troops,
        Troops = troops,
        SummonRound = -1,
    };

    private static void FillOtherSlots(L12PlayerState player, string except)
    {
        for (var row = 0; row < 2; row++)
        for (var slot = 0; slot < 3; slot++)
        {
            var key = $"{row}:{slot}";
            if (key == except || player.Field[row][slot] is not null) continue;
            player.Field[row][slot] = PlainLegion($"occupant-{player.PlayerIndex}-{row}-{slot}",
                player.PlayerIndex, 3000);
        }
    }

    private static L12Prompt OnlyPrompt(L12GameEngine game) => Assert.Single(game.State.PendingPrompts);

    private static L12Command Resolve(L12Prompt prompt, string choice) => new("resolvePrompt",
        PromptId: prompt.PromptId, Choice: choice, ActivationId: prompt.ActivationId,
        SourceInstanceId: prompt.SourceInstanceId, SourceCardId: prompt.SourceCardId,
        Step: prompt.Step, CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller);

    private static void Send(L12GameEngine game, L12Prompt prompt, string choice)
        => Send(game, Resolve(prompt, choice));

    private static void Send(L12GameEngine game, L12Command command)
    {
        var prompt = OnlyPrompt(game);
        var result = game.Handle(prompt.PlayerIndex, command);
        Assert.True(result.Accepted, result.Error);
    }

    private static void AssertRejectedWithoutChange(L12GameEngine game, int controller, L12Command command)
    {
        var raw = game.SerializeFullState();
        var hash = game.ComputeStateHash();
        var random = game.RandomState;
        var revision = game.State.Revision;
        var factSequence = game.CardFactSignalSequence;
        Assert.False(game.Handle(controller, command).Accepted);
        Assert.Equal(raw, game.SerializeFullState());
        Assert.Equal(hash, game.ComputeStateHash());
        Assert.Equal(random, game.RandomState);
        Assert.Equal(revision, game.State.Revision);
        Assert.Equal(factSequence, game.CardFactSignalSequence);
    }

    private static L12GameEngine RestoreExactV2(L12GameEngine game)
    {
        var raw = game.SerializeFullState();
        var hash = game.ComputeStateHash();
        var random = game.RandomState!.Value;
        var revision = game.State.Revision;
        var factSequence = game.CardFactSignalSequence;
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, raw, random, factSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, utcNow: () => Now);
        Assert.True(restored.State.SourceVacatedEntrySlotProtocolEnabled);
        Assert.Equal(raw, restored.SerializeFullState());
        Assert.Equal(hash, restored.ComputeStateHash());
        Assert.Equal(random, restored.RandomState);
        Assert.Equal(revision, restored.State.Revision);
        Assert.Equal(factSequence, restored.CardFactSignalSequence);
        return restored;
    }

    private static L12GameEngine RestoreWithoutSourceVacatedProtocol(L12GameEngine game)
    {
        var checkpoint = JsonNode.Parse(game.SerializeFullState())!.AsObject();
        Assert.True(checkpoint.Remove(nameof(L12GameState.SourceVacatedEntrySlotProtocolEnabled)));
        return L12GameEngine.RestoreCheckpoint(Catalog, checkpoint.ToJsonString(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, utcNow: () => Now);
    }

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == name && candidate.GetParameters().Length == args.Length);
        return method.Invoke(target, args);
    }

    private static int CountInstance(L12GameEngine game, string instanceId)
        => game.State.Players.Sum(player =>
            player.Field.SelectMany(row => row).Count(card => card?.InstanceId == instanceId)
            + player.Hand.Count(card => card.InstanceId == instanceId)
            + player.Library.Count(card => card.InstanceId == instanceId)
            + player.Graveyard.Count(card => card.InstanceId == instanceId)
            + player.Resolving.Count(card => card.InstanceId == instanceId)
            + player.Removed.Count(card => card.InstanceId == instanceId));

    private sealed record HiddenPassFixture(L12GameEngine Game, L12CardInstance Source,
        L12CardInstance Entrant, L12MoraleCard Morale);
}
