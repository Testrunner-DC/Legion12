using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class OwnerTurnEndModifierRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-17T08:00:00Z");

    public static IEnumerable<object[]> FamilyCases()
    {
        foreach (var card in new[] { "S01-0017", "S01-0320", "S01-0412", "S01-0420" })
        foreach (var owner in new[] { 0, 1 })
        foreach (var restore in new[] { false, true })
            yield return [card, owner, restore];
    }

    [Theory]
    [MemberData(nameof(FamilyCases))]
    public void FamilyExpiresOnlyAtItsControllersNextTurnEnd(string cardId, int owner, bool restore)
    {
        var game = Arrange(owner);
        Grant(ref game, cardId, owner, restore);
        var modifier = Assert.Single(Target(game, owner).TimedModifiers);
        Assert.Equal(Catalog.Cards[cardId].NameZh, modifier.Source);
        var grantTurn = game.State.TurnSerial;
        var grantPlayer = game.State.ActivePlayer;
        if (grantPlayer == owner)
        {
            End(ref game, restore);
            Assert.Single(Target(game, owner).TimedModifiers);
        }
        Assert.Equal(1 - owner, game.State.ActivePlayer);
        End(ref game, restore);
        Assert.Equal(owner, game.State.ActivePlayer);
        Assert.True(game.State.TurnSerial > grantTurn);
        Assert.Single(Target(game, owner).TimedModifiers);
        // The reduction affects only the legions present at settlement, never a later-entry aura.
        var later = Card("S01-0001", "later-legion", 1 - owner);
        game.State.Players[1 - owner].Field[0][1] = later;
        Invoke(game, "RecalculateContinuousTroops");
        Assert.Empty(later.TimedModifiers);
        Assert.Equal(later.Cost, later.CurrentCost);
        End(ref game, restore);
        Assert.Empty(Target(game, owner).TimedModifiers);
        Assert.Equal(Target(game, owner).BaseTroops, Target(game, owner).Troops);
        Assert.Equal(0, Target(game, owner).CostModifier);
    }

    [Theory]
    [MemberData(nameof(FamilyCases))]
    public void AnExtraOpposingTurnCannotConsumeTheOwnersExpiry(string cardId, int owner, bool restore)
    {
        var game = Arrange(owner);
        Grant(ref game, cardId, owner, restore);
        if (game.State.ActivePlayer == owner) End(ref game, restore);
        Assert.Equal(1 - owner, game.State.ActivePlayer);
        game.State.ExtraTurnsForPlayer = 1 - owner;
        End(ref game, restore);
        Assert.Equal(1 - owner, game.State.ActivePlayer);
        Assert.Single(Target(game, owner).TimedModifiers);
        End(ref game, restore);
        Assert.Equal(owner, game.State.ActivePlayer);
        Assert.Single(Target(game, owner).TimedModifiers);
        End(ref game, restore);
        Assert.Empty(Target(game, owner).TimedModifiers);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    public void AnExtraOwnTurnIsTheNextActualOwnTurn(int owner, bool restore)
    {
        var game = Arrange(owner);
        Grant(ref game, "S01-0320", owner, restore);
        game.State.ExtraTurnsForPlayer = owner;
        End(ref game, restore);
        Assert.Equal(owner, game.State.ActivePlayer);
        Assert.Single(Target(game, owner).TimedModifiers);
        End(ref game, restore);
        Assert.Empty(Target(game, owner).TimedModifiers);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void RemainingOwnerLayerKeepsItsStatusCloneAndPromotionIdentity(int owner)
    {
        var game = Arrange(owner);
        Grant(ref game, "S01-0320", owner, restore: true);
        End(ref game, restore: true);
        var target = Target(game, owner);
        Assert.True(Assert.Single(target.TimedModifiers).ExpiresAfterTurn < game.State.TurnSerial);
        var statuses = Assert.IsType<List<L12StatusEffectView>>(Invoke(game, "BuildStatusEffects",
            game.State.Players[1 - owner], target, 0));
        Assert.Contains(statuses, status => status.Kind == "power-down" && status.Source == "复仇血鹰");
        var clone = target.Clone();
        Assert.NotSame(target.TimedModifiers[0], clone.TimedModifiers[0]);
        Assert.Equal(target.TimedModifiers[0].OwnerTurnEndExpiry, clone.TimedModifiers[0].OwnerTurnEndExpiry);
        var promoted = Card("S01-0001", "promoted", 1 - owner);
        L12S2ZoneOps.InheritPromotionState(target, promoted);
        Assert.Equal(target.TimedModifiers[0].OwnerTurnEndExpiry, Assert.Single(promoted.TimedModifiers).OwnerTurnEndExpiry);
        L12DerivedStats.ResetForCompletedTurn(promoted, game.State.TurnSerial, 1 - owner);
        Assert.Single(promoted.TimedModifiers);
        L12DerivedStats.ResetForCompletedTurn(promoted, game.State.TurnSerial + 1, owner);
        Assert.Empty(promoted.TimedModifiers);
    }

    [Fact]
    public void OwnerLayerRequiresAnExplicitCompletedPlayerBeforeAnyMutation()
    {
        var card = Card("S01-0001", "explicit-actor", 0);
        card.TimedModifiers.Add(new() { TroopsDelta = -1000, ExpiresAfterTurn = 5, Source = "owner",
            OwnerTurnEndExpiry = new() { Controller = 0, GrantedTurnSerial = 5 } });
        card.TimedModifiers.Add(new() { CostDelta = -1, ExpiresAfterTurn = 5, Source = "legacy" });
        var before = JsonSerializer.Serialize(card);
        Assert.Throws<ArgumentException>(() => L12DerivedStats.ResetForCompletedTurn(card, 5));
        Assert.Equal(before, JsonSerializer.Serialize(card));
        L12DerivedStats.ResetForCompletedTurn(card, 5, 0);
        Assert.Single(card.TimedModifiers);
    }

    [Theory]
    [InlineData(-1, 5, true)] [InlineData(2, 5, true)]
    [InlineData(0, -1, true)] [InlineData(0, 6, true)]
    [InlineData(0, 5, false)]
    public void InvalidOwnerExpiryCannotBecomeAnEarlyOrPermanentEffect(int owner, int grant, bool enabled)
    {
        var game = Arrange(0);
        Grant(ref game, "S01-0320", 0, false);
        var state = JsonNode.Parse(game.SerializeFullState())!.AsObject();
        if (!enabled) Assert.True(state.Remove(nameof(L12GameState.OwnerTurnEndModifierProtocolEnabled)));
        var expiry = state["Players"]![1]!["Field"]![0]![0]!["TimedModifiers"]![0]!["OwnerTurnEndExpiry"]!.AsObject();
        expiry["Controller"] = owner; expiry["GrantedTurnSerial"] = grant;
        Assert.Throws<InvalidDataException>(() => L12GameEngine.RestoreCheckpoint(Catalog, state.ToJsonString(),
            game.RandomState!.Value, game.CardFactSignalSequence));
    }

    [Theory]
    [InlineData("Controller")] [InlineData("GrantedTurnSerial")]
    public void AnIncompleteOwnerExpiryRecordIsRejectedRatherThanDefaultedToPlayerZero(string field)
    {
        var game = Arrange(0);
        Grant(ref game, "S01-0320", 0, false);
        var state = JsonNode.Parse(game.SerializeFullState())!.AsObject();
        var expiry = state["Players"]![1]!["Field"]![0]![0]!["TimedModifiers"]![0]!["OwnerTurnEndExpiry"]!.AsObject();
        Assert.True(expiry.Remove(field));
        Assert.Throws<JsonException>(() => L12GameEngine.RestoreCheckpoint(Catalog, state.ToJsonString(),
            game.RandomState!.Value, game.CardFactSignalSequence));
    }

    [Fact]
    public void LethalFingerprintDistinguishesOwnerExpiryButNotEquivalentLayerOrdering()
    {
        var game = Arrange(0); var card = Target(game, 0);
        var a = new L12TimedModifier { Source = "same", TroopsDelta = -1000, ExpiresAfterTurn = 5,
            OwnerTurnEndExpiry = new() { Controller = 0, GrantedTurnSerial = 4 } };
        var b = new L12TimedModifier { Source = "same", TroopsDelta = -1000, ExpiresAfterTurn = 5,
            OwnerTurnEndExpiry = new() { Controller = 1, GrantedTurnSerial = 5 } };
        card.TimedModifiers.AddRange([a, b]);
        var first = Assert.IsType<string>(Invoke(game, "LethalStateFingerprint", card));
        card.TimedModifiers.Reverse();
        Assert.Equal(first, Invoke(game, "LethalStateFingerprint", card));
        card.TimedModifiers.Clear(); card.TimedModifiers.AddRange([a, a]);
        Assert.NotEqual(first, Invoke(game, "LethalStateFingerprint", card));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RealJournalTailRestoresExactLegacyOrNewOwnerExpiry(bool enabled)
    {
        var game = Arrange(0);
        if (!enabled)
        {
            var state = JsonNode.Parse(game.SerializeFullState())!.AsObject();
            Assert.True(state.Remove(nameof(L12GameState.OwnerTurnEndModifierProtocolEnabled)));
            game = L12GameEngine.RestoreCheckpoint(Catalog, state.ToJsonString(), game.RandomState!.Value,
                game.CardFactSignalSequence, autoPassEmptyResponses: false,
                concealHiddenResponseAvailability: false, utcNow: () => Now);
            Assert.False(game.State.OwnerTurnEndModifierProtocolEnabled);
        }
        Grant(ref game, "S01-0320", 0, false);
        var modifier = Assert.Single(Target(game, 0).TimedModifiers);
        Assert.Equal(enabled, modifier.OwnerTurnEndExpiry is not null);
        if (!enabled)
        {
            Assert.DoesNotContain(nameof(L12GameState.OwnerTurnEndModifierProtocolEnabled), game.SerializeFullState());
            Assert.DoesNotContain(nameof(L12TimedModifier.OwnerTurnEndExpiry), game.SerializeFullState());
        }
        var path = Path.Combine(Path.GetTempPath(), "owner-expiry-journal", Guid.NewGuid().ToString("N"), "matches.db");
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync(); recorder.AttachCatalog(Catalog);
        await recorder.StartAsync(game, "sandbox");
        for (var sequence = 1; sequence <= 3; sequence++)
        {
            var actor = game.State.ActivePlayer; var command = new L12Command("endTurn");
            var result = game.Handle(actor, command); Assert.True(result.Accepted, result.Error);
            Assert.Empty(game.State.PendingPrompts);
            await recorder.AppendAsync(game, sequence, actor, JsonSerializer.Serialize(command), result,
                $"owner-end-{enabled}-{sequence}");
            Assert.Equal(enabled && sequence < 3, Target(game, 0).TimedModifiers.Count == 1);
        }
        var expectedRaw = game.SerializeFullState(); var expectedHash = game.ComputeStateHash();
        var recovered = Assert.IsType<L12JournalRecoveryState>(await recorder.LoadJournalEngineAsync(game.State.MatchId));
        Assert.Equal(3, recovered.CommandSequence);
        Assert.Equal(expectedRaw, recovered.Engine.SerializeFullState());
        Assert.Equal(expectedHash, recovered.Engine.ComputeStateHash());
        Assert.Equal(enabled, recovered.Engine.State.OwnerTurnEndModifierProtocolEnabled);
    }

    [Fact]
    public void FullPoolNextOwnEndScanRemainsFourModifierCardsAndTwoSeparateLifecycles()
    {
        var cards = Catalog.Cards.Values.Where(card => Regex.IsMatch(card.Effect ?? "",
            "(下个|下一|下次).{0,12}(回合结束|结束阶段)"));
        Assert.Equal(new[] { "S01-0017", "S01-0320", "S01-0412", "S01-0420", "S02-0503", "S02-0523" },
            cards.Select(card => card.Id).OrderBy(id => id, StringComparer.Ordinal));
    }

    private static L12GameEngine Arrange(int owner)
    {
        var game = new L12GameEngine(Catalog, "synthetic-owner-end", "LOCAL", 17108001,
            ["合成甲", "合成乙"], [0, 0], skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2, utcNow: () => Now);
        game.State.PendingPrompts.Clear();
        game.State.ActivePlayer = owner; game.State.FirstPlayer = 0;
        game.State.Round = 3; game.State.TurnSerial = 5; game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3]; player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear(); player.Library.Clear(); player.Graveyard.Clear(); player.Removed.Clear();
            player.Resolving.Clear(); player.Morale.Clear(); player.MoraleDeck.Clear();
            player.UsedAbilities.Clear(); player.Relic = null; player.ExtraRelics.Clear();
            for (var i = 0; i < 12; i++)
                player.Library.Add(Card("S01-0003", $"draw-{player.PlayerIndex}-{i}", player.PlayerIndex));
        }
        game.State.Players[1 - owner].Field[0][0] = Card("S01-0001", "target", 1 - owner);
        return game;
    }

    private static void Grant(ref L12GameEngine game, string cardId, int owner, bool restore)
    {
        var source = Card(cardId, "source", owner);
        if (cardId == "S01-0412")
        {
            game.State.Players[owner].Field[0][0] = source;
            var result = game.HandleGm(new("destroyCard", owner, CardInstanceId: source.InstanceId));
            Assert.True(result.Accepted, result.Error);
        }
        else
        {
            source.Hidden = true; source.SetRound = 0;
            game.State.Players[owner].Field[1][0] = source;
            if (cardId == "S01-0320")
            {
                game.State.Players[owner].Field[0][0] = Card("S01-0309", "fallen", owner);
                var result = game.HandleGm(new("destroyCard", owner, CardInstanceId: "fallen"));
                Assert.True(result.Accepted, result.Error);
            }
            else
            {
                game.State.ActivePlayer = 1 - owner;
                Target(game, owner).Tapped = true;
                // Existing real post-attack collector; no fabricated card-effect resolver or candidate.
                Invoke(game, "QueueS1PostAttackReactions", 1 - owner);
            }
        }
        Drain(ref game, restore);
        Assert.Single(Target(game, owner).TimedModifiers);
    }

    private static void Drain(ref L12GameEngine game, bool restore)
    {
        for (var i = 0; game.State.PendingPrompts.Count > 0; i++)
        {
            Assert.True(i < 80, "Finite declaration and response chain required.");
            var prompt = Assert.Single(game.State.PendingPrompts);
            L12Command command;
            if (prompt.Continuation == "trigger-batch-order")
                command = new("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: [.. prompt.ValidChoices]);
            else
            {
                var choice = prompt.Kind == "response" ? "pass"
                    : prompt.ValidChoices.Contains("target") ? "target"
                    : prompt.ValidChoices.Contains("mode:all") ? "mode:all"
                    : prompt.SourceInstanceId == "source" && prompt.ValidChoices.Contains("mode:use") ? "mode:use"
                    : prompt.ValidChoices.Contains("mode:none") ? "mode:none"
                    : throw new InvalidOperationException($"Unexpected {prompt.Kind}/{prompt.Continuation}: {string.Join(',', prompt.ValidChoices)}");
                command = new("resolvePrompt", PromptId: prompt.PromptId, Choice: choice);
            }
            Send(ref game, prompt.PlayerIndex, command, restore);
        }
        Assert.Empty(game.State.EffectStack); Assert.Empty(game.State.DeferredEffectStack);
        Assert.Empty(game.State.PendingActivations); Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates); Assert.Null(game.State.ResponseWindow);
    }

    private static void End(ref L12GameEngine game, bool restore)
    {
        Send(ref game, game.State.ActivePlayer, new("endTurn"), restore);
        Drain(ref game, restore);
        Assert.Equal(L12Phase.Main, game.State.Phase);
    }

    private static void Send(ref L12GameEngine game, int actor, L12Command command, bool restore)
    {
        var result = game.Handle(actor, command); Assert.True(result.Accepted, result.Error);
        if (restore) game = RestoreExact(game);
    }

    private static L12GameEngine RestoreExact(L12GameEngine game)
    {
        var raw = game.SerializeFullState(); var hash = game.ComputeStateHash();
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, raw, game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, utcNow: () => Now);
        Assert.Equal(raw, restored.SerializeFullState()); Assert.Equal(hash, restored.ComputeStateHash());
        return restored;
    }

    private static L12CardInstance Target(L12GameEngine game, int owner)
        => Assert.IsType<L12CardInstance>(game.State.Players[1 - owner].Field[0][0]);

    private static object? Invoke(L12GameEngine game, string name, params object?[] args)
        => (typeof(L12GameEngine).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(name)).Invoke(game, args);

    private static L12CardInstance Card(string id, string instance, int owner)
    {
        var d = Catalog.Cards[id];
        return new() { CardId = id, InstanceId = instance, OwnerIndex = owner, Name = d.NameZh,
            CardType = d.CardType, IsCounterTactic = d.IsCounterTactic, Faction = d.Faction,
            Cost = d.Cost ?? 0, HasPrintedCost = d.Cost.HasValue, BaseTroops = d.Troops ?? 0,
            Troops = d.Troops ?? 0, EffectText = d.Effect, ImageUrl = d.ImageUrl,
            Traits = [.. d.Traits], Profession = d.Profession, SummonRound = -1 };
    }
}
