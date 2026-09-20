using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class Bug20260920UnifiedRulesTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, string? firstMaster = null,
        bool autoPassEmptyResponses = true)
    {
        var first = Catalog.DeckAt(0);
        if (firstMaster is not null)
            first = new L12PresetDeckDefinition
            {
                Name = $"{firstMaster}统一规则测试",
                MasterId = firstMaster,
                CardIds = [.. first.CardIds],
                MoraleIds = [.. first.MoraleIds],
                SpecialIds = [],
            };
        var game = new L12GameEngine(Catalog, "bug-20260920-unified", "BUG-20260920", seed,
            ["甲", "乙"], [first, Catalog.DeckAt(0)], skipPreparation: true,
            autoPassEmptyResponses: autoPassEmptyResponses,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
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
            player.Morale.Clear();
            player.SpecialZones.Trials.Clear();
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
            IsCounterTactic = definition.IsCounterTactic,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 1000,
            Troops = definition.Troops ?? 1000,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }

    private static object? Invoke(L12GameEngine game, string method, params object?[] args)
        => typeof(L12GameEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(game, args);

    private static void QueueTrigger(L12GameEngine game, int controller, L12CardInstance source,
        string trigger, Dictionary<string, string>? data = null)
    {
        var candidate = Assert.IsType<L12TriggerCandidate>(Invoke(game, "CreateTriggerCandidate",
            controller, source, trigger, $"{source.Name}统一规则测试", data, source));
        Invoke(game, "QueueTriggerCandidates", (object)new[] { candidate });
    }

    private static void Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game, int maximum = 24)
    {
        var count = 0;
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response" && count++ < maximum)
            Resolve(game, "pass");
        Assert.True(count < maximum, "响应窗口未收束");
    }

    [Fact]
    [Trait("L12Bug", "BUG-20260920-3ee0c83e")]
    public void AmbushCanRespondToPrintedTrialTriggerWhileDisastersRemainUnresponsive()
    {
        var game = Create(202609201, autoPassEmptyResponses: false);
        var ambush = Card("S01-0019", "trial-trigger-ambush");
        ambush.Hidden = true;
        game.State.Players[1].Field[1][0] = ambush;
        game.State.Players[1].Field[0][0] = Card("S01-0001", "ambush-own-target");
        var trial = Card("S02-06S3", "printed-trial-trigger");
        trial.TrialCompleted = true;

        Invoke(game, "PushEffect", 0, trial, "trial-complete", "试炼翻面后的印刷触发效果", null, null);
        Resolve(game, "pass");

        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(1, response.PlayerIndex);
        Assert.Contains(ambush.InstanceId, response.ValidChoices);
    }

    [Fact]
    [Trait("L12Bug", "BUG-20260920-85104a12")]
    public void WildCampUsesEffectiveFactionGrantedByWorldRing()
    {
        var game = Create(202609202);
        var player = game.State.Players[0];
        var camp = Card("S01-0007", "ring-wild-camp");
        var universal = Card("S02-0003", "ring-universal-legion");
        player.Relic = Card("S02-0008", "world-ring");
        player.Hand.Add(camp);
        player.Library.Add(universal);
        for (var index = 0; index < camp.Cost; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = player.Faction == "tianting" ? "S01-01C1" : Catalog.MoraleIdentities.ForFaction(player.Faction).CanonicalCardId,
                InstanceId = $"camp-cost-{index}",
            });

        var play = game.Handle(0, new L12Command("playCard", camp.InstanceId));

        Assert.True(play.Accepted, play.Error);
        var search = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("camp-pick", search.Data["action"]);
        Assert.Contains(universal.InstanceId, search.ValidChoices);
    }

    [Fact]
    [Trait("L12Bug", "BUG-20260920-a12ee0a1")]
    public void MerlinSearchTreatsNoPrintedCostNaturalGiftAsZeroWithoutMakingItPlayable()
    {
        var game = Create(202609203, "S02-06M1");
        var player = game.State.Players[0];
        var merlin = Card("S02-0603", "merlin-source");
        var gift = Card("ST06-10", "natural-gift");
        player.Field[0][0] = merlin;
        player.Library.Add(gift);
        player.SpecialZones.Runes = 1;

        var begin = game.Handle(0, new L12Command("activateAbility", merlin.InstanceId, Ability: "merlinRune"));
        Assert.True(begin.Accepted, begin.Error);
        Resolve(game, "mode:search");
        PassResponses(game);

        var search = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-merlin-search", search.Data["action"]);
        Assert.Contains(gift.InstanceId, search.ValidChoices);
        Assert.False(gift.HasPrintedCost);
    }

    [Fact]
    [Trait("L12Bug", "BUG-20260920-1f52fdcd")]
    public void TsukuyomiSkipsImpossibleMoveButStillDiscountsTheDeclaredLegion()
    {
        var game = Create(202609204, "S02-04M1");
        var player = game.State.Players[0];
        var master = Card("S02-04M1", "master-0");
        var moved = Card("S02-0401", "already-moved");
        var target = Card("S02-0402", "boxed-target");
        player.Field[0][1] = target;
        player.Field[0][0] = Card("S02-0403", "box-left");
        player.Field[0][2] = Card("S02-0404", "box-right");
        player.Field[1][1] = Card("S02-0405", "box-below");
        player.Morale.Add(new L12MoraleCard { CardId = "S01-04C1", InstanceId = "tsukuyomi-cost" });
        var before = target.CurrentCost;

        QueueTrigger(game, 0, master, "friendly-legion-moves",
            new Dictionary<string, string> { ["ability"] = "tsukuyomiFollowMove", ["moved"] = moved.InstanceId });
        Resolve(game, "mode:use");
        Resolve(game, target.InstanceId);
        Assert.True(player.Morale.Single().Tapped);
        PassResponses(game);

        Assert.Same(target, player.Field[0][1]);
        Assert.Equal(before - 1, target.CurrentCost);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-noop"
            && entry.Text.Contains("跳过位移", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Bug", "BUG-20260920-e9d41475")]
    [Trait("L12Bug", "BUG-20260920-5301827f")]
    public void KingsSwordIsOneDerivedTokenAndCannotEnterTheMainDeck()
    {
        var sword = Catalog.Cards["S02-06S2"];
        Assert.Equal("token", sword.CardType);
        Assert.Equal(1, sword.DeckLimit);

        var preset = Catalog.PresetDecks.First(deck => Catalog.Cards[deck.MasterId].Faction == "otherworld");
        var cards = preset.CardIds.ToList();
        cards[0] = sword.Id;
        var submission = new L12CustomDeckSubmission
        {
            Name = "王者之剑不得进入主牌库",
            MasterId = preset.MasterId,
            CardIds = cards,
            MoraleIds = preset.MoraleIds.ToList(),
            SpecialIds = preset.SpecialIds.ToList(),
        };

        Assert.False(L12DeckValidator.TryValidate(Catalog, submission, out _, out var error));
        Assert.Contains("不能放入主牌库", error);
    }

    [Fact]
    [Trait("L12Bug", "BUG-20260920-a33c0f0d")]
    public void FinalDisasterDamageDoesNotCreateAnderstorpDrawTrigger()
    {
        var game = Create(202609205);
        var player = game.State.Players[0];
        player.Relic = Card("S02-0305", "anderstorp-ring");
        player.Library.Add(Card("S01-0001", "would-be-draw"));
        game.State.ActiveDisaster = Card("S01-DS10", "final-disaster");
        var before = player.Hp;

        Invoke(game, "ResolveTurnStartDisasterEffectIfNeeded");

        Assert.Equal(before - 1, player.Hp);
        Assert.Empty(game.State.PendingActivations);
        Assert.DoesNotContain(game.State.PendingTriggerStackCandidates,
            candidate => candidate.Data.GetValueOrDefault("ability") == "anderstorpRingDraw");
        Assert.DoesNotContain(game.State.EffectStack,
            item => item.Data.GetValueOrDefault("ability") == "anderstorpRingDraw");
    }

    [Fact]
    [Trait("L12Bug", "BUG-20260920-756391d0")]
    public void TrialAdvanceSkipsAMaxedUnflippedTrialAndContinuesToTheNextOne()
    {
        var game = Create(202609206);
        var player = game.State.Players[0];
        var blocked = Card("S02-06S3", "maxed-unflipped");
        blocked.TrialProgress = 8;
        var next = Card("S02-06S4", "next-trial");
        next.TrialProgress = 3;
        player.SpecialZones.Trials.AddRange([blocked, next]);

        var advanced = Assert.IsType<bool>(Invoke(game, "AdvanceTrial", 0, 1, null));

        Assert.True(advanced);
        Assert.Equal(8, blocked.TrialProgress);
        Assert.Equal(4, next.TrialProgress);
        Assert.Equal(4, player.SpecialZones.TrialLevel);
    }

    [Fact]
    [Trait("L12Bug", "BUG-20260920-c20824b3")]
    public void AlternateArtVersionsOfTheSameMoraleIdentityAutoPayAsOneChoiceKind()
    {
        var game = Create(202609207);
        var player = game.State.Players[0];
        var identity = Catalog.MoraleIdentities.ForFaction(player.Faction);
        Assert.True(identity.VersionCardIds.Count >= 2);
        player.Morale.Add(new L12MoraleCard
        {
            CardId = identity.VersionCardIds[0], InstanceId = "same-morale-version-a",
        });
        player.Morale.Add(new L12MoraleCard
        {
            CardId = identity.VersionCardIds[1], InstanceId = "same-morale-version-b",
        });

        var manual = Assert.IsType<bool>(Invoke(game, "NeedsManualOrdinaryResourcePayment",
            player, 1, null, 0));

        Assert.False(manual);
    }
}
