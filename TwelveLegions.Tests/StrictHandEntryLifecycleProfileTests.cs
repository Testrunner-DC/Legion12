using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class StrictHandEntryLifecycleProfileTests
{
    private const string LiuBeiAbilityId = "S01-0105:ability:enter:ee4ec5ee9f9e1cce";
    private const string XishiAbilityId = "S01-0116:ability:static:74c527aaab5e91cd";
    private const string KabaAbilityId = "S01-0213:ability:after-attack:bf52deb7316f89d3";

    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "strict-hand-entry-profile", "STRICTHAND", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
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
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            OwnerIndex = owner,
            SummonRound = -1,
        };
    }

    private static void AddMorale(L12PlayerState player, int count, bool tapped = false)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-01C1",
                InstanceId = $"strict-morale-{player.PlayerIndex}-{index}",
                Tapped = tapped,
            });
    }

    private static object? Invoke(L12GameEngine game, string method, params object?[] args)
        => typeof(L12GameEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(game, args);

    private static void QueueCandidate(L12GameEngine game, L12CardInstance source, string trigger)
        => Invoke(game, "QueueTriggerCandidates", (object)new[]
        {
            new L12TriggerCandidate
            {
                CandidateId = $"strict-candidate-{source.InstanceId}",
                Controller = 0,
                SourceInstanceId = source.InstanceId,
                SourceCardId = source.CardId,
                SourceName = source.Name,
                SourceSnapshot = source,
                Trigger = trigger,
                Text = source.EffectText ?? source.Name,
            },
        });

    private static L12Prompt Prompt(L12GameEngine game) => Assert.Single(game.State.PendingPrompts);

    private static L12Prompt Resolve(L12GameEngine game, string? choice = null,
        IReadOnlyList<string>? cardIds = null)
    {
        var prompt = Prompt(game);
        var result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt",
            PromptId: prompt.PromptId, Choice: choice,
            CardInstanceIds: cardIds is null ? null : [.. cardIds]));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 80; safety++)
        {
            if (game.State.PendingPrompts.FirstOrDefault() is { } prompt)
            {
                if (prompt.ValidChoices.Contains("pass")) Resolve(game, "pass");
                else if (prompt.ValidChoices.Contains("mode:none")) Resolve(game, "mode:none");
                else if (prompt.Continuation == "trigger-batch-order")
                {
                    var result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt",
                        PromptId: prompt.PromptId, CardInstanceIds: [.. prompt.ValidChoices]));
                    Assert.True(result.Accepted, result.Error);
                }
                else throw new Xunit.Sdk.XunitException(
                    $"未预期的严格手牌登场续接：{prompt.Kind}/{prompt.Continuation}");
                continue;
            }
            if (game.State.EffectStack.Count == 0) return;
            Invoke(game, "ResolveTopStack");
        }
        throw new Xunit.Sdk.XunitException("严格手牌登场响应链未在安全次数内结束");
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

    [Fact]
    public void KabaFreeEntryDoesNotInventAMoralePaymentCost()
    {
        var ability = Assert.Single(Catalog.AtomicEffects.Find("S01-0213")!.Abilities,
            candidate => candidate.AbilityId == KabaAbilityId);
        Assert.DoesNotContain(ability.Atoms, atom => atom.Kind == L12AtomKinds.PayMorale);
        Assert.Contains(ability.Atoms, atom => atom.Kind == L12AtomKinds.Rest);
    }

    [Fact]
    [L12AbilityEvidence(LiuBeiAbilityId, "normal", "reconnect", "duplicate-submit",
        "single-candidate-choice", "presentation-consumers")]
    public void LiuBeiRestoresItsPrivateBrotherChoiceAndCommitsTheDeclaredInstanceOnce()
    {
        var game = Create(94001);
        var player = game.State.Players[0];
        var liuBei = Card("S01-0105", "strict-liubei");
        var guanYu = Card("S01-0106", "strict-guanyu");
        player.Field[0][0] = liuBei;
        player.Hand.Add(guanYu);
        AddMorale(player, 2);
        QueueCandidate(game, liuBei, "enter");

        Resolve(game, "mode:use");
        var payment = Prompt(game);
        Assert.Equal("target-morale", payment.Kind);
        Resolve(game, player.Morale[0].InstanceId);
        var handChoice = Prompt(game);
        Assert.True(handChoice.IsPrivate);
        Assert.Contains(guanYu.InstanceId, handChoice.ValidChoices);
        Assert.Contains("skip", handChoice.ValidChoices);
        Assert.Single(game.SnapshotFor(0).Prompts);
        Assert.Empty(game.SnapshotFor(1).Prompts);

        game = Restore(game);
        handChoice = Prompt(game);
        Assert.Contains(guanYu.InstanceId, handChoice.ValidChoices);
        Resolve(game, guanYu.InstanceId);
        var duplicate = game.Handle(0, new L12Command("resolvePrompt",
            PromptId: handChoice.PromptId, Choice: guanYu.InstanceId));
        Assert.False(duplicate.Accepted);
        Resolve(game, "0:1");

        Assert.Single(game.State.EffectStack, item => item.SourceInstanceId == liuBei.InstanceId);
        Assert.Single(game.SnapshotFor(0).EffectStack);
        PassResponses(game);
        player = game.State.Players[0];
        Assert.Equal(guanYu.InstanceId, player.Field[0][1]?.InstanceId);
        Assert.Single(player.Morale);
    }

    [Fact]
    [L12AbilityEvidence(LiuBeiAbilityId, "no-target")]
    public void LiuBeiMayPayItsCostAndResolveWithoutABrotherTarget()
    {
        var game = Create(94002);
        var player = game.State.Players[0];
        var liuBei = Card("S01-0105", "strict-liubei-no-target");
        player.Field[0][0] = liuBei;
        AddMorale(player, 1);
        QueueCandidate(game, liuBei, "enter");

        Resolve(game, "mode:use");
        if (game.State.PendingPrompts.FirstOrDefault()?.Kind == "target-morale")
            Resolve(game, player.Morale[0].InstanceId);
        PassResponses(game);

        Assert.Empty(player.Morale);
        Assert.Empty(game.State.EffectStack);
        Assert.Same(liuBei, player.Field[0][0]);
    }

    [Fact]
    [L12AbilityEvidence(LiuBeiAbilityId, "payment-cancel")]
    public void LiuBeiManualReturnCostCanBeCancelledWithoutPartialPayment()
    {
        var game = Create(94003);
        var player = game.State.Players[0];
        var liuBei = Card("S01-0105", "strict-liubei-cancel");
        player.Field[0][0] = liuBei;
        AddMorale(player, 2);
        QueueCandidate(game, liuBei, "enter");

        Resolve(game, "mode:use");
        var payment = Prompt(game);
        Assert.Contains("skip", payment.ValidChoices);
        Resolve(game, "skip");

        Assert.Equal(2, player.Morale.Count);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.PendingActivations);
    }

    [Fact]
    [L12AbilityEvidence(LiuBeiAbilityId, "negated")]
    public void NegatedLiuBeiEntryKeepsTheReturnedCostButDoesNotMoveTheBrother()
    {
        var game = Create(94004);
        var player = game.State.Players[0];
        var liuBei = Card("S01-0105", "strict-liubei-negated");
        var guanYu = Card("S01-0106", "strict-guanyu-negated");
        player.Field[0][0] = liuBei;
        player.Hand.Add(guanYu);
        AddMorale(player, 1);
        QueueCandidate(game, liuBei, "enter");
        Resolve(game, "mode:use");
        if (game.State.PendingPrompts.FirstOrDefault()?.Kind == "target-morale")
            Resolve(game, player.Morale[0].InstanceId);
        Resolve(game, guanYu.InstanceId);
        Resolve(game, "0:1");
        Assert.Empty(player.Morale);
        Assert.Single(game.State.EffectStack).Negated = true;

        PassResponses(game);

        Assert.Contains(guanYu, player.Hand);
        Assert.Null(player.Field[0][1]);
        Assert.Empty(player.Morale);
    }

    [Fact]
    [L12AbilityEvidence(LiuBeiAbilityId, "target-invalidated")]
    public void LiuBeiDoesNotReplaceADeclaredBrotherOrOverwriteAnOccupiedSlotAtSettlement()
    {
        var game = Create(94005);
        var player = game.State.Players[0];
        var liuBei = Card("S01-0105", "strict-liubei-stale");
        var guanYu = Card("S01-0106", "strict-guanyu-stale");
        var occupant = Card("S01-0003", "strict-liubei-occupant");
        player.Field[0][0] = liuBei;
        player.Hand.Add(guanYu);
        AddMorale(player, 1);
        QueueCandidate(game, liuBei, "enter");
        Resolve(game, "mode:use");
        if (game.State.PendingPrompts.FirstOrDefault()?.Kind == "target-morale")
            Resolve(game, player.Morale[0].InstanceId);
        Resolve(game, guanYu.InstanceId);
        Resolve(game, "0:1");
        player.Field[0][1] = occupant;

        PassResponses(game);

        Assert.Contains(guanYu, player.Hand);
        Assert.Same(occupant, player.Field[0][1]);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("不改选其他对象", StringComparison.Ordinal));
    }

    [Fact]
    [L12AbilityEvidence(XishiAbilityId, "normal", "reconnect", "duplicate-submit",
        "single-candidate-choice", "presentation-consumers")]
    public void XishiRestoresItsSingleHandChoiceAndRunsSummonThenDrawOnce()
    {
        var game = Create(94101);
        var player = game.State.Players[0];
        var xishi = Card("S01-0116", "strict-xishi");
        var summon = Card("S01-0003", "strict-xishi-summon");
        var draw = Card("S01-0004", "strict-xishi-draw");
        player.Field[0][0] = xishi;
        player.Hand.Add(summon);
        player.Library.Add(draw);
        AddMorale(player, 1);

        Assert.True(game.Handle(0, new L12Command("activateAbility", xishi.InstanceId,
            Ability: "xishiExchange")).Accepted);
        var handChoice = Prompt(game);
        Assert.True(handChoice.IsPrivate);
        Assert.Contains(summon.InstanceId, handChoice.ValidChoices);
        Assert.Contains("skip", handChoice.ValidChoices);
        Assert.Single(game.SnapshotFor(0).Prompts);
        game = Restore(game);
        handChoice = Prompt(game);
        Resolve(game, summon.InstanceId);
        var duplicate = game.Handle(0, new L12Command("resolvePrompt",
            PromptId: handChoice.PromptId, Choice: summon.InstanceId));
        Assert.False(duplicate.Accepted);
        Resolve(game, "0:0");
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(game.State.EffectStack)
            .Data.GetValueOrDefault("presentationSceneId")));

        PassResponses(game);
        player = game.State.Players[0];
        Assert.Equal(summon.InstanceId, player.Field[0][0]?.InstanceId);
        Assert.Contains(player.Hand, card => card.InstanceId == draw.InstanceId);
        Assert.Contains(player.Graveyard, card => card.InstanceId == xishi.InstanceId);
        Assert.Empty(player.Morale);
    }

    [Fact]
    [L12AbilityEvidence(XishiAbilityId, "payment-cancel")]
    public void XishiManualReturnCostCanBeCancelledBeforeDiscardingTheSource()
    {
        var game = Create(94102);
        var player = game.State.Players[0];
        var xishi = Card("S01-0116", "strict-xishi-cancel");
        player.Field[0][0] = xishi;
        AddMorale(player, 2);
        player.Morale[1].Tapped = true;

        Assert.True(game.Handle(0, new L12Command("activateAbility", xishi.InstanceId,
            Ability: "xishiExchange")).Accepted);
        Resolve(game, cardIds: []);
        var payment = Prompt(game);
        Assert.Contains("cancel", payment.ValidChoices);
        Resolve(game, "cancel");

        Assert.Same(xishi, player.Field[0][0]);
        Assert.Equal(2, player.Morale.Count);
        Assert.Empty(player.Graveyard);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [L12AbilityEvidence(XishiAbilityId, "negated")]
    public void NegatedXishiSummonPreservesItsPaidCostsAndStopsTheDependentDraw()
    {
        var game = Create(94103);
        var player = game.State.Players[0];
        var xishi = Card("S01-0116", "strict-xishi-negated");
        var summon = Card("S01-0003", "strict-xishi-negated-summon");
        var draw = Card("S01-0004", "strict-xishi-negated-draw");
        player.Field[0][0] = xishi;
        player.Hand.Add(summon);
        player.Library.Add(draw);
        AddMorale(player, 1);
        Assert.True(game.Handle(0, new L12Command("activateAbility", xishi.InstanceId,
            Ability: "xishiExchange")).Accepted);
        Resolve(game, summon.InstanceId);
        Resolve(game, "0:0");
        Assert.Empty(player.Morale);
        Assert.Contains(xishi, player.Graveyard);
        Assert.Single(game.State.EffectStack).Negated = true;

        PassResponses(game);

        Assert.Contains(summon, player.Hand);
        Assert.DoesNotContain(draw, player.Hand);
        Assert.Empty(player.Morale);
    }

    [Fact]
    [L12AbilityEvidence(KabaAbilityId, "normal", "reconnect", "duplicate-submit",
        "presentation-consumers")]
    public void KabaRestoresItsPublicSlotChoiceAndLocksMoraleOnlyAfterSuccessfulEntry()
    {
        var game = Create(94201);
        var player = game.State.Players[0];
        var kaba = Card("S01-0213", "strict-kaba");
        player.Hand.Add(kaba);
        AddMorale(player, 1, tapped: true);
        QueueCandidate(game, kaba, "reaction");

        var mode = Prompt(game);
        Assert.Contains("mode:use", mode.ValidChoices);
        Assert.Single(game.SnapshotFor(0).Prompts);
        Resolve(game, "mode:use");
        game = Restore(game);
        var slot = Prompt(game);
        Assert.Contains("0:0", slot.ValidChoices);
        var slotPrompt = Prompt(game);
        Resolve(game, "0:0");
        var duplicate = game.Handle(0, new L12Command("resolvePrompt",
            PromptId: slotPrompt.PromptId, Choice: "0:0"));
        Assert.False(duplicate.Accepted);
        Assert.Single(game.SnapshotFor(0).EffectStack);

        PassResponses(game);
        player = game.State.Players[0];
        Assert.Equal(kaba.InstanceId, player.Field[0][0]?.InstanceId);
        Assert.Equal(game.State.Round + 1, Assert.Single(player.Morale).CannotUntapUntilRound);
    }

    [Fact]
    [L12AbilityEvidence(KabaAbilityId, "no-target")]
    public void KabaWithNoOpenSlotCanOnlyDeclineAndRemainsInHand()
    {
        var game = Create(94202);
        var player = game.State.Players[0];
        var kaba = Card("S01-0213", "strict-kaba-no-slot");
        player.Hand.Add(kaba);
        for (var row = 0; row < 2; row++)
        for (var slot = 0; slot < 3; slot++)
            player.Field[row][slot] = Card("S01-0003", $"strict-occupant-{row}-{slot}");

        QueueCandidate(game, kaba, "reaction");
        Assert.Empty(game.State.PendingPrompts);
        Assert.Contains(kaba, player.Hand);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.PendingActivations);
    }
}
