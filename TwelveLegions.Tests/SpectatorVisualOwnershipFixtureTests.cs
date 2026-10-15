using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SpectatorVisualOwnershipFixtureTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(
        Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record FixtureFrame(string Label, L12GameSnapshot Spectator,
        string SnapshotSha256, string StateHash);

    private static L12GameEngine Create(string matchId, int seed,
        bool autoPassEmptyResponses)
    {
        var game = new L12GameEngine(Catalog, matchId, "SPECTATOR-VISUAL-OWNER", seed,
            ["公开测试甲", "公开测试乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: autoPassEmptyResponses,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
            player.Relic = null;
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner = 0)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            OwnerIndex = owner,
            CardId = definition.Id,
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

    private static string[] AddPrivateHands(L12GameEngine game, string prefix)
    {
        var ids = new[] { $"{prefix}-private-a", $"{prefix}-private-b" };
        game.State.Players[0].Hand.Add(Card("S01-0001", ids[0], 0));
        game.State.Players[1].Hand.Add(Card("S01-0001", ids[1], 1));
        return ids;
    }

    private static string SnapshotHash(L12GameSnapshot snapshot)
        => Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot, Wire))));

    private static void AssertPublicSpectator(L12GameSnapshot snapshot,
        IReadOnlyCollection<string> privateInstanceIds)
    {
        var json = JsonSerializer.Serialize(snapshot, Wire);
        using var document = JsonDocument.Parse(json);
        var players = document.RootElement.GetProperty("players").EnumerateArray().ToArray();
        Assert.Equal(2, players.Length);
        Assert.All(players, player =>
        {
            Assert.False(player.TryGetProperty("hand", out _));
            Assert.True(player.TryGetProperty("handCount", out var handCount));
            Assert.True(handCount.GetInt32() >= 1);
        });
        Assert.All(privateInstanceIds,
            instanceId => Assert.DoesNotContain(instanceId, json, StringComparison.Ordinal));
    }

    private static FixtureFrame CaptureStableFrame(L12GameEngine game, string label,
        IReadOnlyCollection<string> privateInstanceIds, bool autoPassEmptyResponses)
    {
        var stateHash = game.ComputeStateHash();
        var spectator = game.SnapshotForSpectator();
        var spectatorHash = SnapshotHash(spectator);
        Assert.Equal(spectatorHash, SnapshotHash(game.SnapshotForSpectator()));
        Assert.Equal(stateHash, game.ComputeStateHash());
        AssertPublicSpectator(spectator, privateInstanceIds);

        var checkpoint = game.SerializeFullState();
        Assert.Equal(stateHash, game.ComputeStateHash());
        var randomState = game.RandomState
            ?? throw new InvalidOperationException("Focused visual fixture lost RNG state");
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, randomState,
            game.CardFactSignalSequence, autoPassEmptyResponses,
            concealHiddenResponseAvailability: false);
        Assert.Equal(stateHash, restored.ComputeStateHash());
        Assert.Equal(spectatorHash, SnapshotHash(restored.SnapshotForSpectator()));
        Assert.Equal(spectatorHash, SnapshotHash(restored.SnapshotForSpectator()));
        AssertPublicSpectator(restored.SnapshotForSpectator(), privateInstanceIds);
        return new FixtureFrame(label, spectator, spectatorHash, stateHash);
    }

    [Fact]
    public void ActualHandleTrajectoriesExportStablePublicSpectatorStateFacts()
    {
        var attackGame = Create("spectator-visual-attack", 26101501,
            autoPassEmptyResponses: true);
        var attackPrivate = AddPrivateHands(attackGame, "attack");
        var attacker = Card("S01-0001", "spectator-visual-attacker");
        attackGame.State.Players[0].Field[0][0] = attacker;
        var attackBaseline = CaptureStableFrame(attackGame, "attack-baseline-active",
            attackPrivate, autoPassEmptyResponses: true);
        var attackResult = attackGame.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(attackResult.Accepted, attackResult.Error);
        Assert.True(attacker.Tapped);
        var attackFact = Assert.Single(attackGame.State.Events, entry => entry.Type == "attack"
            && entry.PlayerCardStateTransition?.InstanceId == attacker.InstanceId);
        Assert.Equal(new L12PlayerCardStateTransition(attacker.InstanceId, false, true),
            attackFact.PlayerCardStateTransition);
        var attackCommit = CaptureStableFrame(attackGame, "attack-committed-rest",
            attackPrivate, autoPassEmptyResponses: true);

        var activeRestGame = Create("spectator-visual-active-rest", 26101502,
            autoPassEmptyResponses: false);
        var activeRestPrivate = AddPrivateHands(activeRestGame, "active-rest");
        var magician = Card("S02-0003", "spectator-visual-magician");
        activeRestGame.State.Players[0].Field[0][0] = magician;
        var activeRestBaseline = CaptureStableFrame(activeRestGame, "active-rest-baseline-active",
            activeRestPrivate, autoPassEmptyResponses: false);
        var activeRestResult = activeRestGame.Handle(0,
            new L12Command("activateAbility", magician.InstanceId, Ability: "disableCounters"));
        Assert.True(activeRestResult.Accepted, activeRestResult.Error);
        Assert.True(magician.Tapped);
        var activeRestFact = Assert.Single(activeRestGame.State.Events, entry => entry.Type == "state"
            && entry.PlayerCardStateTransition?.InstanceId == magician.InstanceId);
        Assert.Equal(new L12PlayerCardStateTransition(magician.InstanceId, false, true),
            activeRestFact.PlayerCardStateTransition);
        var activeRestCommit = CaptureStableFrame(activeRestGame, "active-rest-committed",
            activeRestPrivate, autoPassEmptyResponses: false);

        var readyGame = Create("spectator-visual-ready", 26101503,
            autoPassEmptyResponses: true);
        var readyPrivate = AddPrivateHands(readyGame, "ready");
        var gram = Card("S01-0317", "spectator-visual-gram");
        gram.Tapped = true;
        readyGame.State.Players[0].Relic = gram;
        readyGame.State.Players[0].Morale.AddRange([
            new L12MoraleCard { InstanceId = "spectator-visual-morale-a", CardId = "S01-03R1" },
            new L12MoraleCard { InstanceId = "spectator-visual-morale-b", CardId = "S01-03R1" },
        ]);
        var readyBaseline = CaptureStableFrame(readyGame, "ready-baseline-rested",
            readyPrivate, autoPassEmptyResponses: true);
        var readyResult = readyGame.Handle(0,
            new L12Command("activateAbility", gram.InstanceId, Ability: "gramReady"));
        Assert.True(readyResult.Accepted, readyResult.Error);
        Assert.False(gram.Tapped);
        var readyFact = Assert.Single(readyGame.State.Events, entry => entry.Type == "state"
            && entry.PlayerCardStateTransition?.InstanceId == gram.InstanceId);
        Assert.Equal(new L12PlayerCardStateTransition(gram.InstanceId, true, false),
            readyFact.PlayerCardStateTransition);
        var readyCommit = CaptureStableFrame(readyGame, "ready-committed-active",
            readyPrivate, autoPassEmptyResponses: true);

        var output = Environment.GetEnvironmentVariable("L12_SPECTATOR_VISUAL_FIXTURE_OUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        var fullOutput = Path.GetFullPath(output);
        var allowedRoot = Path.GetFullPath(
            @"D:\GPT\Legion12\artifacts\spectator-single-visual-owner-20261015");
        Assert.True(fullOutput.StartsWith(allowedRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase), $"Fixture output escaped evidence root: {fullOutput}");
        Assert.EndsWith(".json", fullOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("battle-authority-engine-v5", fullOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(fullOutput), $"Refusing to overwrite fixture: {fullOutput}");
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
        File.WriteAllText(fullOutput, JsonSerializer.Serialize(new
        {
            schema = 1,
            generatedFrom = "L12GameEngine.Handle + SnapshotForSpectator",
            attack = new[] { attackBaseline, attackCommit },
            activeRest = new[] { activeRestBaseline, activeRestCommit },
            ready = new[] { readyBaseline, readyCommit },
            facts = new
            {
                attack = new { attackFact.Sequence, attackFact.PlayerCardStateTransition,
                    finalTapped = attacker.Tapped },
                activeRest = new { activeRestFact.Sequence, activeRestFact.PlayerCardStateTransition,
                    finalTapped = magician.Tapped },
                ready = new { readyFact.Sequence, readyFact.PlayerCardStateTransition,
                    finalTapped = gram.Tapped },
            },
        }, Wire));
    }
}
