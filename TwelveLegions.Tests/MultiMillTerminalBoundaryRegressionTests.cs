using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class MultiMillTerminalBoundaryRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    public void RealValkyrieDrawReplacementLosesImmediatelyWhenTwoDiscardsCannotFinish(int actor, int available)
    {
        var game = Create(actor, preparation: true);
        SetLibrary(game, actor, available, firstZealot: true);
        Assert.True(game.Handle(0, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        var start = game.State.EventSequence;
        var moraleBefore = game.State.Players[actor].Morale.Count;
        Assert.True(game.Handle(1, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        AssertTerminal(game, actor, available, start);
        Assert.Equal(moraleBefore, game.State.Players[actor].Morale.Count);
        Assert.DoesNotContain(game.State.Events.Where(e => e.Sequence > start), e => e.Type == "morale");
        var events = JsonSerializer.Serialize(game.State.Events);
        game = Restore(game);
        AssertTerminal(game, actor, available, start);
        Assert.False(game.Handle(actor, new L12Command("endTurn")).Accepted);
        Assert.False(game.Handle(actor, new L12Command("resolvePrompt", PromptId: "obsolete", Choice: "pass")).Accepted);
        Assert.Equal(events, JsonSerializer.Serialize(game.State.Events));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    public void GmValkyrieDrawPhaseKeepsGameOverAsLastEvent(int actor, int available)
    {
        var game = Create(actor);
        SetLibrary(game, actor, available, firstZealot: true);
        game.State.Phase = L12Phase.Reset;
        var start = game.State.EventSequence;
        var result = game.HandleGm(new L12GmCommand("nextPhase", actor));
        Assert.True(result.Accepted, result.Error);
        AssertTerminal(game, actor, available, start);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(0, 2, 2)]
    [InlineData(0, 3, 2)]
    [InlineData(1, 2, 2)]
    [InlineData(1, 3, 2)]
    [InlineData(0, 3, 3)]
    [InlineData(1, 3, 3)]
    public void SharedBulkMillCompletesAndLogsAllMovedCardsBeforeAnyDiscardTriggerInteraction(int actor, int available, int requested)
    {
        var game = Create(actor);
        SetLibrary(game, actor, available, firstZealot: true);
        var expected = game.State.Players[actor].Library.Take(requested).Select(c => c.InstanceId).ToArray();
        var before = game.State.EventSequence;
        Invoke(game, "Mill", game.State.Players[actor], requested, "整次弃置测试");
        Assert.NotEqual(L12Phase.GameOver, game.State.Phase);
        var mill = Assert.Single(game.State.Events, e => e.Sequence > before && e.Type == "mill");
        Assert.Equal(expected, mill.Cards.Select(c => c.InstanceId));
        Assert.Equal(expected, game.State.Players[actor].Graveyard.Select(c => c.InstanceId));
        var interactions = game.State.Events.Where(e => e.Sequence > before && e.Type is "prompt" or "effect-trigger" or "stack-push").ToArray();
        Assert.NotEmpty(interactions);
        Assert.All(interactions, interaction => Assert.True(mill.Sequence < interaction.Sequence,
            "整次弃置及公开移动记录完成后才能开放弃置触发的选择或结算"));
        Assert.Equal(available - requested, game.State.Players[actor].Library.Count);
        var ids = game.State.Players[actor].Graveyard.Select(c=>c.InstanceId).ToArray();
        game = Restore(game);
        Assert.Equal(ids, game.State.Players[actor].Graveyard.Select(c=>c.InstanceId));
        Assert.Single(game.State.Events, e=>e.Sequence==mill.Sequence);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SameBulkDiscardCollectsBothZealotsBeforeOpeningOneOrderingInteraction(int actor)
    {
        var game = Create(actor);
        SetLibrary(game, actor, 3, firstZealot: true);
        game.State.Players[actor].Library[1] = Card("S02-0006", "second-zealot", actor);
        Invoke(game, "Mill", game.State.Players[actor], 2, "双狂热者整次弃置");
        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-order", order.Kind);
        Assert.Equal(2, order.ValidChoices.Count);
        Assert.Equal(2, order.MinChoose);
        Assert.Equal(2, order.MaxChoose);
        var both = Assert.Single(game.State.PendingTriggerBatches);
        Assert.Equal(new[] { "mill-card-0", "second-zealot" }, both.Candidates.Select(c=>c.SourceInstanceId));
        Assert.Empty(game.State.Players[actor].UsedAbilities);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PartialBulkMillLosesWithoutPreparingEvenTheFirstZealotTrigger(int actor)
    {
        var game = Create(actor);
        SetLibrary(game, actor, 1, firstZealot: true);
        var start = game.State.EventSequence;
        Invoke(game, "Mill", game.State.Players[actor], 2, "不足两张的整次弃置");
        AssertTerminal(game, actor, 1, start);
        Assert.DoesNotContain(game.State.Events.Where(e=>e.Sequence>start), e=>e.Type is "prompt" or "effect-trigger" or "effect-order" or "stack-push");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FinishingAnAlreadyTerminatedMillCannotPublishResultsOrQueueAnySuccessor(int actor)
    {
        var game = Create(actor);
        SetLibrary(game, actor, 1, firstZealot: false);
        var source = Card("S01-03D1", "terminal-mill-source", actor);
        game.State.Players[actor].Resolving.Add(source);
        var item = new L12StackItem { StackItemId = "terminal-origin", Controller = actor,
            SourceInstanceId = source.InstanceId, SourceCardId = source.CardId, SourceName = source.Name,
            SourceSnapshot = source.Clone(), Trigger = "active", Text = "终局后不继续",
        };
        game.State.EffectStack.Add(item); game.State.IsResolvingStack = true;
        Invoke(game, "Mill", game.State.Players[actor], 2, "必然终局的整次弃置");
        AssertTerminal(game, actor, 1, 0);
        var playersBefore = JsonSerializer.Serialize(game.State.Players);
        var beforeSequence = game.State.EventSequence;
        Invoke(game, "FinishStackItem", item);
        Assert.Equal(playersBefore, JsonSerializer.Serialize(game.State.Players));
        Assert.Equal(L12Phase.GameOver, game.State.Phase);
        Assert.Equal(1 - actor, game.State.Winner);
        Assert.Empty(game.State.PendingPrompts); Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack); Assert.Empty(game.State.PendingTriggerBatches);
        Assert.All(game.State.Events.Where(e => e.Sequence > beforeSequence), e => {
            Assert.Equal("effect-result", e.Type);
            Assert.Equal("failed", e.EffectResultStatus);
        });
        var closed = game.SerializeFullState();
        var factCount = game.CardFactSignals.Count;
        Invoke(game, "FinishStackItem", item);
        Assert.Equal(closed, game.SerializeFullState());
        Assert.Equal(factCount, game.CardFactSignals.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void LegacyGramCannotOpenTargetChoiceAfterItsBulkDiscardAlreadyLost(int actor)
    {
        var game = Create(actor);
        SetLibrary(game, actor, 1, firstZealot: false);
        var source = Card("S01-0317", "gram-terminal-source", actor);
        game.State.Players[actor].Relic = source;
        var target = Card("S01-0003", "gram-possible-target", 1 - actor, troops: 1000);
        game.State.Players[1 - actor].Field[0][0] = target;
        var item = new L12StackItem { StackItemId="gram-terminal-origin", Controller=actor,
            SourceInstanceId=source.InstanceId, SourceCardId=source.CardId, SourceName=source.Name,
            SourceSnapshot=source.Clone(), Trigger="enter", Text=source.EffectText ?? source.Name };
        game.State.EffectStack.Add(item); game.State.IsResolvingStack=true;
        Assert.True(Assert.IsType<bool>(Invoke(game, "TryResolveS1FactionEnter", item, source)));
        Assert.Equal(1-actor, game.State.Winner);
        Assert.Equal(L12Phase.GameOver, game.State.Phase);
        Assert.Empty(game.State.PendingPrompts); Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack); Assert.Empty(game.State.PendingTriggerBatches);
        var over=Assert.Single(game.State.Events, e=>e.Type=="game-over");
        Assert.All(game.State.Events.Where(e=>e.Sequence>over.Sequence), e=> {
            Assert.Equal("effect-result", e.Type); Assert.Equal("failed", e.EffectResultStatus);
        });
        Assert.Same(target, game.State.Players[1-actor].Field[0][0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TerminatedValhallaClosesUnusedRecoveryWithoutExecutingItOrPublishingAProcessedTarget(int actor)
    {
        var game=Create(actor); SetLibrary(game, actor, 1, firstZealot: false);
        var source=Card("S01-03D1", "valhalla-terminal-source", actor);
        var recovery=Card("S01-0301", "valhalla-unprocessed-target", actor);
        var player=game.State.Players[actor]; player.Resolving.Add(source); player.Graveyard.Add(recovery);
        var item=new L12StackItem { StackItemId="valhalla-terminal-origin", Controller=actor,
            SourceInstanceId=source.InstanceId, SourceCardId=source.CardId, SourceName=source.Name,
            SourceSnapshot=source.Clone(), Trigger="active", Text="英灵殿的整次弃置" };
        item.Data["compositePlan"]="active:S01-03D1:valhallaRecover";
        item.Data["compositeSegment"]="0"; item.Data["atomicFlow"]="valhalla-mill";
        item.Data["declared:recoverMode"]="mode:recover"; item.Data["declared:graveCard"]=recovery.InstanceId;
        game.State.EffectStack.Add(item); game.State.IsResolvingStack=true;
        Invoke(game,"Mill",player,2,"英灵殿");
        var playersBefore=JsonSerializer.Serialize(game.State.Players);
        Invoke(game,"FinishStackItem",item);
        Assert.Equal(playersBefore,JsonSerializer.Serialize(game.State.Players));
        var results=game.State.Events.Where(e=>e.Type=="effect-result" && e.Cards.Any(c=>c.InstanceId==source.InstanceId)).ToArray();
        Assert.Equal(new[] { "failed", "skipped" },results.Select(e=>e.EffectResultStatus));
        Assert.All(results,e=>Assert.Null(e.PlayerLogSemantic?.TargetInstanceId));
        Assert.Empty(game.State.PendingPrompts); Assert.Empty(game.State.EffectStack); Assert.Empty(game.State.PendingActivations);
        var before=game.SerializeFullState(); Invoke(game,"FinishStackItem",item);
        Assert.Equal(before,game.SerializeFullState());
        Restore(game);
    }

    private static void AssertTerminal(L12GameEngine game, int actor, int moved, long start)
    {
        Assert.Equal(1-actor, game.State.Winner);
        Assert.Equal(L12Phase.GameOver, game.State.Phase);
        Assert.Equal(moved, game.State.Players[actor].Graveyard.Count);
        Assert.Empty(game.State.Players[actor].Library);
        Assert.Empty(game.State.PendingPrompts); Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack); Assert.Empty(game.State.DeferredEffectStack);
        Assert.Empty(game.State.PendingTriggerBatches); Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Null(game.State.ResponseWindow); Assert.False(game.State.IsResolvingStack);
        var over = Assert.Single(game.State.Events.Where(e=>e.Sequence>start), e=>e.Type=="game-over");
        Assert.Equal(over.Sequence, game.State.Events.Last().Sequence);
        foreach (var mill in game.State.Events.Where(e=>e.Sequence>start && e.Type=="mill")) Assert.True(mill.Sequence<over.Sequence);
    }

    private static void SetLibrary(L12GameEngine game, int actor, int count, bool firstZealot)
    {
        var player=game.State.Players[actor]; player.Library.Clear(); player.Hand.Clear(); player.Graveyard.Clear();
        for(var i=0;i<count;i++) player.Library.Add(Card(i==0 && firstZealot ? "S02-0006" : "S01-0003", $"mill-card-{i}", actor));
    }
    private static L12GameEngine Create(int actor, bool preparation=false)
    {
        var original=Catalog.DeckAt(0);
        var valkyrie=new L12PresetDeckDefinition { Name="瓦尔基里批量边界", MasterId="S01-03M1",
            CardIds=[..original.CardIds], MoraleIds=[..original.MoraleIds], SpecialIds=[..original.SpecialIds] };
        var decks=new[] { original, original }; decks[actor]=valkyrie;
        var game=new L12GameEngine(Catalog,"bulk-mill-boundary","BULKMILL",921230+actor,["甲","乙"],decks,
            skipPreparation:true, autoPassEmptyResponses:false, concealHiddenResponseAvailability:false,
            disasterMode:"none", stateFormatVersion:L12PersistenceContract.CurrentStateFormatVersion);
        game.State.ActivePlayer=actor; game.State.FirstPlayer=actor; game.State.Round=1;
        if(!preparation)
        {
            game.State.PendingPrompts.Clear(); game.State.PendingActivations.Clear();
            game.State.EffectStack.Clear(); game.State.DeferredEffectStack.Clear();
            game.State.PendingTriggerBatches.Clear(); game.State.PendingTriggerStackCandidates.Clear();
            game.State.ResponseWindow=null; game.State.PendingDefense=null; game.State.Phase=L12Phase.Main;
            game.State.Events.Clear(); game.State.Log.Clear();
        }
        foreach(var p in game.State.Players)
        {
            p.Field[0]=new L12CardInstance?[3]; p.Field[1]=new L12CardInstance?[3];
            p.Relic=null; p.ExtraRelics.Clear(); p.Graveyard.Clear(); p.Resolving.Clear(); p.UsedAbilities.Clear();
        }
        return game;
    }
    private static L12CardInstance Card(string id,string instance,int owner,int? troops=null)
    {
        var d=Catalog.Cards[id];
        return new L12CardInstance { InstanceId=instance,CardId=id,Name=d.NameZh,CardType=d.CardType,Faction=d.Faction,
            Cost=d.Cost??0,BaseTroops=troops??d.Troops??0,Troops=troops??d.Troops??0,EffectText=d.Effect,Traits=[..d.Traits],OwnerIndex=owner };
    }
    private static object? Invoke(L12GameEngine game,string name,params object?[] args)
    {
        var method=Assert.Single(typeof(L12GameEngine).GetMethods(BindingFlags.Instance|BindingFlags.NonPublic),
            m=>m.Name==name && m.GetParameters().Length==args.Length
                && m.GetParameters().Zip(args).All(p=>p.Second is null || p.First.ParameterType.IsInstanceOfType(p.Second)));
        return method.Invoke(game,args);
    }
    private static L12GameEngine Restore(L12GameEngine game)
    {
        var json=game.SerializeFullState();
        var expected=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        var restored=L12GameEngine.RestoreCheckpoint(Catalog,json,game.RandomState!.Value,game.CardFactSignalSequence,
            autoPassEmptyResponses:false,concealHiddenResponseAvailability:false);
        Assert.Equal(expected,restored.ComputeStateHash()); Assert.Equal(json,restored.SerializeFullState());
        return restored;
    }
}
