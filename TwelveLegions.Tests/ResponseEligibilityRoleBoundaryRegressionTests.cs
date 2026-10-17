using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ResponseEligibilityRoleBoundaryRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-17T07:00:00Z");

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)]
    [InlineData(0, true)] [InlineData(1, true)]
    public void OpponentAgreeingToPeaceDoesNotTurnItsHandAddTimingIntoOurOwnTacticEffect(int actor, bool restore)
    {
        var game = Create(actor);
        game.State.Players[actor].Hand.Add(Card("S01-0015", "peace", actor));
        SetCounter(game, actor, "S01-0224", "own-codex");
        SetCounter(game, 1 - actor, "S01-0224", "opponent-codex");
        for (var i = 0; i < 4; i++)
            game.State.Players[actor].Morale.Add(new() { InstanceId = $"cost-{i}", CardId = "S01-01C1" });
        Send(ref game, actor, new("playCard", "peace"), restore);
        var agreed = false;
        var realTacticResponseOffered = false;
        for (var n = 0; !Idle(game); n++)
        {
            Assert.True(n < 60, "Peace chain must return to an actionable state.");
            var prompt = Assert.Single(game.State.PendingPrompts);
            Assert.DoesNotContain("own-codex", prompt.ValidChoices);
            if (prompt.Kind == "response")
            {
                realTacticResponseOffered |= prompt.ValidChoices.Contains("opponent-codex");
                Send(ref game, prompt.PlayerIndex, Resolve(prompt, "pass"), restore);
            }
            else
            {
                Assert.Equal("opponent-confirm", prompt.Kind);
                Assert.Equal(1 - actor, prompt.PlayerIndex);
                Assert.Contains("agree", prompt.ValidChoices);
                agreed = true;
                var command = Resolve(prompt, "agree");
                Send(ref game, prompt.PlayerIndex, command, restore);
                RejectWithoutMutation(game, prompt.PlayerIndex, command);
            }
        }
        Assert.True(agreed);
        Assert.True(realTacticResponseOffered, "Real opposing tactic effects remain eligible for Wisdom Codex.");
        Assert.All(game.State.Players, player =>
            Assert.True(player.Field[1][0] is { Hidden: true, CardId: "S01-0224" }));
        Assert.Single(game.State.Players[actor].Graveyard, card => card.InstanceId == "peace");
        Assert.DoesNotContain(game.CardFactSignals, fact => fact.CardInstanceId is "own-codex" or "opponent-codex");
        Assert.True(Idle(game));
    }

    public static IEnumerable<object[]> ProtectedHandCases()
    {
        foreach (var protector in new[] { "S01-0201", "S01-0202", "ST02-01" })
        foreach (var responder in new[] { "S01-0002", "S02-0005" })
        foreach (var actor in new[] { 0, 1 })
        foreach (var restore in new[] { false, true })
            yield return [protector, responder, actor, restore];
    }

    [Theory]
    [MemberData(nameof(ProtectedHandCases))]
    public void CounterTacticProtectionDoesNotSuppressAHandLegionResponse(
        string protector, string responder, int actor, bool restore)
    {
        var game = Create(actor);
        var defender = 1 - actor;
        var defenderHpBefore = game.State.Players[defender].Hp;
        var attacker = Card(protector, "protected-attacker", actor);
        // Isolate response classification, not the natural entry or charge grant of each protector.
        attacker.HasCharge = true;
        attacker.SummonRound = game.State.Round;
        game.State.Players[actor].Field[0][0] = attacker;
        var target = Card("S01-0102", "defended-legion", defender, troops: 20000);
        if (responder == "S01-0002") game.State.Players[defender].Field[0][0] = target;
        game.State.Players[defender].Hand.Add(Card(responder, "hand-legion", defender));
        game.State.Players[actor].Hand.Add(Card(responder, "wrong-side-legion", actor));
        SetCounter(game, defender, "S01-0016", "protected-counter");
        Assert.True(L12StructuredCardRules.HasSummonTurnCounterTacticProtection(attacker, game.State.Round));
        Send(ref game, actor, new("attack", attacker.InstanceId,
            Target: new(responder == "S01-0002" ? "legion" : "master", responder == "S01-0002" ? target.InstanceId : null)), restore);
        var selected = false;
        for (var n = 0; !Idle(game); n++)
        {
            Assert.True(n < 100, "Protected attack must drain without a stuck prompt.");
            if (game.State.PendingPrompts.FirstOrDefault() is { } prompt)
            {
                Assert.DoesNotContain("wrong-side-legion", prompt.ValidChoices);
                if (prompt.Kind == "response" && game.State.EffectStack.LastOrDefault()?.Trigger == "opponent-attack")
                {
                    Assert.DoesNotContain("protected-counter", prompt.ValidChoices);
                    if (prompt.PlayerIndex == defender && !selected)
                    {
                        Assert.Contains("hand-legion", prompt.ValidChoices);
                        var command = Resolve(prompt, "hand-legion");
                        Send(ref game, defender, command, restore);
                        RejectWithoutMutation(game, defender, command);
                        selected = true;
                        continue;
                    }
                }
                var choice = prompt.Continuation == "stack-response-puppet-slot" ? "0:1"
                    : prompt.Kind == "response" ? "pass"
                    : prompt.ValidChoices.Contains("mode:none") ? "mode:none"
                    : prompt.ValidChoices.Contains("skip") ? "skip"
                    : prompt.ValidChoices.Contains("no") ? "no"
                    : prompt.ValidChoices.First();
                Send(ref game, prompt.PlayerIndex, Resolve(prompt, choice), restore);
                continue;
            }
            if (game.State.PendingDefense is { Stage: L12CombatStage.DefenseChoice })
            {
                Send(ref game, defender, new("resolveDefense", CardInstanceIds: []), restore);
                continue;
            }
            Assert.Fail("No public command boundary can advance the protected attack.");
        }
        Assert.True(selected);
        Assert.DoesNotContain(game.State.Players[defender].Hand, card => card.InstanceId == "hand-legion");
        Assert.Single(game.State.Players[defender].Graveyard, card => card.InstanceId == "hand-legion");
        Assert.True(game.State.Players[defender].Field[1][0] is { Hidden: true, InstanceId: "protected-counter" });
        Assert.Equal(defenderHpBefore, game.State.Players[defender].Hp);
        if (responder == "S01-0002") Assert.NotNull(game.State.Players[defender].Field[0][0]);
        Assert.True(Idle(game));
    }

    [Theory]
    [InlineData("authority-event", false)]
    [InlineData("composite-continuation", false)]
    [InlineData("opponent-attack", false)]
    [InlineData("play", true)]
    [InlineData("active", true)]
    [InlineData("reaction", true)]
    public void WisdomCodexUsesTheExistingEffectRoleClassifierForTacticAndArtifactSources(string trigger, bool expected)
    {
        foreach (var sourceId in new[] { "S01-0015", "S01-0219" })
        {
            var game = Create(0);
            var source = Card(sourceId, "classified-source", 0);
            game.State.Players[0].Resolving.Add(source);
            var item = new L12StackItem
            {
                StackItemId = "synthetic-role-boundary", Controller = 0,
                SourceCardId = sourceId, SourceInstanceId = source.InstanceId,
                SourceName = source.Name, Trigger = trigger, Text = source.EffectText ?? "",
            };
            var method = typeof(L12GameEngine).GetMethod("CanUseS1ReactionAtStack", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.Equal(expected, (bool)method.Invoke(game, ["S01-0224", 1, item])!);
            Assert.False((bool)method.Invoke(game, ["S01-0224", 0, item])!);
            item.Data["unrespondable"] = "true";
            Assert.False((bool)method.Invoke(game, ["S01-0224", 1, item])!);
        }
    }

    [Fact]
    public void FullCatalogProtectionAndHandResponseFamiliesStayExplicitlyBounded()
    {
        Assert.Equal(new[] { "S01-0201", "S01-0202", "ST02-01" }, Catalog.Cards.Values
            .Where(card => L12StructuredCardSemantics.HasSummonTurnCounterTacticProtection(card.Id))
            .Select(card => card.Id).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "S01-0002" }, Catalog.Cards.Values
            .Where(card => L12StructuredCardSemantics.IsMercenaryHandBlockResponse(card.Id)).Select(card => card.Id));
        Assert.Equal(new[] { "S02-0005" }, Catalog.Cards.Values
            .Where(card => L12StructuredCardSemantics.UsesSpecialResponsePlan(card.Id, "rest-enter-front-and-retarget"))
            .Select(card => card.Id));
    }

    private static L12GameEngine Create(int actor)
    {
        var game = new L12GameEngine(Catalog, "synthetic-response-role-boundary", "LOCAL", 17068339,
            ["合成甲", "合成乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2, disasterMode: "none", utcNow: () => Now);
        game.State.PendingPrompts.Clear(); game.State.ActivePlayer = actor; game.State.FirstPlayer = 0;
        game.State.Round = 3; game.State.TurnSerial = 5; game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null; game.State.DisasterDeck.Clear();
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3]; player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear(); player.Graveyard.Clear(); player.Library.Clear(); player.Resolving.Clear();
            player.Morale.Clear(); player.MoraleDeck.Clear(); player.UsedAbilities.Clear(); player.Relic = null; player.ExtraRelics.Clear();
            for (var i = 0; i < 12; i++) player.Library.Add(Card("S01-0003", $"library-{player.PlayerIndex}-{i}", player.PlayerIndex));
        }
        return game;
    }

    private static void SetCounter(L12GameEngine game, int owner, string cardId, string instanceId)
    {
        var card = Card(cardId, instanceId, owner); card.Hidden = true; card.SetRound = 0;
        game.State.Players[owner].Field[1][0] = card;
    }

    private static L12CardInstance Card(string id, string instance, int owner, int? troops = null)
    {
        var d = Catalog.Cards[id];
        return new() { CardId = id, InstanceId = instance, OwnerIndex = owner, Name = d.NameZh, CardType = d.CardType,
            IsCounterTactic = d.IsCounterTactic, Faction = d.Faction, ImageUrl = d.ImageUrl,
            Cost = d.Cost ?? 0, HasPrintedCost = d.Cost.HasValue, EffectText = d.Effect, Traits = [.. d.Traits],
            Profession = d.Profession, EffectiveProfession = d.Profession, BaseTroops = troops ?? d.Troops ?? 0,
            Troops = troops ?? d.Troops ?? 0, DisasterLevel = d.DisasterLevel ?? 0, TrialValue = d.TrialValue ?? 0, SummonRound = -1 };
    }

    private static L12Command Resolve(L12Prompt p, string choice) => new("resolvePrompt", PromptId: p.PromptId,
        Choice: choice, ActivationId: p.ActivationId, SourceInstanceId: p.SourceInstanceId, SourceCardId: p.SourceCardId,
        Step: p.Step, CreatedRevision: p.CreatedRevision, Controller: p.Controller);

    private static void Send(ref L12GameEngine game, int player, L12Command command, bool restore)
    {
        var result = game.Handle(player, command); Assert.True(result.Accepted, result.Error);
        if (!restore) return;
        var raw = game.SerializeFullState(); var hash = game.ComputeStateHash(); var revision = game.State.Revision;
        game = L12GameEngine.RestoreCheckpoint(Catalog, raw, game.RandomState!.Value, game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, utcNow: () => Now);
        Assert.Equal(raw, game.SerializeFullState()); Assert.Equal(hash, game.ComputeStateHash()); Assert.Equal(revision, game.State.Revision);
    }

    private static void RejectWithoutMutation(L12GameEngine game, int actor, L12Command command)
    {
        var raw = game.SerializeFullState(); var hash = game.ComputeStateHash();
        var random = game.RandomState; var sequence = game.CardFactSignalSequence;
        Assert.False(game.Handle(actor, command).Accepted);
        Assert.Equal(raw, game.SerializeFullState()); Assert.Equal(hash, game.ComputeStateHash());
        Assert.Equal(random, game.RandomState); Assert.Equal(sequence, game.CardFactSignalSequence);
    }

    private static bool Idle(L12GameEngine game) => game.State.PendingPrompts.Count == 0 && game.State.PendingDefense is null
        && game.State.EffectStack.Count == 0 && game.State.DeferredEffectStack.Count == 0 && game.State.PendingActivations.Count == 0
        && game.State.PendingTriggerBatches.Count == 0 && game.State.PendingTriggerStackCandidates.Count == 0
        && game.State.ResponseWindow is null && !game.State.IsResolvingStack;
}
