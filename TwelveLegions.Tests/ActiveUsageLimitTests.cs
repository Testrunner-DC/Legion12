using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ActiveUsageLimitTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static L12GameEngine Create(string masterId = "S02-03M1")
    {
        var basis = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition { Name = "次数边界", MasterId = masterId,
            CardIds = [.. basis.CardIds], MoraleIds = [.. basis.MoraleIds], SpecialIds = [] };
        var game = new L12GameEngine(Catalog, "usage-limit", "USAGE", 91751, ["甲", "乙"], [deck, basis],
            skipPreparation: true, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0; game.State.Round = 3; game.State.TurnSerial = 5; game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.Hand.Clear(); player.Morale.Clear(); player.UsedAbilities.Clear();
        }
        game.State.Players[0].Hp = 3;
        for (var index = 0; index < 4; index++)
            game.State.Players[0].Morale.Add(new() { InstanceId = $"morale-{index}", CardId = "S01-03C1" });
        return game;
    }

    private static IEnumerable<(string CardId, string Ability, string Text, string CardText)> Entries(L12GameEngine game)
    {
        var getter = typeof(L12GameEngine).GetMethod("GetAbilities", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var card in Catalog.Cards.Values.OrderBy(card => card.Id))
        foreach (var view in ((IEnumerable<L12AbilityView>)getter.Invoke(game, [card.Id])!).Where(view => !view.TriggerOnly))
            yield return (card.Id, view.Id, view.Label, card.Effect ?? "");
    }

    private static string UsageKey(string cardId, string ability) => (string)typeof(L12GameEngine)
        .GetMethod("ActiveAbilityUsageKey", PrivateStatic)!.Invoke(null, ["source", cardId, ability])!;
    private static bool IsBlocked(L12GameEngine game, L12PlayerState player, string cardId, string ability) => (bool)typeof(L12GameEngine)
        .GetMethod("HasUsedLimitedActiveAbility", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(game, [player, cardId, "source", ability])!;

    [Fact]
    public void CardsWithoutPrintedUsageLimitsDoNotInheritDefaultOrLegacyOnceKeys()
    {
        var game = Create();
        var entries = Entries(game).ToArray();
        if (Environment.GetEnvironmentVariable("L12_ACTIVE_USAGE_AUDIT") is { Length: > 0 } output)
            File.WriteAllText(output, JsonSerializer.Serialize(entries.Select(entry => new
            {
                entry.CardId, entry.Ability, entry.Text, entry.CardText,
                RestCost = L12StructuredCardRules.IsActiveRestAbility(entry.CardId, entry.Ability),
            }), new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        var incorrectlyBlocked = new List<string>();
        foreach (var entry in entries.Where(entry => !Regex.IsMatch(entry.CardText, @"回合\s*[1一]\s*次|每回合|本局.*次")))
        {
            var player = game.State.Players[0];
            player.UsedAbilities.Add(UsageKey(entry.CardId, entry.Ability));
            if (IsBlocked(game, player, entry.CardId, entry.Ability)) incorrectlyBlocked.Add($"{entry.CardId}|{entry.Ability}");
            player.UsedAbilities.Clear();
        }
        Assert.True(incorrectlyBlocked.Count == 0, "未印刷次数限制却受到通用一次键限制：" + string.Join(", ", incorrectlyBlocked));
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var guard = 0; guard < 20 && game.State.PendingPrompts.FirstOrDefault() is { } prompt; guard++)
        {
            Assert.Equal("response", prompt.Kind);
            Assert.True(game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
        Assert.Empty(game.State.PendingPrompts); Assert.Empty(game.State.EffectStack);
    }

    // Reviewed per-effect limits, including canonical morale aliases and branch groups.
    // This list is intentionally independent of the production registry.
    [Theory]
    [InlineData("S01-0003", "extendedRange", false)]
    [InlineData("S01-0004", "destroyInfiltrator", false)]
    [InlineData("S01-0105", "searchBrothers", false)]
    [InlineData("S01-0109", "addMorale", false)]
    [InlineData("S01-0113", "extendedRange", false)]
    [InlineData("S01-0116", "xishiExchange", false)]
    [InlineData("S01-0117", "artifactDraw", false)]
    [InlineData("S01-0117", "artifactSearch", false)]
    [InlineData("S01-01C1", "factionAddActive", true)]
    [InlineData("S01-01C1", "factionZeroRecovery", true)]
    [InlineData("S01-01D1", "palaceReward", true)]
    [InlineData("S01-01D1", "palaceExchange", false)]
    [InlineData("S01-01M1", "drawCycle", true)]
    [InlineData("S01-01M1", "nonLethal", true)]
    [InlineData("S01-01M2", "mengpoSilence", true)]
    [InlineData("S01-01M2", "mengpoMorale", true)]
    [InlineData("S01-0214", "cleopatraGuard", false)]
    [InlineData("S01-0215", "ankhReady", false)]
    [InlineData("S01-0215", "ankhDraw", false)]
    [InlineData("S01-02C1", "sunGuard", true)]
    [InlineData("S01-02C1", "sunDraw", true)]
    [InlineData("S01-02D1", "sunTopThree", true)]
    [InlineData("S01-02D1", "sunBottomEnemy", true)]
    [InlineData("S01-02M1", "isisCanopic", false)]
    [InlineData("S01-02M1", "isisVictory", false)]
    [InlineData("S01-02M2", "isisVictory", false)]
    [InlineData("S01-02M3", "medjedDebuff", true)]
    [InlineData("S01-0307", "alvidaSummon", false)]
    [InlineData("S01-0314", "olgaDebuff", false)]
    [InlineData("S01-0317", "gramDamage", false)]
    [InlineData("S01-0317", "gramReady", false)]
    [InlineData("S01-03C1", "asgardDraw", true)]
    [InlineData("S01-03D1", "valhallaDiscount", true)]
    [InlineData("S01-03D1", "valhallaRecover", true)]
    [InlineData("S01-03D1", "valhallaKill", false)]
    [InlineData("S01-03M1", "valkyrieRecover", true)]
    [InlineData("S01-03M2", "lokiCycle", true)]
    [InlineData("S01-03M2", "lokiHeal", true)]
    [InlineData("S01-0415", "revealHidden", false)]
    [InlineData("S01-0417", "kusanagiDebuff", true)]
    [InlineData("S01-0417", "kusanagiStrong", true)]
    [InlineData("S01-04C1", "factionDrawMove", true)]
    [InlineData("S01-04D1", "yomiDiscount", true)]
    [InlineData("S01-04D1", "yomiSweep", true)]
    [InlineData("S01-04D1", "yomiRecover", false)]
    [InlineData("S01-04M1", "amaterasuKill", true)]
    [InlineData("S01-04M1", "amaterasuReady", true)]
    [InlineData("S01-04M2", "frontBuff", true)]
    [InlineData("S01-04M2", "kusanagi", false)]
    [InlineData("S02-0003", "disableCounters", false)]
    [InlineData("S02-0104", "shennongReset", false)]
    [InlineData("S02-01M1", "wukongTransform", true)]
    [InlineData("S02-0204", "imhotepDiscount", false)]
    [InlineData("S02-0205", "scarabSummon", false)]
    [InlineData("S02-0205", "scarabDebuff", true)]
    [InlineData("S02-02M1", "nephthysSacrifice", true)]
    [InlineData("S02-0301", "thorHammerRevive", true)]
    [InlineData("S02-03M1", "thorCharge", false)]
    [InlineData("S02-0404", "magatamaMove", false)]
    [InlineData("S02-0404", "magatamaImmortal", false)]
    [InlineData("S02-0510", "hippolytaRevive", false)]
    [InlineData("S02-0513", "aristotleDiscount", false)]
    [InlineData("S02-0520", "forgePromotionDiscount", false)]
    [InlineData("S02-0520", "forgeReadyOnKill", false)]
    [InlineData("S02-05C1", "olympusMoraleFlip", true)]
    [InlineData("S02-05C1", "godPowerDraw", true)]
    [InlineData("S02-05C1A", "olympusMoraleFlip", true)]
    [InlineData("S02-05C1A", "godPowerDraw", true)]
    [InlineData("S02-05D1", "divinityFlipMorale", true)]
    [InlineData("S02-05D1", "divinityPower", true)]
    [InlineData("S02-05D1", "divinityFreePromotion", false)]
    [InlineData("S02-05M1", "artemisBuff", true)]
    [InlineData("S02-05M2", "prometheusTopThree", true)]
    [InlineData("S02-0603", "merlinRune", false)]
    [InlineData("S02-0604", "galahadGrailReward", false)]
    [InlineData("S02-0616", "amakineTop", false)]
    [InlineData("S02-06C1", "factionGainRune", true)]
    [InlineData("S02-06C1", "runeUse", true)]
    [InlineData("S02-06D1", "avalonRecover", true)]
    [InlineData("S02-06D1", "avalonDebuff", false)]
    [InlineData("S02-06M1", "morriganReadyOnKill", true)]
    [InlineData("S02-06S3", "completeTrial", false)]
    [InlineData("S02-06S4", "completeTrial", false)]
    [InlineData("S02-06S5", "completeTrial", false)]
    [InlineData("S02-06S5", "fenianReady", true)]
    [InlineData("S02-06S6", "completeTrial", false)]
    [InlineData("S02-06S6", "crusadeTrialNoLoss", true)]
    [InlineData("S02-06S6", "crusadeRichardPiercing", true)]
    [InlineData("S02-06S6", "crusadeRecover", true)]
    [InlineData("ST01-C1", "factionAddActive", true)]
    [InlineData("ST01-C1", "factionZeroRecovery", true)]
    [InlineData("ST02-05", "oasisDancerBuff", false)]
    [InlineData("ST02-C1", "sunGuard", true)]
    [InlineData("ST02-C1", "sunDraw", true)]
    [InlineData("ST02-M1", "horusRevive", true)]
    [InlineData("ST03-05", "christinaFreeTactic", false)]
    [InlineData("ST03-07", "kaneMillOne", false)]
    [InlineData("ST03-C1", "asgardDraw", true)]
    [InlineData("ST03-M1", "sifCycle", true)]
    [InlineData("ST04-06", "oiranTransfer", false)]
    [InlineData("ST04-C1", "factionDrawMove", true)]
    [InlineData("ST05-06", "telemachusTopThree", false)]
    [InlineData("ST05-C1", "olympusMoraleFlip", true)]
    [InlineData("ST05-C1", "godPowerDraw", true)]
    [InlineData("ST05-M1", "athenaFrontBuff", true)]
    [InlineData("ST06-09", "lightSwordActive", false)]
    [InlineData("ST06-C1", "factionGainRune", true)]
    [InlineData("ST06-C1", "runeUse", true)]
    [InlineData("ST06-M1", "nuadaReadyMorale", true)]
    [InlineData("ST06-S1", "completeTrial", false)]
    [InlineData("ST06-S1", "skyCityDiscount", true)]
    public void EveryRegisteredEntryUsesOnlyItsExplicitLimitForReadsAndWrites(string cardId, string ability, bool limited)
    {
        var game = Create();
        var player = game.State.Players[0];
        var key = UsageKey(cardId, ability);
        player.UsedAbilities.Add(key);
        Assert.Equal(limited, IsBlocked(game, player, cardId, ability));
        player.UsedAbilities.Clear();
        var definition = Catalog.Cards[cardId];
        var source = new L12CardInstance { InstanceId = "source", CardId = cardId, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction };
        typeof(L12GameEngine).GetMethod("RecordLimitedActiveAbilityUse", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(game, [player, source, ability]);
        if (limited) Assert.Equal(key, Assert.Single(player.UsedAbilities));
        else Assert.Empty(player.UsedAbilities);
        if (limited) Assert.Matches(@"回合\s*[1一]\s*次", definition.Effect!);
    }

    [Fact]
    public void ExplicitUsageMatrixCoversEveryRegisteredEntryAndDoesNotGuessFutureCards()
    {
        var expected = typeof(ActiveUsageLimitTests).GetMethod(nameof(EveryRegisteredEntryUsesOnlyItsExplicitLimitForReadsAndWrites))!
            .GetCustomAttributes<InlineDataAttribute>().SelectMany(attribute =>
                attribute.GetData(typeof(ActiveUsageLimitTests).GetMethod(nameof(EveryRegisteredEntryUsesOnlyItsExplicitLimitForReadsAndWrites))!))
            .Select(args => $"{args[0]}|{args[1]}").Order().ToArray();
        Assert.Equal(111, expected.Length);
        Assert.Equal(expected, Entries(Create()).Select(entry => $"{entry.CardId}|{entry.Ability}").Order());
        Assert.Null(L12ActiveUsageRules.Find("UNREGISTERED", "thorCharge"));
        Assert.Null(L12ActiveUsageRules.Find("S01-04M2", "kusanagi")); // A sibling has a cap; this effect does not.
        Assert.NotNull(L12ActiveUsageRules.Find("S01-04M2", "frontBuff"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThorCanPayAgainAfterResolutionOrNegationAndV2Recovery(bool negateFirst)
    {
        var game = Create();
        var activate = new L12Command("activateAbility", "master-0", Ability: "thorCharge");
        Assert.True(game.Handle(0, activate).Accepted);
        Assert.Single(game.State.EffectStack).Negated = negateFirst;
        PassResponses(game);
        Assert.Equal(!negateFirst, game.State.Players[0].MasterCannotHeal);
        Assert.Equal(2, game.State.Players[0].Morale.Count(card => card.Tapped));
        Assert.DoesNotContain("active:master-0:thorCharge", game.State.Players[0].UsedAbilities);
        game.State.Players[0].UsedAbilities.Add("active:master-0:thorCharge"); // Legacy erroneous marker.
        game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        var second = game.Handle(0, activate);
        Assert.True(second.Accepted, second.Error);
        PassResponses(game);
        Assert.All(game.State.Players[0].Morale, card => Assert.True(card.Tapped));
        Assert.True(game.State.Players[0].MasterCannotHeal);
        Assert.Equal(2, game.State.Events.Count(entry => entry.Type == "effect-result" && entry.Cards.Any(card => card.CardId == "S02-03M1")));
        var before = game.SerializeFullState();
        Assert.False(game.Handle(0, activate).Accepted); // Actual fee, not a fabricated limit.
        Assert.Equal(before, game.SerializeFullState());
    }

    private static L12CardInstance Card(string cardId, string instanceId)
    {
        var card = Catalog.Cards[cardId];
        return new() { InstanceId = instanceId, CardId = cardId, Name = card.NameZh,
            CardType = card.CardType, Faction = card.Faction, OwnerIndex = 0 };
    }

    [Theory]
    [InlineData("S01-01M2", "mengpoSilence,mengpoMorale", "mengpo-choice")]
    [InlineData("S01-03M2", "lokiCycle,lokiHeal", "loki")]
    [InlineData("S01-0417", "kusanagiDebuff,kusanagiStrong", "choice")]
    [InlineData("S02-06S6", "crusadeTrialNoLoss,crusadeRichardPiercing,crusadeRecover", "crusade-choice")]
    public void PrintedBranchChoicesConsumeOneSharedLimit(string cardId, string abilities, string group)
    {
        var game = Create();
        var player = game.State.Players[0];
        player.UsedAbilities.Add($"active:source:{group}");
        foreach (var ability in abilities.Split(','))
        {
            Assert.Equal($"active:source:{group}", UsageKey(cardId, ability));
            Assert.True(IsBlocked(game, player, cardId, ability));
        }
        Assert.False(IsBlocked(game, player, cardId, "unregistered"));
    }

    [Theory]
    [InlineData("S01-01M2", "mengpo-choice", "resolved")]
    [InlineData("S01-01M2", "mengpo-choice", "negated")]
    [InlineData("S01-01M2", "mengpo-choice", "failed")]
    [InlineData("S01-03M2", "loki", "resolved")]
    [InlineData("S01-03M2", "loki", "negated")]
    [InlineData("S01-03M2", "loki", "failed")]
    public void ShennongResetsOneSharedGroupAfterRecoveryWithoutTouchingOtherMarkers(
        string masterId, string group, string outcome)
    {
        var game = Create(masterId);
        var player = game.State.Players[0];
        player.Morale.RemoveRange(1, 3); // No ambiguous resource selection in this fixture.
        player.Relic = Card("S02-0104", "shennong");
        var key = $"active:master-0:{group}";
        player.UsedAbilities.UnionWith([key, "unrelated-state", "trigger:factionZeroRecovery"]);
        var begin = game.Handle(0, new L12Command("activateAbility", "shennong", Ability: "shennongReset"));
        Assert.True(begin.Accepted, begin.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        var choice = Assert.Single(prompt.ValidChoices, option => option != "skip");
        Assert.Contains(" / ", prompt.Data[choice]); // Both branches, one shared counter.
        game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        player = game.State.Players[0];
        var command = new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice);
        Assert.True(game.Handle(0, command).Accepted);
        Assert.False(game.Handle(0, command).Accepted);
        Assert.True(player.Relic!.Tapped);
        Assert.Empty(player.Morale);
        var item = Assert.Single(game.State.EffectStack);
        item.Negated = outcome == "negated";
        if (outcome == "failed") player.UsedAbilities.Remove(key);
        PassResponses(game);
        Assert.Equal(outcome == "negated", player.UsedAbilities.Contains(key));
        Assert.Contains("unrelated-state", player.UsedAbilities);
        Assert.Equal(outcome, Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == "shennong")).EffectResultStatus);
    }

    [Fact]
    public void ShennongDoesNotOfferToResetLegacyUnlimitedUsage()
    {
        var game = Create();
        var player = game.State.Players[0];
        player.Relic = Card("S02-0104", "shennong");
        player.UsedAbilities.Add("active:master-0:thorCharge");
        var before = game.SerializeFullState();
        Assert.False(game.Handle(0, new L12Command("activateAbility", "shennong", Ability: "shennongReset")).Accepted);
        Assert.Equal(before, game.SerializeFullState());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GramMayPayToReadyAgainButMustBeRestedAndPayEachTime(bool negateFirst)
    {
        var game = Create();
        var player = game.State.Players[0];
        player.Relic = Card("S01-0317", "gram");
        player.Relic.Tapped = true;
        var activate = new L12Command("activateAbility", "gram", Ability: "gramReady");
        Assert.True(game.Handle(0, activate).Accepted);
        Assert.Single(game.State.EffectStack).Negated = negateFirst;
        PassResponses(game);
        Assert.Equal(negateFirst, player.Relic.Tapped);
        if (!negateFirst)
        {
            var before = game.SerializeFullState();
            Assert.False(game.Handle(0, activate).Accepted); // Active artifact cannot pay this effect.
            Assert.Equal(before, game.SerializeFullState());
            player.Relic.Tapped = true; // A later legal rest, isolated from unrelated graveyard costs.
        }
        Assert.True(game.Handle(0, activate).Accepted);
        PassResponses(game);
        Assert.False(player.Relic.Tapped);
        Assert.All(player.Morale, card => Assert.True(card.Tapped));
        Assert.DoesNotContain("active:gram:gramReady", player.UsedAbilities);
    }

    [Fact]
    [Trait("L12Evidence", "entry:effect-ready-restriction-gram")]
    public void GramMayPayMoraleButItsReadySegmentFailsWhileBlocked()
    {
        var game = Create();
        var player = game.State.Players[0];
        var gram = Card("S01-0317", "blocked-gram");
        gram.Tapped = true;
        gram.CannotReadyByEffectUntilTurn = game.State.TurnSerial;
        player.Relic = gram;

        var result = game.Handle(0, new L12Command("activateAbility", gram.InstanceId,
            Ability: "gramReady"));

        Assert.True(result.Accepted, result.Error);
        PassResponses(game);
        Assert.Equal(4, player.Morale.Count);
        Assert.Equal(2, player.Morale.Count(morale => morale.Tapped));
        Assert.True(gram.Tapped);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("无法因效果转为活跃", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitLimitedEffectStaysLimitedAfterResolutionOrNegation(bool negateFirst)
    {
        var game = Create("S01-04M2");
        game.State.Players[0].Field[0][0] = Card("S01-0401", "buff-target");
        var activate = new L12Command("activateAbility", "master-0", Ability: "frontBuff");
        var begin = game.Handle(0, activate);
        Assert.True(begin.Accepted, begin.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "buff-target")).Accepted);
        Assert.Single(game.State.EffectStack).Negated = negateFirst;
        PassResponses(game);
        Assert.Contains("active:master-0:frontBuff", game.State.Players[0].UsedAbilities);
        var before = game.SerializeFullState();
        var again = game.Handle(0, activate);
        Assert.False(again.Accepted);
        Assert.Contains("本回合", again.Error);
        Assert.Equal(before, game.SerializeFullState());
    }
}
