using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class NextResetLockPlayerLogRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    public static IEnumerable<object[]> ElizabethCases()
    {
        foreach (var actor in new[] { 0, 1 })
        foreach (var restore in new[] { false, true })
        foreach (var declared in new[] { 0, 1, 2 })
        for (var stale = 0; stale <= declared; stale++)
            yield return [actor, restore, declared, stale];
    }

    [Theory]
    [MemberData(nameof(ElizabethCases))]
    public void ElizabethRecordsOneActualPublicRestrictionWithOnlySuccessfullyAffectedCount(
        int actor, bool restore, int declared, int stale)
    {
        var game = Create(actor);
        var source = Card("ST06-01", "log-elizabeth", actor);
        game.State.Players[actor].Field[0][0] = source;
        var targets = Enumerable.Range(0, declared).Select(i => new L12MoraleCard
        {
            CardId = "S01-01C1", InstanceId = $"log-morale-{i}", Tapped = i >= stale,
        }).ToArray();
        game.State.Players[1 - actor].Morale.AddRange(targets);
        var item = Item(actor, source, "enter");
        item.Data["atomicFlow"] = "elizabeth-lock-morale";
        item.Data["declared:moraleTargets"] = string.Join('|', targets.Select(target => target.InstanceId));
        game.State.EffectStack.Add(item);
        if (restore) game = Restore(game);
        Assert.True(Assert.IsType<bool>(Invoke(game, "TryResolveStarterTargetedEffect", game.State.EffectStack.Single())));
        var actual = declared - stale;
        var receipts = game.State.Events.Where(entry => entry.PlayerLogSemantic?.SourceInstanceId == source.InstanceId
            && entry.PlayerLogSemantic.ActionLabel == "发动登场时效果").ToArray();
        if (actual == 0) Assert.Empty(receipts);
        else
        {
            var receipt = Assert.Single(receipts);
            Assert.Equal(actor, receipt.PlayerIndex);
            Assert.Contains($"所选{actual}张士气下个重置阶段无法转为活跃", receipt.PlayerLogSemantic!.OutcomeLabel);
            Assert.Single(receipt.Cards);
            Assert.Equal(source.InstanceId, receipt.Cards[0].InstanceId);
            Assert.DoesNotContain("逆结算", receipt.PlayerLogSemantic.OutcomeLabel);
            foreach (var viewer in new[] { actor, 1 - actor, -1 })
            {
                var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(viewer), new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var visible = snapshot.GetProperty("recentEvents").EnumerateArray()
                    .Single(e => e.GetProperty("sequence").GetInt64() == receipt.Sequence);
                Assert.Equal(receipt.PlayerLogSemantic.OutcomeLabel,
                    visible.GetProperty("playerLogSemantic").GetProperty("outcomeLabel").GetString());
            }
        }
        Assert.Equal(actual, game.State.Players[1 - actor].Morale.Count(target => target.CannotUntapAtNextReset));
        Assert.Equal(actual, game.State.Players[1 - actor].Morale.Count(target => target.Tapped));
        Assert.DoesNotContain(game.State.Events, e => e.Type == "effect" && e.Text == "所选士气下个重置阶段无法转为活跃");
        if (restore)
        {
            var before = JsonSerializer.Serialize(receipts);
            game = Restore(game);
            Assert.Equal(before, JsonSerializer.Serialize(game.State.Events.Where(e => receipts.Any(old => old.Sequence == e.Sequence))));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ActualElizabethPlayProducesOneNamedRestrictionForTwoMorale(int actor)
    {
        var game = Create(actor);
        var source = Card("ST06-01", "command-elizabeth", actor);
        game.State.Players[actor].Hand.Add(source);
        for (var i = 0; i < 7; i++) game.State.Players[actor].Morale.Add(new L12MoraleCard
        { CardId = "S01-01C1", InstanceId = $"payment-{actor}-{i}" });
        var targets = Enumerable.Range(0, 2).Select(i => new L12MoraleCard
        { CardId = "S01-01C2", InstanceId = $"command-target-{i}", Tapped = true }).ToArray();
        game.State.Players[1-actor].Morale.AddRange(targets);
        var played = game.Handle(actor, new L12Command("playCard", source.InstanceId, Row: 0, Slot: 0,
            CardInstanceIds: game.State.Players[actor].Morale.Select(m => m.InstanceId).ToList()));
        Assert.True(played.Accepted, played.Error);
        var guard = 0;
        while (game.State.PendingPrompts.FirstOrDefault() is { } prompt)
        {
            Assert.True(++guard < 30);
            var result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                Choice: prompt.Kind == "response" ? "pass" : null,
                CardInstanceIds: prompt.Kind == "response" ? null : targets.Select(m => m.InstanceId).ToList(),
                ActivationId: prompt.ActivationId, SourceInstanceId: prompt.SourceInstanceId,
                SourceCardId: prompt.SourceCardId, Step: prompt.Step,
                CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller));
            Assert.True(result.Accepted, result.Error);
        }
        Assert.Empty(game.State.EffectStack);
        var receipt = Assert.Single(game.State.Events, e => e.PlayerLogSemantic?.SourceInstanceId == source.InstanceId
            && e.PlayerLogSemantic.OutcomeLabel.Contains("所选2张士气下个重置阶段无法转为活跃", StringComparison.Ordinal));
        Assert.Equal("发动登场时效果", receipt.PlayerLogSemantic!.ActionLabel);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("hand")]
    [InlineData("library")]
    [InlineData("missing")]
    public void RestrictionNeverReconstructsPrivateOrMissingSourceIdentity(string privacy)
    {
        var game = Create(0);
        var source = Card("ST06-01", "private-lock-source", 0);
        source.Hidden = privacy == "hidden";
        if (privacy == "hand") game.State.Players[0].Hand.Add(source);
        else if (privacy == "library") game.State.Players[0].Library.Add(source);
        else if (privacy != "missing") game.State.Players[0].Field[0][0] = source;
        var item = Item(0, source, "enter", privacy != "missing");
        var target = Card("S01-0003", "public-lock-target", 1);
        target.Tapped = true;
        Invoke(game, "LockUntilNextResetByEffect", item, target);
        var receipt = game.State.Events.Last();
        Assert.True(target.CannotUntapAtNextReset);
        Assert.Null(receipt.PlayerLogSemantic);
        Assert.Empty(receipt.Cards);
        Assert.DoesNotContain(source.Name, receipt.Text);
        Assert.DoesNotContain(source.InstanceId, JsonSerializer.Serialize(receipt));
    }

    [Theory]
    [InlineData("S01-0102", "enter")]
    [InlineData("S01-0108", "death")]
    [InlineData("S01-0011", "play")]
    [InlineData("S01-0213", "enter")]
    [InlineData("S02-0505", "promotion-enter")]
    public void SharedRestrictionReceiptDoesNotChangeTappedStateOrRevealCoveredTarget(string cardId, string trigger)
    {
        var game = Create(0);
        var source = Card(cardId, "shared-lock-source", 0);
        game.State.Players[0].Field[0][0] = source;
        var target = Card("S01-0003", "covered-lock-target", 1);
        target.Hidden = true;
        target.Tapped = true;
        Invoke(game, "LockUntilNextResetByEffect", Item(0, source, trigger), target);
        Assert.True(target.Tapped);
        Assert.True(target.CannotUntapAtNextReset);
        var receipt = game.State.Events.Last();
        Assert.Single(receipt.Cards);
        Assert.Equal(source.InstanceId, receipt.Cards[0].InstanceId);
        Assert.Equal("所选盖伏卡下个重置阶段无法转为活跃", receipt.PlayerLogSemantic!.OutcomeLabel);
        Assert.Null(receipt.PlayerLogSemantic.TargetInstanceId);
        Assert.DoesNotContain(target.Name, receipt.Text);
    }

    private static L12GameEngine Create(int actor)
    {
        var game = new L12GameEngine(Catalog, "lock-player-log", "LOCK-LOG", 921180 + actor,
            ["甲", "乙"], [Catalog.DeckAt(0), Catalog.DeckAt(1)], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            disasterMode: "none", stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion);
        game.State.PendingPrompts.Clear(); game.State.PendingActivations.Clear();
        game.State.EffectStack.Clear(); game.State.DeferredEffectStack.Clear();
        game.State.PendingTriggerBatches.Clear(); game.State.PendingTriggerStackCandidates.Clear();
        game.State.ResponseWindow = null; game.State.PendingDefense = null;
        game.State.Events.Clear(); game.State.Log.Clear();
        game.State.ActivePlayer = actor; game.State.FirstPlayer = actor;
        game.State.Round = 3; game.State.TurnSerial = 12; game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null; game.State.DisasterValue = 0; game.State.DisasterDeck.Clear();
        foreach (var p in game.State.Players)
        {
            p.Field[0] = new L12CardInstance?[3]; p.Field[1] = new L12CardInstance?[3];
            p.Hand.Clear(); p.Graveyard.Clear(); p.Removed.Clear(); p.Resolving.Clear();
            p.Relic = null; p.ExtraRelics.Clear(); p.Morale.Clear(); p.MoraleDeck.Clear();
            p.UsedAbilities.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string id, string instance, int owner)
    {
        var d = Catalog.Cards[id];
        return new L12CardInstance { InstanceId = instance, CardId = id, Name = d.NameZh, CardType = d.CardType,
            Faction = d.Faction, Cost = d.Cost ?? 0, BaseTroops = d.Troops ?? 0, Troops = d.Troops ?? 0,
            EffectText = d.Effect, OwnerIndex = owner, Traits = [.. d.Traits], Profession = d.Profession,
            EffectiveProfession = d.Profession };
    }
    private static L12StackItem Item(int actor, L12CardInstance source, string trigger, bool includeSnapshot = true) => new()
    {
        StackItemId = "log-restriction-stack", Controller = actor, SourceInstanceId = source.InstanceId,
        SourceCardId = source.CardId, SourceName = source.Name, SourceSnapshot = includeSnapshot ? source.Clone() : null,
        Trigger = trigger, Text = source.EffectText ?? source.Name,
    };
    private static object? Invoke(L12GameEngine game, string name, params object?[] args)
    {
        var method = Assert.Single(typeof(L12GameEngine).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic),
            m => m.Name == name && m.GetParameters().Length == args.Length
                && m.GetParameters().Zip(args).All(p => p.Second is null || p.First.ParameterType.IsInstanceOfType(p.Second)));
        return method.Invoke(game, args);
    }
    private static L12GameEngine Restore(L12GameEngine game)
    {
        // These unit cases call the private resolver without Handle's revision
        // boundary. Hash the exact current canonical state independently rather
        // than reuse an engine hash cached before that direct fixture mutation.
        var json = game.SerializeFullState();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, json, game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        Assert.Equal(hash, restored.ComputeStateHash());
        Assert.Equal(json, restored.SerializeFullState());
        return restored;
    }
}
