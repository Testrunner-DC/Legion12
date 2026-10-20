using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class XiShiQinLiangyuZeroMoraleReportedTests
{
    private const string XiShiId = "reported-xishi";
    private const string QinLiangyuId = "reported-qin-liangyu";
    private const string StartingMoraleId = "reported-starting-morale";
    private static readonly L12Catalog Catalog =
        L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void XiShiReturnKeepsZeroMoraleEligibilityAfterQinLiangyuAddsItsEntryMorale(
        int actor, bool restoreZeroMoraleDeclaration)
    {
        var game = RunReportedChain(actor, acceptZeroMoraleRecovery: true,
            restoreZeroMoraleDeclaration: restoreZeroMoraleDeclaration);
        var player = game.State.Players[actor];

        Assert.Equal(QinLiangyuId, player.Field[0][0]?.InstanceId);
        Assert.False(player.Field[0][0]!.Tapped);
        Assert.Contains(player.Graveyard, card => card.InstanceId == XiShiId);
        Assert.DoesNotContain(player.Hand, card => card.InstanceId == QinLiangyuId);
        Assert.Equal(3, player.Morale.Count);
        Assert.All(player.Morale, morale => Assert.True(morale.Tapped));
        Assert.Contains("trigger:factionZeroRecovery", player.UsedAbilities);

        var zeroRecovery = Assert.Single(game.State.Events, entry => entry.Type == "faction-effect"
            && entry.Text.Contains("追加 2 张休整士气", StringComparison.Ordinal));
        var qinLiangyuEntry = Assert.Single(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("秦良玉从士气牌库追加1张休整士气", StringComparison.Ordinal));
        Assert.True(qinLiangyuEntry.Sequence < zeroRecovery.Sequence,
            "西施的连续效果与秦良玉登场效果先完成；天廷归零资格按返还时锁定，随后独立询问并结算");
        Assert.Equal(2, game.State.Players[1 - actor].Morale.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void DecliningZeroMoraleRecoveryDoesNotSuppressQinLiangyuUnconditionalEntryMorale(int actor)
    {
        var game = RunReportedChain(actor, acceptZeroMoraleRecovery: false,
            restoreZeroMoraleDeclaration: false);
        var player = game.State.Players[actor];

        Assert.Equal(QinLiangyuId, player.Field[0][0]?.InstanceId);
        Assert.Contains(player.Graveyard, card => card.InstanceId == XiShiId);
        Assert.Single(player.Morale);
        Assert.True(player.Morale[0].Tapped);
        Assert.DoesNotContain("trigger:factionZeroRecovery", player.UsedAbilities);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "faction-effect"
            && entry.Text.Contains("追加 2 张休整士气", StringComparison.Ordinal));
        Assert.Single(game.State.Events, entry => entry.Type == "effect-declined"
            && entry.Cards.Any(card => card.CardId == "S01-01C1"));
        Assert.Single(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("秦良玉从士气牌库追加1张休整士气", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("S01-01M2", 0)]
    [InlineData("S01-01M2", 1)]
    [InlineData("S02-01M1", 0)]
    [InlineData("S02-01M1", 1)]
    [InlineData("ST01-M1", 0)]
    [InlineData("ST01-M1", 1)]
    public void EveryOtherTiantingMasterOnEitherSeatUsesTheSameFactionZeroQueue(
        string masterId, int actor)
    {
        var game = RunReportedChain(actor, acceptZeroMoraleRecovery: true,
            restoreZeroMoraleDeclaration: false, masterId: masterId);
        var player = game.State.Players[actor];

        Assert.Equal(masterId, player.MasterId);
        Assert.Equal("tianting", player.Faction);
        Assert.Equal(3, player.Morale.Count);
        Assert.Single(game.State.Events, entry => entry.Type == "faction-effect"
            && entry.Text.Contains("追加 2 张休整士气", StringComparison.Ordinal));
        if (masterId == "ST01-M1")
            Assert.DoesNotContain(game.State.Events,
                entry => entry.Text.Contains("嫦娥使我方追加1张休整的士气", StringComparison.Ordinal));
    }

    [Fact]
    public void ChangeDualCandidateKeepsFactionRecoveryIndependentAcrossV2Restore()
    {
        var game = RunReportedChain(0, acceptZeroMoraleRecovery: true,
            restoreZeroMoraleDeclaration: true, masterId: "ST01-M1");

        Assert.Equal(3, game.State.Players[0].Morale.Count);
        Assert.Single(game.State.Events, entry => entry.Type == "faction-effect"
            && entry.Text.Contains("追加 2 张休整士气", StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events,
            entry => entry.Text.Contains("嫦娥使我方追加1张休整的士气", StringComparison.Ordinal));
    }

    [Fact]
    public void NonTiantingMasterDoesNotAcquireTheTiantingZeroMoraleTrigger()
    {
        var game = RunReportedChain(0, acceptZeroMoraleRecovery: false,
            restoreZeroMoraleDeclaration: false, masterId: "S01-04M2",
            expectedZeroDeclarationCount: 0);
        var player = game.State.Players[0];

        Assert.NotEqual("tianting", player.Faction);
        Assert.Single(player.Morale);
        Assert.DoesNotContain(player.UsedAbilities, key =>
            key.Contains("factionZeroRecovery", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "faction-effect");
    }

    [Fact]
    public void EmptyMoraleDeckAtTheReturnDoesNotCreateAnUnpayableFactionTrigger()
    {
        var game = RunReportedChain(0, acceptZeroMoraleRecovery: false,
            restoreZeroMoraleDeclaration: false, expectedZeroDeclarationCount: 0,
            configurePlayer: player =>
            {
                player.Morale.Clear();
                player.Morale.Add(Morale(StartingMoraleId, "S02-0010"));
                player.MoraleDeck.Clear();
            });
        var player = game.State.Players[0];

        Assert.Empty(player.Morale);
        Assert.Contains(player.Graveyard, card => card.InstanceId == StartingMoraleId
            && card.CardId == "S02-0010");
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "faction-effect");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ReturningToZeroOutsideTheOwnersTurnDoesNotQueueTheFactionTrigger(int defender)
    {
        var attacker = 1 - defender;
        var game = CreateReportedGame(defender);
        game.State.ActivePlayer = attacker;
        game.State.FirstPlayer = attacker;
        var attackingLegion = Card("S01-0002", "reported-off-turn-attacker", attacker);
        var emptyCity = Card("S01-0120", "reported-off-turn-empty-city", defender);
        emptyCity.Hidden = true;
        emptyCity.SetRound = 0;
        game.State.Players[attacker].Field[0][0] = attackingLegion;
        game.State.Players[defender].Field[1][0] = emptyCity;

        var attack = game.Handle(attacker, new L12Command("attack", attackingLegion.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(attack.Accepted, attack.Error);
        ResolveOnlyPrompt(game, "pass");
        ResolveOnlyPrompt(game, emptyCity.InstanceId);
        ResolveOnlyPrompt(game, StartingMoraleId);

        var drawMode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", drawMode.Continuation);
        Assert.Equal(defender, drawMode.PlayerIndex);
        Assert.DoesNotContain(game.State.Players[defender].UsedAbilities,
            key => key.Contains("factionZeroRecovery", StringComparison.OrdinalIgnoreCase));
        ResolvePrompt(game, drawMode, "mode:none");
        PassResponsePrompts(game);

        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "faction-effect");
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.SourceCardId == "S01-01C1");
    }

    [Fact]
    public void ASecondZeroMoraleReturnInTheSameTurnDoesNotOfferTheOncePerTurnTriggerAgain()
    {
        var game = RunReportedChain(0, acceptZeroMoraleRecovery: true,
            restoreZeroMoraleDeclaration: false);
        var player = game.State.Players[0];
        var firstTriggerCount = game.State.Events.Count(entry => entry.Type == "faction-effect"
            && entry.Text.Contains("追加 2 张休整士气", StringComparison.Ordinal));
        player.Morale.Add(Morale("reported-second-return"));
        Assert.Equal(4, player.Morale.Count);
        var returnedIds = player.Morale.Select(morale => morale.InstanceId).ToArray();

        var activate = game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "nonLethal"));
        Assert.True(activate.Accepted, activate.Error);
        if (game.State.PendingPrompts.FirstOrDefault() is { Continuation: "active-return-choice" } payment)
        {
            var returned = game.Handle(0, new L12Command("resolvePrompt",
                PromptId: payment.PromptId, CardInstanceIds: [.. returnedIds]));
            Assert.True(returned.Accepted, returned.Error);
        }
        PassResponsePrompts(game);

        Assert.Empty(player.Morale);
        Assert.All(returnedIds, id => Assert.Contains(player.MoraleDeck, morale => morale.InstanceId == id));
        var dogDeclaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("S02-01S1", dogDeclaration.SourceCardId);
        Assert.Equal("pending-activation", dogDeclaration.Continuation);
        ResolvePrompt(game, dogDeclaration, "mode:none");
        PassResponsePrompts(game);
        Assert.Equal(firstTriggerCount, game.State.Events.Count(entry => entry.Type == "faction-effect"
            && entry.Text.Contains("追加 2 张休整士气", StringComparison.Ordinal)));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    public void RestoredLegacyPendingMarkerWithoutReturnEventCannotBorrowALaterNonZeroReturnLock()
    {
        var game = CreateReportedGame(0);
        var player = game.State.Players[0];
        player.UsedAbilities.Add("pending:factionZeroRecovery");
        player.PendingStarterMoraleReturnEvents = 0;
        player.Hand.Clear();
        player.Field[0][0] = Card("S01-0116", "reported-legacy-xishi", 0);
        player.Library.Clear();
        player.Library.Add(Card("S01-0101", "reported-legacy-draw", 0));
        player.Morale.Clear();
        for (var index = 0; index < 2; index++)
            player.Morale.Add(Morale($"reported-legacy-payment-{index}"));
        game = Restore(game);

        var activate = game.Handle(0, new L12Command("activateAbility", "reported-legacy-xishi",
            Ability: "xishiExchange"));
        Assert.True(activate.Accepted, activate.Error);
        var declaration = Assert.Single(game.State.PendingPrompts);
        var declaredNone = game.Handle(0, new L12Command("resolvePrompt",
            PromptId: declaration.PromptId, CardInstanceIds: []));
        Assert.True(declaredNone.Accepted, declaredNone.Error);
        PassResponsePrompts(game);

        Assert.Single(game.State.Players[0].Morale);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "pending-activation"
            && prompt.SourceCardId == "S01-01C1");
        Assert.DoesNotContain(game.State.Players[0].UsedAbilities,
            key => key.Contains("factionZeroRecovery", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "faction-effect");
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.SourceCardId == "S01-01C1");
    }

    [Fact]
    public void ZeroLockSurvivesAnAdditionalNonZeroReturnWithinTheSameBatch()
    {
        var game = CreateReportedGame(0);
        var player = game.State.Players[0];
        player.Morale.Clear();
        player.UsedAbilities.Clear();
        player.PendingStarterMoraleReturnEvents = 0;

        InvokePrivate(game, "RegisterReturnedMorale", player, 1);
        Assert.Contains("pending:factionZeroRecovery", player.UsedAbilities);
        Assert.Equal(1, player.PendingStarterMoraleReturnEvents);

        player.Morale.Add(Morale("reported-same-batch-qin"));
        var returnedAgain = Morale("reported-same-batch-return");
        player.Morale.Add(returnedAgain);
        player.Morale.Remove(returnedAgain);
        player.MoraleDeck.Add(returnedAgain);
        InvokePrivate(game, "RegisterReturnedMorale", player, 1);

        Assert.Contains("pending:factionZeroRecovery", player.UsedAbilities);
        Assert.Equal(2, player.PendingStarterMoraleReturnEvents);
        InvokePrivate(game, "FlushStarterResourceTriggerBatches");
        Assert.Contains(game.State.PendingPrompts, prompt => prompt.Continuation == "pending-activation"
            && prompt.SourceCardId == "S01-01C1");
    }

    [Fact]
    public void CurrentZeroLegacyFallbackAndItsQueuedWorkSurviveALaterRegistration()
    {
        var game = CreateReportedGame(0);
        var player = game.State.Players[0];
        player.Morale.Clear();
        player.UsedAbilities.Clear();
        player.UsedAbilities.Add("pending:factionZeroRecovery");
        player.PendingStarterMoraleReturnEvents = 0;

        InvokePrivate(game, "AfterStackSettled");
        var fallback = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", fallback.Continuation);
        Assert.Equal("S01-01C1", fallback.SourceCardId);
        Assert.Contains("pending:factionZeroRecovery", player.UsedAbilities);
        Assert.Contains("queued:factionZeroRecovery", player.UsedAbilities);

        player.Morale.Add(Morale("reported-fallback-kept"));
        InvokePrivate(game, "RegisterReturnedMorale", player, 1);

        Assert.Contains("pending:factionZeroRecovery", player.UsedAbilities);
        Assert.Contains("queued:factionZeroRecovery", player.UsedAbilities);
        Assert.Contains(game.State.PendingActivations, activation => activation.SourceCardId == "S01-01C1");
        Assert.Contains(game.State.PendingPrompts, prompt => prompt.PromptId == fallback.PromptId);
    }

    private static L12GameEngine RunReportedChain(int actor, bool acceptZeroMoraleRecovery,
        bool restoreZeroMoraleDeclaration, string masterId = "S01-01M1",
        int expectedZeroDeclarationCount = 1, Action<L12PlayerState>? configurePlayer = null)
    {
        var game = CreateReportedGame(actor, masterId);
        var player = game.State.Players[actor];
        configurePlayer?.Invoke(player);
        Assert.Single(player.Morale);
        Assert.False(player.Morale[0].Tapped);

        var play = game.Handle(actor, new L12Command("playCard", XiShiId, Row: 0, Slot: 0));
        Assert.True(play.Accepted, play.Error);
        PassResponsePrompts(game);
        player = game.State.Players[actor];
        Assert.Equal(XiShiId, player.Field[0][0]?.InstanceId);
        Assert.True(Assert.Single(player.Morale).Tapped);

        var activate = game.Handle(actor, new L12Command("activateAbility", XiShiId,
            Ability: "xishiExchange"));
        Assert.True(activate.Accepted, activate.Error);
        ResolveOnlyPrompt(game, QinLiangyuId);
        ResolveOnlyPrompt(game, "0:0");

        var zeroDeclarationCount = 0;
        var restored = false;
        for (var safety = 0; safety < 100; safety++)
        {
            if (game.State.PendingPrompts.FirstOrDefault() is not { } prompt)
            {
                Assert.Empty(game.State.EffectStack);
                Assert.Empty(game.State.PendingActivations);
                Assert.Empty(game.State.PendingTriggerStackCandidates);
                Assert.Empty(game.State.PendingTriggerBatches);
                Assert.Null(game.State.ResponseWindow);
                break;
            }

            Assert.Single(game.State.PendingPrompts);
            if (prompt.Kind == "response")
            {
                Assert.Contains("pass", prompt.ValidChoices);
                ResolvePrompt(game, prompt, "pass");
                continue;
            }

            if (prompt.Continuation == "trigger-batch-order")
            {
                var ordered = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt",
                    PromptId: prompt.PromptId, CardInstanceIds: [.. prompt.ValidChoices]));
                Assert.True(ordered.Accepted, ordered.Error);
                continue;
            }

            if (prompt.Continuation == "pending-activation" && prompt.SourceCardId == "S01-01C1")
            {
                zeroDeclarationCount++;
                player = game.State.Players[actor];
                Assert.Equal(actor, prompt.PlayerIndex);
                var qinLiangyuMorale = Assert.Single(player.Morale);
                Assert.True(qinLiangyuMorale.Tapped);
                Assert.NotEqual(StartingMoraleId, qinLiangyuMorale.InstanceId);
                Assert.Contains(player.MoraleDeck, morale => morale.InstanceId == StartingMoraleId);
                Assert.Contains("mode:use", prompt.ValidChoices);
                Assert.Contains("mode:none", prompt.ValidChoices);

                if (restoreZeroMoraleDeclaration && !restored)
                {
                    game = Restore(game);
                    restored = true;
                    prompt = Assert.Single(game.State.PendingPrompts);
                    Assert.Equal("S01-01C1", prompt.SourceCardId);
                    Assert.Single(game.State.Players[actor].Morale);
                    Assert.Contains(game.State.Players[actor].MoraleDeck,
                        morale => morale.InstanceId == StartingMoraleId);
                }

                ResolvePrompt(game, prompt, acceptZeroMoraleRecovery ? "mode:use" : "mode:none");
                continue;
            }

            if (prompt.Continuation == "pending-activation" && prompt.SourceCardId == "ST01-M1")
            {
                Assert.Contains("mode:none", prompt.ValidChoices);
                ResolvePrompt(game, prompt, "mode:none");
                continue;
            }

            if (prompt.Continuation == "pending-activation"
                && prompt.ValidChoices.Contains("mode:use")
                && !prompt.ValidChoices.Contains("mode:none"))
            {
                ResolvePrompt(game, prompt, "mode:use");
                continue;
            }

            throw new Xunit.Sdk.XunitException(
                $"未预期的西施联动续接：{prompt.Kind}/{prompt.Continuation}/{prompt.SourceCardId}");
        }

        Assert.Equal(expectedZeroDeclarationCount, zeroDeclarationCount);
        Assert.Equal(restoreZeroMoraleDeclaration && expectedZeroDeclarationCount > 0, restored);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        return game;
    }

    private static L12GameEngine CreateReportedGame(int actor, string masterId = "S01-01M1")
    {
        var basis = Catalog.DeckAt(0);
        var tianting = new L12PresetDeckDefinition
        {
            Name = "西施秦良玉公开反馈回归",
            MasterId = masterId,
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [],
        };
        var decks = actor == 0
            ? new[] { tianting, basis }
            : new[] { basis, tianting };
        var game = new L12GameEngine(Catalog, "reported-xishi-qin-liangyu", "XI-QIN",
            102019 + actor, ["甲", "乙"], decks, skipPreparation: true,
            disasterMode: "none", autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = actor;
        game.State.FirstPlayer = actor;
        game.State.Round = 3;
        game.State.TurnSerial = 5;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;

        foreach (var participant in game.State.Players)
        {
            participant.Hand.Clear();
            participant.Library.Clear();
            participant.Graveyard.Clear();
            participant.Resolving.Clear();
            participant.Morale.Clear();
            participant.MoraleDeck.Clear();
            participant.UsedAbilities.Clear();
            foreach (var row in participant.Field) Array.Clear(row);
        }

        var player = game.State.Players[actor];
        player.Hand.Add(Card("S01-0116", XiShiId, actor));
        player.Hand.Add(Card("S01-0114", QinLiangyuId, actor));
        player.Library.Add(Card("S01-0101", "reported-xishi-draw", actor));
        player.Morale.Add(Morale(StartingMoraleId));
        for (var index = 0; index < 4; index++)
            player.MoraleDeck.Add(Morale($"reported-morale-deck-{index}"));

        var opponent = game.State.Players[1 - actor];
        opponent.Morale.Add(Morale("reported-opponent-morale-a"));
        opponent.Morale.Add(Morale("reported-opponent-morale-b"));
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
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

    private static L12MoraleCard Morale(string instanceId, string cardId = "S01-01C1") => new()
    {
        InstanceId = instanceId,
        CardId = cardId,
    };

    private static void PassResponsePrompts(L12GameEngine game)
    {
        for (var safety = 0; safety < 20
             && game.State.PendingPrompts.FirstOrDefault() is { Kind: "response" } prompt; safety++)
            ResolvePrompt(game, prompt, "pass");
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
    }

    private static void ResolveOnlyPrompt(L12GameEngine game, string choice)
        => ResolvePrompt(game, Assert.Single(game.State.PendingPrompts), choice);

    private static void ResolvePrompt(L12GameEngine game, L12Prompt prompt, string choice)
    {
        Assert.Contains(choice, prompt.ValidChoices);
        var result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt",
            PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

    private static object? InvokePrivate(L12GameEngine game, string methodName, params object?[] arguments)
        => typeof(L12GameEngine).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == methodName && method.GetParameters().Length == arguments.Length)
            .Invoke(game, arguments);
}
