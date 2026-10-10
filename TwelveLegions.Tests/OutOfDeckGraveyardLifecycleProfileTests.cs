using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class OutOfDeckGraveyardLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence("S01-0212:ability:static:6d8b57888db9839b", "exact-card-family", "text-independent")]
    [L12AbilityEvidence("S02-0201:ability:continuous:16b90b36ef8afe2c", "exact-card-family", "text-independent")]
    public void SpecialDeckAndDepartureDefinitionsMatchTheClosedFamilyWithoutReadingDisplayText()
    {
        var actual = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.ExecutionModel is "continuous" or "rule"
                && L12StructuredCardSemantics.HasOutOfDeckGraveyardLifecycle(ability.CardId)
                && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.MoveZone))
            .Select(ability => ability.AbilityId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(EffectLifecycleProfiles.OutOfDeckGraveyardLifecycleAbilityIds
            .OrderBy(id => id, StringComparer.Ordinal), actual);

        var knownWithoutText = Definition("S01-0212", string.Empty);
        var unrelatedWithCopiedText = Definition("TEST-COPIED-RULE",
            "构筑时不计入卡组数量，不能进入手牌和牌库，游戏开始时置入墓地，以任何形式离场均视为置入所有者墓地");
        Assert.True(L12SpecialDeckRules.DoesNotCountTowardMainDeck(knownWithoutText));
        Assert.True(L12SpecialDeckRules.StartsInGraveyard(knownWithoutText));
        Assert.False(L12SpecialDeckRules.DoesNotCountTowardMainDeck(unrelatedWithCopiedText));
        Assert.False(L12SpecialDeckRules.StartsInGraveyard(unrelatedWithCopiedText));

        var knownInstance = Card("S02-0201", "known-without-text", effectText: string.Empty);
        var unrelatedInstance = Card("S01-0003", "unrelated-with-copied-text",
            effectText: unrelatedWithCopiedText.Effect);
        Assert.True(L12SpecialDeckRules.CannotEnterHandOrLibrary(knownInstance));
        Assert.True(L12SpecialDeckRules.AlwaysReturnsToOwnerGraveyard(knownInstance));
        Assert.False(L12SpecialDeckRules.CannotEnterHandOrLibrary(unrelatedInstance));
        Assert.False(L12SpecialDeckRules.AlwaysReturnsToOwnerGraveyard(unrelatedInstance));
    }

    [Theory]
    [L12AbilityEvidence("S01-0212:ability:static:6d8b57888db9839b", "deck-count", "opening-graveyard")]
    [L12AbilityEvidence("S02-0201:ability:continuous:16b90b36ef8afe2c", "deck-count", "opening-graveyard")]
    [InlineData("S01-0212")]
    [InlineData("S02-0201")]
    public void BothCardsAreExcludedFromDeckCountAndStartInGraveyard(string cardId)
    {
        var definition = Catalog.Cards[cardId];
        Assert.True(L12SpecialDeckRules.DoesNotCountTowardMainDeck(definition));
        Assert.True(L12SpecialDeckRules.StartsInGraveyard(definition));

        var baseDeck = Catalog.DeckAt(2);
        var deck = new L12PresetDeckDefinition
        {
            Name = "特殊牌库生命周期",
            MasterId = baseDeck.MasterId,
            CardIds = [.. baseDeck.CardIds, cardId],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [.. baseDeck.SpecialIds],
        };
        var game = new L12GameEngine(Catalog, "special-deck-lifecycle", "SPECIAL-DECK", 72100,
            ["甲", "乙"], [deck, Catalog.DeckAt(0)], skipPreparation: true, disasterMode: "none");
        var player = game.State.Players[0];
        Assert.Contains(player.Graveyard, card => card.CardId == cardId);
        Assert.DoesNotContain(player.Library, card => card.CardId == cardId);
        Assert.DoesNotContain(player.Hand, card => card.CardId == cardId);
    }

    [Theory]
    [L12AbilityEvidence("S01-0212:ability:static:6d8b57888db9839b", "hand-filter", "library-filter",
        "owner-graveyard", "all-departure-destinations", "controller-owner-split",
        "normal", "duplicate-submit", "presentation-consumers")]
    [L12AbilityEvidence("S02-0201:ability:continuous:16b90b36ef8afe2c", "hand-filter", "library-filter",
        "owner-graveyard", "all-departure-destinations", "controller-owner-split",
        "normal", "duplicate-submit", "presentation-consumers")]
    [InlineData("S01-0212", "hand")]
    [InlineData("S01-0212", "library-top")]
    [InlineData("S01-0212", "library-bottom")]
    [InlineData("S01-0212", "removed")]
    [InlineData("S02-0201", "hand")]
    [InlineData("S02-0201", "library-top")]
    [InlineData("S02-0201", "library-bottom")]
    [InlineData("S02-0201", "removed")]
    public void EveryFieldDepartureDestinationIsReplacedWithTheOwnersGraveyard(string cardId, string destination)
    {
        var game = Create(72101);
        var owner = game.State.Players[0];
        var controller = game.State.Players[1];
        var card = Card(cardId, $"departure-{cardId}-{destination}", owner: 0);
        controller.Field[0][0] = card;

        var moved = Assert.IsType<bool>(Invoke(game, "MoveFieldCardToZone",
            controller, card, destination, "被测试效果移动", true));

        Assert.True(moved);
        Assert.Contains(card, owner.Graveyard);
        Assert.DoesNotContain(card, owner.Hand);
        Assert.DoesNotContain(card, owner.Library);
        Assert.DoesNotContain(card, owner.Removed);
        Assert.DoesNotContain(card, controller.Graveyard);
        Assert.Null(controller.Field[0][0]);
        Assert.Contains(game.State.Events, entry => entry.Type == "replacement"
            && entry.PlayerIndex == owner.PlayerIndex && entry.Cards.Any(snapshot => snapshot.InstanceId == card.InstanceId));
        var ownerGraveyardProjection = Assert.IsType<L12CardInstance[]>(Invoke(game, "SnapshotGraveyard", owner));
        Assert.Contains(ownerGraveyardProjection, snapshot => snapshot.InstanceId == card.InstanceId);

        var duplicate = Assert.IsType<bool>(Invoke(game, "MoveFieldCardToZone",
            controller, card, destination, "重复提交不得再次移动", true));
        Assert.False(duplicate);
        Assert.Single(owner.Graveyard, existing => existing.InstanceId == card.InstanceId);
    }

    [Theory]
    [InlineData("S01-0212")]
    [InlineData("S02-0201")]
    [L12AbilityEvidence("S01-0212:ability:static:6d8b57888db9839b", "reconnect")]
    [L12AbilityEvidence("S02-0201:ability:continuous:16b90b36ef8afe2c", "reconnect")]
    public void OwnerGraveyardReplacementRemainsAuthoritativeAfterReconnect(string cardId)
    {
        var game = Create(721011, stateFormatVersion: 2);
        var owner = game.State.Players[0];
        var controller = game.State.Players[1];
        var card = Card(cardId, $"restore-{cardId}", owner: 0);
        controller.Field[1][2] = card;

        Assert.True(Assert.IsType<bool>(Invoke(game, "MoveFieldCardToZone",
            controller, card, "hand", "被测试效果移动", true)));
        game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

        Assert.Contains(game.State.Players[0].Graveyard,
            restored => restored.InstanceId == card.InstanceId);
        Assert.DoesNotContain(game.State.Players[1].Graveyard,
            restored => restored.InstanceId == card.InstanceId);
        Assert.DoesNotContain(game.State.Players.SelectMany(player => player.Hand),
            restored => restored.InstanceId == card.InstanceId);
    }

    [Fact]
    [L12AbilityEvidence("S01-0212:ability:static:6d8b57888db9839b", "derived-card-precedence")]
    public void DerivedCardVanishRuleKeepsPriorityOverTheGraveyardLifecycle()
    {
        var game = Create(72102);
        var player = game.State.Players[0];
        var derived = Card("S01-0212", "derived-precedence", cardType: "token", owner: 0);
        player.Field[0][0] = derived;

        var moved = Assert.IsType<bool>(Invoke(game, "MoveFieldCardToZone",
            player, derived, "hand", "被测试效果移动", true));

        Assert.True(moved);
        Assert.DoesNotContain(derived, player.Graveyard);
        Assert.DoesNotContain(derived, player.Hand);
        Assert.Contains(game.State.Events, entry => entry.Type == "derived-vanished"
            && entry.Cards.Any(snapshot => snapshot.InstanceId == derived.InstanceId));
    }

    private static L12GameEngine Create(int seed, int stateFormatVersion = 0)
    {
        var game = new L12GameEngine(Catalog, "out-of-deck-graveyard", "OUT-OF-DECK", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: stateFormatVersion);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 5;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Removed.Clear();
        }
        return game;
    }

    private static L12CardDefinition Definition(string cardId, string? effect) => new()
    {
        Id = cardId,
        Number = cardId,
        NameZh = cardId,
        CardType = "legion",
        Product = "TEST",
        Faction = "universal",
        Effect = effect,
    };

    private static L12CardInstance Card(string cardId, string instanceId, string? effectText = null,
        string? cardType = null, int owner = 0)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = cardType ?? definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            EffectText = effectText ?? definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            OwnerIndex = owner,
        };
    }

    private static object? Invoke(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == methodName && candidate.GetParameters().Length == arguments.Length);
        return method.Invoke(target, arguments);
    }
}
