using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6KBRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static readonly IReadOnlyDictionary<string, int> AuditedAbilityCounts =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["S01-0201"] = 4, ["S01-0202"] = 3, ["S01-0203"] = 2, ["S01-0204"] = 5,
            ["S01-0205"] = 2, ["S01-0206"] = 4, ["S01-0207"] = 2, ["S01-0208"] = 3,
            ["S01-0209"] = 3, ["S01-0210"] = 3, ["S01-0211"] = 2, ["S01-0212"] = 3,
            ["S01-0213"] = 2, ["S01-0214"] = 2, ["S01-0215"] = 2, ["S01-0216"] = 2,
            ["S01-0217"] = 2, ["S01-0218"] = 2, ["S01-0219"] = 2, ["S01-0220"] = 2,
            ["S01-0221"] = 1, ["S01-0222"] = 1, ["S01-0223"] = 1, ["S01-0224"] = 2,
            ["S01-02C1"] = 2, ["S01-02D1"] = 4, ["S01-02M1"] = 2, ["S01-02M2"] = 2,
            ["S01-02M3"] = 2,
            ["S01-0301"] = 4, ["S01-0302"] = 3, ["S01-0303"] = 3, ["S01-0304"] = 3,
            ["S01-0305"] = 2, ["S01-0306"] = 3, ["S01-0307"] = 2, ["S01-0308"] = 3,
            ["S01-0309"] = 3, ["S01-0310"] = 3, ["S01-0311"] = 2, ["S01-0312"] = 1,
            ["S01-0313"] = 3, ["S01-0314"] = 3, ["S01-0315"] = 1, ["S01-0316"] = 2,
            ["S01-0317"] = 3, ["S01-0318"] = 1, ["S01-0319"] = 1, ["S01-0320"] = 1,
            ["S01-03C1"] = 1, ["S01-03D1"] = 4, ["S01-03M1"] = 2, ["S01-03M2"] = 1,
        };

    private static L12GameEngine Create(int seed, int factionIndex = 0)
    {
        var game = new L12GameEngine(Catalog, "atomic-review-batch6kb", "ATOMIC6KB", seed,
            ["甲", "乙"], [factionIndex, factionIndex], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
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
            player.Morale.Clear();
            player.Resolving.Clear();
            player.ExtraRelics.Clear();
            player.Relic = null;
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int? troops = null)
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
            BaseTroops = troops ?? definition.Troops ?? 0,
            Troops = troops ?? definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            SummonRound = -1,
        };
    }

    private static void AddMorale(L12PlayerState player, int count)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-03C1",
                InstanceId = $"batch6kb-morale-{player.PlayerIndex}-{index}",
            });
    }

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().Name, name);
        return method.Invoke(target, args);
    }

    private static void QueueTrigger(L12GameEngine game, L12CardInstance source, string trigger)
        => Invoke(game, "QueueOrPushTriggeredEffect", 0, source, trigger, "6K-B逐能力审计", null,
            new Dictionary<string, string>());

    private static void Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static L12StackItem PassUntilFlow(L12GameEngine game, string flow)
    {
        for (var safety = 0; safety < 80; safety++)
        {
            var item = game.State.EffectStack.SingleOrDefault(candidate =>
                candidate.Data.GetValueOrDefault("atomicFlow") == flow);
            if (item is not null) return item;
            var prompt = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("response", prompt.Kind);
            Resolve(game, "pass");
        }
        throw new Xunit.Sdk.XunitException($"未进入预期效果段 {flow}");
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 80 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            Resolve(game, "pass");
    }

    [Fact]
    [Trait("L12Evidence", "batch:6K-B-inventory")]
    public void SunCityAndAsgardAuditFreezesEveryCardAndAbility()
    {
        Assert.Equal(53, AuditedAbilityCounts.Count);
        Assert.Equal(124, AuditedAbilityCounts.Values.Sum());
        Assert.All(AuditedAbilityCounts, pair =>
        {
            var card = Assert.Contains(pair.Key, Catalog.Cards);
            Assert.Contains(card.Faction, new[] { "taiyangcheng", "asgard" });
            Assert.False(string.IsNullOrWhiteSpace(card.Effect));
            Assert.True(pair.Value > 0);
        });
    }

    [Theory]
    [InlineData("S01-0201", "attack")]
    [InlineData("S01-0201", "death")]
    [InlineData("S01-0315", "enter")]
    [InlineData("S01-0213", "reaction")]
    [InlineData("S01-0223", "reaction")]
    [Trait("L12Evidence", "entry:batch6kb-public-trigger-plans")]
    public void NewlyAuditedPublicChoicesUseTheTriggerDeclarationPlanner(string cardId, string trigger)
    {
        var method = typeof(L12GameEngine).GetMethod("HasPublicTriggerDeclarationPlan",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.True(Assert.IsType<bool>(method.Invoke(null, [cardId, trigger, null])));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0201")]
    public void ThutmoseDeclaresThePublicKillTargetAndKeepsBothSegmentsIndependent()
    {
        var game = Create(8201);
        var source = Card("S01-0201", "batch6kb-thutmose");
        var low = Card("S01-0302", "batch6kb-thutmose-low", 1000);
        var high = Card("S01-0202", "batch6kb-thutmose-high", 4000);
        low.OwnerIndex = 1;
        high.OwnerIndex = 1;
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[0][0] = low;
        game.State.Players[1].Field[0][1] = high;

        QueueTrigger(game, source, "attack");

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Contains(low.InstanceId, declaration.ValidChoices);
        Assert.DoesNotContain(high.InstanceId, declaration.ValidChoices);
        Assert.Empty(game.State.EffectStack);
        Resolve(game, low.InstanceId);

        var debuff = Assert.Single(game.State.EffectStack);
        Assert.Equal("thutmose-debuff", debuff.Data["atomicFlow"]);
        debuff.Negated = true;
        var kill = PassUntilFlow(game, "thutmose-kill");
        Assert.Equal(low.InstanceId, kill.Data["declared:killTarget"]);
        Assert.Contains(low, game.State.Players[1].Field.SelectMany(row => row));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0201")]
    public void ThutmoseWithoutAKillTargetStartsAtTheMandatoryDebuffWithoutAnEmptyDeclaration()
    {
        var game = Create(8215);
        var source = Card("S01-0201", "batch6kb-thutmose-no-target");
        var enemy = Card("S01-0302", "batch6kb-thutmose-too-large", 4000);
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[0][0] = enemy;

        QueueTrigger(game, source, "death");

        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "pending-activation");
        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal("thutmose-debuff", first.Data["atomicFlow"]);
        Assert.Equal("mode:none", first.Data["declared:killMode"]);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0209")]
    public void NefertitiLetsTheAffectedOpponentChooseItsPrivateDiscardDuringResolution()
    {
        var game = Create(8216);
        var source = Card("S01-0209", "batch6kb-nefertiti");
        game.State.Players[0].Field[0][0] = source;
        for (var index = 0; index < 6; index++)
            game.State.Players[1].Hand.Add(Card("S01-0001", $"batch6kb-nefertiti-hand-{index}"));

        QueueTrigger(game, source, "enter");
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "pending-activation");
        PassResponses(game);

        var discard = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(1, discard.PlayerIndex);
        Assert.True(discard.IsPrivate);
        Assert.Equal("nefertiti-discard", discard.Data["action"]);
        Assert.Equal(6, discard.ValidChoices.Count);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0315")]
    public void IvarDeclaresOptInWithoutLeakingTheHiddenTopThree()
    {
        var game = Create(8202);
        var player = game.State.Players[0];
        var source = Card("S01-0315", "batch6kb-ivar");
        var eligible = Card("S01-0301", "batch6kb-ivar-eligible");
        var hidden = Card("S01-0201", "batch6kb-ivar-hidden");
        player.Field[0][0] = source;
        player.Library.AddRange([eligible, hidden]);

        QueueTrigger(game, source, "enter");

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:none", declaration.ValidChoices);
        Assert.Contains("mode:use", declaration.ValidChoices);
        Assert.DoesNotContain(eligible.InstanceId, declaration.ValidChoices);
        Assert.DoesNotContain(eligible.InstanceId, JsonSerializer.Serialize(game.SnapshotFor(1)));
        Resolve(game, "mode:use");

        Assert.Single(game.State.EffectStack);
        Assert.DoesNotContain(eligible.InstanceId, JsonSerializer.Serialize(game.SnapshotFor(1)));
        PassResponses(game);
        var search = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("faction-search-pick", search.Data["action"]);
        Assert.Contains(eligible.InstanceId, search.ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0315")]
    public void IvarDecliningDoesNotCreateAnEmptyResponseStack()
    {
        var game = Create(8203);
        var source = Card("S01-0315", "batch6kb-ivar-none");
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[0].Library.Add(Card("S01-0301", "batch6kb-ivar-none-top"));

        QueueTrigger(game, source, "enter");
        Resolve(game, "mode:none");

        Assert.Empty(game.State.EffectStack);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
    }

    [Theory]
    [InlineData("S01-0216", "canopic-box-search", "canopic-box-heal-discard")]
    [InlineData("S01-0218", "canopic-two-free", "canopic-two-discard")]
    [InlineData("S01-0219", "canopic-three-morale", "canopic-three-discard")]
    [Trait("L12Evidence", "entry:batch6kb-canopic-independent-followup")]
    public void CanopicDeterministicFollowupsRemainIndependentAfterTheFirstSegmentIsNegated(
        string cardId, string firstFlow, string secondFlow)
    {
        var game = Create(8204 + cardId[^1]);
        var player = game.State.Players[0];
        var source = Card(cardId, $"batch6kb-{cardId}");
        player.ExtraRelics.Add(source);
        player.Hp = 5;

        QueueTrigger(game, source, "enter");

        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal(firstFlow, first.Data["atomicFlow"]);
        first.Negated = true;
        var second = PassUntilFlow(game, secondFlow);
        Assert.Contains(source, player.ExtraRelics);
        PassResponses(game);
        Assert.Contains(source, player.Graveyard);
        if (cardId == "S01-0216") Assert.Equal(6, player.Hp);
    }

    [Theory]
    [InlineData("S01-02D1", "sunTopThree", "sun-top-three-search", "sun-top-three-recover")]
    [InlineData("S01-03D1", "valhallaRecover", "valhalla-mill", "valhalla-recover")]
    [Trait("L12Evidence", "entry:batch6kb-divinity-independent-followup")]
    public void DivinityPublicRecoveryTargetSurvivesTheFirstSegmentBeingNegated(
        string sourceCardId, string ability, string firstFlow, string secondFlow)
    {
        var game = Create(8210 + sourceCardId[^2]);
        var player = game.State.Players[0];
        var source = Card(sourceCardId, $"batch6kb-source-{sourceCardId}");
        var recover = Card(sourceCardId.StartsWith("S01-02", StringComparison.Ordinal) ? "S01-0201" : "S01-0301",
            $"batch6kb-recover-{sourceCardId}");
        player.Relic = source;
        player.Graveyard.Add(recover);
        player.Library.AddRange([
            Card("S01-0001", $"batch6kb-library-a-{sourceCardId}"),
            Card("S01-0002", $"batch6kb-library-b-{sourceCardId}"),
            Card("S01-0003", $"batch6kb-library-c-{sourceCardId}"),
        ]);
        AddMorale(player, 2);

        var activated = game.Handle(0, new L12Command("activateAbility", source.InstanceId, Ability: ability));
        Assert.True(activated.Accepted, activated.Error);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Contains(recover.InstanceId, declaration.ValidChoices);
        Resolve(game, recover.InstanceId);

        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal(firstFlow, first.Data["atomicFlow"]);
        first.Negated = true;
        var second = PassUntilFlow(game, secondFlow);
        Assert.Contains(recover, player.Graveyard);
        PassResponses(game);
        Assert.Contains(recover, player.Hand);
    }

    [Theory]
    [InlineData("S01-02D1", "sunTopThree")]
    [InlineData("S01-03D1", "valhallaRecover")]
    [Trait("L12Evidence", "entry:batch6m-effective-faction-recovery-declaration")]
    public void DivinityRecoveryDeclarationUsesRingModifiedEffectiveFaction(
        string sourceCardId, string ability)
    {
        var game = Create(8218 + sourceCardId[^2], sourceCardId == "S01-02D1" ? 2 : 3);
        var player = game.State.Players[0];
        var source = Card(sourceCardId, $"batch6m-source-{sourceCardId}");
        var ring = Card("S02-0008", $"batch6m-ring-{sourceCardId}");
        var universal = Card("S01-0001", $"batch6m-universal-{sourceCardId}");
        player.Relic = source;
        player.ExtraRelics.Add(ring);
        player.Graveyard.Add(universal);
        player.Library.AddRange([
            Card("S01-0002", $"batch6m-library-a-{sourceCardId}"),
            Card("S01-0003", $"batch6m-library-b-{sourceCardId}"),
            Card("S01-0004", $"batch6m-library-c-{sourceCardId}"),
        ]);
        AddMorale(player, 2);

        var activated = game.Handle(0, new L12Command("activateAbility", source.InstanceId, Ability: ability));

        Assert.True(activated.Accepted, activated.Error);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Contains(universal.InstanceId, declaration.ValidChoices);
    }
}
