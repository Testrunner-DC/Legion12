using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class RingEffectiveFactionClosure20261029Tests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    public static IEnumerable<object[]> Cases()
    {
        foreach (var owner in new[] { 0, 1 })
        foreach (var ring in new[] { 0, 1, 2 })
        foreach (var restore in new[] { false, true }) yield return [owner, ring, restore];
    }
    private static L12CardInstance Card(string id, string instance, int owner)
    {
        var d = Catalog.Cards[id];
        return new() { CardId = id, InstanceId = instance, OwnerIndex = owner, Name = d.NameZh,
            CardType = d.CardType, Faction = d.Faction, ImageUrl = d.ImageUrl, Cost = d.Cost ?? 0,
            HasPrintedCost = d.Cost.HasValue, EffectText = d.Effect, IsCounterTactic = d.IsCounterTactic,
            Traits = [..d.Traits], Profession = d.Profession, EffectiveProfession = d.Profession,
            BaseTroops = d.Troops ?? 0, Troops = d.Troops ?? 0, DisasterLevel = d.DisasterLevel ?? 0,
            TrialValue = d.TrialValue ?? 0, SummonRound = -1 };
    }
    private static L12GameEngine Create(int owner, int ring, string faction, bool restore)
    {
        var basis = Catalog.DeckAt(0);
        var master = faction == "taiyangcheng" ? "S02-02M1"
            : Catalog.Cards.Values.First(c => c.CardType == "master" && c.Faction == faction).Id;
        var deck = new L12PresetDeckDefinition { Name = "有效阵营合成回归", MasterId = master,
            CardIds = [..basis.CardIds], MoraleIds = [..basis.MoraleIds], SpecialIds = [] };
        var game = new L12GameEngine(Catalog, "ring-closure", "RING-CLOSURE", 291029,
            ["甲", "乙"], owner == 0 ? [deck, basis] : [basis, deck], skipPreparation: true,
            autoPassEmptyResponses: true, concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = owner; game.State.FirstPlayer = owner;
        game.State.Round = 2; game.State.TurnSerial = 9; game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null; game.State.DisasterDeck.Clear();
        foreach (var p in game.State.Players)
        {
            p.Field[0] = new L12CardInstance?[3]; p.Field[1] = new L12CardInstance?[3];
            p.Hand.Clear(); p.Library.Clear(); p.Graveyard.Clear(); p.Resolving.Clear();
            p.Morale.Clear(); p.UsedAbilities.Clear(); p.Relic = null; p.ExtraRelics.Clear();
        }
        if (ring == 1) game.State.Players[owner].Relic = Card("S02-0008", "ring", owner);
        if (ring == 2) game.State.Players[owner].ExtraRelics.Add(Card("S02-0008", "ring", owner));
        return restore ? Restore(game) : game;
    }
    private static L12GameEngine Restore(L12GameEngine game) => L12GameEngine.RestoreCheckpoint(
        Catalog, game.SerializeFullState(), game.RandomState!.Value, game.CardFactSignalSequence,
        autoPassEmptyResponses: true, concealHiddenResponseAvailability: false);
    private static object? Invoke(L12GameEngine game, string name, params object?[] args)
        => typeof(L12GameEngine).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(game, args);
    private static L12StackItem Stack(L12GameEngine game, int owner, string source)
    {
        var item = new L12StackItem { StackItemId = "stack", Controller = owner,
            SourceInstanceId = "source", SourceCardId = source, SourceName = Catalog.Cards[source].NameZh,
            Trigger = "enter", Text = "有效阵营合成回归" };
        game.State.EffectStack.Add(item); game.State.IsResolvingStack = true; return item;
    }
    private static void Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(choice, prompt.ValidChoices);
        var result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    [Theory, MemberData(nameof(Cases))]
    public void FullCatalogEffectiveFactionNeverMutatesPrintedIdentity(int owner, int ring, bool restore)
    {
        foreach (var faction in new[] { "tianting", "taiyangcheng", "asgard", "gaotianyuan", "olympus", "otherworld" })
        {
            var game = Create(owner, ring, faction, restore); var player = game.State.Players[owner];
            foreach (var d in Catalog.Cards.Values)
            {
                var card = Card(d.Id, d.Id, owner);
                var expected = d.Faction == "universal" && ring > 0 ? faction : d.Faction;
                Assert.Equal(expected, L12StructuredCardRules.EffectiveFaction(player, card));
                Assert.Equal(d.Faction, card.Faction);
                Assert.Equal(d.CardType, card.CardType);
                Assert.Equal(d.Traits, card.Traits);
            }
        }
    }

    [Theory, MemberData(nameof(Cases))]
    public void NephthysTriggersOnceForConvertedDeathsAndDeclineReleasesReservation(int owner, int ring, bool restore)
    {
        var game = Create(owner, ring, "taiyangcheng", restore);
        game.State.ActivePlayer = 1 - owner;
        var player = game.State.Players[owner];
        var first = Card("S01-0002", "first", owner); var second = Card("S01-0002", "second", owner);
        player.Graveyard.AddRange([first, second, Card("S02-0201", "scarab", owner)]);
        var deaths = new (int Controller, L12CardInstance Card, L12CardInstance SourceSnapshot)[]
            { (owner, first, first), (owner, second, second) };
        Invoke(game, "QueueSimultaneousDeathTriggers", (object)deaths);
        if (ring == 0) { Assert.Empty(game.State.PendingPrompts); return; }
        var key = L12MasterTriggeredUsageRules.Key("nephthysScarab", owner, game.State.TurnSerial);
        Assert.Single(game.State.PendingPrompts);
        Assert.Contains($"{key}:pending", player.UsedAbilities);
        if (restore) game = Restore(game);
        Resolve(game, "mode:none"); player = game.State.Players[owner];
        Assert.DoesNotContain(key, player.UsedAbilities); Assert.DoesNotContain($"{key}:pending", player.UsedAbilities);
        Assert.NotNull(Invoke(game, "BuildNephthysOwnDeathCandidate", owner, first));
        player.UsedAbilities.Remove($"{key}:pending");
        player.Relic = null; player.ExtraRelics.Clear();
        Assert.Null(Invoke(game, "BuildNephthysOwnDeathCandidate", owner, first));
    }

    [Theory, MemberData(nameof(Cases))]
    public void ConvertedLegionActualCombatDeathCanSummonScarab(int owner, int ring, bool restore)
    {
        var game = Create(owner, ring, "taiyangcheng", restore); game.State.ActivePlayer = 1 - owner;
        game.State.Players[owner].Field[0][0] = Card("S01-0004", "target", owner);
        game.State.Players[owner].Graveyard.Add(Card("S02-0201", "scarab", owner));
        game.State.Players[owner].Library.Add(Card("S01-01C1", "infiltrator-death-draw", owner));
        game.State.Players[1 - owner].Field[0][0] = Card("ST03-01", "attacker", 1 - owner);
        var result = game.Handle(1 - owner, new L12Command("attack", "attacker", Target: new L12AttackTarget("legion", "target")));
        Assert.True(result.Accepted, result.Error);
        var accepted = false;
        for (var guard = 0; guard < 20 && game.State.PendingPrompts.Count > 0; guard++)
        {
            if (restore) game = Restore(game);
            var p = Assert.Single(game.State.PendingPrompts);
            if (p.Continuation == "trigger-batch-order")
            {
                // The infiltrator's mandatory death draw and Nephthys are two legitimate effects.
                var order = game.Handle(p.PlayerIndex, new L12Command("resolvePrompt",
                    PromptId: p.PromptId, CardInstanceIds: [..p.ValidChoices]));
                Assert.True(order.Accepted, order.Error);
                continue;
            }
            var choice = p.ValidChoices.Contains("mode:use") ? "mode:use"
                : p.ValidChoices.Contains("scarab") ? "scarab"
                : p.ValidChoices.Contains("0:0") ? "0:0" : "pass";
            if (choice is "mode:use" or "scarab") accepted = true;
            Resolve(game, choice);
        }
        Assert.Empty(game.State.PendingPrompts);
        Assert.Equal(ring > 0, accepted);
        Assert.Equal(ring > 0, game.State.Players[owner].Field.SelectMany(row => row).Any(c => c?.InstanceId == "scarab"));
        Assert.Contains(game.State.Players[owner].Graveyard, c => c.InstanceId == "target");
        if (ring > 0) Assert.Null(Invoke(game, "BuildNephthysOwnDeathCandidate", owner, Card("S01-0002", "later", owner)));
    }

    [Theory, MemberData(nameof(Cases))]
    public void SaladinAttackBonusUsesEffectiveFaction(int owner, int ring, bool restore)
    {
        var game = Create(owner, ring, "taiyangcheng", restore); var p = game.State.Players[owner];
        var attacker = Card("S01-0002", "attacker", owner); p.Field[0][0] = attacker;
        p.Field[0][1] = Card("S01-0206", "saladin", owner);
        if (restore) { game = Restore(game); attacker = game.State.Players[owner].Field[0][0]!; }
        var before = attacker.Troops;
        var result = game.Handle(owner, new L12Command("attack", "attacker", Target: new L12AttackTarget("master")));
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(ring > 0 ? 1000 : 0, game.State.PendingDefense?.TemporaryAttackerTroopsBonus);
        Assert.Equal(before + (ring > 0 ? 1000 : 0), attacker.Troops);
    }

    [Theory, MemberData(nameof(Cases))]
    public void LiJingRevealOffersConvertedLegionRecruitment(int owner, int ring, bool restore)
    {
        var game = Create(owner, ring, "tianting", restore); var p = game.State.Players[owner];
        p.Library.Add(Card("S01-0004", "top", owner));
        p.Morale.Add(new L12MoraleCard { CardId = "S01-01C1", InstanceId = "morale" });
        var item = Stack(game, owner, "S01-0103"); Invoke(game, "BeginLiJingEffect", item);
        if (restore) game = Restore(game);
        Assert.Equal(ring > 0, Assert.Single(game.State.PendingPrompts).ValidChoices.Contains("recruit"));
    }

    [Theory, MemberData(nameof(Cases))]
    public void CosmosYinRevealConsumesOnlyAnEffectiveTiantingLegion(int owner, int ring, bool restore)
    {
        var game = Create(owner, ring, "tianting", restore); var p = game.State.Players[owner];
        p.Library.Add(Card("S01-0004", "top", owner));
        var item = Stack(game, owner, "S02-0106"); Invoke(game, "ResolveS2CosmosYin", item);
        if (restore) game = Restore(game);
        Assert.Equal(ring > 0, game.State.Players[owner].Graveyard.Any(c => c.InstanceId == "top"));
        Assert.Equal(ring == 0, game.State.Players[owner].Library.Any(c => c.InstanceId == "top"));
    }

    [Theory, MemberData(nameof(Cases))]
    public void ShanheEligibilityUsesControllerWithoutChangingPrintedFaction(int owner, int ring, bool restore)
    {
        var game = Create(owner, ring, "tianting", restore); var card = Card("S01-0004", "top", owner);
        var method = typeof(L12GameEngine).GetMethod("IsShanheSearchCandidate", BindingFlags.NonPublic | BindingFlags.Static)!;
        var args = method.GetParameters().Length == 1 ? new object[] { card } : [game.State.Players[owner], card];
        Assert.Equal(ring > 0, (bool)method.Invoke(null, args)!);
        Assert.Equal("universal", card.Faction);
    }

    [Theory, MemberData(nameof(Cases))]
    public void TutankhamunDeclaredTargetUsesTheSameFactionAtSettlement(int owner, int ring, bool restore)
    {
        var game = Create(owner, ring, "taiyangcheng", restore); var p = game.State.Players[owner];
        p.Graveyard.Add(Card("S01-0004", "target", owner));
        var item = Stack(game, owner, "S01-0207"); item.Data["declared:recoverTarget"] = "target";
        Assert.True((bool)Invoke(game, "TryResolveS1FactionDeath", item, Card("S01-0207", "source", owner))!);
        if (restore) game = Restore(game);
        Assert.Equal(ring > 0, game.State.Players[owner].Library.Any(c => c.InstanceId == "target"));
        Assert.Equal(ring == 0, game.State.Players[owner].Graveyard.Any(c => c.InstanceId == "target"));
    }

    [Theory, MemberData(nameof(Cases))]
    public void FearlessAssassinationDeclaredTargetUsesEffectiveFactionAtSettlement(int owner, int ring, bool restore)
    {
        var game = Create(owner, ring, "taiyangcheng", restore); var p = game.State.Players[owner];
        p.Field[0][0] = Card("S01-0002", "target", owner);
        var item = Stack(game, owner, "S02-0206");
        item.Data["atomicFlow"] = "fearless-assassination"; item.Data["declared:buffTarget"] = "target";
        Assert.True((bool)Invoke(game, "TryResolveS2FactionTactic", item, Card("S02-0206", "source", owner))!);
        if (restore) game = Restore(game);
        Assert.Equal(ring > 0 ? 8000 : 5000, game.State.Players[owner].Field[0][0]!.Troops);
    }

    [Theory, MemberData(nameof(Cases))]
    public void NephthysKeepsTurnCostAndOncePerTurnRestrictions(int owner, int ring, bool restore)
    {
        foreach (var restriction in new[] { "own-turn", "cost-one", "already-used" })
        {
            var game = Create(owner, ring, "taiyangcheng", restore); var p = game.State.Players[owner];
            game.State.ActivePlayer = restriction == "own-turn" ? owner : 1 - owner;
            var card = Card(restriction == "cost-one" ? "S02-0005" : "S01-0004", "target", owner);
            if (restriction == "already-used") p.UsedAbilities.Add(L12MasterTriggeredUsageRules.Key("nephthysScarab", owner, game.State.TurnSerial));
            Assert.Null(Invoke(game, "BuildNephthysOwnDeathCandidate", owner, card));
        }
    }

    [Theory, MemberData(nameof(Cases))]
    public void ImhotepRevalidatesEffectiveFactionAfterResponse(int owner, int ring, bool removeRingAfterDeclaration)
    {
        var game = Create(owner, ring, "taiyangcheng", false); var p = game.State.Players[owner];
        var target = Card("S01-0001", "target", owner); p.Graveyard.Add(target);
        var source = Card("S02-0204", "source", owner); p.Field[0][0] = source;
        var item = Stack(game, owner, source.CardId); item.Data["declared:target"] = "target";
        if (removeRingAfterDeclaration)
        {
            p.Relic = null; p.ExtraRelics.Clear(); game = Restore(game);
            item = Assert.Single(game.State.EffectStack); source = game.State.Players[owner].Field[0][0]!;
        }
        Assert.True((bool)Invoke(game, "TryResolveBatch6JAEnterEffect", item, source)!);
        var expected = ring > 0 && !removeRingAfterDeclaration;
        Assert.Equal(expected, game.State.Players[owner].Hand.Any(c => c.InstanceId == "target"));
        Assert.Equal(!expected, game.State.Players[owner].Graveyard.Any(c => c.InstanceId == "target"));
    }

    [Theory, MemberData(nameof(Cases))]
    public void CanuteCannotReinterpretAnOutOfZoneTargetThroughItsControllersRing(int owner, int ring, bool restore)
    {
        foreach (var zone in new[] { "field", "graveyard", "hand", "library", "opponent-field" })
        {
            var game = Create(owner, ring, "asgard", false); var p = game.State.Players[owner];
            var target = Card("S01-0306", "target", owner);
            if (zone == "field") p.Field[0][1] = target;
            else if (zone == "graveyard") p.Graveyard.Add(target);
            else if (zone == "hand") p.Hand.Add(target);
            else if (zone == "library") p.Library.Add(target);
            else game.State.Players[1-owner].Field[0][1] = target;
            var source = Card("S02-0303", "source", owner); p.Field[0][0] = source;
            var item = Stack(game, owner, source.CardId); item.Data["declared:targets"] = "target";
            if (restore) { game = Restore(game); item = Assert.Single(game.State.EffectStack); source = game.State.Players[owner].Field[0][0]!; }
            Assert.True((bool)Invoke(game, "TryResolveBatch6JAEnterEffect", item, source)!);
            Assert.Equal(zone is "field" or "graveyard", game.State.Events.Any(e => e.Text.Contains("卡纽特大帝触发了1张", StringComparison.Ordinal)));
        }
    }
}
