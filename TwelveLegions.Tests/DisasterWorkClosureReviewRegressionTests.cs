using System.Reflection;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Tests;

public sealed class DisasterWorkClosureReviewRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private readonly ITestOutputHelper _output;

    public DisasterWorkClosureReviewRegressionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    [Trait("L12Evidence", "review:pump-state-death-before-disaster")]
    public void PumpedUnrespondableCompositeTailMustPublishAuraDeathBeforeDisaster()
    {
        var game = CreateGame("pump-review", 2026100917);
        var owner = game.State.Players[0];
        var opponent = game.State.Players[1];
        var hijikata = Card("S01-0406", "review-hijikata", 0);
        // Use a cost-two target with no death trigger. The earlier audit fixture used
        // S01-0004, whose own death trigger opened another command boundary and
        // accidentally gave the ordinary Handle post-check time to catch Mozi.
        var broad = Card("S01-0002", "review-broad", 1);
        var hannibal = Card("S02-0516", "review-hannibal", 1);
        var mozi = Card("S01-0110", "review-mozi", 1);
        broad.CostModifier = -2;
        hannibal.CostModifier = -2;
        mozi.Troops = -500;
        owner.Hand.Add(hijikata);
        opponent.Field[0] = [broad, hannibal, mozi];
        game.State.DisasterValue = 8;
        game.State.DisasterDeck.Add(Card("S01-DS08", "review-disaster", 0));
        for (var index = 0; index < hijikata.Cost; index++)
            owner.Morale.Add(new L12MoraleCard { CardId = "S01-04C1", InstanceId = $"review-morale-{index}" });

        Assert.True(game.Handle(0, new L12Command("playCard", hijikata.InstanceId, Row: 0, Slot: 0)).Accepted);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            CardInstanceIds: [broad.InstanceId, hannibal.InstanceId])).Accepted);
        PassResponses(game);

        var trace = string.Join(" | ", game.State.Events.Select(entry =>
            $"{entry.Sequence}:{entry.Type}:{string.Join(',', entry.Cards.Select(card => card.InstanceId))}"));
        _output.WriteLine(trace);
        var death = game.State.Events.SingleOrDefault(entry => entry.Type == "leave"
            && entry.Cards.Any(card => card.InstanceId == mozi.InstanceId));
        var disaster = game.State.Events.SingleOrDefault(entry => entry.Type == "disaster");
        Assert.NotNull(death);
        Assert.NotNull(disaster);
        Assert.True(death.Sequence < disaster.Sequence, trace);
    }

    [Theory]
    [InlineData("snapshot", false)]
    [InlineData("snapshot", true)]
    [InlineData("stale-ack", false)]
    [InlineData("stale-ack", true)]
    [Trait("L12Evidence", "review:orphan-reconcile-does-not-invent-progress")]
    public void ReconciliationOfLastOrphanOnlyCleansTheOrphan(string entry, bool restore)
    {
        var game = CreateGame($"reconcile-{entry}-{restore}", 2026100918 + (restore ? 1 : 0));
        game.State.DisasterValue = 9;
        game.State.CheckDisasterAfterStack = true;
        game.State.DisasterDeck.Add(Card("S01-DS08", $"review-reconcile-disaster-{entry}-{restore}", 0));
        var prompt = OrphanActivationPrompt(game, $"review-orphan-{entry}-{restore}");
        game.State.PendingPrompts.Add(prompt);
        if (restore) game = Restore(game);

        if (entry == "snapshot")
            _ = game.SnapshotFor(0);
        else
            Assert.True(game.Handle(0,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "missing-card")).Accepted);

        var trace = string.Join(" | ", game.State.Events.Select(item => $"{item.Sequence}:{item.Type}"));
        _output.WriteLine(trace);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Contains(game.State.Events, item => item.Type == "activation-reconciled");
        Assert.Null(game.State.ActiveDisaster);
        Assert.DoesNotContain(game.State.Events, item => item.Type == "disaster");
        Assert.True(game.State.CheckDisasterAfterStack);
    }

    [Fact]
    [Trait("L12Evidence", "review:reconcile-preserves-existing-finish-progress")]
    public void CommittedParentFinishRequestMustSurviveLaterOrphanCleanup()
    {
        var game = CreateGame("reconcile-finish-then-orphan", 2026100920);
        var owner = game.State.Players[0];
        var source = Card("S01-0002", "review-committed-source", 0);
        owner.Field[0][0] = source;
        for (var index = 0; index < 4; index++)
            owner.Graveyard.Add(Card("S01-0001", $"review-committed-grave-{index}", 0));
        var parent = new L12StackItem
        {
            StackItemId = "review-committed-parent",
            Controller = 0,
            SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId,
            SourceName = source.Name,
            Trigger = "effect",
            Text = "真实已提交父项",
        };
        game.State.EffectStack.Add(parent);
        game.State.IsResolvingStack = true;
        Assert.True(BeginFixedGraveReturnResolution(game, parent));
        var committed = Assert.Single(game.State.PendingActivations);
        Assert.Equal(parent.StackItemId, committed.CommittedCompletion);
        Assert.Single(game.State.PendingPrompts);

        // This second transaction has no authoritative activation. It must be cleaned,
        // but while it still exists it also proves that Finish's existing pump request
        // cannot run synchronously between reconciliation snapshot entries.
        game.State.PendingPrompts.Add(OrphanActivationPrompt(game, "review-following-orphan"));
        owner.Graveyard.RemoveAt(0); // Invalidate the real fixed-count declaration.
        game.State.CheckDisasterAfterStack = true;
        game.State.DisasterValue = 9;
        game.State.DisasterDeck.Add(Card("S01-DS08", "review-following-disaster", 0));

        _ = game.SnapshotFor(0);

        var trace = string.Join(" | ", game.State.Events.Select(item => $"{item.Sequence}:{item.Type}"));
        _output.WriteLine(trace);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.Contains(game.State.Events, item => item.Type == "effect-result"
            && item.Cards.Any(card => card.InstanceId == source.InstanceId));
        Assert.Contains(game.State.Events, item => item.Type == "activation-reconciled");
        Assert.NotNull(game.State.ActiveDisaster);
        Assert.Contains(game.State.Events, item => item.Type == "disaster");
        Assert.False(game.State.CheckDisasterAfterStack);
    }

    private static L12GameEngine CreateGame(string id, int seed)
    {
        var game = new L12GameEngine(Catalog, id, "WORK-REVIEW", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, disasterMode: "all", stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 7;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
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
            for (var index = 0; index < 8; index++)
                player.Library.Add(Card("S01-0002", $"review-lib-{player.PlayerIndex}-{index}", player.PlayerIndex));
        }
        return game;
    }

    private static L12CardInstance Card(string id, string instance, int owner)
    {
        var definition = Catalog.Cards[id];
        return new L12CardInstance
        {
            InstanceId = instance,
            OwnerIndex = owner,
            CardId = id,
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
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }

    private static L12Prompt OrphanActivationPrompt(L12GameEngine game, string id)
        => new()
        {
            PromptId = id,
            PlayerIndex = 0,
            Kind = "card",
            Text = "孤立提示",
            ValidChoices = ["missing-card"],
            MinChoose = 1,
            MaxChoose = 1,
            IsPrivate = true,
            Continuation = "pending-activation",
            ActivationId = $"{id}-activation",
            SourceInstanceId = $"{id}-source",
            SourceCardId = "S01-0001",
            Step = 0,
            CreatedRevision = game.State.Revision,
            Controller = 0,
            Data = new Dictionary<string, string> { ["activationId"] = $"{id}-activation" },
        };

    private static bool BeginFixedGraveReturnResolution(L12GameEngine game, L12StackItem parent)
    {
        var method = typeof(L12GameEngine).GetMethod("BeginFixedGraveReturnResolution",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<bool>(method.Invoke(game, [parent, 0, 4]));
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            Assert.IsType<L12RandomState>(game.RandomState), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 20; safety++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt is null || prompt.Kind != "response") return;
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
        Assert.Fail("Response chain did not finish.");
    }
}
