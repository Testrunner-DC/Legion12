using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class QueuedUiProjectionRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static L12GameEngine Create(int seed)
        => new(Catalog, "queued-ui-projection", "QUEUED-UI", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true);

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
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost is not null,
            EffectText = definition.Effect,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
        };
    }

    private static JsonElement ProjectedCard(L12GameEngine game, int playerIndex = 0)
        => JsonSerializer.SerializeToElement(game.SnapshotFor(playerIndex), WebJson)
            .GetProperty("players")[playerIndex].GetProperty("field")[0][0];

    [Fact]
    public void ActiveTauntRemainsAnActiveKeywordWhenItIsNotSuppressed()
    {
        var game = Create(95031);
        game.State.Players[0].Field[0][0] = Card("ST01-04", "normal-taunt");

        var projected = ProjectedCard(game);

        Assert.Contains(projected.GetProperty("activeKeywords").EnumerateArray(),
            keyword => keyword.GetString() == "挑衅");
        Assert.DoesNotContain(projected.GetProperty("statusEffects").EnumerateArray(),
            effect => effect.GetProperty("kind").GetString() == "keyword-disabled");
    }

    [Fact]
    public void NieYinniangSuppressionRetainsTauntIdentityAsDisabledStatus()
    {
        var game = Create(95032);
        var player = game.State.Players[0];
        player.Field[0][0] = Card("ST01-04", "nie-yinniang-suppressed-taunt");
        player.UsedAbilities.Add($"starter-taunt-disabled:{game.State.TurnSerial}");

        var projected = ProjectedCard(game);

        Assert.DoesNotContain(projected.GetProperty("activeKeywords").EnumerateArray(),
            keyword => keyword.GetString() == "挑衅");
        var disabled = Assert.Single(projected.GetProperty("statusEffects").EnumerateArray(),
            effect => effect.GetProperty("kind").GetString() == "keyword-disabled");
        Assert.Equal("挑衅", disabled.GetProperty("label").GetString());
        Assert.Equal("聂隐娘", disabled.GetProperty("source").GetString());
    }

    [Fact]
    public void DisasterSuppressionUsesTheSameDisabledKeywordProjection()
    {
        var game = Create(95033);
        game.State.Players[0].Field[0][0] = Card("ST01-04", "disaster-suppressed-taunt");
        game.State.ActiveDisaster = Card("S02-DS02", "active-mist-disaster");

        var projected = ProjectedCard(game);

        Assert.DoesNotContain(projected.GetProperty("activeKeywords").EnumerateArray(),
            keyword => keyword.GetString() == "挑衅");
        var disabled = Assert.Single(projected.GetProperty("statusEffects").EnumerateArray(),
            effect => effect.GetProperty("kind").GetString() == "keyword-disabled");
        Assert.Equal("挑衅", disabled.GetProperty("label").GetString());
        Assert.Equal("迷雾绝境", disabled.GetProperty("source").GetString());
    }

    [Fact]
    public void TemporaryMoraleIsAuthoritativeInSnapshotsAndExpiresWithItsOwnersRest()
    {
        var game = Create(95034);
        var activePlayer = game.State.ActivePlayer;
        var player = game.State.Players[activePlayer];
        player.TemporaryMorale = 2;
        game.State.Phase = L12Phase.Main;

        var before = JsonSerializer.SerializeToElement(game.SnapshotFor(activePlayer), WebJson)
            .GetProperty("players")[activePlayer];
        Assert.Equal(2, before.GetProperty("temporaryMorale").GetInt32());

        Assert.True(game.Handle(activePlayer, new L12Command("endTurn")).Accepted);
        Assert.Equal(0, player.TemporaryMorale);
    }
}
