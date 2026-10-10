using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AnnihilationV2ContinuationTests(
    LegacyAnnihilationCheckpointFixture fixture)
    : IClassFixture<LegacyAnnihilationCheckpointFixture>
{
    public static TheoryData<bool> LegacyFlowCases => new()
    {
        false, // legacy SourceName/card Name, no atomicFlow
        true,  // explicit legacy atomicFlow
    };

    [Theory]
    [MemberData(nameof(LegacyFlowCases))]
    public void LegacyV2CheckpointContinuesThroughTheFinalDisasterBranch(
        bool explicitLegacyAtomicFlow)
    {
        var checkpoint = fixture.CreateCheckpoint(explicitLegacyAtomicFlow);
        var restored = L12GameEngine.RestoreCheckpoint(fixture.Catalog,
            checkpoint.StateJson, checkpoint.RandomState,
            checkpoint.CardFactSignalSequence,
            autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

        Assert.Equal(L12PersistenceContract.CurrentStateFormatVersion,
            restored.State.StateFormatVersion);
        Assert.Equal(checkpoint.StateHash, restored.ComputeStateHash());
        Assert.Equal(LegacyAnnihilationCheckpointFixture.CardId,
            restored.State.ActiveDisaster?.CardId);
        Assert.Equal(LegacyAnnihilationCheckpointFixture.LegacyName,
            restored.State.ActiveDisaster?.Name);

        var stack = Assert.Single(restored.State.EffectStack);
        Assert.Equal(checkpoint.StackItemId, stack.StackItemId);
        Assert.Equal(LegacyAnnihilationCheckpointFixture.LegacyName, stack.SourceName);
        if (checkpoint.ExplicitLegacyAtomicFlow)
            Assert.Equal(LegacyAnnihilationCheckpointFixture.LegacyName,
                stack.Data["atomicFlow"]);
        else
            Assert.False(stack.Data.ContainsKey("atomicFlow"));

        // This is a persisted legacy continuation boundary: the only old prompt
        // choice is pass. The second priority pass is then performed automatically
        // because disaster authority timing has no legal card responses.
        var prompt = Assert.Single(restored.State.PendingPrompts);
        Assert.Equal("stack-response", prompt.Continuation);
        Assert.Equal(checkpoint.StackItemId, prompt.StackItemId);
        Assert.Collection(prompt.ValidChoices,
            choice => Assert.Equal("pass", choice));
        var eventSequenceBeforeContinuation = restored.State.EventSequence;

        var continued = restored.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));

        Assert.True(continued.Accepted, continued.Error);
        Assert.Null(restored.State.ResponseWindow);
        Assert.Empty(restored.State.PendingPrompts);
        Assert.Empty(restored.State.EffectStack);
        Assert.Equal(2, restored.State.Events.Count(item =>
            item.Sequence > eventSequenceBeforeContinuation && item.Type == "priority-pass"));
        var activeEvent = Assert.Single(restored.State.Events, item =>
            item.Sequence > eventSequenceBeforeContinuation && item.Type == "disaster-active");
        Assert.Contains(LegacyAnnihilationCheckpointFixture.LegacyName,
            activeEvent.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(restored.State.Events, item =>
            item.Sequence > eventSequenceBeforeContinuation && item.Type == "response");

        AssertStableCatalogIdentity();
        AssertTurnStartDamageRemainsOnePointAndNonLethal(restored);
    }

    private void AssertStableCatalogIdentity()
    {
        var definition = fixture.Catalog.Cards[LegacyAnnihilationCheckpointFixture.CardId];
        var effect = Assert.IsType<L12AtomicCardEffect>(
            fixture.Catalog.AtomicEffects.Find(LegacyAnnihilationCheckpointFixture.CardId));

        Assert.Equal(LegacyAnnihilationCheckpointFixture.CardId,
            L12ActiveDisasterRules.AnnihilationCardId);
        Assert.Equal(LegacyAnnihilationCheckpointFixture.CardId, definition.Id);
        Assert.Equal(LegacyAnnihilationCheckpointFixture.CanonicalName, definition.NameZh);
        Assert.Null(definition.Cost);
        Assert.True(L12ActiveDisasterRules.DisasterValueLocked(definition.Id));
        Assert.Contains(effect.Abilities,
            ability => ability.AbilityId == LegacyAnnihilationCheckpointFixture.StaticAbilityId);
        Assert.Contains(effect.Abilities,
            ability => ability.AbilityId == LegacyAnnihilationCheckpointFixture.TurnStartAbilityId);
    }

    private static void AssertTurnStartDamageRemainsOnePointAndNonLethal(
        L12GameEngine restored)
    {
        Assert.Equal(L12Phase.Main, restored.State.Phase);
        Assert.Equal(2, restored.State.Players[0].Hp);
        Assert.Equal(1, restored.State.Players[1].Hp);
        var eventSequenceBeforeTurnEnd = restored.State.EventSequence;

        var ended = restored.Handle(restored.State.ActivePlayer, new L12Command("endTurn"));

        Assert.True(ended.Accepted, ended.Error);
        Assert.Equal(1, restored.State.Players[0].Hp);
        Assert.Equal(1, restored.State.Players[1].Hp);
        Assert.Null(restored.State.Winner);
        Assert.Equal(0, restored.State.DisasterValue);
        Assert.Equal(LegacyAnnihilationCheckpointFixture.CardId,
            restored.State.ActiveDisaster?.CardId);
        var damage = Assert.Single(restored.State.Events, item =>
            item.Sequence > eventSequenceBeforeTurnEnd && item.Type == "damage");
        Assert.Equal(0, damage.PlayerIndex);
        Assert.Contains(LegacyAnnihilationCheckpointFixture.CanonicalName,
            damage.Text, StringComparison.Ordinal);
        Assert.Contains("失去 1 点非致命伤害", damage.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(restored.State.PendingPrompts,
            item => item.Kind == "response");
    }
}
