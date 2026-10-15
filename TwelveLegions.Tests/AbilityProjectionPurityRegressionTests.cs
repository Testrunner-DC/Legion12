using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AbilityProjectionPurityRegressionTests
{
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-10-15T08:00:00Z");

    private static readonly string[] CurrentMasterCardIds =
    [
        "S01-01M1", "S01-01M2", "S01-02M1", "S01-02M2", "S01-02M3", "S01-03M1", "S01-03M2",
        "S01-04M1", "S01-04M2", "S02-01M1", "S02-02M1", "S02-03M1", "S02-04M1", "S02-05M1",
        "S02-05M2", "S02-06M1", "S02-06M2", "ST01-M1", "ST02-M1", "ST03-M1", "ST04-M1", "ST05-M1",
        "ST06-M1",
    ];

    private static readonly string[] ResolvedAuthoritativeSourceZones =
    [
        "field", "relic", "extra-relic", "trial", "graveyard",
    ];

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(0, true, false)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    public void BattlefieldMasterLegionProjectionKeepsAuthorityReadOnlyWhenMasterAndLegionTapDiffer(
        int owner, bool masterTapped, bool legionTapped)
    {
        var game = Create(owner, $"projection-owner{owner}-master{masterTapped}-legion{legionTapped}");
        var player = game.State.Players[owner];
        player.MasterTapped = masterTapped;
        var wukong = Card("S02-01M1", $"projection-wukong-{owner}", owner);
        wukong.IsMasterLegion = true;
        wukong.Tapped = legionTapped;
        wukong.AttacksThisTurn = legionTapped ? 1 : 0;
        player.Field[0][0] = wukong;

        var original = CaptureAuthority(game);
        AssertRepeatedRecipientProjectionsAreReadOnly(game, original, owner, masterTapped, legionTapped);

        var restored = Restore(original);
        AssertAuthorityEquals(original, restored);
        var restoredWukong = Assert.IsType<L12CardInstance>(restored.State.Players[owner].Field[0][0]);
        Assert.Equal(legionTapped, restoredWukong.Tapped);
        Assert.Equal(wukong.AttacksThisTurn, restoredWukong.AttacksThisTurn);
        AssertRepeatedRecipientProjectionsAreReadOnly(restored, original, owner, masterTapped, legionTapped);
    }

    [Fact]
    public void EveryCatalogMasterResolvedFromARealSourceZoneRemainsUnchangedDuringAbilityViewConstruction()
    {
        var actualMasterIds = Catalog.Cards.Values
            .Where(card => card.CardType == "master")
            .Select(card => card.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(CurrentMasterCardIds, actualMasterIds);

        var buildAbilityViews = typeof(L12GameEngine).GetMethod("BuildAbilityViews", PrivateInstance);
        Assert.NotNull(buildAbilityViews);

        foreach (var cardId in actualMasterIds)
        {
            var game = Create(0, $"all-master-sources-{cardId}");
            var player = game.State.Players[0];
            for (var zoneIndex = 0; zoneIndex < ResolvedAuthoritativeSourceZones.Length; zoneIndex++)
            {
                var zone = ResolvedAuthoritativeSourceZones[zoneIndex];
                var source = Card(cardId, $"source-{cardId}-{zone}", 0);
                source.Tapped = zoneIndex % 2 == 0;
                player.MasterTapped = !source.Tapped;
                PlaceSource(player, source, zone);
                var before = CaptureAuthority(game);

                var views = buildAbilityViews!.Invoke(game, [player, cardId, source.InstanceId]);

                Assert.IsAssignableFrom<IReadOnlyCollection<L12AbilityView>>(views);
                Assert.Equal(zoneIndex % 2 == 0, source.Tapped);
                AssertAuthorityEquals(before, game);
                RemoveSource(player, source, zone);
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ReturnedWukongKeepsItsPostAttackRestStateAcrossProjectionAndRestore(int owner)
    {
        var game = Create(owner, $"returned-wukong-owner{owner}");
        var player = game.State.Players[owner];
        player.MasterTapped = false;
        var wukong = Card("S02-01M1", $"returned-wukong-{owner}", owner);
        wukong.IsMasterLegion = true;
        wukong.Tapped = true;
        wukong.AttacksThisTurn = 1;
        player.Field[0][0] = wukong;

        var returned = typeof(L12GameEngine).GetMethod("ReturnWukongMasterLegions", PrivateInstance)!
            .Invoke(game, [player, "进攻结算后", false]);

        Assert.True(Assert.IsType<bool>(returned));
        Assert.Same(wukong, player.MasterLegionState);
        Assert.True(wukong.Tapped);
        Assert.Equal(1, wukong.AttacksThisTurn);
        var original = CaptureAuthority(game);
        AssertReturnedMasterProjectionIsReadOnly(game, original, owner, wukong.InstanceId);

        var restored = Restore(original);
        AssertAuthorityEquals(original, restored);
        var stored = Assert.IsType<L12CardInstance>(restored.State.Players[owner].MasterLegionState);
        Assert.True(stored.Tapped);
        Assert.Equal(1, stored.AttacksThisTurn);
        AssertReturnedMasterProjectionIsReadOnly(restored, original, owner, stored.InstanceId);
    }

    private static void AssertRepeatedRecipientProjectionsAreReadOnly(L12GameEngine game,
        AuthoritySnapshot original, int owner, bool masterTapped, bool legionTapped)
    {
        var first = CaptureRecipientProjections(game, original, owner, masterTapped, legionTapped);
        var second = CaptureRecipientProjections(game, original, owner, masterTapped, legionTapped);
        Assert.Equal(first, second);
        AssertAuthorityEquals(original, game);
    }

    private static string[] CaptureRecipientProjections(L12GameEngine game, AuthoritySnapshot original,
        int owner, bool masterTapped, bool legionTapped)
    {
        var snapshots = new[]
        {
            game.SnapshotFor(owner),
            game.SnapshotFor(1 - owner),
            game.SnapshotForSpectator(),
            game.SnapshotForReferee(),
        };
        var serialized = new string[snapshots.Length];
        for (var index = 0; index < snapshots.Length; index++)
        {
            var snapshot = snapshots[index];
            Assert.Equal(original.StateHash, snapshot.StateHash);
            serialized[index] = JsonSerializer.Serialize(snapshot, WireJson);
            using var json = JsonDocument.Parse(serialized[index]);
            var projectedPlayer = json.RootElement.GetProperty("players")[owner];
            Assert.Equal(masterTapped, projectedPlayer.GetProperty("master").GetProperty("tapped").GetBoolean());
            Assert.Equal(legionTapped,
                projectedPlayer.GetProperty("field")[0][0].GetProperty("tapped").GetBoolean());
            AssertAuthorityEquals(original, game);
        }

        using var ownerJson = JsonDocument.Parse(serialized[0]);
        var ownerPlayer = ownerJson.RootElement.GetProperty("players")[owner];
        Assert.Contains(ownerPlayer.GetProperty("master").GetProperty("abilities").EnumerateArray(),
            ability => ability.GetProperty("id").GetString() == "wukongTransform");
        Assert.Contains(ownerPlayer.GetProperty("field")[0][0].GetProperty("abilities").EnumerateArray(),
            ability => ability.GetProperty("id").GetString() == "wukongTransform");
        return serialized;
    }

    private static void AssertReturnedMasterProjectionIsReadOnly(L12GameEngine game,
        AuthoritySnapshot original, int owner, string privateInstanceId)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var snapshot in new[]
                     {
                         game.SnapshotFor(owner), game.SnapshotFor(1 - owner),
                         game.SnapshotForSpectator(), game.SnapshotForReferee(),
                     })
            {
                Assert.Equal(original.StateHash, snapshot.StateHash);
                var json = JsonSerializer.SerializeToElement(snapshot, WireJson);
                var projectedPlayer = json.GetProperty("players")[owner];
                Assert.True(projectedPlayer.GetProperty("master")
                    .GetProperty("tapped").GetBoolean());
                foreach (var row in projectedPlayer.GetProperty("field").EnumerateArray())
                {
                    foreach (var fieldCard in row.EnumerateArray())
                    {
                        Assert.True(fieldCard.ValueKind == JsonValueKind.Null
                                    || fieldCard.GetProperty("instanceId").GetString() != privateInstanceId);
                    }
                }
                AssertAuthorityEquals(original, game);
            }
        }
    }

    private static L12GameEngine Create(int wukongOwner, string caseId)
    {
        var baseDeck = Catalog.DeckAt(0);
        var wukongDeck = new L12PresetDeckDefinition
        {
            Name = "能力投影纯读",
            MasterId = "S02-01M1",
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        var decks = wukongOwner == 0
            ? new[] { wukongDeck, baseDeck }
            : new[] { baseDeck, wukongDeck };
        var game = new L12GameEngine(Catalog, $"ability-projection-{caseId}", "ABILITY-PROJECTION", 20261015,
            ["甲", "乙"], decks, skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            utcNow: () => FixedNow, stateFormatVersion: 2);
        game.State.ActivePlayer = wukongOwner;
        game.State.FirstPlayer = wukongOwner;
        game.State.Round = 3;
        game.State.TurnSerial = 10;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        game.State.DisasterDeck.Clear();
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Removed.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.Relic = null;
            player.ExtraRelics.Clear();
            player.SpecialZones.Trials.Clear();
            player.SpecialZones.CanopicProgress.Clear();
            player.SpecialZones.GodPower.Clear();
            player.MasterLegionState = null;
            player.UsedAbilities.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = cardId,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            EffectText = definition.Effect,
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            Traits = [.. definition.Traits],
            OwnerIndex = owner,
            SummonRound = 0,
        };
    }

    private static void PlaceSource(L12PlayerState player, L12CardInstance source, string zone)
    {
        switch (zone)
        {
            case "field": player.Field[0][0] = source; break;
            case "relic": player.Relic = source; break;
            case "extra-relic": player.ExtraRelics.Add(source); break;
            case "trial": player.SpecialZones.Trials.Add(source); break;
            case "graveyard": player.Graveyard.Add(source); break;
            default: throw new ArgumentOutOfRangeException(nameof(zone), zone, null);
        }
    }

    private static void RemoveSource(L12PlayerState player, L12CardInstance source, string zone)
    {
        switch (zone)
        {
            case "field": player.Field[0][0] = null; break;
            case "relic": player.Relic = null; break;
            case "extra-relic": Assert.True(player.ExtraRelics.Remove(source)); break;
            case "trial": Assert.True(player.SpecialZones.Trials.Remove(source)); break;
            case "graveyard": Assert.True(player.Graveyard.Remove(source)); break;
            default: throw new ArgumentOutOfRangeException(nameof(zone), zone, null);
        }
    }

    private static AuthoritySnapshot CaptureAuthority(L12GameEngine game)
    {
        var fullState = game.SerializeFullState();
        return new AuthoritySnapshot(fullState, Sha256(fullState), game.ComputeStateHash(),
            Assert.IsType<L12RandomState>(game.RandomState), game.CardFactSignalSequence);
    }

    private static void AssertAuthorityEquals(AuthoritySnapshot expected, L12GameEngine game)
    {
        var actualState = game.SerializeFullState();
        Assert.Equal(expected.FullState, actualState);
        Assert.Equal(expected.FullStateSha256, Sha256(actualState));
        Assert.Equal(expected.StateHash, game.ComputeStateHash());
        Assert.Equal(expected.RandomState, game.RandomState);
        Assert.Equal(expected.CardFactSignalSequence, game.CardFactSignalSequence);
    }

    private static L12GameEngine Restore(AuthoritySnapshot snapshot)
        => L12GameEngine.RestoreCheckpoint(Catalog, snapshot.FullState, snapshot.RandomState,
            snapshot.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, utcNow: () => FixedNow);

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record AuthoritySnapshot(string FullState, string FullStateSha256, string StateHash,
        L12RandomState RandomState, long CardFactSignalSequence);
}
