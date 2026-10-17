using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class CatapultSummonTurnTargetPermissionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-17T08:30:00Z");

    public static IEnumerable<object[]> PaidTargets()
    {
        foreach (var owner in new[] { 0, 1 })
        foreach (var restore in new[] { false, true })
        foreach (var target in new[] { "master", "back" })
            yield return [owner, restore, target];
    }

    [Theory]
    [MemberData(nameof(PaidTargets))]
    public void PaidRangeAllowsOnlyPrintedTargetsOnTheSummonTurn(int owner, bool restore, string target)
    {
        var game = Arrange(owner, "S01-0003");
        var source = Source(game, owner);
        RejectUnchanged(game, owner, new("attack", "source", Target: new("master")));
        RejectUnchanged(game, owner, new("attack", "source", Target: new("legion", "back")));
        Pay(ref game, owner, restore);
        source = Source(game, owner);
        Assert.False(source.HasCharge);
        Assert.Equal(2, game.State.Players[owner].Morale.Count(card => card.Tapped));
        var targets = game.SnapshotFor(owner).LegalAttackTargets.GetValueOrDefault("source") ?? [];
        Assert.Contains("master", targets); Assert.Contains("back", targets);
        Assert.DoesNotContain("front", targets);
        RejectUnchanged(game, owner, new("attack", "source", Target: new("legion", "front")));
        Send(ref game, owner, new("attack", "source", Target: new(target == "master" ? "master" : "legion",
            target == "master" ? null : "back")), restore);
        source = Source(game, owner);
        Assert.True(source.Tapped); Assert.Equal(1, source.AttacksThisTurn); Assert.False(source.HasCharge);
        RejectUnchanged(game, owner, new("attack", "source", Target: new("master")));
        SettleCombat(ref game, restore);
        Assert.Null(game.State.PendingDefense); Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.PendingPrompts); Assert.Null(game.State.ResponseWindow);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void ABackOnlyAbilityDoesNotGainSummonTurnChargeOrMasterPermission(int owner)
    {
        var game = Arrange(owner, "S01-0113");
        Pay(ref game, owner, true);
        Assert.False(Source(game, owner).HasCharge);
        Assert.False(game.SnapshotFor(owner).LegalAttackTargets.ContainsKey("source"));
        foreach (var target in new[] { new L12AttackTarget("master"), new("legion", "front"), new("legion", "back") })
            RejectUnchanged(game, owner, new("attack", "source", Target: target));
    }

    [Fact]
    public void FullPoolPaidRangeRulesKeepBackOnlyAndSummonTurnPermissionsSeparate()
    {
        var rules = Catalog.Cards.Values.Where(card => L12StructuredCardSemantics.ExtendedRangeRule(card.Id) is not null)
            .Select(card => card.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "S01-0003", "S01-0113" }, rules);
        Assert.True(L12StructuredCardSemantics.ExtendedRangeRule("S01-0003")!.AllowsSummonTurnBackAndMaster);
        Assert.False(L12StructuredCardSemantics.ExtendedRangeRule("S01-0113")!.AllowsSummonTurnBackAndMaster);
    }

    private static L12GameEngine Arrange(int owner, string cardId)
    {
        var game = new L12GameEngine(Catalog, "catapult-summon-permission", "LOCAL", 17108300,
            ["合成甲", "合成乙"], [0, 0], skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2, utcNow: () => Now);
        game.State.PendingPrompts.Clear(); game.State.ActivePlayer = owner; game.State.FirstPlayer = 0;
        game.State.Round = 3; game.State.TurnSerial = 5; game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.Hand.Clear(); player.Graveyard.Clear(); player.Resolving.Clear();
            player.Morale.Clear(); player.UsedAbilities.Clear(); player.Relic = null; player.ExtraRelics.Clear();
        }
        var source = Card(cardId, "source", owner); source.SummonRound = game.State.Round;
        game.State.Players[owner].Field[1][0] = source;
        game.State.Players[1 - owner].Field[0][0] = Card("S01-0002", "front", 1 - owner);
        game.State.Players[1 - owner].Field[1][0] = Card("S01-0002", "back", 1 - owner);
        for (var i = 0; i < 2; i++)
            game.State.Players[owner].Morale.Add(new() { InstanceId = $"cost-{i}", CardId = "S01-01C1" });
        return game;
    }

    private static void Pay(ref L12GameEngine game, int owner, bool restore)
    {
        Send(ref game, owner, new("activateAbility", "source", Ability: "extendedRange"), restore);
        if (game.State.PendingPrompts.FirstOrDefault() is { Kind: "resource-payment" or "resource-return" } payment)
            Send(ref game, owner, new("resolvePrompt", PromptId: payment.PromptId,
                Choice: payment.ValidChoices.First(choice => choice != "cancel")), restore);
        Pass(ref game, restore);
        Assert.Empty(game.State.PendingPrompts); Assert.Empty(game.State.EffectStack);
    }

    private static void Pass(ref L12GameEngine game, bool restore)
    {
        for (var n = 0; game.State.PendingPrompts.FirstOrDefault() is { Kind: "response" } prompt; n++)
        {
            Assert.True(n < 40);
            Send(ref game, prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"), restore);
        }
    }

    private static void SettleCombat(ref L12GameEngine game, bool restore)
    {
        for (var n = 0; game.State.PendingDefense is not null || game.State.PendingPrompts.Count > 0; n++)
        {
            Assert.True(n < 60);
            Pass(ref game, restore);
            if (game.State.PendingDefense is { Stage: L12CombatStage.DefenseChoice } defense)
                Send(ref game, 1 - defense.AttackerPlayer, new("resolveDefense", CardInstanceIds: []), restore);
            else if (game.State.PendingPrompts.Count > 0)
                Assert.Fail("Unexpected non-response combat prompt.");
            else if (game.State.PendingDefense is { } stalled)
                Assert.Fail($"Unexpected stalled combat stage: {stalled.Stage}");
        }
    }

    private static void RejectUnchanged(L12GameEngine game, int owner, L12Command command)
    {
        var raw = game.SerializeFullState(); var hash = game.ComputeStateHash(); var random = game.RandomState;
        Assert.False(game.Handle(owner, command).Accepted);
        Assert.Equal(raw, game.SerializeFullState()); Assert.Equal(hash, game.ComputeStateHash()); Assert.Equal(random, game.RandomState);
    }

    private static void Send(ref L12GameEngine game, int owner, L12Command command, bool restore)
    {
        var result = game.Handle(owner, command); Assert.True(result.Accepted, result.Error);
        if (!restore) return;
        var raw = game.SerializeFullState(); var hash = game.ComputeStateHash(); var random = game.RandomState!.Value;
        game = L12GameEngine.RestoreCheckpoint(Catalog, raw, random, game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, utcNow: () => Now);
        Assert.Equal(raw, game.SerializeFullState()); Assert.Equal(hash, game.ComputeStateHash());
        Assert.Equal(random, game.RandomState);
    }

    private static L12CardInstance Source(L12GameEngine game, int owner)
        => Assert.IsType<L12CardInstance>(game.State.Players[owner].Field[1][0]);

    private static L12CardInstance Card(string id, string instance, int owner)
    {
        var d = Catalog.Cards[id];
        return new() { CardId = id, InstanceId = instance, OwnerIndex = owner, Name = d.NameZh,
            CardType = d.CardType, Faction = d.Faction, Cost = d.Cost ?? 0, HasPrintedCost = d.Cost.HasValue,
            BaseTroops = d.Troops ?? 0, Troops = d.Troops ?? 0, EffectText = d.Effect,
            ImageUrl = d.ImageUrl, Traits = [.. d.Traits], Profession = d.Profession, SummonRound = -1 };
    }
}
