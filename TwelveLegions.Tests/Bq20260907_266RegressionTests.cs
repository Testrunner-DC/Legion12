using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class Bq20260907_266RegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, string? firstMasterId = null)
    {
        L12GameEngine game;
        if (firstMasterId is null)
        {
            game = new L12GameEngine(Catalog, "bq-20260907-266", "BQ266", seed, ["甲", "乙"], [0, 1],
                skipPreparation: true, autoPassEmptyResponses: false,
                concealHiddenResponseAvailability: false);
        }
        else
        {
            var baseDeck = Catalog.DeckAt(0);
            var firstDeck = new L12PresetDeckDefinition
            {
                Name = $"{firstMasterId}测试牌库",
                MasterId = firstMasterId,
                CardIds = [.. baseDeck.CardIds],
                MoraleIds = [.. baseDeck.MoraleIds],
                SpecialIds = [.. baseDeck.SpecialIds],
            };
            game = new L12GameEngine(Catalog, "bq-20260907-266", "BQ266", seed, ["甲", "乙"],
                [firstDeck, baseDeck], skipPreparation: true, autoPassEmptyResponses: false,
                concealHiddenResponseAvailability: false);
        }

        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 7;
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
            player.MoraleDeck.Clear();
            player.UsedAbilities.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId)
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
            HasPrintedCost = definition.Cost is not null,
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

    private static void AddReadyMorale(L12PlayerState player, int count)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-03C1",
                InstanceId = $"bq266-morale-{player.PlayerIndex}-{index}",
            });
    }

    private static L12Prompt Prompt(L12GameEngine game) => Assert.Single(game.State.PendingPrompts);

    private static void Choose(L12GameEngine game, string choice)
    {
        var prompt = Prompt(game);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void ChooseMany(L12GameEngine game, params string[] choices)
    {
        var prompt = Prompt(game);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: choices.ToList()));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game, int maximum = 24)
    {
        var count = 0;
        while (game.State.PendingPrompts.FirstOrDefault() is { Kind: "response" } prompt && count++ < maximum)
        {
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
        Assert.True(count < maximum, "响应窗口未在限定次数内结束");
    }

    private static (L12CardInstance Hunt, L12CardInstance[] GraveCards, L12CardInstance Target)
        ArrangeHuntingMoment(L12GameEngine game, string prefix)
    {
        var player = game.State.Players[0];
        var hunt = Card("S01-0319", $"{prefix}-hunt");
        var graveCards = Enumerable.Range(0, 4)
            .Select(index => Card("S01-0001", $"{prefix}-grave-{index}"))
            .ToArray();
        var target = Card("S01-0103", $"{prefix}-target");
        player.Hand.Add(hunt);
        player.Graveyard.AddRange(graveCards);
        game.State.Players[1].Field[0][0] = target;
        AddReadyMorale(player, hunt.Cost);
        return (hunt, graveCards, target);
    }

    private static void DeclareHuntingMoment(L12GameEngine game,
        L12CardInstance hunt, IReadOnlyList<L12CardInstance> graveCards, L12CardInstance target)
    {
        var begin = game.Handle(0, new L12Command("playCard", hunt.InstanceId));
        Assert.True(begin.Accepted, begin.Error);
        Assert.Contains("墓地", Prompt(game).Text, StringComparison.Ordinal);
        ChooseMany(game, graveCards.Select(card => card.InstanceId).ToArray());
        Assert.Contains(target.InstanceId, Prompt(game).ValidChoices);
        Choose(game, target.InstanceId);
    }

    [Fact]
    public void HuntingMomentCatalogUsesTheApprovedEffectTextWithoutAColonCost()
    {
        Assert.Equal("将墓地4张卡牌自选顺序返回我方牌库底部，击杀对方1张兵力不高于6000的军团。",
            Catalog.Cards["S01-0319"].Effect);
    }

    [Fact]
    public void NegatedHuntingMomentReturnsNoCardsAndKillsNoTarget()
    {
        var game = Create(26601);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var (hunt, graveCards, target) = ArrangeHuntingMoment(game, "bq266-negated");

        DeclareHuntingMoment(game, hunt, graveCards, target);

        var item = Assert.Single(game.State.EffectStack);
        Assert.Equal("hunt-effect", item.Data["atomicFlow"]);
        Assert.Equal("response", Prompt(game).Kind);
        Assert.Empty(player.Library);
        Assert.All(graveCards, card => Assert.Contains(card, player.Graveyard));
        item.Negated = true;
        PassResponses(game);

        Assert.Empty(player.Library);
        Assert.All(graveCards, card => Assert.Contains(card, player.Graveyard));
        Assert.Same(target, opponent.Field[0][0]);
        Assert.DoesNotContain(target, opponent.Graveyard);
        Assert.DoesNotContain(game.State.Events, actionEvent => actionEvent.Type == "effect"
            && actionEvent.Text.Contains("返回牌库底部", StringComparison.Ordinal));
    }

    [Fact]
    public void HuntingMomentKeepsItsOrderedReturnWhenTheKillTargetBecomesInvalid()
    {
        var game = Create(26602);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var (hunt, graveCards, target) = ArrangeHuntingMoment(game, "bq266-stale-target");

        DeclareHuntingMoment(game, hunt, graveCards, target);
        Assert.Single(game.State.EffectStack);
        opponent.Field[0][0] = null;
        opponent.Graveyard.Add(target);
        PassResponses(game);

        Assert.Equal(graveCards.Select(card => card.InstanceId),
            player.Library.Select(card => card.InstanceId));
        Assert.Contains(game.State.Events, actionEvent => actionEvent.Type == "effect"
            && actionEvent.Text.Contains("返回牌库底部", StringComparison.Ordinal));
        Assert.Contains(game.State.Events, actionEvent => actionEvent.Type == "effect-cancelled"
            && actionEvent.Text.Contains("击杀目标已失效", StringComparison.Ordinal)
            && actionEvent.Text.Contains("不撤销", StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events, actionEvent => actionEvent.Text.Contains("被猎杀时刻击杀", StringComparison.Ordinal));
    }

    [Fact]
    public void HuntingMomentWithAStaleGraveDeclarationReturnsNothingButStillKills()
    {
        var game = Create(266021);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var (hunt, graveCards, target) = ArrangeHuntingMoment(game, "bq266-stale-grave");

        DeclareHuntingMoment(game, hunt, graveCards, target);
        var staleCard = graveCards[2];
        player.Graveyard.Remove(staleCard);
        player.Hand.Add(staleCard);
        PassResponses(game);

        Assert.Empty(player.Library);
        Assert.Contains(staleCard, player.Hand);
        Assert.All(graveCards.Where(card => card != staleCard), card => Assert.Contains(card, player.Graveyard));
        Assert.Contains(target, opponent.Graveyard);
        Assert.Contains(game.State.Events, actionEvent => actionEvent.Type == "effect"
            && actionEvent.Text.Contains("不返回墓地卡牌", StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events, actionEvent => actionEvent.Type == "effect"
            && actionEvent.Text.Contains("依声明顺序返回牌库底部", StringComparison.Ordinal));
    }

    [Fact]
    public void CancellingHuntingMomentSelectionPaysNothingAndMovesNothing()
    {
        var game = Create(26603);
        var player = game.State.Players[0];
        var (hunt, graveCards, _) = ArrangeHuntingMoment(game, "bq266-cancel");

        var begin = game.Handle(0, new L12Command("playCard", hunt.InstanceId));
        Assert.True(begin.Accepted, begin.Error);
        var selection = Prompt(game);
        Assert.Contains("skip", selection.ValidChoices);
        var mixedSkip = game.Handle(0, new L12Command("resolvePrompt", PromptId: selection.PromptId,
            Choice: "skip", CardInstanceIds: [graveCards[0].InstanceId]));
        Assert.False(mixedSkip.Accepted);
        Assert.Contains("必须选择", mixedSkip.Error);
        Assert.Same(selection, Prompt(game));
        Choose(game, "skip");

        Assert.Contains(hunt, player.Hand);
        Assert.All(graveCards, card => Assert.Contains(card, player.Graveyard));
        Assert.Empty(player.Library);
        Assert.Empty(player.Resolving);
        Assert.Empty(game.State.EffectStack);
        Assert.Equal(hunt.Cost, player.Morale.Count(card => !card.Tapped));
    }

    [Fact]
    public void PtolemyRepeatedHuntingMomentAlsoReturnsTheGraveCardsBeforeKilling()
    {
        var game = Create(26604);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var ptolemy = Card("S01-0211", "bq266-ptolemy");
        var graveCards = Enumerable.Range(0, 4)
            .Select(index => Card("S01-0001", $"bq266-ptolemy-grave-{index}"))
            .ToArray();
        var target = Card("S01-0103", "bq266-ptolemy-target");
        player.Hand.Add(ptolemy);
        player.Graveyard.AddRange(graveCards);
        opponent.Field[0][0] = target;
        AddReadyMorale(player, ptolemy.Cost);
        player.LastActiveTacticCardId = "S01-0319";
        player.LastActiveTacticTurnSerial = game.State.TurnSerial;

        var play = game.Handle(0, new L12Command("playCard", ptolemy.InstanceId, Row: 0, Slot: 0));
        Assert.True(play.Accepted, play.Error);
        PassResponses(game);
        Assert.Contains("墓地", Prompt(game).Text, StringComparison.Ordinal);
        ChooseMany(game, graveCards.Select(card => card.InstanceId).ToArray());
        Choose(game, target.InstanceId);
        Assert.Equal("hunt-effect", Assert.Single(game.State.EffectStack).Data["atomicFlow"]);
        PassResponses(game);

        Assert.Equal(graveCards.Select(card => card.InstanceId),
            player.Library.Select(card => card.InstanceId));
        Assert.Contains(target, opponent.Graveyard);
        Assert.Same(ptolemy, player.Field[0][0]);
        Assert.DoesNotContain(player.Graveyard, card => card.InstanceId.StartsWith("repeat-effect-", StringComparison.Ordinal));
        Assert.DoesNotContain(player.Resolving, card => card.InstanceId.StartsWith("repeat-effect-", StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events, actionEvent => actionEvent.Type == "cost"
            && actionEvent.Text.Contains("墓地", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("S02-06C1")]
    [InlineData("ST06-C1")]
    public void OtherworldRuneAbilityProjectionNormalizesIdentityAndDisablesAtZeroRunes(string cardId)
    {
        var game = Create(26605, "S02-06M1");
        var player = game.State.Players[0];
        player.SpecialZones.Runes = 0;

        var zeroViews = Assert.IsType<List<L12AbilityView>>(Invoke(game, "BuildAbilityViews",
            player, cardId, "faction-0"));
        var zeroRune = Assert.Single(zeroViews, ability => ability.Id == "runeUse");
        Assert.False(zeroRune.Enabled);
        Assert.Equal("需要消耗1符文", zeroRune.DisabledReason);

        player.SpecialZones.Runes = 1;
        var oneViews = Assert.IsType<List<L12AbilityView>>(Invoke(game, "BuildAbilityViews",
            player, cardId, "faction-0"));
        var oneRune = Assert.Single(oneViews, ability => ability.Id == "runeUse");
        Assert.True(oneRune.Enabled);
        Assert.Null(oneRune.DisabledReason);
    }

    [Fact]
    public void RuneUseRetainsCancelStaleResourceAndOncePerTurnAuthorityChecks()
    {
        var game = Create(26606, "S02-06M1");
        var player = game.State.Players[0];

        player.SpecialZones.Runes = 0;
        var forgedAtZero = game.Handle(0,
            new L12Command("activateAbility", "faction-0", Ability: "runeUse"));
        Assert.False(forgedAtZero.Accepted);
        Assert.Contains("需要消耗1符文", forgedAtZero.Error);
        Assert.Empty(game.State.PendingPrompts);

        player.SpecialZones.Runes = 1;
        var cancellable = game.Handle(0,
            new L12Command("activateAbility", "faction-0", Ability: "runeUse"));
        Assert.True(cancellable.Accepted, cancellable.Error);
        Assert.Contains("skip", Prompt(game).ValidChoices);
        Choose(game, "skip");
        Assert.Equal(1, player.SpecialZones.Runes);
        Assert.DoesNotContain("active:faction-0:runeUse", player.UsedAbilities);

        Assert.True(game.Handle(0,
            new L12Command("activateAbility", "faction-0", Ability: "runeUse")).Accepted);
        player.SpecialZones.Runes = 0;
        Choose(game, "mode:draw");
        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.DoesNotContain("active:faction-0:runeUse", player.UsedAbilities);
        Assert.Contains(game.State.Events, actionEvent => actionEvent.Type == "ability-rejected"
            && actionEvent.Text.Contains("需要消耗1符文", StringComparison.Ordinal));

        var drawn = Card("S01-0001", "bq266-rune-draw");
        player.Library.Add(drawn);
        player.SpecialZones.Runes = 1;
        Assert.True(game.Handle(0,
            new L12Command("activateAbility", "faction-0", Ability: "runeUse")).Accepted);
        Choose(game, "mode:draw");
        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Contains("active:faction-0:runeUse", player.UsedAbilities);
        PassResponses(game);
        Assert.Contains(drawn, player.Hand);

        player.SpecialZones.Runes = 1;
        var repeated = game.Handle(0,
            new L12Command("activateAbility", "faction-0", Ability: "runeUse"));
        Assert.False(repeated.Accepted);
        Assert.Contains("已经发动", repeated.Error);
        Assert.Equal(1, player.SpecialZones.Runes);
        Assert.Empty(game.State.PendingPrompts);
    }

    private static object? Invoke(object target, string method, params object?[] args)
        => target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == method && candidate.GetParameters().Length == args.Length)
            .Invoke(target, args);
}
