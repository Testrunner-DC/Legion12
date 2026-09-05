using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class Bq20260904_03RegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "bq-20260904-03", "BQ090403", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: true,
            concealHiddenResponseAvailability: false);
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
            player.Morale.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = definition.Id, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction, ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0, HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect, Traits = [.. definition.Traits], Profession = definition.Profession,
            EffectiveProfession = definition.Profession, BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0, DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0, SummonRound = -1, OwnerIndex = 0,
        };
    }

    private static L12CardInstance[] SnapshotHand(L12GameEngine game, int playerIndex)
        => (L12CardInstance[])typeof(L12GameEngine).GetMethod("SnapshotHand",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game, [playerIndex])!;

    private static void AddMorale(L12PlayerState player, int count)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "ST03-C1",
                InstanceId = $"bq090403-morale-{index}",
            });
    }

    [Fact]
    public void OptionalEntryDiscountsKeepPrintedCurrentCostButExposeMinimumAffordableCost()
    {
        var game = Create(904031);
        var player = game.State.Players[0];
        var rollo = Card("S02-0302", "rollo");
        var sigurd = Card("ST03-01", "sigurd");
        player.Hand.AddRange([rollo, sigurd]);
        for (var index = 0; index < 4; index++)
            player.Graveyard.Add(Card("ST03-02", $"grave-{index}"));

        var snapshot = SnapshotHand(game, 0);
        var rolloView = Assert.Single(snapshot, card => card.InstanceId == rollo.InstanceId);
        var sigurdView = Assert.Single(snapshot, card => card.InstanceId == sigurd.InstanceId);
        Assert.Equal(rollo.Cost, rolloView.PlayCost);
        Assert.Equal(Math.Max(0, rollo.Cost - 2), rolloView.MinimumPlayCost);
        Assert.Equal(sigurd.Cost, sigurdView.PlayCost);
        Assert.Equal(Math.Max(0, sigurd.Cost - 1), sigurdView.MinimumPlayCost);
    }

    [Fact]
    public void MedusaShockResolvesAgainstAdjacentLegionsAndExpiresWithTheAttack()
    {
        var game = Create(904032);
        var medusa = Card("ST05-09", "medusa");
        var left = Card("ST01-01", "left");
        var primary = Card("ST01-01", "primary");
        var right = Card("ST01-01", "right");
        primary.Troops = 9000;
        game.State.Players[0].Field[0][0] = medusa;
        game.State.Players[1].Field[0][0] = left;
        game.State.Players[1].Field[0][1] = primary;
        game.State.Players[1].Field[0][2] = right;

        var result = game.Handle(0, new L12Command("attack", medusa.InstanceId,
            Target: new L12AttackTarget("legion", primary.InstanceId)));

        Assert.True(result.Accepted, result.Error);
        Assert.Contains(game.State.Events, entry => entry.Text.Contains("震击使进攻目标左右相邻军团", StringComparison.Ordinal));
        Assert.Equal(left.BaseTroops - 2000, left.Troops);
        Assert.Equal(right.BaseTroops - 2000, right.Troops);
        Assert.False(medusa.HasShock);
        Assert.Null(game.State.PendingDefense);
    }

    [Fact]
    public void VikingRaiderUsesGenericAfterDamageTriggerFromARealMasterAttack()
    {
        var game = Create(904033);
        var raider = Card("ST03-04", "raider");
        game.State.Players[1].Hp = 10;
        game.State.Players[0].Field[0][0] = raider;
        game.State.Players[0].Library.Add(Card("ST01-04", "draw-1"));
        game.State.Players[0].Library.Add(Card("ST01-05", "draw-2"));

        var result = game.Handle(0, new L12Command("attack", raider.InstanceId,
            Target: new L12AttackTarget("master")));

        Assert.True(result.Accepted, result.Error);
        Assert.True(game.Handle(1, new L12Command("resolveDefense", CardInstanceIds: [])).Accepted);
        Assert.True(game.State.Players[0].Hand.Count == 2,
            $"hand={game.State.Players[0].Hand.Count}; phase={game.State.Phase}; prompts={string.Join(';', game.State.PendingPrompts.Select(p => $"{p.Kind}/{p.Continuation}/{string.Join(',', p.ValidChoices)}"))}; events={string.Join(" | ", game.State.Events.Select(e => e.Text))}");
        Assert.Contains(game.State.Events, entry => entry.Text.Contains("维京掠夺者对主宰造成伤害后抽取2张牌", StringComparison.Ordinal));
    }

    [Fact]
    public void ErikUsesGenericAfterDamageRouteAndDiscardsExactlyOnce()
    {
        var game = Create(9040331);
        var erik = Card("S01-0308", "erik-after-damage");
        var first = Card("ST01-04", "erik-discard-first");
        var second = Card("ST01-05", "erik-discard-second");
        game.State.Players[0].Field[0][0] = erik;
        game.State.Players[1].Hand.AddRange([first, second]);

        var attack = game.Handle(0, new L12Command("attack", erik.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(attack.Accepted, attack.Error);
        Assert.True(game.Handle(1, new L12Command("resolveDefense", CardInstanceIds: [])).Accepted);

        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(1, prompt.PlayerIndex);
        Assert.Equal("erik-discard", prompt.Data.GetValueOrDefault("action"));
        var submit = new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: [first.InstanceId]);
        Assert.True(game.Handle(1, submit).Accepted);
        Assert.Single(game.State.Players[1].Hand);
        Assert.Contains(first, game.State.Players[1].Graveyard);

        Assert.False(game.Handle(1, submit).Accepted);
        Assert.Single(game.State.Players[1].Hand);
        Assert.DoesNotContain(second, game.State.Players[1].Graveyard);
    }

    [Fact]
    public void SigurdOffersOptionalGraveReturnThenUsesTheDiscountOnlyAfterSelection()
    {
        var game = Create(904034);
        var player = game.State.Players[0];
        var sigurd = Card("ST03-01", "sigurd-play");
        var returned = Card("ST03-02", "sigurd-return");
        player.Hand.Add(sigurd);
        player.Graveyard.Add(returned);
        AddMorale(player, sigurd.Cost);

        var play = game.Handle(0, new L12Command("playCard", sigurd.InstanceId, Row: 0, Slot: 0));

        Assert.True(play.Accepted, play.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("starter-sigurd-grave-cost", prompt.Continuation);
        Assert.Contains(returned.InstanceId, prompt.ValidChoices);
        Assert.Equal(0, prompt.MinChoose);

        var resolve = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: [returned.InstanceId]));

        Assert.True(resolve.Accepted, resolve.Error);
        Assert.Same(sigurd, player.Field[0][0]);
        Assert.DoesNotContain(returned, player.Graveyard);
        Assert.Same(returned, player.Library[^1]);
        Assert.Equal(sigurd.Cost - 1, player.Morale.Count(card => card.Tapped));
    }
}
