using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class RagnarApocalypseChargeRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12CardInstance Card(string id, string instance, int owner)
    {
        var d = Catalog.Cards[id];
        return new L12CardInstance
        {
            InstanceId = instance, CardId = id, OwnerIndex = owner, Name = d.NameZh,
            CardType = d.CardType, Faction = d.Faction, ImageUrl = d.ImageUrl,
            Cost = d.Cost ?? 0, HasPrintedCost = d.Cost.HasValue, EffectText = d.Effect,
            Traits = [.. d.Traits], Profession = d.Profession,
            BaseTroops = d.Troops ?? 0, Troops = d.Troops ?? 0,
            DisasterLevel = d.DisasterLevel ?? 0, SummonRound = -1,
        };
    }

    private static L12GameEngine Create(int actor, int hp)
    {
        var basis = Catalog.DeckAt(0);
        var game = new L12GameEngine(Catalog, "synthetic-ragnar-apocalypse", "LOCAL", 107100 + actor + hp,
            ["合成甲", "合成乙"], [basis, basis], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.PendingPrompts.Clear();
        game.State.ActivePlayer = actor;
        game.State.FirstPlayer = actor;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(Card("S01-DS07", "apocalypse", actor));
        game.State.DisasterValue = 7;
        foreach (var p in game.State.Players)
        {
            p.Field[0] = new L12CardInstance?[3]; p.Field[1] = new L12CardInstance?[3];
            p.Hand.Clear(); p.Library.Clear(); p.Graveyard.Clear(); p.Resolving.Clear();
            p.ExtraRelics.Clear(); p.Relic = null; p.SpecialZones.CanopicProgress.Clear();
            p.Morale.Clear(); p.UsedAbilities.Clear();
            for (var i = 0; i < 20; i++) p.Library.Add(Card("S01-0003", $"library-{p.PlayerIndex}-{i}", p.PlayerIndex));
            var sentinel = Card("S01-0003", $"hand-sentinel-{p.PlayerIndex}", p.PlayerIndex);
            sentinel.HasCharge = true;
            sentinel.SummonRound = game.State.Round;
            p.Hand.Add(sentinel);
        }
        var actorState = game.State.Players[actor];
        actorState.Hp = hp;
        actorState.Field[0][0] = Card("S01-0003", "old-field-discard", actor);
        actorState.Field[0][1] = Card("S01-0003", "old-field-keep", actor);
        actorState.Hand.Insert(0, Card("S01-0303", "ragnar", actor));
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game, bool restore)
        => !restore ? game : L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState!.Value, game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static void Handle(L12GameEngine game, int owner, L12Command command)
    {
        var result = game.Handle(owner, command);
        Assert.True(result.Accepted, result.Error);
    }

    private static void Resolve(L12GameEngine game, L12Prompt p, string choice)
        => Handle(game, p.PlayerIndex, new L12Command("resolvePrompt", PromptId: p.PromptId, Choice: choice));

    private static L12CardInstance Ragnar(L12GameEngine game, int actor)
        => Assert.Single(game.State.Players[actor].Field.SelectMany(row => row), c => c?.InstanceId == "ragnar")!;

    public static IEnumerable<object[]> Cases()
    {
        foreach (var owner in new[] { 0, 1 })
        foreach (var payment in new[] { "hp7-normal", "self-damage", "hp8-normal" })
        foreach (var recovery in new[] { "none", "after-entry", "mid-hand" })
            yield return [owner, payment, recovery];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("L12Evidence", "card:S01-0303")]
    [Trait("L12Evidence", "disaster:apocalypse-preserves-survivor-state")]
    public void ActualApocalypseKeepsSurvivingRagnarsEntryChargeAndAttackPermission(
        int actor, string payment, string recovery)
    {
        var selfDamage = payment == "self-damage";
        var hp = payment == "hp7-normal" ? 7 : 8;
        var charge = payment != "hp8-normal";
        var game = Create(actor, hp);
        var player = game.State.Players[actor];
        var cost = player.Hand.Single(c => c.InstanceId == "ragnar").Cost - (selfDamage ? 1 : 0);
        for (var i = 0; i < cost; i++) player.Morale.Add(new L12MoraleCard
        {
            CardId = "S01-03C1", InstanceId = $"morale-{i}",
        });
        Handle(game, actor, new L12Command("playCard", "ragnar", Row: 0, Slot: 2,
            Choice: selfDamage ? null : "normal-cost"));
        if (selfDamage)
        {
            var q = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("play-cost-choice", q.Continuation);
            Resolve(game, q, "yes");
        }
        for (var n = 0; n < 40 && game.State.ActiveDisaster?.CardId != "S01-DS07"; n++)
        {
            var q = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("response", q.Kind);
            Resolve(game, q, "pass");
        }
        Assert.Equal("S01-DS07", game.State.ActiveDisaster!.CardId);
        Assert.Equal(charge, Ragnar(game, actor).HasCharge);
        Assert.Equal(selfDamage ? 7 : hp, game.State.Players[actor].Hp);
        game = Restore(game, recovery == "after-entry");
        for (var n = 0; n < 40 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; n++)
            Resolve(game, game.State.PendingPrompts[0], "pass");
        var keep = Assert.Single(game.State.PendingPrompts, q => q.Data.GetValueOrDefault("action") == "disaster-keep-field");
        Handle(game, keep.PlayerIndex, new L12Command("resolvePrompt", PromptId: keep.PromptId,
            CardInstanceIds: ["old-field-discard"]));
        var orders = 0;
        while (game.State.PendingPrompts.Count > 0)
        {
            var q = game.State.PendingPrompts.OrderBy(p => p.PlayerIndex).First();
            Assert.Equal("disaster-apocalypse-hand-order", q.Data.GetValueOrDefault("action"));
            Handle(game, q.PlayerIndex, new L12Command("resolvePrompt", PromptId: q.PromptId,
                TopCardInstanceIds: [], BottomCardInstanceIds: q.ValidChoices.AsEnumerable().Reverse().ToList()));
            orders++;
            game = Restore(game, recovery == "mid-hand" && orders == 1);
        }
        var ragnar = Ragnar(game, actor);
        Assert.Equal(charge, ragnar.HasCharge);
        Assert.Equal(game.State.Round, ragnar.SummonRound);
        Assert.Equal(2, game.State.Players[actor].Field.SelectMany(row => row).Count(c => c is not null));
        Assert.Single(game.State.Players[actor].Graveyard, c => c.InstanceId == "old-field-discard");
        foreach (var p in game.State.Players)
        {
            Assert.Equal(4, p.Hand.Count);
            var sentinel = Assert.Single(p.Library, c => c.InstanceId == $"hand-sentinel-{p.PlayerIndex}");
            Assert.False(sentinel.HasCharge);
            Assert.Equal(0, sentinel.SummonRound);
        }
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(actor), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var shown = snapshot.GetProperty("players")[actor].GetProperty("field").EnumerateArray()
            .SelectMany(row => row.EnumerateArray()).Single(c => c.ValueKind != JsonValueKind.Null
                && c.GetProperty("instanceId").GetString() == "ragnar");
        Assert.Equal(charge, shown.GetProperty("activeKeywords").EnumerateArray().Any(k => k.GetString() == "冲锋"));
        var attack = game.Handle(actor, new L12Command("attack", "ragnar", Target: new L12AttackTarget("master")));
        Assert.Equal(charge, attack.Accepted);
    }
}
