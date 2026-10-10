using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class TrialPlayerLogSemanticTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12CardInstance Card(string id, string instanceId, int owner = 0)
    {
        var definition = Catalog.Cards[id];
        return new L12CardInstance
        {
            CardId = id,
            InstanceId = instanceId,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            EffectText = definition.Effect,
            ImageUrl = definition.ImageUrl,
            OwnerIndex = owner,
            Profession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }

    private static L12GameEngine Game(string matchId)
    {
        var game = new L12GameEngine(Catalog, matchId, "TRIAL", 92801, ["甲", "乙"], [0, 0],
            skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        game.State.Round = 2;
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.SpecialZones.Trials.Clear();
            player.Hand.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
        }
        return game;
    }

    [Fact]
    public void AdvanceTrialPublishesSafeStructuredSemanticsForEveryRecipient()
    {
        var game = Game("trial-player-log-progress");
        var hiddenTrial = Card("S02-06S4", "hidden-trial");
        var source = Card("S02-0614", "public-source");
        game.State.Players[0].SpecialZones.Trials.Add(hiddenTrial);
        game.State.Players[0].Field[0][0] = source;

        var advanced = Assert.IsType<bool>(typeof(L12GameEngine)
            .GetMethod("AdvanceTrial", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(game, [0, 2, source]));

        Assert.True(advanced);
        foreach (var events in new[]
                 {
                     game.SnapshotFor(0).RecentEvents,
                     game.SnapshotFor(1).RecentEvents,
                     game.SnapshotForSpectator().RecentEvents,
                 })
        {
            var progress = Assert.Single(events, item => item.Type == "trial");
            var semantic = Assert.IsType<L12PlayerLogSemantic>(progress.PlayerLogSemantic);
            Assert.Equal("推进试炼", semantic.ActionLabel);
            Assert.Equal("试炼 0→2", semantic.OutcomeLabel);
            Assert.Equal(source.InstanceId, semantic.SourceInstanceId);
            Assert.Null(semantic.TargetInstanceId);
            Assert.Null(semantic.TargetName);
            Assert.DoesNotContain(hiddenTrial.Name, progress.Text);
            Assert.DoesNotContain(hiddenTrial.InstanceId, progress.Text);
        }
    }

    [Fact]
    public void CompleteTrialPublishesCompletionInsteadOfAdvancement()
    {
        var game = Game("trial-player-log-completion");
        var trial = Card("S02-06S4", "completed-trial");
        trial.TrialProgress = 8;
        game.State.Players[0].SpecialZones.Trials.Add(trial);

        typeof(L12GameEngine).GetMethod("CompleteTrialRuleAction",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(game, [0, trial]);

        var completed = Assert.Single(game.State.Events, item => item.Type == "trial");
        var semantic = Assert.IsType<L12PlayerLogSemantic>(completed.PlayerLogSemantic);
        Assert.Equal("完成试炼", semantic.ActionLabel);
        Assert.Equal("试炼已翻至完成面", semantic.OutcomeLabel);
        Assert.Equal(trial.InstanceId, semantic.SourceInstanceId);
    }

    [Fact]
    public void ProgressRedactionPreservesUnknownPresentationMetadata()
    {
        var source = Card("S02-0614", "visible-source");
        var hiddenTrial = Card("S02-06S4", "hidden-target");
        var authority = new L12ActionEvent(17, "trial", 0,
            $"《{hiddenTrial.Name}》试炼进度 1 → 3", [source, hiddenTrial])
        {
            EffectSceneId = "scene-17",
            EffectAbilityId = "ability-17",
            EffectSegmentId = "segment-17",
            EffectSegmentIndex = 1,
            EffectSegmentCount = 2,
            EffectBranchId = "branch-17",
            EffectBranchLabel = "选择推进",
            EffectResultStatus = "resolved",
            PlayerLogGroupId = "trial:17",
            PlayerLogTiming = "enter",
            PlayerLogDecisionLabel = "选择推进试炼",
            PlayerLogSemantic = new L12PlayerLogSemantic("推进试炼", "试炼 1→3",
                source.InstanceId, "不应直接信任的来源名称", hiddenTrial.InstanceId, hiddenTrial.Name),
        };

        var projected = L12TrialProgressVisibility.PublicEvent(authority);

        Assert.Equal("scene-17", projected.EffectSceneId);
        Assert.Equal("ability-17", projected.EffectAbilityId);
        Assert.Equal("segment-17", projected.EffectSegmentId);
        Assert.Equal(1, projected.EffectSegmentIndex);
        Assert.Equal(2, projected.EffectSegmentCount);
        Assert.Equal("branch-17", projected.EffectBranchId);
        Assert.Equal("选择推进", projected.EffectBranchLabel);
        Assert.Equal("resolved", projected.EffectResultStatus);
        Assert.Equal("trial:17", projected.PlayerLogGroupId);
        Assert.Equal("enter", projected.PlayerLogTiming);
        Assert.Equal("选择推进试炼", projected.PlayerLogDecisionLabel);
        var semantic = Assert.IsType<L12PlayerLogSemantic>(projected.PlayerLogSemantic);
        Assert.Equal(source.InstanceId, semantic.SourceInstanceId);
        Assert.Equal(source.Name, semantic.SourceName);
        Assert.Null(semantic.TargetInstanceId);
        Assert.Null(semantic.TargetName);
        Assert.DoesNotContain(hiddenTrial.Name, projected.Text);
        Assert.Equal(source.InstanceId, Assert.Single(projected.Cards).InstanceId);
    }

    [Fact]
    public async Task PlayerReplayPreservesTheSameSafeTrialSemanticsAsLiveSnapshots()
    {
        var game = Game("trial-player-log-replay");
        var hiddenTrial = Card("S02-06S4", "replay-hidden-trial");
        var source = Card("S02-0614", "replay-public-source");
        game.State.Players[0].SpecialZones.Trials.Add(hiddenTrial);
        game.State.Players[0].Field[0][0] = source;
        var directory = Path.Combine(Path.GetTempPath(), "l12-trial-player-log",
            Guid.NewGuid().ToString("N"));
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        recorder.AttachCatalog(Catalog);
        await recorder.InitializeAsync();
        await recorder.StartAsync(game);

        var command = new L12Command("activateAbility", source.InstanceId, Ability: "trialAdvance");
        var result = game.Handle(0, command);
        Assert.True(result.Accepted, result.Error);
        await recorder.AppendAsync(game, 1, 0, JsonSerializer.Serialize(command), result);
        game.ConcludeByAuthority(0, "试炼日志回放测试结束");
        await recorder.CompleteAsync(game);

        foreach (var account in new[] { "甲", "乙" })
        {
            var replay = Assert.IsType<L12MatchDetail>(
                await recorder.GetMatchForPlayerAsync(game.State.MatchId, account));
            var events = replay.Commands[0].State.GetProperty("Events").EnumerateArray();
            var progress = events.Single(item => item.GetProperty("Type").GetString() == "trial");
            var semantic = progress.GetProperty("PlayerLogSemantic");
            Assert.Equal("推进试炼", semantic.GetProperty("ActionLabel").GetString());
            Assert.Equal($"试炼 0→{source.TrialValue}", semantic.GetProperty("OutcomeLabel").GetString());
            Assert.Equal(source.InstanceId, semantic.GetProperty("SourceInstanceId").GetString());
            Assert.Equal(source.Name, semantic.GetProperty("SourceName").GetString());
            Assert.False(semantic.TryGetProperty("TargetInstanceId", out var targetId)
                && targetId.ValueKind != JsonValueKind.Null);
            Assert.False(semantic.TryGetProperty("TargetName", out var targetName)
                && targetName.ValueKind != JsonValueKind.Null);
            Assert.DoesNotContain(hiddenTrial.Name, progress.GetProperty("Text").GetString());
        }
    }
}
