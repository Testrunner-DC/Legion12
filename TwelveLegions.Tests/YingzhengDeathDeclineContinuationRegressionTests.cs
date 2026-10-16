using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class YingzhengDeathDeclineContinuationRegressionTests
{
    private static readonly L12Catalog Catalog =
        L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-17T00:00:00Z");

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "bug:BUG-20260907-89fc4c42")]
    public void DecliningAlliedSunWuDeathStillCompletesYingzhengAndFactionRecovery(
        int actor, bool restoreBetweenCommands)
    {
        var basis = Catalog.DeckAt(0);
        Assert.Equal("tianting", Catalog.Cards[basis.MasterId].Faction);
        var game = new L12GameEngine(Catalog, "yingzheng-death-decline", "SYNTHETIC",
            890700 + actor, ["P0", "P1"], [basis, basis], skipPreparation: true,
            disasterMode: "none", autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2, utcNow: () => Now);
        game.State.ActivePlayer = actor;
        game.State.FirstPlayer = actor;
        game.State.Round = 3;
        game.State.TurnSerial = 21;
        game.State.Phase = L12Phase.Main;
        game.State.DisasterValue = 0;
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
            player.Relic = null;
            player.ExtraRelics.Clear();
            for (var i = 0; i < 10; i++)
                player.Library.Add(Card("S01-0002", $"library-{player.PlayerIndex}-{i}", player.PlayerIndex));
        }
        var owner = game.State.Players[actor];
        owner.Hand.Add(Card("S02-0101", "yingzheng", actor));
        owner.Hand.Add(Card("S02-0101", "entry-cost", actor));
        owner.Field[0][1] = Card("S01-0112", "allied-sunwu", actor);
        owner.Graveyard.Add(Card("S01-0016", "legal-recovery-tactic", actor));
        Assert.True(L12CounterTacticRules.IsTactic(Catalog.Cards["S01-0016"]));
        Assert.InRange(Catalog.Cards["S01-0016"].Cost!.Value, 0, 4);
        for (var i = 0; i < 8; i++)
            owner.Morale.Add(new L12MoraleCard { CardId = "S01-01C1", InstanceId = $"morale-{i}" });
        for (var i = 0; i < 4; i++)
            owner.MoraleDeck.Add(new L12MoraleCard { CardId = "S01-01C1", InstanceId = $"deck-morale-{i}" });

        var deathDeclines = 0;
        var factionActivations = 0;
        var paidEntryCost = false;
        void Send(int controller, L12Command command)
        {
            if (restoreBetweenCommands)
            {
                var raw = game.SerializeFullState();
                var hash = game.ComputeStateHash();
                var factSequence = game.CardFactSignalSequence;
                game = L12GameEngine.RestoreCheckpoint(Catalog, raw, game.RandomState!.Value,
                    factSequence, autoPassEmptyResponses: false,
                    concealHiddenResponseAvailability: false, utcNow: () => Now);
                Assert.Equal(raw, game.SerializeFullState());
                Assert.Equal(hash, game.ComputeStateHash());
                Assert.Equal(factSequence, game.CardFactSignalSequence);
            }
            var result = game.Handle(controller, command);
            Assert.True(result.Accepted, result.Error);
        }

        Send(actor, new L12Command("playCard", "yingzheng", Row: 0, Slot: 0));
        for (var safety = 0; game.State.PendingPrompts.Count > 0 && safety < 80; safety++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            string? choice;
            List<string>? ids = null;
            if (prompt.Continuation == "s2-yingzheng-enter-cost")
            {
                Assert.False(paidEntryCost);
                Assert.Contains("entry-cost", prompt.ValidChoices);
                choice = "entry-cost";
                paidEntryCost = true;
            }
            else if (prompt.Kind == "response")
            {
                Assert.Contains("pass", prompt.ValidChoices);
                choice = "pass";
            }
            else if (prompt.Continuation == "trigger-batch-order")
            {
                choice = null;
                ids = [.. prompt.ValidChoices];
            }
            else if (prompt.SourceInstanceId == "allied-sunwu")
            {
                Assert.Equal("pending-activation", prompt.Continuation);
                Assert.Contains("mode:use", prompt.ValidChoices);
                Assert.Contains("mode:none", prompt.ValidChoices);
                choice = "mode:none";
                deathDeclines++;
            }
            else
            {
                Assert.Equal("pending-activation", prompt.Continuation);
                Assert.Contains("士气·天廷", prompt.Text, StringComparison.Ordinal);
                Assert.Contains("mode:use", prompt.ValidChoices);
                choice = "mode:use";
                factionActivations++;
            }
            var command = new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                Choice: choice, CardInstanceIds: ids, ActivationId: prompt.ActivationId,
                SourceInstanceId: prompt.SourceInstanceId, SourceCardId: prompt.SourceCardId,
                Step: prompt.Step, CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller);
            Send(prompt.PlayerIndex, command);
            var after = game.SerializeFullState();
            Assert.False(game.Handle(prompt.PlayerIndex, command).Accepted);
            Assert.Equal(after, game.SerializeFullState());
        }

        owner = game.State.Players[actor];
        Assert.True(paidEntryCost);
        Assert.Equal(1, deathDeclines);
        Assert.Equal(1, factionActivations);
        Assert.Single(owner.Graveyard, card => card.InstanceId == "entry-cost");
        Assert.Single(owner.Graveyard, card => card.InstanceId == "allied-sunwu");
        Assert.Single(owner.Graveyard, card => card.InstanceId == "legal-recovery-tactic");
        Assert.DoesNotContain(owner.Hand, card => card.InstanceId == "legal-recovery-tactic");
        Assert.Equal("yingzheng", owner.Field[0][0]?.InstanceId);
        Assert.Equal(game.State.TurnSerial, owner.FactionMoraleAdditionForbiddenUntilTurn);
        Assert.Equal(2, owner.Morale.Count);
        Assert.All(owner.Morale, morale => Assert.True(morale.Tapped));
        Assert.Single(game.State.Events, entry => entry.Type == "effect-declined"
            && entry.Cards.Any(card => card.InstanceId == "allied-sunwu"));
        Assert.Single(game.State.Events, entry => entry.Type == "faction-effect"
            && entry.Text.Contains("追加 2 张休整士气", StringComparison.Ordinal));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
        Send(actor, new L12Command("endTurn"));
    }

    private static L12CardInstance Card(string id, string instanceId, int owner)
    {
        var d = Catalog.Cards[id];
        return new L12CardInstance
        {
            CardId = id, InstanceId = instanceId, OwnerIndex = owner, Name = d.NameZh,
            CardType = d.CardType, Faction = d.Faction, IsCounterTactic = d.IsCounterTactic,
            ImageUrl = d.ImageUrl, Cost = d.Cost ?? 0, HasPrintedCost = d.Cost.HasValue,
            EffectText = d.Effect, Traits = [.. d.Traits], BaseTroops = d.Troops ?? 0,
            Troops = d.Troops ?? 0, Profession = d.Profession,
            EffectiveProfession = d.Profession, SummonRound = -1,
        };
    }
}
