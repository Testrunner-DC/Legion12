using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class TrialProgressPrivacyTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static L12CardInstance Card(string id, string instance)
    {
        var card = Catalog.Cards[id];
        return new L12CardInstance
        {
            CardId = id, InstanceId = instance, Name = card.NameZh, CardType = card.CardType,
            Faction = card.Faction, EffectText = card.Effect, ImageUrl = card.ImageUrl, OwnerIndex = 0,
            Profession = card.Profession, BaseTroops = card.Troops ?? 0, Troops = card.Troops ?? 0,
            TrialValue = card.TrialValue ?? 0, SummonRound = -1,
        };
    }

    private static L12GameEngine Game(string matchId = "trial-privacy", int version = 2)
    {
        var game = new L12GameEngine(Catalog, matchId, "TRIAL", 91730, ["甲", "乙"], [0, 0],
            skipPreparation: true, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: version);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        game.State.Round = 2;
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.SpecialZones.Trials.Clear();
            player.Hand.Clear(); player.Graveyard.Clear(); player.Resolving.Clear();
        }
        return game;
    }

    private static void AssertNoIdentity(object value, L12CardInstance trial)
    {
        var json = JsonSerializer.Serialize(value, Json);
        Assert.DoesNotContain(trial.Name, json);
        Assert.DoesNotContain(trial.CardId, json);
        Assert.DoesNotContain(trial.InstanceId, json);
        if (trial.ImageUrl is not null) Assert.DoesNotContain(trial.ImageUrl, json);
    }

    private static void AssertNoTrialDisclosure(IEnumerable<L12ActionEvent> events, L12CardInstance trial)
    {
        foreach (var entry in events)
        {
            AssertNoIdentity(entry with { Cards = [] }, trial);
            // Public source card text can mention a trial by name (e.g. Galahad).
            // That printed text is not evidence of which hidden trial was advanced.
            var cards = JsonSerializer.Serialize(entry.Cards, Json);
            Assert.DoesNotContain(trial.CardId, cards);
            Assert.DoesNotContain(trial.InstanceId, cards);
        }
    }

    [Theory]
    [InlineData("S02-0618")]
    [InlineData("S02-0609")]
    [InlineData("S02-0604")]
    [InlineData("S02-0613")]
    [InlineData("S02-0606")]
    [InlineData("S02-0614")]
    [InlineData("S02-0617")]
    [InlineData("S02-0610")]
    public void EveryUsualTrialSourcePublishesOnlyProgressForEveryHiddenTrial(string sourceId)
    {
        foreach (var definition in Catalog.Cards.Values.Where(card => card.CardType == "trial"))
        {
            var game = Game();
            var trial = Card(definition.Id, "hidden-trial-target");
            game.State.Players[0].SpecialZones.Trials.Add(trial);
            var source = Card(sourceId, "public-trial-legion");
            game.State.Players[0].Field[0][0] = source;
            var result = game.Handle(0, new L12Command("activateAbility", source.InstanceId, Ability: "trialAdvance"));
            Assert.True(result.Accepted, result.Error);
            var progress = Assert.Single(game.SnapshotFor(1).RecentEvents, entry => entry.Type == "trial");
            Assert.Equal($"试炼进度 0 → {source.TrialValue}", progress.Text);
            AssertNoTrialDisclosure([progress], trial);
            Assert.Equal(source.InstanceId, Assert.Single(progress.Cards).InstanceId);
            Assert.Equal(source.TrialValue, trial.TrialProgress);
            Assert.False(trial.TrialCompleted);
            foreach (var snapshot in new[] { game.SnapshotFor(0), game.SnapshotFor(1), game.SnapshotForSpectator() })
            {
                AssertNoTrialDisclosure(snapshot.RecentEvents, trial);
                AssertNoTrialDisclosure([snapshot.LastAction!], trial);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedProgressNeverAttachesTheUnrevealedTrialWhenPublicSourceIsAbsent(bool sourceIsTrial)
    {
        var game = Game();
        var trial = Card("S02-06S4", "unrevealed-trial");
        trial.TrialProgress = 7;
        game.State.Players[0].SpecialZones.Trials.Add(trial);
        typeof(L12GameEngine).GetMethod("AdvanceTrial", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(game, [0, 2, sourceIsTrial ? trial : null]);
        var progress = Assert.Single(game.SnapshotFor(1).RecentEvents, entry => entry.Type == "trial");
        Assert.Equal("试炼进度 7 → 8", progress.Text);
        Assert.Empty(progress.Cards);
        AssertNoIdentity(progress, trial);
        Assert.False(trial.TrialCompleted); // Reaching 8 does not reveal/complete the trial.
    }

    [Fact]
    public void LegacyProgressIsRedactedInRecoveredViewsWithoutRewritingTheCheckpoint()
    {
        var game = Game();
        var trial = Card("S02-06S4", "legacy-hidden-trial");
        game.State.Players[0].SpecialZones.Trials.Add(trial);
        var legacy = new L12ActionEvent(++game.State.EventSequence, "trial", 0,
            $"《{trial.Name}》试炼进度 0 → 2", [trial.Clone()]) { EffectText = trial.EffectText };
        game.State.Events.Add(legacy); game.State.LastAction = legacy; game.State.Log.Add(legacy.Text);
        game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
            game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        var raw = game.SerializeFullState();
        foreach (var snapshot in new[] { game.SnapshotFor(0), game.SnapshotFor(1), game.SnapshotForSpectator() })
        {
            AssertNoIdentity(snapshot.RecentEvents, trial);
            AssertNoIdentity(snapshot.LastAction!, trial);
        }
        Assert.Equal(raw, game.SerializeFullState());
    }

    [Fact]
    public async Task LegacyPlayerReplayRedactsProgressTextCardsLastActionAndLogButNotCompletion()
    {
        var game = Game("legacy-trial-replay", 0);
        var trial = Card("S02-06S4", "replay-hidden-trial");
        game.State.Players[0].SpecialZones.Trials.Add(trial);
        var legacy = new L12ActionEvent(++game.State.EventSequence, "trial", 0,
            $"《{trial.Name}》试炼进度 0 → 2", [trial.Clone()]);
        game.State.Events.Add(legacy); game.State.LastAction = legacy; game.State.Log.Add(legacy.Text);
        var path = Path.Combine(Path.GetTempPath(), "l12-trial-privacy", Guid.NewGuid().ToString("N"), "matches.db");
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        await recorder.StartAsync(game.State);
        await recorder.AppendAsync(game, 1, 0, "{\"type\":\"noop\"}", CommandResult.Ok());
        trial.TrialCompleted = true;
        var complete = new L12ActionEvent(++game.State.EventSequence, "trial", 0, $"完成试炼《{trial.Name}》", [trial.Clone()]);
        game.State.Events.Add(complete); game.State.LastAction = complete; game.State.Log.Add(complete.Text);
        await recorder.AppendAsync(game, 2, 0, "{\"type\":\"noop\"}", CommandResult.Ok());
        game.ConcludeByAuthority(0, "合成回放结束");
        await recorder.CompleteAsync(game);
        var detail = Assert.IsType<L12MatchDetail>(await recorder.GetMatchForPlayerAsync(game.State.MatchId, "乙"));
        AssertNoIdentity(detail.Commands[0].State, trial);
        var last = detail.Commands[1].State.GetProperty("LastAction");
        Assert.Equal(complete.Text, last.GetProperty("Text").GetString());
        Assert.Equal(trial.CardId, last.GetProperty("Cards")[0].GetProperty("CardId").GetString());
        Assert.Contains(trial.Name, (await recorder.GetMatchAsync(game.State.MatchId))!.Commands[0].State
            .GetProperty("LastAction").GetProperty("Text").GetString());
    }

    [Fact]
    public async Task JournalReplayPreservesOldCheckpointHashesAndRedactsBothOldAndNewProgress()
    {
        var game = Game("journal-trial-privacy");
        var trial = Card("S02-06S4", "journal-hidden-trial");
        game.State.Players[0].SpecialZones.Trials.Add(trial);
        var source = Card("S02-0614", "journal-public-source");
        game.State.Players[0].Field[0][0] = source;
        var legacy = new L12ActionEvent(++game.State.EventSequence, "trial", 0,
            $"《{trial.Name}》试炼进度 0 → 1", [trial.Clone()]);
        game.State.Events.Add(legacy); game.State.LastAction = legacy; game.State.Log.Add(legacy.Text);
        var path = Path.Combine(Path.GetTempPath(), "l12-trial-privacy", Guid.NewGuid().ToString("N"), "matches.db");
        await using var recorder = new MatchRecorder(path);
        recorder.AttachCatalog(Catalog);
        await recorder.InitializeAsync();
        await recorder.StartAsync(game);
        var advance = new L12Command("activateAbility", source.InstanceId, Ability: "trialAdvance");
        var advanced = game.Handle(0, advance);
        Assert.True(advanced.Accepted, advanced.Error);
        await recorder.AppendAsync(game, 1, 0, JsonSerializer.Serialize(advance), advanced);
        var surrender = new L12Command("surrender");
        await recorder.AppendAsync(game, 2, 1, JsonSerializer.Serialize(surrender), game.Handle(1, surrender));
        await recorder.CompleteAsync(game);
        var opponent = Assert.IsType<L12MatchDetail>(await recorder.GetMatchForPlayerAsync(game.State.MatchId, "乙"));
        AssertNoIdentity(opponent.Commands[0].State, trial);
        var owner = Assert.IsType<L12MatchDetail>(await recorder.GetMatchForPlayerAsync(game.State.MatchId, "甲"));
        Assert.Equal(trial.CardId, owner.Commands[0].State.GetProperty("Players")[0]
            .GetProperty("SpecialZones").GetProperty("Trials")[0].GetProperty("CardId").GetString());
        AssertNoIdentity(owner.Commands[0].State.GetProperty("Events"), trial);
        AssertNoIdentity(owner.Commands[0].State.GetProperty("Log"), trial);
    }
}
