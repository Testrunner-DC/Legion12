using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class TrialLegionIdentityRegressionTests
{
    private static readonly string[] TrialLegionIds =
    [
        "S02-0604", "S02-0606", "S02-0609", "S02-0610", "S02-0613", "S02-0614", "S02-0617", "S02-0618",
        "ST06-06", "ST06-07", "ST06-08",
    ];

    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
        => new(Catalog, "trial-legion-identity", "TRIAL-ID", seed, ["甲", "乙"], [0, 0], skipPreparation: true);

    private static L12CardInstance Card(string cardId, string instanceId,
        int? trialValue = null, string? cardType = null)
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
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            TrialValue = trialValue ?? definition.TrialValue ?? 0,
        };
    }

    private static (L12PlayerState Player, L12CardInstance Trial) PrepareCrusade(
        L12GameEngine game, string targetCardId, bool holdResponse = false)
    {
        var player = game.State.Players[0];
        var trial = Card("S02-06S6", $"crusade-source-{targetCardId}");
        trial.TrialCompleted = true;
        player.SpecialZones.Trials.Clear();
        player.SpecialZones.Trials.Add(trial);
        player.SpecialZones.Runes = 1;
        player.Field[0][0] = Card(targetCardId, $"trial-target-{targetCardId}");
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        if (holdResponse)
        {
            var opponent = game.State.Players[1];
            var response = Card("S01-0019", $"response-{targetCardId}");
            response.Hidden = true;
            response.SetRound = 0;
            opponent.Field[1][2] = response;
            opponent.Field[0][2] = Card("S01-0004", $"response-target-{targetCardId}");
        }
        return (player, trial);
    }

    private static void PassResponses(L12GameEngine game)
    {
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var prompt = game.State.PendingPrompts[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
    }

    public static TheoryData<string> TrialLegions => new(TrialLegionIds);

    [Theory]
    [MemberData(nameof(TrialLegions))]
    public void StructuredTrialLegionIdentityCoversTheWholeCurrentPool(string cardId)
    {
        Assert.True(L12StructuredCardRules.IsTrialLegion(Card(cardId, $"identity-{cardId}")));
    }

    [Fact]
    public void TrialLegionIdentityDoesNotTreatTrialValueAsFactionTraitOrProfession()
    {
        var ordinary = Card("S02-0601", "ordinary-round-table-knight");
        Assert.False(L12StructuredCardRules.IsTrialLegion(ordinary));

        var nonLegion = Card("S02-06S6", "completed-trial-card", trialValue: 2);
        Assert.False(L12StructuredCardRules.IsTrialLegion(nonLegion));
    }

    [Fact]
    public void NonLegionWithTrialValueCannotInvokeTheUsualTrialAction()
    {
        var game = Create(69100);
        var player = game.State.Players[0];
        var trialCard = Card("S02-06S6", "not-a-trial-legion", trialValue: 2);
        player.SpecialZones.Trials.Clear();
        player.SpecialZones.Trials.Add(trialCard);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        var result = game.Handle(0, new L12Command("activateAbility", trialCard.InstanceId,
            Ability: "trialAdvance"));

        Assert.False(result.Accepted);
        Assert.False(trialCard.Tapped);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "trial-action");
    }

    [Fact]
    public void RestedAmakineProtectsAStarterTrialLegionOnlyWhileItIsActive()
    {
        var game = Create(69101);
        var attackerPlayer = game.State.ActivePlayer;
        var defenderPlayer = 1 - attackerPlayer;
        var attacker = Card("S02-0004", "trial-identity-attacker");
        attacker.SummonRound = 0;
        game.State.Players[attackerPlayer].Field[0][0] = attacker;
        var amakine = Card("S02-0616", "trial-identity-amakine");
        amakine.Tapped = true;
        var starterTrial = Card("ST06-06", "starter-trial-legion");
        game.State.Players[defenderPlayer].Field[0][0] = starterTrial;
        game.State.Players[defenderPlayer].Field[1][0] = amakine;
        game.State.Phase = L12Phase.Main;

        var protectedAttack = game.Handle(attackerPlayer, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", starterTrial.InstanceId)));
        Assert.False(protectedAttack.Accepted);
        Assert.Contains("阿麦金", protectedAttack.Error);

        starterTrial.Tapped = true;
        var restedTargetAttack = game.Handle(attackerPlayer, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", starterTrial.InstanceId)));
        Assert.True(restedTargetAttack.Accepted, restedTargetAttack.Error);
    }

    [Theory]
    [MemberData(nameof(TrialLegions))]
    public void CrusadeCandidateGenerationUsesStructuredTrialLegionIdentity(string cardId)
    {
        var game = Create(69102);
        var (player, trial) = PrepareCrusade(game, cardId);
        var target = player.Field[0][0]!;

        Assert.True(game.Handle(0, new L12Command("activateAbility", trial.InstanceId,
            Ability: "crusadeTrialNoLoss")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(target.InstanceId, prompt.ValidChoices);
    }

    [Fact]
    public void CrusadeCommitRevalidatesTrialIdentityBeforePaying()
    {
        var game = Create(69103);
        var (player, trial) = PrepareCrusade(game, "ST06-07");
        var target = player.Field[0][0]!;
        Assert.True(game.Handle(0, new L12Command("activateAbility", trial.InstanceId,
            Ability: "crusadeTrialNoLoss")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);

        player.Field[0][0] = Card("S02-0601", target.InstanceId);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: target.InstanceId)).Accepted);

        Assert.Equal(1, player.SpecialZones.Runes);
        Assert.Empty(game.State.EffectStack);
        Assert.Contains(game.State.Events, entry => entry.Type == "ability-rejected");
    }

    [Fact]
    public void CrusadeSettlementRevalidatesTrialIdentityAndKeepsThePaidCost()
    {
        var game = Create(69104);
        var (player, trial) = PrepareCrusade(game, "ST06-08", holdResponse: true);
        var target = player.Field[0][0]!;
        Assert.True(game.Handle(0, new L12Command("activateAbility", trial.InstanceId,
            Ability: "crusadeTrialNoLoss")).Accepted);
        var selection = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: selection.PromptId,
            Choice: target.InstanceId)).Accepted);
        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Single(game.State.EffectStack);

        var replacement = Card("S02-0601", target.InstanceId);
        player.Field[0][0] = replacement;
        PassResponses(game);

        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Equal(0, replacement.NextAttackNoLossUses);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("不再是【试炼军团】", StringComparison.Ordinal));
    }

    [Fact]
    public void CrusadeStructuredTargetSurvivesV2RecoveryAndSettlesExactlyOnce()
    {
        var game = Create(69105);
        var (player, trial) = PrepareCrusade(game, "ST06-06", holdResponse: true);
        var target = player.Field[0][0]!;
        Assert.True(game.Handle(0, new L12Command("activateAbility", trial.InstanceId,
            Ability: "crusadeTrialNoLoss")).Accepted);
        var selection = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: selection.PromptId,
            Choice: target.InstanceId)).Accepted);
        var oldResponse = Assert.Single(game.State.PendingPrompts);
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);

        game = L12GameEngine.RestoreCheckpoint(Catalog,
            game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,"), random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        player = game.State.Players[0];
        target = player.Field[0][0]!;
        PassResponses(game);

        Assert.Equal(1, target.NextAttackNoLossUses);
        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.False(game.Handle(oldResponse.PlayerIndex, new L12Command("resolvePrompt",
            PromptId: oldResponse.PromptId, Choice: "pass")).Accepted);
    }
}
