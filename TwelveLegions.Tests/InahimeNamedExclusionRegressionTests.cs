using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class InahimeNamedExclusionRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    public static IEnumerable<object[]> Cases()
    {
        foreach (var timing in new[] { "enter", "attack" })
        foreach (var owner in new[] { 0, 1 })
        foreach (var restore in new[] { false, true })
            yield return [timing, owner, restore];
    }

    public static IEnumerable<object[]> IdentityCases()
    {
        foreach (var row in Cases())
        foreach (var cardId in new[] { "S01-0416", "s01-0416" })
            yield return [..row, cardId];
    }

    [Theory]
    [MemberData(nameof(IdentityCases))]
    [Trait("L12Evidence", "card:S01-0416")]
    [Trait("L12Evidence", "rule:inahime-named-target-exclusion")]
    public void OtherNamedCopyIsNotOfferedOrAcceptedButDifferentLegionStillResolves(
        string timing, int owner, bool restore, string namedCardId)
    {
        var game = Prepare(timing, owner, restore, includeOther: true, namedCardId);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", prompt.Continuation);
        Assert.Contains("inahime-other", prompt.ValidChoices);
        Assert.DoesNotContain("inahime-source", prompt.ValidChoices);
        Assert.DoesNotContain("inahime-copy", prompt.ValidChoices);
        Assert.DoesNotContain("skip", prompt.ValidChoices);

        var before = game.SerializeFullState();
        var forged = game.Handle(owner,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "inahime-copy"));
        Assert.False(forged.Accepted);
        Assert.Equal(before, game.SerializeFullState());

        var choose = game.Handle(owner,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "inahime-other"));
        Assert.True(choose.Accepted, choose.Error);
        Pass(game);
        Assert.Equal(5000, game.State.Players[owner].Field[0][2]!.Troops);
        Assert.Equal(1000, game.State.Players[owner].Field[0][1]!.Troops);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("L12Evidence", "rule:inahime-only-same-name-no-target")]
    public void OnlyOtherNamedCopyEndsNaturallyWithoutADeadChoice(string timing, int owner, bool restore)
    {
        var game = Prepare(timing, owner, restore, includeOther: false);
        Pass(game);
        Assert.Equal(timing == "attack" ? L12Phase.Defense : L12Phase.Main, game.State.Phase);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Null(game.State.ResponseWindow);
        Assert.Equal(1000, game.State.Players[owner].Field[0][1]!.Troops);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("L12Evidence", "rule:inahime-declaration-rechecks-named-identity")]
    public void TargetBecomingNamedCopyBeforeDeclarationCannotEnterTheStack(
        string timing, int owner, bool restore)
    {
        var game = Prepare(timing, owner, restore: false, includeOther: true);
        var promptId = Assert.Single(game.State.PendingPrompts).PromptId;
        game.State.Players[owner].Field[0][2] = Card("S01-0416", "inahime-other", owner);
        if (restore) game = Restore(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(promptId, prompt.PromptId);
        // A saved declaration can still display its old choices. Commit must
        // rebuild current eligibility, never grant the now-ineligible identity.
        var attempt = game.Handle(owner,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "inahime-other"));
        Assert.True(attempt.Accepted, attempt.Error);
        Pass(game);
        Assert.Equal(1000, game.State.Players[owner].Field[0][2]!.Troops);
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceInstanceId == "inahime-source");
        Assert.Contains(game.State.Events, item => item.Type == "ability-rejected"
            && item.Text.Contains("失效", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("L12Evidence", "rule:inahime-resolution-rechecks-named-identity")]
    public void TargetBecomingNamedCopyDuringResponseFailsRatherThanBuffingIt(
        string timing, int owner, bool restore)
    {
        var game = Prepare(timing, owner, restore: false, includeOther: true);
        var prompt = Assert.Single(game.State.PendingPrompts);
        var chosen = game.Handle(owner,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "inahime-other"));
        Assert.True(chosen.Accepted, chosen.Error);
        Assert.NotNull(game.State.ResponseWindow);
        Assert.Contains(game.State.EffectStack, item => item.SourceInstanceId == "inahime-source");
        game.State.Players[owner].Field[0][2] = Card("S01-0416", "inahime-other", owner);
        if (restore) game = Restore(game);
        Pass(game);
        var target = game.State.Players[owner].Field[0][2]!;
        Assert.Equal(1000, target.Troops);
        Assert.Empty(target.TimedModifiers);
        Assert.Contains(game.State.Events, item => item.Type == "effect-failed"
            && item.Text.Contains("不再符合条件", StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events, item => item.Type == "effect-cancelled"
            && item.Text.Contains("稻姬本多小松", StringComparison.Ordinal));
    }

    private static L12GameEngine Prepare(string timing, int owner, bool restore, bool includeOther,
        string namedCardId = "S01-0416")
    {
        var deck = Catalog.DeckAt(1);
        var game = new L12GameEngine(Catalog, "inahime-named-exclusion", "INAHIME-NAMED", 20261022,
            ["甲", "乙"], [deck, deck], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = owner; game.State.FirstPlayer = owner;
        game.State.Round = 2; game.State.TurnSerial = 3; game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null; game.State.DisasterValue = 0;
        game.State.DisasterDeck.Clear(); game.State.RemovedDisasters.Clear();
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3]; player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear(); player.Library.Clear(); player.Graveyard.Clear(); player.Removed.Clear();
            player.Resolving.Clear(); player.Morale.Clear(); player.MoraleDeck.Clear();
            player.UsedAbilities.Clear(); player.Relic = null;
        }
        var self = game.State.Players[owner];
        var source = Card("S01-0416", "inahime-source", owner);
        self.Field[0][1] = Card(namedCardId, "inahime-copy", owner);
        if (includeOther) self.Field[0][2] = Card("S01-0404", "inahime-other", owner);
        if (timing == "attack")
        {
            self.Field[0][0] = source;
            var attack = game.Handle(owner,
                new L12Command("attack", source.InstanceId, Target: new L12AttackTarget("master")));
            Assert.True(attack.Accepted, attack.Error);
        }
        else
        {
            self.Hand.Add(source);
            for (var index = 0; index < source.Cost; index++)
                self.Morale.Add(new L12MoraleCard { CardId = "S01-04C1", InstanceId = "inahime-pay-" + index });
            var play = game.Handle(owner, new L12Command("playCard", source.InstanceId, Row: 0, Slot: 0));
            Assert.True(play.Accepted, play.Error);
            var payment = game.State.PendingPrompts.SingleOrDefault();
            if (payment?.Continuation == "play-morale-choice")
            {
                var ids = self.Morale.Where(card => !card.Tapped && payment.ValidChoices.Contains(card.InstanceId))
                    .Select(card => card.InstanceId).Take(int.Parse(payment.Data["cost"])).ToList();
                var paid = game.Handle(owner,
                    new L12Command("resolvePrompt", PromptId: payment.PromptId, CardInstanceIds: ids));
                Assert.True(paid.Accepted, paid.Error);
            }
            Pass(game);
        }
        return restore ? Restore(game) : game;
    }

    [Theory]
    [InlineData("enter", 0)]
    [InlineData("enter", 1)]
    [InlineData("attack", 0)]
    [InlineData("attack", 1)]
    [Trait("L12Evidence", "rule:inahime-printed-identity-not-cosmetic-label")]
    public void DisplayLabelsDoNotOverrideThePrintedCardIdentity(string timing, int owner)
    {
        var game = Prepare(timing, owner, restore: false, includeOther: true);
        // Rebuild candidate options through the normal restored declaration
        // commit, while cosmetic labels cannot turn either identity into another.
        game.State.Players[owner].Field[0][1] = Card("S01-0416", "inahime-copy", owner, "异画展示名称");
        game.State.Players[owner].Field[0][2] = Card("S01-0404", "inahime-other", owner, "稻姬本多小松");
        game = Restore(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.DoesNotContain("inahime-copy", prompt.ValidChoices);
        Assert.Contains("inahime-other", prompt.ValidChoices);
        var chosen = game.Handle(owner,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "inahime-other"));
        Assert.True(chosen.Accepted, chosen.Error);
        Pass(game);
        Assert.Equal(5000, game.State.Players[owner].Field[0][2]!.Troops);
        Assert.Equal(1000, game.State.Players[owner].Field[0][1]!.Troops);
    }

    private static L12CardInstance Card(string id, string instanceId, int owner, string? displayName = null)
    {
        var card = Catalog.Cards[id];
        return new L12CardInstance { CardId = id, InstanceId = instanceId, Name = displayName ?? card.NameZh, CardType = card.CardType,
            Faction = card.Faction, Cost = card.Cost ?? 0, HasPrintedCost = card.Cost.HasValue, EffectText = card.Effect,
            BaseTroops = card.Troops ?? 0, Troops = card.Troops ?? 0, Traits = [..card.Traits],
            Profession = card.Profession, EffectiveProfession = card.Profession, OwnerIndex = owner, SummonRound = -1 };
    }

    private static void Pass(L12GameEngine game)
    {
        for (var count = 0; count < 100 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; count++)
        {
            var prompt = game.State.PendingPrompts[0];
            var pass = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(pass.Accepted, pass.Error);
        }
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
}
