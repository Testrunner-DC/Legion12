using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PiercingNormalAttackTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [Trait("L12Evidence", "keyword:piercing")]
    [Trait("L12Evidence", "card:S02-DS02")]
    public void FogDeadEndNegatesPiercingWhenRemainingTroopsAreExactlyTwoThousand()
    {
        var game = Create(81201);
        var attacker = Card("S02-0606", "fog-piercing-attacker", 3000, 0);
        var defeated = Card("S01-0102", "fog-piercing-defeated", 1000, 1);
        defeated.Tapped = true;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = defeated;
        game.State.ActiveDisaster = Card("S02-DS02", "fog-dead-end", ownerIndex: -1);

        StartAttack(game, attacker, defeated);
        AdvanceUntil(game, () => game.State.Events.Any(entry => entry.Type == "effect-failed"
            && entry.Text.Contains("贯穿进攻失败", StringComparison.Ordinal)
            && entry.Text.Contains("迷雾绝境", StringComparison.Ordinal)));

        Assert.Equal(2000, attacker.Troops);
        Assert.Equal(1, attacker.AttacksThisTurn);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "piercing");
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("兵力不高于2000", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "keyword:piercing")]
    [Trait("L12Evidence", "card:S02-DS02")]
    public void FogDeadEndAllowsPiercingWhenRemainingTroopsAreAboveTwoThousand()
    {
        var game = Create(81203);
        var attacker = Card("S02-0606", "fog-above-threshold-attacker", 3001, 0);
        var defeated = Card("S01-0102", "fog-above-threshold-defeated", 1000, 1);
        defeated.Tapped = true;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = defeated;
        game.State.ActiveDisaster = Card("S02-DS02", "fog-above-threshold", ownerIndex: -1);

        StartAttack(game, attacker, defeated);
        AdvanceUntil(game, () => game.State.PendingDefense?.Target.Type == "master"
            && game.State.PendingPrompts.Any(prompt => prompt.Kind == "response"));

        var pending = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.Equal(2001, attacker.Troops);
        Assert.Equal("master", pending.Target.Type);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("迷雾绝境", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "keyword:piercing")]
    [Trait("L12Evidence", "card:S02-DS05")]
    public void WrathAllowsPiercingWhenTheOnlyRemainingEnemyLegionIsOutOfRange()
    {
        var game = Create(81206);
        var attacker = Card("S02-0606", "wrath-out-of-range-attacker", 5000, 0);
        var defeated = Card("S01-0102", "wrath-out-of-range-defeated", 1000, 1);
        var unreachable = Card("S01-0103", "wrath-out-of-range-remaining", 2000, 1);
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = defeated;
        game.State.Players[1].Field[1][0] = unreachable;
        game.State.ActiveDisaster = Card("S02-DS05", "wrath-out-of-range", ownerIndex: -1);

        StartAttack(game, attacker, defeated);
        AdvanceUntil(game, () => game.State.PendingDefense?.Target.Type == "master"
            && game.State.PendingPrompts.Any(prompt => prompt.Kind == "response"));

        var pending = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.Equal("master", pending.Target.Type);
        Assert.Contains(unreachable, game.State.Players[1].Field[1]);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("暴怒之罪", StringComparison.Ordinal));
        Assert.Single(game.State.Events, entry => entry.Type == "piercing");
    }

    [Fact]
    [Trait("L12Evidence", "keyword:piercing")]
    [Trait("L12Evidence", "card:S01-DS02")]
    public void NightParadePiercingSelectsMasterBeforeResponseAddsDamageAndSurvivesRestore()
    {
        var game = Create(81202);
        var attacker = Card("S02-0611", "night-parade-piercing-attacker", 5000, 0);
        var defeated = Card("S01-0004", "night-parade-piercing-defeated", 1000, 1);
        var response = Card("S02-0005", "night-parade-response", ownerIndex: 1);
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = defeated;
        game.State.Players[1].Hand.Add(response);
        for (var index = 0; index < 5; index++)
            game.State.Players[1].Library.Add(
                Card("S01-0001", $"night-parade-library-{index}", ownerIndex: 1));
        game.State.Players[1].Hp = 8;
        game.State.ActiveDisaster = Card("S01-DS02", "night-parade", ownerIndex: -1);
        var hpBefore = game.State.Players[1].Hp;

        StartAttack(game, attacker, defeated);
        AdvanceUntil(game, () => game.State.PendingDefense?.Target.Type == "master"
            && game.State.PendingPrompts.Any(prompt => prompt.Kind == "response"));

        var pending = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.Equal("master", pending.Target.Type);
        Assert.Equal(2, pending.MasterDamage);
        Assert.True(pending.SuppressAttackTriggers);
        Assert.Equal(1, attacker.AttacksThisTurn);
        Assert.Single(game.State.Events, entry => entry.Type == "piercing");

        game = Restore(game);
        pending = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        attacker = Assert.IsType<L12CardInstance>(game.State.Players[0].Field[0][0]);
        Assert.Equal("master", pending.Target.Type);
        Assert.Equal(2, pending.MasterDamage);
        Assert.True(pending.SuppressAttackTriggers);
        Assert.Equal(1, attacker.AttacksThisTurn);
        Assert.Single(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        Assert.Single(game.State.Events, entry => entry.Type == "piercing");

        AdvanceUntil(game, () => game.State.PendingDefense is null
            && game.State.PendingPrompts.Count == 0
            && game.State.EffectStack.Count == 0
            && game.State.Phase == L12Phase.Main);

        Assert.Equal(hpBefore - 2, game.State.Players[1].Hp);
        Assert.Equal(1, attacker.AttacksThisTurn);
        Assert.Single(game.State.Events, entry => entry.Type == "piercing");
    }

    [Fact]
    [Trait("L12Evidence", "keyword:piercing")]
    [Trait("L12Evidence", "card:S01-DS02")]
    public void NightParadeNonDisasterPiercingKeepsBaseDamageAndDoesNotRepeatAttackTiming()
    {
        var game = Create(81204);
        var attacker = Card("S02-0606", "night-parade-no-level-attacker", 5000, 0);
        var defeated = Card("S01-0102", "night-parade-no-level-defeated", 1000, 1);
        var attackCost = Card("S01-0001", "percival-attack-cost", ownerIndex: 0);
        var response = Card("S02-0005", "night-parade-no-level-response", ownerIndex: 1);
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[0].Hand.Add(attackCost);
        game.State.Players[1].Field[0][0] = defeated;
        game.State.Players[1].Hand.Add(response);
        game.State.ActiveDisaster = Card("S01-DS02", "night-parade-no-level", ownerIndex: -1);
        var attackTimingPromptCount = 0;

        StartAttack(game, attacker, defeated);
        AdvanceUntil(game, () => game.State.PendingDefense?.Target.Type == "master"
                && game.State.PendingPrompts.Any(prompt => prompt.Kind == "response"),
            prompt =>
            {
                if (prompt.Text.Contains("帕西瓦尔：是否弃置1张手牌", StringComparison.Ordinal))
                    attackTimingPromptCount++;
            });

        var pending = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.Equal(0, attacker.DisasterLevel);
        Assert.Equal(1, pending.MasterDamage);
        Assert.True(pending.SuppressAttackTriggers);
        Assert.Equal(1, attackTimingPromptCount);
        Assert.Contains(attackCost, game.State.Players[0].Hand);
        Assert.DoesNotContain(game.State.Events, entry => entry.Text.Contains("帕西瓦尔本回合兵力+2000",
            StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "keyword:piercing")]
    [Trait("L12Evidence", "ability:crusadeRichardPiercing")]
    public void CrusadeGrantedRichardRunsTheSameGeneratedAttackEntry()
    {
        var game = Create(81205);
        var attacker = Card("S02-0608", "crusade-richard-piercing-attacker", 5000, 0);
        var defeated = Card("S01-0102", "crusade-richard-piercing-defeated", 1000, 1);
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = defeated;
        game.State.Players[0].UsedAbilities.Add(
            $"crusade-piercing:{attacker.InstanceId}:{game.State.TurnSerial}");

        StartAttack(game, attacker, defeated);
        AdvanceUntil(game, () => game.State.PendingDefense?.Target.Type == "master"
            && game.State.PendingPrompts.Any(prompt => prompt.Kind == "response"));

        var pending = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.Equal(attacker.InstanceId, pending.AttackerInstanceId);
        Assert.Equal("master", pending.Target.Type);
        Assert.True(pending.SuppressAttackTriggers);
        Assert.Single(game.State.Events, entry => entry.Type == "piercing"
            && entry.Cards.Any(card => card.CardId == attacker.CardId));
    }

    private static void StartAttack(L12GameEngine game, L12CardInstance attacker, L12CardInstance target)
    {
        var result = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId)));
        Assert.True(result.Accepted, result.Error);
    }

    private static void AdvanceUntil(L12GameEngine game, Func<bool> completed,
        Action<L12Prompt>? observePrompt = null)
    {
        for (var guard = 0; guard < 100 && !completed(); guard++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt is not null)
            {
                observePrompt?.Invoke(prompt);
                if (prompt.MinChoose > 1)
                {
                    var selected = prompt.ValidChoices.Take(prompt.MinChoose).ToList();
                    var multiResult = game.Handle(prompt.PlayerIndex,
                        new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                            CardInstanceIds: selected));
                    Assert.True(multiResult.Accepted, multiResult.Error);
                    continue;
                }
                var choice = prompt.Kind == "response" ? "pass"
                    : prompt.ValidChoices.Contains("mode:none") ? "mode:none"
                    : prompt.ValidChoices.Contains("skip") ? "skip"
                    : prompt.ValidChoices.Contains("no") ? "no"
                    : prompt.ValidChoices[0];
                var result = game.Handle(prompt.PlayerIndex,
                    new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
                Assert.True(result.Accepted, result.Error);
                continue;
            }

            if (game.State.PendingDefense is { Stage: L12CombatStage.DefenseChoice } pending)
            {
                var result = game.Handle(1 - pending.AttackerPlayer,
                    new L12Command("resolveDefense", CardInstanceIds: []));
                Assert.True(result.Accepted, result.Error);
                continue;
            }

            throw new InvalidOperationException(
                $"贯穿回归在没有可推进提示或防守选择时停滞：Phase={game.State.Phase}, "
                + $"PendingDefense={(game.State.PendingDefense is null ? "null" : game.State.PendingDefense.Stage)}, "
                + $"EffectStack={game.State.EffectStack.Count}, Deferred={game.State.DeferredEffectStack.Count}, "
                + $"TriggerBatches={game.State.PendingTriggerBatches.Count}, "
                + $"TriggerCandidates={game.State.PendingTriggerStackCandidates.Count}, "
                + $"Hp={game.State.Players[0].Hp}/{game.State.Players[1].Hp}, "
                + $"Winner={game.State.Winner}, WinnerReason={game.State.WinnerReason}");
        }

        Assert.True(completed(), "贯穿回归未在限定步骤内到达预期状态");
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "piercing-normal-attack", "PIERCING", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.UsedAbilities.Clear();
        }
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 5;
        game.State.Phase = L12Phase.Main;
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static L12CardInstance Card(string cardId, string instanceId, int troops = 0, int ownerIndex = 0)
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
            HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = troops > 0 ? troops : definition.Troops ?? 0,
            Troops = troops > 0 ? troops : definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
            OwnerIndex = ownerIndex,
        };
    }
}
