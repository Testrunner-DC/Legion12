using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ZoneMovementPresentationEventTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void MillPublishesOneOrderedAuthorityEventForEveryMovedCard(int count)
    {
        var game = Create(73010 + count);
        var player = game.State.Players[0];
        var cards = Enumerable.Range(0, count).Select(index => Card("S01-0001", $"mill-{index}", 0)).ToArray();
        player.Library.Clear();
        player.Library.AddRange(cards);
        game.State.Events.Clear();

        Invoke(game, "Mill", player, count, "动画契约测试");

        var movement = Assert.Single(game.State.Events, entry => entry.Type == "mill");
        Assert.Equal(cards.Select(card => card.InstanceId), movement.Cards.Select(card => card.InstanceId));
        Assert.Equal(cards.Select(card => card.InstanceId), player.Graveyard.Select(card => card.InstanceId));
        Assert.Empty(player.Library);
        Assert.Equal(movement.Sequence, game.SnapshotFor(0).RecentEvents.Single(entry => entry.Type == "mill").Sequence);
        Assert.Equal(movement.Sequence, game.SnapshotFor(1).RecentEvents.Single(entry => entry.Type == "mill").Sequence);
        Assert.Equal(movement.Sequence, game.SnapshotForSpectator().RecentEvents.Single(entry => entry.Type == "mill").Sequence);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void GraveToLibraryBottomPublishesExactlyOneReturnPerCardInAuthorityOrder(int count)
    {
        var game = Create(73020 + count);
        var player = game.State.Players[0];
        var existing = Card("S01-0001", "existing-library", 0);
        var cards = Enumerable.Range(0, count).Select(index => Card("S01-0003", $"grave-bottom-{index}", 0)).ToArray();
        player.Library.Clear();
        player.Library.Add(existing);
        player.Graveyard.Clear();
        player.Graveyard.AddRange(cards);
        game.State.Events.Clear();

        Invoke(game, "MoveGraveToLibraryBottom", player, cards.AsEnumerable());

        var returns = game.State.Events.Where(entry => entry.Type == "return").ToArray();
        Assert.Equal(count, returns.Length);
        Assert.Equal(cards.Select(card => card.InstanceId),
            returns.Select(entry => Assert.Single(entry.Cards).InstanceId));
        Assert.Equal(Enumerable.Range(0, count).Select(index => returns[0].Sequence + index),
            returns.Select(entry => entry.Sequence));
        Assert.Equal(new[] { existing.InstanceId }.Concat(cards.Select(card => card.InstanceId)),
            player.Library.Select(card => card.InstanceId));
        Assert.Empty(player.Graveyard);

        foreach (var snapshot in new[] { game.SnapshotFor(0), game.SnapshotFor(1), game.SnapshotForSpectator() })
            Assert.Equal(cards.Select(card => card.InstanceId), snapshot.RecentEvents
                .Where(entry => entry.Type == "return").Select(entry => Assert.Single(entry.Cards).InstanceId));

        var restored = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        Assert.Equal(player.Library.Select(card => card.InstanceId),
            restored.State.Players[0].Library.Select(card => card.InstanceId));
        Assert.Equal(returns.Select(entry => entry.Sequence), restored.State.Events
            .Where(entry => entry.Type == "return").Select(entry => entry.Sequence));
    }

    [Fact]
    public void GraveToLibraryTopPublishesOneReturnAndKeepsFinalZoneConsistent()
    {
        var game = Create(73030);
        var player = game.State.Players[0];
        var existing = Card("S01-0001", "existing-top-library", 0);
        var returned = Card("S01-0003", "grave-top", 0);
        player.Library.Clear();
        player.Library.Add(existing);
        player.Graveyard.Clear();
        player.Graveyard.Add(returned);
        game.State.Events.Clear();

        Invoke(game, "MoveGraveToLibraryTop", player, returned.InstanceId);

        var movement = Assert.Single(game.State.Events, entry => entry.Type == "return");
        Assert.Equal(returned.InstanceId, Assert.Single(movement.Cards).InstanceId);
        Assert.Contains("牌库顶部", movement.Text, StringComparison.Ordinal);
        Assert.Equal(new[] { returned.InstanceId, existing.InstanceId },
            player.Library.Select(card => card.InstanceId));
        Assert.Empty(player.Graveyard);
    }

    [Fact]
    public void SplitGraveDestinationCallerDoesNotRepublishBottomReturn()
    {
        var game = Create(73031);
        var player = game.State.Players[0];
        var handCard = Card("S01-0001", "split-to-hand", 0);
        var bottomCard = Card("S01-0003", "split-to-bottom", 0);
        player.Hand.Clear();
        player.Library.Clear();
        player.Graveyard.Clear();
        player.Graveyard.AddRange([handCard, bottomCard]);
        game.State.Events.Clear();
        var item = new L12StackItem
        {
            StackItemId = "split-grave-destinations",
            Controller = 0,
            SourceInstanceId = "split-source",
            SourceCardId = "S01-0004",
            SourceName = "测试来源",
            Trigger = "active",
            Text = "一张回手，一张回库",
        };

        Invoke(game, "ResolveDeclaredGraveDestinations", item,
            handCard.InstanceId, bottomCard.InstanceId, null);

        var movement = Assert.Single(game.State.Events, entry => entry.Type == "return"
            && entry.Cards.Any(card => card.InstanceId == bottomCard.InstanceId));
        Assert.Equal(bottomCard.InstanceId, Assert.Single(movement.Cards).InstanceId);
        Assert.Single(player.Library, card => card.InstanceId == bottomCard.InstanceId);
        Assert.Single(player.Hand, card => card.InstanceId == handCard.InstanceId);
        Assert.Empty(player.Graveyard);
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "zone-movement-events", "ZONE-MOVEMENT", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActiveDisaster = null;
        return game;
    }

    private static object? Invoke(object target, string name, params object?[] arguments)
        => target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(method => method.Name == name && method.GetParameters().Length == arguments.Length)
            .Invoke(target, arguments);

    private static L12CardInstance Card(string cardId, string instanceId, int ownerIndex)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            EffectText = definition.Effect,
            Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost is not null,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            OwnerIndex = ownerIndex,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
        };
    }
}
