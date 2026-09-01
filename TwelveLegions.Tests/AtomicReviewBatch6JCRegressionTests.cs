using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6JCRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var deck = Catalog.DeckAt(0);
        var game = new L12GameEngine(Catalog, "atomic-review-batch6jc", "ATOMIC6JC", seed,
            ["甲", "乙"], [deck, deck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 8;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
            player.UsedAbilities.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner = 0)
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
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
            OwnerIndex = owner,
        };
    }

    private static void InvokeVoid(L12GameEngine game, string methodName, params object?[] arguments)
    {
        var method = typeof(L12GameEngine).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(game, arguments);
    }

    private static L12Prompt ResolveOnlyPrompt(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static L12StackItem BeginOkitaTop(L12GameEngine game, L12CardInstance top)
    {
        var player = game.State.Players[0];
        var okita = Card("S02-0403", $"batch6jc-okita-{top.InstanceId}");
        player.Field[0][0] = okita;
        player.Library.Add(top);
        var item = new L12StackItem
        {
            StackItemId = $"batch6jc-parent-{top.InstanceId}",
            Controller = 0,
            SourceInstanceId = okita.InstanceId,
            SourceCardId = okita.CardId,
            SourceName = okita.Name,
            Trigger = "attack",
            Text = "冲田总司进攻时效果",
        };
        game.State.EffectStack.Add(item);
        InvokeVoid(game, "BeginS2OkitaAttack", item);
        return item;
    }

    private static L12StackItem BeginLiMuRevealedTactic(L12GameEngine game, L12CardInstance tactic)
    {
        var player = game.State.Players[0];
        var liMu = Card("S02-0102", $"batch6jc-limu-{tactic.InstanceId}");
        player.Field[0][0] = liMu;
        player.Library.Add(tactic);
        var item = new L12StackItem
        {
            StackItemId = $"batch6jc-limu-parent-{tactic.InstanceId}",
            Controller = 0,
            SourceInstanceId = liMu.InstanceId,
            SourceCardId = liMu.CardId,
            SourceName = liMu.Name,
            Trigger = "enter",
            Text = "李牧展示段",
        };
        item.Data["s2-limu-top"] = tactic.InstanceId;
        game.State.EffectStack.Add(item);
        InvokeVoid(game, "PlayS2LiMuRevealedTactic", item);
        return item;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100 && game.State.PendingPrompts.Count > 0; safety++)
        {
            var prompt = game.State.PendingPrompts[0];
            if (prompt.Kind != "response") break;
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-okita-slot-pending-activation")]
    public void OkitaLegionUsesPendingActivationAndMayCoverOwnCounterTactic()
    {
        var game = Create(9701);
        var player = game.State.Players[0];
        var top = Card("S01-0410", "batch6jc-okita-legion");
        for (var row = 0; row < 2; row++)
        for (var fieldSlot = 0; fieldSlot < 3; fieldSlot++)
            if (row != 0 || fieldSlot != 0)
                player.Field[row][fieldSlot] = Card("S01-0101", $"batch6jc-fill-{row}-{fieldSlot}");
        var counter = Card("S01-0018", "batch6jc-covered-counter");
        counter.Hidden = true;
        player.Field[1][2] = counter;

        BeginOkitaTop(game, top);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-okita-top", mode.Data.GetValueOrDefault("action"));
        ResolveOnlyPrompt(game, "play");

        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", slot.Continuation);
        Assert.Contains("1:2", slot.ValidChoices);
        Assert.DoesNotContain("s2-okita-slot", slot.Data.Values);
        Assert.Single(game.State.PendingActivations,
            activation => activation.Ability == "effect-generated-free-play");
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-okita-invalid-slot-no-fallback")]
    public void OkitaDeclaredSlotFailureDoesNotOverwriteOrFallBackToHand()
    {
        var game = Create(9702);
        var player = game.State.Players[0];
        var top = Card("S01-0410", "batch6jc-invalid-slot-legion");
        BeginOkitaTop(game, top);
        ResolveOnlyPrompt(game, "play");
        var slot = Assert.Single(game.State.PendingPrompts);
        var declared = slot.ValidChoices.First();
        var parts = declared.Split(':');
        var blocker = Card("S01-0101", "batch6jc-late-blocker");
        player.Field[int.Parse(parts[0])][int.Parse(parts[1])] = blocker;

        ResolveOnlyPrompt(game, declared);

        Assert.Contains(top, player.Library);
        Assert.DoesNotContain(top, player.Hand);
        Assert.DoesNotContain(top, player.Field.SelectMany(row => row));
        Assert.Same(blocker, player.Field[int.Parse(parts[0])][int.Parse(parts[1])]);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("位置已失效", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-okita-legion-authority-entry")]
    public void OkitaLegionFreePlayUsesTheNonHandEntryAuthorityEvent()
    {
        var game = Create(9703);
        var player = game.State.Players[0];
        var top = Card("S01-0410", "batch6jc-authority-legion");
        BeginOkitaTop(game, top);
        ResolveOnlyPrompt(game, "play");
        var slot = Assert.Single(game.State.PendingPrompts);
        var declared = slot.ValidChoices.First();

        ResolveOnlyPrompt(game, declared);

        Assert.DoesNotContain(top, player.Library);
        Assert.Contains(top, player.Field.SelectMany(row => row));
        Assert.Equal(game.State.Round, top.SummonRound);
        Assert.Contains(game.State.AuthorityEvents, entry => entry.Type == "non-hand-entry"
            && entry.TargetInstanceId == top.InstanceId && entry.OriginZone == "library");
        Assert.Contains(game.State.Events, entry => entry.Type == "play"
            && entry.Cards.Any(card => card.InstanceId == top.InstanceId));
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-okita-counter-cover-commit")]
    public void OkitaLegionCommitDisplacesCoveredCounterBeforeAuthorityEntry()
    {
        var game = Create(97031);
        var player = game.State.Players[0];
        var top = Card("S01-0410", "batch6jc-cover-legion");
        var counter = Card("S01-0018", "batch6jc-cover-counter");
        counter.Hidden = true;
        player.Field[1][2] = counter;
        BeginOkitaTop(game, top);
        ResolveOnlyPrompt(game, "play");
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("1:2", slot.ValidChoices);

        ResolveOnlyPrompt(game, "1:2");

        Assert.Same(top, player.Field[1][2]);
        Assert.Contains(counter, player.Graveyard);
        Assert.False(counter.Hidden);
        Assert.Contains(game.State.Events, entry => entry.Type == "counter-displaced"
            && entry.Cards.Any(card => card.InstanceId == counter.InstanceId));
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-limu-simple-common-free-play")]
    public void LiMuSimpleTacticUsesTheCommonEffectGeneratedPlayTransaction()
    {
        var game = Create(9704);
        var tactic = Card("S01-0008", "batch6jc-limu-simple");

        BeginLiMuRevealedTactic(game, tactic);

        Assert.DoesNotContain(tactic, game.State.Players[0].Library);
        Assert.Contains(tactic, game.State.Players[0].Resolving);
        var child = Assert.Single(game.State.EffectStack,
            item => item.SourceInstanceId == tactic.InstanceId);
        Assert.Equal("free", child.Data.GetValueOrDefault("effectGeneratedPlay"));
        Assert.Equal("library", child.Data.GetValueOrDefault("originZone"));
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-limu-composite-common-free-play")]
    public void LiMuCompositeTacticKeepsPrivateZoneUntilItsPublicDeclarationCommits()
    {
        var game = Create(9705);
        var target = Card("S01-0101", "batch6jc-volley-target", owner: 1);
        game.State.Players[1].Field[0][0] = target;
        var tactic = Card("S01-0005", "batch6jc-limu-composite");

        BeginLiMuRevealedTactic(game, tactic);

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Contains(tactic, game.State.Players[0].Resolving);
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceInstanceId == tactic.InstanceId);
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-composite-free-play-marker")]
    public void CompositeFreeTacticCarriesTheSameAuthorityMarkerAfterDeclaration()
    {
        var game = Create(97051);
        game.State.Players[1].Field[0][0] = Card("S01-0101", "batch6jc-composite-target", owner: 1);
        var tactic = Card("S01-0005", "batch6jc-marked-composite");
        BeginLiMuRevealedTactic(game, tactic);

        ResolveOnlyPrompt(game, "mode:front");

        var child = Assert.Single(game.State.EffectStack,
            item => item.SourceInstanceId == tactic.InstanceId);
        Assert.Equal("free", child.Data.GetValueOrDefault("effectGeneratedPlay"));
        Assert.Equal("library", child.Data.GetValueOrDefault("originZone"));
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-free-tactic-negated-no-rollback")]
    public void NegatingAFreeTacticDoesNotReturnItToTheLibrary()
    {
        var game = Create(97052);
        var tactic = Card("S01-0008", "batch6jc-negated-free-tactic");
        BeginLiMuRevealedTactic(game, tactic);
        var child = Assert.Single(game.State.EffectStack,
            item => item.SourceInstanceId == tactic.InstanceId);
        child.Negated = true;

        PassResponses(game);

        Assert.Contains(tactic, game.State.Players[0].Graveyard);
        Assert.DoesNotContain(tactic, game.State.Players[0].Library);
        Assert.DoesNotContain(tactic, game.State.Players[0].Resolving);
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-okita-source-left")]
    public void OkitaLeavingAfterTheRevealedPlayChoiceDoesNotSwallowTheCommittedCard()
    {
        var game = Create(97053);
        var player = game.State.Players[0];
        var top = Card("S01-0410", "batch6jc-source-left-legion");
        var parent = BeginOkitaTop(game, top);
        ResolveOnlyPrompt(game, "play");
        var slot = Assert.Single(game.State.PendingPrompts);
        var declared = slot.ValidChoices.First();
        var okita = player.Field.SelectMany(row => row)
            .Single(card => card?.InstanceId == parent.SourceInstanceId)!;
        for (var row = 0; row < 2; row++)
        for (var fieldSlot = 0; fieldSlot < 3; fieldSlot++)
            if (ReferenceEquals(player.Field[row][fieldSlot], okita)) player.Field[row][fieldSlot] = null;
        player.Graveyard.Add(okita);

        ResolveOnlyPrompt(game, declared);

        Assert.Contains(top, player.Field.SelectMany(row => row));
        Assert.DoesNotContain(top, player.Library);
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-okita-artifact-normal-entry")]
    public void OkitaArtifactUsesNormalReplacementAndQueuesItsEnterEffect()
    {
        var game = Create(97054);
        var player = game.State.Players[0];
        var oldRelic = Card("S01-0205", "batch6jc-old-relic");
        player.Relic = oldRelic;
        var artifact = Card("S02-0404", "batch6jc-okita-artifact");
        BeginOkitaTop(game, artifact);

        ResolveOnlyPrompt(game, "play");

        Assert.Same(artifact, player.Relic);
        Assert.Contains(oldRelic, player.Graveyard);
        Assert.DoesNotContain(artifact, player.Library);
        Assert.Equal(game.State.Round, artifact.SummonRound);
        Assert.Contains(game.State.Events, entry => entry.Type == "play"
            && entry.Cards.Any(card => card.InstanceId == artifact.InstanceId));
        Assert.True(game.State.DeferredEffectStack.Concat(game.State.EffectStack)
                .Any(item => item.SourceInstanceId == artifact.InstanceId && item.Trigger == "enter")
            || game.State.PendingTriggerStackCandidates.Any(candidate => candidate.SourceInstanceId == artifact.InstanceId
                && candidate.Trigger == "enter")
            || game.State.PendingPrompts.Any(prompt => prompt.Data.GetValueOrDefault("action") == "s2-magatama-search"),
            "八尺琼勾玉的登场检索必须进入堆叠或已合法开始其隐藏检索提示");
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-okita-ineligible-hand-authority")]
    public void OkitaIneligibleTopUsesTheEffectHandAddAuthorityEvent()
    {
        var game = Create(9706);
        var player = game.State.Players[0];
        var top = Card("S02-0609", "batch6jc-okita-ineligible");

        BeginOkitaTop(game, top);

        Assert.DoesNotContain(top, player.Library);
        Assert.Contains(top, player.Hand);
        Assert.Contains(game.State.AuthorityEvents, entry => entry.Type == "effect-hand-add"
            && entry.TargetInstanceId == top.InstanceId && entry.OriginZone == "library");
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-private-hand-add-redaction")]
    public void HiddenEffectHandAddAuthorityEventDoesNotExposeTheCardToTheOpponent()
    {
        var game = Create(9707);
        var player = game.State.Players[0];
        var secret = Card("S01-0008", "batch6jc-secret-hand-add");
        player.Library.Add(secret);
        player.Library.Remove(secret);

        InvokeVoid(game, "AddCardToHandByEffect", player, secret, "library", "隐藏检索加入手牌");

        var opponent = JsonSerializer.Serialize(game.SnapshotFor(1));
        Assert.DoesNotContain(secret.InstanceId, opponent, StringComparison.Ordinal);
        Assert.DoesNotContain(secret.Name, opponent, StringComparison.Ordinal);
        Assert.Contains(secret, player.Hand);
        Assert.Contains(game.State.AuthorityEvents, entry => entry.Type == "effect-hand-add"
            && entry.TargetInstanceId == secret.InstanceId);
    }
}
