using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ArchitectureP1CrossLayerGoldenTests
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void OrphanedPromptReadOrderDocumentsSnapshotReconciliationWithoutChangingPersistenceTiming()
    {
        var projectionFirst = CreateWithOrphanedPrompt();
        var persistenceFirst = CreateWithOrphanedPrompt();

        _ = projectionFirst.SnapshotFor(0);
        var projectionState = projectionFirst.SerializeFullState();
        var projectionHash = projectionFirst.ComputeStateHash();

        var persistenceStateBeforeReconciliation = persistenceFirst.SerializeFullState();
        var persistenceHashBeforeReconciliation = persistenceFirst.ComputeStateHash();
        _ = persistenceFirst.SnapshotFor(0);
        var persistenceState = persistenceFirst.SerializeFullState();
        var persistenceHash = persistenceFirst.ComputeStateHash();

        Assert.NotEqual(Sha256(projectionState), Sha256(persistenceStateBeforeReconciliation));
        Assert.NotEqual(projectionHash, persistenceHashBeforeReconciliation);
        Assert.Equal(Sha256(projectionState), Sha256(persistenceState));
        Assert.Equal(projectionHash, persistenceHash);
        Assert.Equal(projectionFirst.State.Revision, persistenceFirst.State.Revision);
        Assert.Equal(projectionFirst.State.EventSequence, persistenceFirst.State.EventSequence);
        Assert.Empty(projectionFirst.State.PendingPrompts);
        Assert.Empty(persistenceFirst.State.PendingPrompts);
        Assert.Equal(
            projectionFirst.State.Events.Select(item => (item.Sequence, item.Type, item.Text)),
            persistenceFirst.State.Events.Select(item => (item.Sequence, item.Type, item.Text)));
    }

    [Fact]
    public void ValidPromptActivationBindingSurvivesCheckpointAndEveryRecipientProjection()
    {
        var game = Create();
        var player = game.State.Players[0];
        var activation = new L12PendingActivation
        {
            ActivationId = "architecture-valid-activation",
            Controller = 0,
            SourceInstanceId = "master-0",
            SourceCardId = player.MasterId,
            Ability = "architecture-contract",
            Text = "架构契约选择",
            ValidChoices = ["mode:none"],
            CreatedRevision = game.State.Revision,
            SelectionSteps =
            [
                new L12ActivationSelectionStep
                {
                    Kind = "mode",
                    Text = "架构契约选择",
                    ValidChoices = ["mode:none"],
                    MinChoose = 0,
                    MaxChoose = 1,
                    CancellationPolicy = L12ActivationCancellationPolicy.NotAllowed,
                },
            ],
        };
        game.State.PendingActivations.Add(activation);
        game.State.PendingPrompts.Add(new L12Prompt
        {
            PromptId = "architecture-valid-prompt",
            PlayerIndex = 0,
            Kind = "mode",
            Text = "架构契约选择",
            ValidChoices = ["mode:none"],
            MinChoose = 0,
            MaxChoose = 1,
            IsPrivate = true,
            Continuation = "pending-activation",
            ActivationId = activation.ActivationId,
            SourceInstanceId = activation.SourceInstanceId,
            SourceCardId = activation.SourceCardId,
            Step = 0,
            CreatedRevision = activation.CreatedRevision,
            Controller = activation.Controller,
            Data = new Dictionary<string, string>
            {
                ["activationId"] = activation.ActivationId,
            },
        });

        var random = Assert.IsType<L12RandomState>(game.RandomState);
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), random,
            game.CardFactSignalSequence, game.AutoPassEmptyResponses, game.ConcealHiddenResponseAvailability);

        Assert.Equal(game.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(SnapshotHash(game.SnapshotFor(0)), SnapshotHash(restored.SnapshotFor(0)));
        Assert.Equal(SnapshotHash(game.SnapshotFor(1)), SnapshotHash(restored.SnapshotFor(1)));
        Assert.Equal(SnapshotHash(game.SnapshotForSpectator()), SnapshotHash(restored.SnapshotForSpectator()));
        var prompt = Assert.Single(restored.State.PendingPrompts);
        var pending = Assert.Single(restored.State.PendingActivations);
        Assert.Equal(pending.ActivationId, prompt.ActivationId);
        Assert.Equal(pending.SourceInstanceId, prompt.SourceInstanceId);
        Assert.Equal(pending.CreatedRevision, prompt.CreatedRevision);
    }

    [Fact]
    public void CurrentVersionKernelOperationsStayInsideTheP1PerformanceAndPayloadBudgets()
    {
        var game = Create();
        CompleteMulligan(game);
        _ = game.SnapshotFor(0);
        _ = game.SerializeFullState();

        var stateBytes = Encoding.UTF8.GetByteCount(game.SerializeFullState());
        var playerBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(game.SnapshotFor(0), WireJson));
        var spectatorBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(game.SnapshotForSpectator(), WireJson));
        Assert.InRange(stateBytes, 1, 2_000_000);
        Assert.InRange(playerBytes, 1, 1_000_000);
        Assert.InRange(spectatorBytes, 1, 1_000_000);

        var snapshotSamples = Measure(25, () => _ = game.SnapshotFor(0));
        var restoreSamples = Measure(15, () =>
        {
            var random = Assert.IsType<L12RandomState>(game.RandomState);
            _ = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), random,
                game.CardFactSignalSequence, game.AutoPassEmptyResponses,
                game.ConcealHiddenResponseAvailability);
        });

        Assert.True(Percentile95(snapshotSamples) < TimeSpan.FromSeconds(1),
            $"玩家快照 P95 超出 1 秒预算：{Percentile95(snapshotSamples).TotalMilliseconds:F1} ms");
        Assert.True(Percentile95(restoreSamples) < TimeSpan.FromSeconds(2),
            $"当前版本恢复 P95 超出 2 秒预算：{Percentile95(restoreSamples).TotalMilliseconds:F1} ms");
    }

    private static L12GameEngine CreateWithOrphanedPrompt()
    {
        var game = Create();
        game.State.PendingPrompts.Add(new L12Prompt
        {
            PromptId = "architecture-orphan-prompt",
            PlayerIndex = 0,
            Kind = "card",
            Text = "孤立提示",
            ValidChoices = ["missing-card"],
            MinChoose = 1,
            MaxChoose = 1,
            IsPrivate = true,
            Continuation = "pending-activation",
            ActivationId = "missing-activation",
            SourceInstanceId = "missing-source",
            SourceCardId = "S01-0001",
            Step = 0,
            CreatedRevision = game.State.Revision,
            Controller = 0,
            Data = new Dictionary<string, string> { ["activationId"] = "missing-activation" },
        });
        return game;
    }

    private static L12GameEngine Create()
        => new(Catalog, "architecture-p1-cross-layer", "P1CROSS", 20260924,
            ["甲", "乙"], [0, 1], skipPreparation: true, stateFormatVersion: 2);

    private static void CompleteMulligan(L12GameEngine game)
    {
        Assert.True(game.Handle(0, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        Assert.True(game.Handle(1, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
    }

    private static TimeSpan[] Measure(int count, Action action)
    {
        var samples = new TimeSpan[count];
        for (var index = 0; index < count; index++)
        {
            var started = Stopwatch.GetTimestamp();
            action();
            samples[index] = Stopwatch.GetElapsedTime(started);
        }
        return samples;
    }

    private static TimeSpan Percentile95(IEnumerable<TimeSpan> samples)
    {
        var ordered = samples.Order().ToArray();
        return ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1];
    }

    private static string SnapshotHash(L12GameSnapshot snapshot)
        => Sha256(JsonSerializer.Serialize(snapshot, WireJson));

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
