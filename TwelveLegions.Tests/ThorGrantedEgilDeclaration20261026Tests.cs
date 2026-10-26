using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ThorGrantedEgilDeclaration20261026Tests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(
        Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int owner, int seed)
    {
        var basis = Catalog.DeckAt(0);
        var thor = new L12PresetDeckDefinition
        {
            Name = "索尔赋予埃吉尔声明回归",
            MasterId = "S02-03M1",
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [],
        };
        var decks = owner == 0 ? new[] { thor, basis } : new[] { basis, thor };
        var game = new L12GameEngine(Catalog, "thor-egil-declaration", "THOR-EGIL", seed,
            ["甲", "乙"], decks, skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
        game.State.ActivePlayer = owner;
        game.State.FirstPlayer = owner;
        game.State.Round = 2;
        game.State.TurnSerial = 7;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
            player.Relic = null;
            player.ExtraRelics.Clear();
        }
        game.State.Players[owner].Hp = 3;
        AddReadyMorale(game.State.Players[owner], 10, $"thor-egil-morale-{owner}");
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
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

    private static void AddReadyMorale(L12PlayerState player, int count, string prefix)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-03C1",
                InstanceId = $"{prefix}-{index}",
            });
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

    private static void ResolveChoice(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void ResolveCards(L12GameEngine game, params string[] choices)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: [.. choices]));
        Assert.True(result.Accepted, result.Error);
    }

    private static int PassCurrentResponseWindow(L12GameEngine game)
    {
        var count = 0;
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            ResolveChoice(game, "pass");
            count++;
        }
        return count;
    }

    private static void ResolveThorCharge(L12GameEngine game, int owner)
    {
        var activation = game.Handle(owner,
            new L12Command("activateAbility", $"master-{owner}", Ability: "thorCharge"));
        Assert.True(activation.Accepted, activation.Error);
        Assert.Equal(2, PassCurrentResponseWindow(game));
        Assert.Contains($"s2-thor-charge:{game.State.TurnSerial}",
            game.State.Players[owner].UsedAbilities);
    }

    private static (string EgilId, string TargetId) PrepareEgilBoard(L12GameEngine game, int owner,
        int libraryCount)
    {
        var player = game.State.Players[owner];
        var opponent = game.State.Players[1 - owner];
        var egil = Card("S01-0316", $"egil-{owner}", owner);
        var target = Card("S01-0002", $"egil-target-{owner}", 1 - owner);
        player.Hand.Add(egil);
        opponent.Field[0][0] = target;
        for (var index = 0; index < libraryCount; index++)
            player.Library.Add(Card("S02-0007", $"egil-library-{owner}-{index}", owner));
        return (egil.InstanceId, target.InstanceId);
    }

    private static void PlayEgil(L12GameEngine game, int owner, string egilId)
    {
        var player = game.State.Players[owner];
        var payment = player.Morale.Where(card => !card.Tapped).Take(2)
            .Select(card => card.InstanceId).ToList();
        var play = game.Handle(owner, new L12Command("playCard", egilId,
            CardInstanceIds: payment, Row: 0, Slot: 0));
        Assert.True(play.Accepted, play.Error);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "bug:BUG-20261026-6f111590")]
    [Trait("L12Evidence", "card:S02-03M1")]
    [Trait("L12Evidence", "card:S01-0316")]
    [Trait("L12Evidence", "reconnect:v2-thor-granted-egil-declaration")]
    public void ThorGrantedChargeAndPrintedEgilEachRespondButPrintedDeclarationPaysExactlyOnce(
        int owner, bool restoreAtGrantedResponse)
    {
        var game = Create(owner, 26102630 + owner);
        ResolveThorCharge(game, owner);
        var (egilId, targetId) = PrepareEgilBoard(game, owner, libraryCount: 4);
        PlayEgil(game, owner, egilId);

        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-batch-order", order.Continuation);
        var grantedCandidate = Assert.Single(order.ValidChoices,
            id => order.Data[id].Contains("雷神索尔", StringComparison.Ordinal));
        var printedCandidate = Assert.Single(order.ValidChoices, id => id != grantedCandidate);
        var omittedGranted = game.Handle(owner, new L12Command("resolvePrompt",
            PromptId: order.PromptId, CardInstanceIds: [printedCandidate]));
        Assert.False(omittedGranted.Accepted,
            "索尔赋予的必发效果不能从同一时点排序中漏选");
        ResolveCards(game, printedCandidate, grantedCandidate);

        var grantedPrompt = Assert.Single(game.State.PendingPrompts);
        var cancelGranted = game.Handle(grantedPrompt.PlayerIndex, new L12Command("resolvePrompt",
            PromptId: grantedPrompt.PromptId, Choice: "mode:none"));
        Assert.False(cancelGranted.Accepted,
            "索尔赋予的必发冲锋不能出现或接受‘不发动’选择");
        Assert.Equal("response", grantedPrompt.Kind);
        Assert.DoesNotContain("mode:none", grantedPrompt.ValidChoices);
        var grantedStack = Assert.Single(game.State.EffectStack);
        Assert.Equal("true", grantedStack.Data.GetValueOrDefault("thorGrantedEntryCharge"));
        if (restoreAtGrantedResponse) game = Restore(game);

        var responsePrompts = PassCurrentResponseWindow(game);
        var declarationPrompts = 0;
        var player = game.State.Players[owner];
        var target = Assert.IsType<L12CardInstance>(game.State.Players[1 - owner].Field[0][0]);
        Assert.True(Assert.IsType<L12CardInstance>(player.Field[0][0]).HasCharge);
        Assert.Equal(3, player.Hp);
        Assert.Equal(4, player.Library.Count);
        Assert.Equal(5000, target.CurrentTroops);

        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", mode.Continuation);
        Assert.Contains("mode:use", mode.ValidChoices);
        declarationPrompts++;
        ResolveChoice(game, "mode:use");

        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", targetPrompt.Continuation);
        Assert.Contains(targetId, targetPrompt.ValidChoices);
        declarationPrompts++;
        ResolveChoice(game, targetId);

        player = game.State.Players[owner];
        Assert.Equal(2, player.Hp);
        Assert.Equal(2, player.Library.Count);
        Assert.Equal(2, player.Graveyard.Count(card =>
            card.InstanceId.StartsWith($"egil-library-{owner}-", StringComparison.Ordinal)));
        Assert.Equal("response", Assert.Single(game.State.PendingPrompts).Kind);
        responsePrompts += PassCurrentResponseWindow(game);

        target = Assert.IsType<L12CardInstance>(game.State.Players[1 - owner].Field[0][0]);
        Assert.Equal(3000, target.CurrentTroops);
        Assert.True(Assert.IsType<L12CardInstance>(game.State.Players[owner].Field[0][0]).HasCharge);
        Assert.Equal(2, declarationPrompts);
        Assert.Equal(4, responsePrompts);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "control:no-thor-egil-printed-entry")]
    [Trait("L12Evidence", "card:S01-0316")]
    public void WithoutThorEgilHasOnePrintedDeclarationOnePaymentAndOneResponseWindow()
    {
        var game = Create(owner: 0, seed: 26102640);
        var (egilId, targetId) = PrepareEgilBoard(game, owner: 0, libraryCount: 2);
        PlayEgil(game, owner: 0, egilId);

        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", mode.Continuation);
        ResolveChoice(game, "mode:use");
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(targetId, targetPrompt.ValidChoices);
        ResolveChoice(game, targetId);

        Assert.Equal(2, game.State.Players[0].Hp);
        Assert.Empty(game.State.Players[0].Library);
        Assert.Equal(2, game.State.Players[0].Graveyard.Count(card =>
            card.InstanceId.StartsWith("egil-library-0-", StringComparison.Ordinal)));
        Assert.Equal(2, PassCurrentResponseWindow(game));
        var target = Assert.IsType<L12CardInstance>(game.State.Players[1].Field[0][0]);
        Assert.Equal(3000, target.CurrentTroops);
        Assert.False(Assert.IsType<L12CardInstance>(game.State.Players[0].Field[0][0]).HasCharge);
    }
}
