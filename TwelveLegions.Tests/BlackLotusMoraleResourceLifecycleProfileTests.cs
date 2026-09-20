using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class BlackLotusMoraleResourceLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static L12GameEngine Create(int seed)
    {
        var deck = Catalog.DeckAt(0);
        var game = new L12GameEngine(Catalog, "black-lotus-resource", "BLR", seed,
            ["甲", "乙"], [deck, deck], skipPreparation: true,
            autoPassEmptyResponses: true, concealHiddenResponseAvailability: false, disasterMode: "none");
        game.State.Phase = L12Phase.Main;
        game.State.ActivePlayer = 0;
        game.State.Round = 3;
        game.State.TurnSerial = 5;
        foreach (var player in game.State.Players)
        {
            player.Hand.Clear();
            player.Morale.Clear();
            player.Resolving.Clear();
            player.Graveyard.Clear();
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = cardId,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
        };
    }

    private static JsonElement PlayerSnapshot(L12GameEngine game, int viewer, int player)
        => JsonSerializer.SerializeToElement(game.SnapshotFor(viewer), WebJson)
            .GetProperty("players")[player];

    [Fact]
    [L12AbilityEvidence(EffectLifecycleProfiles.BlackLotusMoraleReturnAbilityId,
        "exact-card-family", "runtime-branch-mapping")]
    public void BlackLotusOwnsTheOnlyStructuredMoraleZoneReplacement()
    {
        var rule = Assert.IsType<L12MoraleZoneResourceRule>(
            L12StructuredCardSemantics.MoraleZoneResourceRule("S02-0010"));

        Assert.Equal("black-lotus", rule.ResourceType);
        Assert.Equal("黑色莲花", rule.DisplayName);
        Assert.True(rule.ReturnsToOwnerGraveyard);
        Assert.Null(L12StructuredCardSemantics.MoraleZoneResourceRule("S02-0009"));

        var profile = EffectLifecycleProfiles.Read(Catalog)[EffectLifecycleProfiles.BlackLotusMoraleReturnAbilityId];
        Assert.Equal("replacement:morale-zone-resource", profile.Id);
    }

    [Fact]
    [L12AbilityEvidence(EffectLifecycleProfiles.BlackLotusMoraleReturnAbilityId,
        "v2-snapshot", "reconnect", "frontend-structured-identity")]
    public void EveryViewerReceivesTheSameAuthoritativeResourceIdentity()
    {
        var game = Create(92701);
        var player = game.State.Players[0];
        player.Morale.Add(new L12MoraleCard
        {
            InstanceId = "snapshot-lotus", CardId = "S02-0010", Tapped = true,
        });
        player.Morale.Add(new L12MoraleCard
        {
            InstanceId = "snapshot-ordinary", CardId = "S01-01C1", Tapped = false,
        });

        foreach (var viewer in new[] { 0, 1 })
        {
            var morale = PlayerSnapshot(game, viewer, 0).GetProperty("morale");
            var lotus = morale.EnumerateArray().Single(card =>
                card.GetProperty("instanceId").GetString() == "snapshot-lotus");
            var ordinary = morale.EnumerateArray().Single(card =>
                card.GetProperty("instanceId").GetString() == "snapshot-ordinary");
            Assert.Equal("black-lotus", lotus.GetProperty("resourceType").GetString());
            Assert.Equal("morale", ordinary.GetProperty("resourceType").GetString());
            Assert.True(lotus.GetProperty("tapped").GetBoolean());
        }
    }

    [Fact]
    [L12AbilityEvidence(EffectLifecycleProfiles.BlackLotusMoraleReturnAbilityId,
        "payment-distinct-identity", "normal", "duplicate-submit")]
    public void PaymentPromptDistinguishesBlackLotusAndConsumesOnlyTheSelectedInstance()
    {
        var game = Create(92702);
        var player = game.State.Players[0];
        var legion = Card("S01-0116", "lotus-payment-legion");
        var lotus = new L12MoraleCard
        {
            InstanceId = "payment-lotus", CardId = "S02-0010", Tapped = false,
        };
        var ordinary = new L12MoraleCard
        {
            InstanceId = "payment-ordinary", CardId = "S01-01C1", Tapped = false,
        };
        player.Hand.Add(legion);
        player.Morale.Add(lotus);
        player.Morale.Add(ordinary);

        var begin = game.Handle(0, new L12Command("playCard", CardInstanceId: legion.InstanceId,
            Row: 0, Slot: 0));
        Assert.True(begin.Accepted, begin.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("resource-payment", prompt.Kind);
        Assert.Equal("black-lotus", prompt.Data[$"{lotus.InstanceId}:resourceType"]);
        Assert.Equal("morale", prompt.Data[$"{ordinary.InstanceId}:resourceType"]);
        Assert.Contains("黑色莲花", prompt.Text);
        Assert.Contains("士气", prompt.Text);

        var paid = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: [lotus.InstanceId]));
        Assert.True(paid.Accepted, paid.Error);
        Assert.True(lotus.Tapped);
        Assert.False(ordinary.Tapped);
        Assert.Same(legion, player.Field[0][0]);

        var duplicate = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: [lotus.InstanceId]));
        Assert.False(duplicate.Accepted);
        Assert.True(lotus.Tapped);
        Assert.False(ordinary.Tapped);
    }

    [Fact]
    [L12AbilityEvidence(EffectLifecycleProfiles.BlackLotusMoraleReturnAbilityId,
        "return-owner-graveyard", "target-invalidated", "duplicate-submit")]
    public void ReturnPromptAndSettlementUseTheSameIdentityWithoutSecondMovement()
    {
        var game = Create(92703);
        var player = game.State.Players[0];
        var liubei = Card("S01-0105", "lotus-return-liubei");
        player.Field[0][0] = liubei;
        player.Morale.Add(new L12MoraleCard
        {
            InstanceId = "return-ordinary", CardId = "S01-01C1", Tapped = false,
        });
        player.Morale.Add(new L12MoraleCard
        {
            InstanceId = "return-lotus", CardId = "S02-0010", Tapped = false,
        });

        var begin = game.Handle(0, new L12Command("activateAbility", liubei.InstanceId,
            Ability: "searchBrothers"));
        Assert.True(begin.Accepted, begin.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("resource-return", prompt.Kind);
        Assert.Equal("black-lotus", prompt.Data["return-lotus:resourceType"]);

        var returned = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: ["return-lotus"]));
        Assert.True(returned.Accepted, returned.Error);
        Assert.DoesNotContain(player.Morale, morale => morale.InstanceId == "return-lotus");
        Assert.Contains(player.Graveyard, card => card.InstanceId == "return-lotus");
        Assert.DoesNotContain(player.MoraleDeck, morale => morale.InstanceId == "return-lotus");

        var duplicate = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: ["return-lotus"]));
        Assert.False(duplicate.Accepted);
        Assert.Single(player.Graveyard, card => card.InstanceId == "return-lotus");
    }

    [Fact]
    [L12AbilityEvidence(EffectLifecycleProfiles.BlackLotusMoraleReturnAbilityId,
        "automatic-return", "return-owner-graveyard")]
    public void OnlyEligibleBlackLotusReturnsAutomaticallyToOwnerGraveyard()
    {
        var game = Create(92704);
        var player = game.State.Players[0];
        var liubei = Card("S01-0105", "automatic-lotus-return-liubei");
        player.Field[0][0] = liubei;
        player.Morale.Add(new L12MoraleCard
        {
            InstanceId = "automatic-lotus", CardId = "S02-0010", Tapped = false,
        });

        var result = game.Handle(0, new L12Command("activateAbility", liubei.InstanceId,
            Ability: "searchBrothers"));

        Assert.True(result.Accepted, result.Error);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "resource-return");
        Assert.DoesNotContain(player.Morale, morale => morale.InstanceId == "automatic-lotus");
        Assert.Contains(player.Graveyard, card => card.InstanceId == "automatic-lotus");
        Assert.DoesNotContain(player.MoraleDeck, morale => morale.InstanceId == "automatic-lotus");
    }
}
