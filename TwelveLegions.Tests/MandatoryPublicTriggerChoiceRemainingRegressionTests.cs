using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class MandatoryPublicTriggerChoiceRemainingRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly string[] DeclineAliases = ["skip", "mode:none", "decline", "refuse", "cancel"];

    private static readonly (string CardId, string Trigger, string Consequence)[] MandatoryEffects =
    [
        ("S01-0215", "enter", "troops-2000"),
        ("S01-0402", "enter", "kill"),
        ("S01-0403", "enter", "kill"),
        ("S01-0411", "enter", "immortal"),
        ("S01-0412", "enter", "cost-minus-3"),
        ("S01-0416", "enter", "troops-1000"),
        ("S01-0416", "attack", "troops-1000"),
        ("S01-0417", "enter", "kill"),
    ];

    public static IEnumerable<object[]> MandatoryRows()
    {
        foreach (var (cardId, trigger, consequence) in MandatoryEffects)
        foreach (var controller in new[] { 0, 1 })
        foreach (var restore in new[] { false, true })
            yield return [cardId, trigger, consequence, controller, restore];
    }

    public static IEnumerable<object[]> NoObjectRows()
    {
        foreach (var (cardId, trigger, _) in MandatoryEffects.Where(effect => effect.CardId != "S01-0411"))
        foreach (var controller in new[] { 0, 1 })
            yield return [cardId, trigger, controller];
    }

    [Theory]
    [MemberData(nameof(MandatoryRows))]
    [Trait("L12Evidence", "rule:remaining-mandatory-public-target-decline-rejected")]
    public void MandatoryPublicTargetRejectsEveryDeclineAliasWithoutMutation(
        string cardId, string trigger, string consequence, int controller, bool restore)
    {
        var fixture = Prepare(cardId, trigger, consequence, controller, restore, withLegalTarget: true);
        var game = fixture.Game;
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", prompt.Continuation);
        Assert.Contains(fixture.TargetId, prompt.ValidChoices);
        Assert.All(DeclineAliases, alias => Assert.DoesNotContain(alias, prompt.ValidChoices));

        foreach (var alias in DeclineAliases)
        {
            var before = game.SerializeFullState();
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: alias));
            Assert.False(result.Accepted);
            Assert.Equal(before, game.SerializeFullState());
        }
    }

    [Theory]
    [MemberData(nameof(MandatoryRows))]
    [Trait("L12Evidence", "rule:remaining-mandatory-public-target-legal-settlement")]
    public void MandatoryPublicTargetSettlesForEitherControllerAndAcrossRestore(
        string cardId, string trigger, string consequence, int controller, bool restore)
    {
        var fixture = Prepare(cardId, trigger, consequence, controller, restore, withLegalTarget: true);
        var game = fixture.Game;
        var prompt = Assert.Single(game.State.PendingPrompts);
        var before = ConsequenceMetric(game, fixture);

        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: fixture.TargetId));
        Assert.True(result.Accepted, result.Error);
        PassResponses(game);

        var after = ConsequenceMetric(game, fixture);
        Assert.True(ConsequenceSucceeded(consequence, before, after),
            $"{cardId}/{trigger} consequence mismatch: {before} -> {after}");
    }

    [Theory]
    [MemberData(nameof(NoObjectRows))]
    [Trait("L12Evidence", "rule:remaining-mandatory-public-target-no-object-natural-end")]
    public void MandatoryPublicTargetWithNoLegalObjectEndsWithoutDeadPrompt(
        string cardId, string trigger, int controller)
    {
        var fixture = Prepare(cardId, trigger, ConsequenceFor(cardId, trigger), controller,
            restore: false, withLegalTarget: false);
        var game = fixture.Game;
        PassResponses(game);

        Assert.Equal(trigger == "attack" ? L12Phase.Defense : L12Phase.Main, game.State.Phase);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Null(game.State.ResponseWindow);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "card:S01-0411")]
    [Trait("L12Evidence", "rule:abe-entry-source-is-legal-self-target")]
    public void AbeEntryMaySelectItsOwnNewlyEnteredLegion(int controller, bool restore)
    {
        var fixture = Prepare("S01-0411", "enter", "immortal", controller, restore, withLegalTarget: true);
        var prompt = Assert.Single(fixture.Game.State.PendingPrompts);

        Assert.Equal(fixture.SourceId, fixture.TargetId);
        Assert.Contains(fixture.SourceId, prompt.ValidChoices);
    }

    private static Fixture Prepare(string cardId, string trigger, string consequence, int controller,
        bool restore, bool withLegalTarget)
    {
        var seed = 2026102200 + Array.FindIndex(MandatoryEffects,
            effect => effect.CardId == cardId && effect.Trigger == trigger) * 10 + controller * 2 + (restore ? 1 : 0);
        var game = Create(seed, controller, cardId.StartsWith("S01-02", StringComparison.Ordinal) ? 2 : 1);
        var player = game.State.Players[controller];
        var opponent = game.State.Players[1 - controller];
        var source = Card(cardId, $"remaining-fix-{cardId}-{trigger}-source-p{controller}", controller);
        string targetId = string.Empty;
        var targetOwner = controller;

        if (trigger == "attack")
        {
            source.SummonRound = -1;
            player.Field[0][0] = source;
            if (withLegalTarget)
            {
                var target = Card("S01-0404", $"remaining-fix-inahime-attack-target-p{controller}", controller);
                player.Field[0][1] = target;
                targetId = target.InstanceId;
            }
            var attack = game.Handle(controller,
                new L12Command("attack", source.InstanceId, Target: new L12AttackTarget("master")));
            Assert.True(attack.Accepted, attack.Error);
        }
        else
        {
            if (withLegalTarget)
            {
                switch (cardId)
                {
                    case "S01-0215":
                    {
                        var target = Card("S01-0212", $"remaining-fix-ankh-target-p{controller}", controller);
                        player.Field[0][1] = target;
                        targetId = target.InstanceId;
                        break;
                    }
                    case "S01-0402":
                    {
                        var target = Card("S01-0002", $"remaining-fix-oda-target-p{controller}", 1 - controller);
                        opponent.Field[0][0] = target;
                        targetId = target.InstanceId;
                        targetOwner = 1 - controller;
                        break;
                    }
                    case "S01-0403":
                    {
                        var target = Card("S01-0002", $"remaining-fix-uesugi-target-p{controller}", 1 - controller);
                        opponent.Field[0][0] = target;
                        for (var index = 0; index < 2; index++)
                        {
                            player.Field[1][index] = Card("S01-0019",
                                $"remaining-fix-uesugi-own-counter-p{controller}-{index}", controller);
                            opponent.Field[1][index] = Card("S01-0019",
                                $"remaining-fix-uesugi-enemy-counter-p{controller}-{index}", 1 - controller);
                        }
                        targetId = target.InstanceId;
                        targetOwner = 1 - controller;
                        break;
                    }
                    case "S01-0411":
                        targetId = source.InstanceId;
                        break;
                    case "S01-0412":
                    {
                        var target = Card("S01-0002", $"remaining-fix-tachibana-target-p{controller}", 1 - controller);
                        opponent.Field[0][0] = target;
                        targetId = target.InstanceId;
                        targetOwner = 1 - controller;
                        break;
                    }
                    case "S01-0416":
                    {
                        var target = Card("S01-0404", $"remaining-fix-inahime-enter-target-p{controller}", controller);
                        player.Field[0][1] = target;
                        targetId = target.InstanceId;
                        break;
                    }
                    case "S01-0417":
                    {
                        var target = Card("S01-0004", $"remaining-fix-kusanagi-target-p{controller}", 1 - controller);
                        opponent.Field[0][0] = target;
                        targetId = target.InstanceId;
                        targetOwner = 1 - controller;
                        break;
                    }
                }
            }

            player.Hand.Add(source);
            AddMorale(player, source.Cost, $"remaining-fix-{cardId}-morale-p{controller}");
            var play = source.CardType == "legion"
                ? game.Handle(controller, new L12Command("playCard", source.InstanceId, Row: 0, Slot: 0))
                : game.Handle(controller, new L12Command("playCard", source.InstanceId));
            Assert.True(play.Accepted, play.Error);
            ResolvePlayPaymentIfAsked(game, controller, cardId == "S01-0215" && withLegalTarget ? targetId : null);
            PassResponses(game);
        }

        if (withLegalTarget)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("pending-activation", prompt.Continuation);
            Assert.Contains(targetId, prompt.ValidChoices);
            if (restore)
            {
                game = Restore(game);
                prompt = Assert.Single(game.State.PendingPrompts);
                Assert.Equal("pending-activation", prompt.Continuation);
                Assert.Contains(targetId, prompt.ValidChoices);
            }
        }

        return new Fixture(game, source.InstanceId, targetId, targetOwner, consequence);
    }

    private static void ResolvePlayPaymentIfAsked(L12GameEngine game, int controller, string? preservedGuardId)
    {
        if (game.State.PendingPrompts.Count != 1) return;
        var prompt = game.State.PendingPrompts[0];
        if (prompt.Continuation != "play-morale-choice") return;
        var cost = int.Parse(prompt.Data["cost"]);
        var chosen = game.State.Players[controller].Morale.Where(card => !card.Tapped)
            .Select(card => card.InstanceId).Where(id => prompt.ValidChoices.Contains(id)).Take(cost).ToList();
        Assert.Equal(cost, chosen.Count);
        if (preservedGuardId is not null) Assert.DoesNotContain(preservedGuardId, chosen);
        var paid = game.Handle(controller,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: chosen));
        Assert.True(paid.Accepted, paid.Error);
        if (preservedGuardId is not null)
            Assert.Contains(game.State.Players[controller].Field.SelectMany(row => row),
                card => card?.InstanceId == preservedGuardId);
    }

    private static int ConsequenceMetric(L12GameEngine game, Fixture fixture)
    {
        var owner = game.State.Players[fixture.TargetOwner];
        if (fixture.Consequence == "kill")
            return owner.Graveyard.Any(card => card.InstanceId == fixture.TargetId) ? 1 : 0;
        var target = Assert.Single(owner.Field.SelectMany(row => row),
            card => card?.InstanceId == fixture.TargetId)!;
        return fixture.Consequence switch
        {
            "troops-1000" or "troops-2000" => target.Troops,
            "cost-minus-3" => target.CurrentCost,
            "immortal" => target.ImmortalUses,
            _ => throw new InvalidOperationException($"Unknown consequence {fixture.Consequence}"),
        };
    }

    private static bool ConsequenceSucceeded(string consequence, int before, int after)
        => consequence switch
        {
            "kill" => before == 0 && after == 1,
            "troops-1000" => after == before + 1000,
            "troops-2000" => after == before + 2000,
            "cost-minus-3" => after == before - 3,
            "immortal" => before == 0 && after >= 1,
            _ => false,
        };

    private static string ConsequenceFor(string cardId, string trigger)
        => MandatoryEffects.Single(effect => effect.CardId == cardId && effect.Trigger == trigger).Consequence;

    private static L12GameEngine Create(int seed, int controller, int deckIndex)
    {
        var deck = Catalog.DeckAt(deckIndex);
        var game = new L12GameEngine(Catalog, "remaining-mandatory-choice", $"REMAINING-FIX-{seed}", seed,
            ["甲", "乙"], [deck, deck], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        Assert.Empty(game.State.PendingPrompts);
        game.State.ActivePlayer = controller;
        game.State.FirstPlayer = controller;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        game.State.DisasterValue = 0;
        game.State.DisasterDeck.Clear();
        game.State.RemovedDisasters.Clear();
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
            player.Relic = null;
        }
        return game;
    }

    private static L12CardInstance Card(string id, string instanceId, int owner)
    {
        var definition = Catalog.Cards[id];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            IsCounterTactic = id == "S01-0019",
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            OwnerIndex = owner,
            SummonRound = -1,
        };
    }

    private static void AddMorale(L12PlayerState player, int count, string prefix)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-04C1",
                InstanceId = $"{prefix}-{index}",
            });
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

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private sealed record Fixture(L12GameEngine Game, string SourceId, string TargetId,
        int TargetOwner, string Consequence);
}
