using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class HandPlayBlockLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence("S02-0205:ability:continuous:44bfa636b58de089", "same-card-exception", "display-and-submit-parity")]
    [L12AbilityEvidence("S02-0305:ability:continuous:26b824128ffced1a", "priority", "display-and-submit-parity")]
    public void HandPlayBlockDefinitionsMatchTheClosedFamily()
    {
        var actual = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.ExecutionModel == "continuous"
                && L12StructuredCardSemantics.HandPlayBlockRule(ability.CardId) is not null)
            .Select(ability => ability.AbilityId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(EffectLifecycleProfiles.HandPlayBlockAbilityIds.OrderBy(id => id, StringComparer.Ordinal), actual);

        var scarab = Assert.IsType<L12HandPlayBlockRule>(L12StructuredCardSemantics.HandPlayBlockRule("S02-0205"));
        var andvaranaut = Assert.IsType<L12HandPlayBlockRule>(L12StructuredCardSemantics.HandPlayBlockRule("S02-0305"));
        Assert.True(scarab.AllowsSameCardId);
        Assert.False(andvaranaut.AllowsSameCardId);
        Assert.True(andvaranaut.Priority < scarab.Priority);
        Assert.Equal("artifact", scarab.BlockedCardType);
        Assert.Equal("artifact", andvaranaut.BlockedCardType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GoldScarabBlocksOtherArtifactsFromEitherArtifactSlotButAllowsItsOwnName(bool useExtraRelic)
    {
        var player = Player();
        PlaceArtifactSource(player, Card("S02-0205", "scarab-source"), useExtraRelic);
        Assert.Null(L12StructuredCardRules.HandPlayBlockReason(player, Card("S02-0205", "same-scarab")));
        Assert.StartsWith("〈黄金圣甲虫〉", L12StructuredCardRules.HandPlayBlockReason(
            player, Card("S02-0520", "other-artifact")));
        Assert.Null(L12StructuredCardRules.HandPlayBlockReason(player, Card("S02-0501", "legion")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AndvaranautBlocksEveryArtifactFromEitherArtifactSlot(bool useExtraRelic)
    {
        var player = Player();
        PlaceArtifactSource(player, Card("S02-0305", "andvaranaut-source"), useExtraRelic);
        Assert.StartsWith("〈安德华拉诺特〉", L12StructuredCardRules.HandPlayBlockReason(
            player, Card("S02-0305", "same-andvaranaut")));
        Assert.StartsWith("〈安德华拉诺特〉", L12StructuredCardRules.HandPlayBlockReason(
            player, Card("S02-0205", "other-artifact")));
        Assert.Null(L12StructuredCardRules.HandPlayBlockReason(player, Card("S02-0301", "legion")));
    }

    [Fact]
    public void StricterBlockerWinsAndSnapshotMatchesAuthoritativeSubmission()
    {
        var game = Create(70300);
        var player = game.State.Players[0];
        player.Relic = Card("S02-0205", "scarab-source");
        player.ExtraRelics.Add(Card("S02-0305", "andvaranaut-source"));
        var handArtifact = Card("S02-0205", "hand-scarab");
        player.Hand.Add(handArtifact);

        var hand = Assert.IsType<L12CardInstance[]>(game.SnapshotFor(0).Players[0].GetType()
            .GetProperty("hand")!.GetValue(game.SnapshotFor(0).Players[0]));
        var view = Assert.Single(hand, card => card.InstanceId == handArtifact.InstanceId);
        Assert.StartsWith("〈安德华拉诺特〉", view.PlayBlockedReason);

        var result = game.Handle(0, new L12Command("playCard", handArtifact.InstanceId));
        Assert.False(result.Accepted);
        Assert.StartsWith("〈安德华拉诺特〉", result.Error);
        Assert.Contains(handArtifact, player.Hand);
    }

    [Fact]
    public void RemovingTheSourceImmediatelyRemovesTheDerivedBlock()
    {
        var player = Player();
        var source = Card("S02-0305", "andvaranaut-source");
        var candidate = Card("S02-0520", "artifact");
        player.Relic = source;
        Assert.NotNull(L12StructuredCardRules.HandPlayBlockReason(player, candidate));
        player.Relic = null;
        Assert.Null(L12StructuredCardRules.HandPlayBlockReason(player, candidate));
    }

    private static void PlaceArtifactSource(L12PlayerState player, L12CardInstance source, bool useExtraRelic)
    {
        if (useExtraRelic) player.ExtraRelics.Add(source);
        else player.Relic = source;
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "hand-play-block", "HAND-BLOCK", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        return game;
    }

    private static L12PlayerState Player() => new()
    {
        Name = "甲", DeckName = "hand-play-block", Faction = "universal", MasterId = "S01-00M1",
        MasterName = "主宰",
    };

    private static L12CardInstance Card(string cardId, string instanceId)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            TrialValue = definition.TrialValue ?? 0,
        };
    }
}
