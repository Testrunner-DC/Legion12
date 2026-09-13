using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SimpleDeathDrawTriggerConsistencyTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    public static IEnumerable<object[]> OptionalRows()
    {
        yield return ["S01-0301"];
        yield return ["S01-0309"];
        yield return ["S02-0203"];
        yield return ["S02-0402"];
        yield return ["S02-0512"];
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "simple-death-draw", "DRAW-DEATH", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
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
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Library.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner = 0)
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            SummonRound = -1,
            OwnerIndex = owner,
        };
    }

    private static object? Invoke(object target, string method, params object?[] args)
    {
        var candidate = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(info => info.Name == method && info.GetParameters().Length == args.Length);
        return candidate.Invoke(target, args);
    }

    private static L12Prompt OnlyPrompt(L12GameEngine game) => Assert.Single(game.State.PendingPrompts);

    private static void ResolveChoice(L12GameEngine game, string choice)
    {
        var prompt = OnlyPrompt(game);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            ResolveChoice(game, "pass");
    }

    private static L12CardInstance QueueDeath(L12GameEngine game, string cardId,
        int controller = 0, int owner = 0)
    {
        var source = Card(cardId, $"draw-source-{cardId}-{controller}-{owner}", owner);
        game.State.Players[controller].Resolving.Add(source);
        Invoke(game, "QueueOrPushTriggeredEffect", controller, source, "death",
            "单抽牌一致性测试", null, new Dictionary<string, string> { ["cause"] = "effect" });
        return source;
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-death-draw-spec")]
    public void SevenExactSingleDrawDeathEffectsUseOneParameterizedProgramShape()
    {
        var expectedCardIds = new[]
        {
            "S01-0004", "S01-0110", "S01-0301", "S01-0309",
            "S02-0203", "S02-0402", "S02-0512",
        };
        Assert.Equal(expectedCardIds, L12SimpleDrawTriggerEffects.All.Select(spec => spec.CardId));

        var nonSettlementKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            L12AtomKinds.Trigger,
            L12AtomKinds.Condition,
            L12AtomKinds.Optional,
            L12AtomKinds.Legacy,
            L12AtomKinds.CompositeFlow,
        };
        var catalogMatches = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.Trigger == "death")
            .Where(ability =>
            {
                var settlement = ability.Atoms.Where(atom => !nonSettlementKinds.Contains(atom.Kind)).ToArray();
                return settlement.Length == 1
                    && settlement[0].Kind == L12AtomKinds.Draw
                    && settlement[0].Parameters.GetValueOrDefault("amount") == "1";
            })
            .Select(ability => ability.CardId)
            .OrderBy(cardId => cardId, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedCardIds, catalogMatches);

        foreach (var spec in L12SimpleDrawTriggerEffects.All)
        {
            var ability = Catalog.AtomicEffects.Find(spec.CardId)!.Abilities
                .Single(item => item.Sequence == spec.AbilitySequence);
            Assert.Equal("death", ability.Trigger);
            Assert.Equal(spec.SettlementText,
                Assert.Single(ability.Presentations, scene =>
                    scene.Flow == L12SingleSegmentTriggeredEffectPresentations.Flow).DefaultText);

            var program = Assert.IsType<L12VerifiedAtomicProgram>(
                L12VerifiedAtomicPrograms.Find(spec.CardId, spec.Trigger));
            Assert.Equal(spec.Optional, program.Atoms.Any(atom => atom.Kind == L12AtomKinds.Optional));
            Assert.Equal(spec.Condition is not null,
                program.Atoms.Any(atom => atom.Kind == L12AtomKinds.Condition));
            var draw = Assert.Single(program.Atoms, atom => atom.Kind == L12AtomKinds.Draw);
            Assert.Equal("1", draw.Parameters["amount"]);
            Assert.Equal(spec.DrawRecipient, draw.Parameters["target"]);
            Assert.Equal(spec.EventText, draw.Parameters["event"]);
            Assert.Equal(spec.EmptyLossReason, draw.Parameters["emptyLossReason"]);
        }
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0110")]
    [Trait("L12Evidence", "entry:simple-death-draw-display-text")]
    public void MoziDeathDrawNeverUsesTheEmbeddedImmortalityReminderAsItsDisplayText()
    {
        var source = Card("S01-0110", "mozi-display-source");
        var text = L12GameEngine.ResolveTriggeredEffectDisplayText(source, "death", "【阵亡时】效果");

        Assert.Equal("阵亡时 抽取1张牌。", text);
        Assert.DoesNotContain("将兵力在本回合变为1000", text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(OptionalRows))]
    [Trait("L12Evidence", "entry:simple-death-draw-optional-lifecycle")]
    public void OptionalSingleDrawDeclaresBeforeStackAndSupportsResolveDeclineReconnectAndDuplicate(
        string cardId)
    {
        var game = Create(9900 + cardId[^1]);
        var player = game.State.Players[0];
        var drawn = Card("S01-0002", $"draw-target-{cardId}");
        player.Library.Add(drawn);
        QueueDeath(game, cardId);

        var prompt = OnlyPrompt(game);
        Assert.Equal("pending-activation", prompt.Continuation);
        Assert.Equal(["mode:none", "mode:use"], prompt.ValidChoices);
        Assert.Empty(game.State.EffectStack);
        Assert.Contains(prompt.PromptId, JsonSerializer.Serialize(game.SnapshotFor(0)),
            StringComparison.Ordinal);
        ResolveChoice(game, "mode:use");
        var spec = L12SimpleDrawTriggerEffects.Find(cardId, "death")!;
        Assert.Single(game.State.Events, entry => entry.Type == "effect-trigger"
            && entry.EffectText == spec.SettlementText
            && entry.Cards.Any(card => card.CardId == cardId));
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.Contains(drawn, player.Library);
        Assert.DoesNotContain(drawn, player.Hand);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "negated"
            && entry.EffectText == spec.SettlementText
            && entry.Cards.Any(card => card.CardId == cardId));

        var declineGame = Create(9950 + cardId[^1]);
        declineGame.State.Players[0].Library.Add(Card("S01-0002", $"decline-target-{cardId}"));
        QueueDeath(declineGame, cardId);
        var declinePrompt = OnlyPrompt(declineGame);
        ResolveChoice(declineGame, "mode:none");
        Assert.Empty(declineGame.State.EffectStack);
        Assert.Single(declineGame.State.Events, entry => entry.Type == "effect-declined"
            && entry.EffectResultStatus == "declined"
            && entry.Cards.Any(card => card.CardId == cardId));
        Assert.False(declineGame.Handle(declinePrompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: declinePrompt.PromptId,
                Choice: "mode:use")).Accepted);
    }

    [Theory]
    [InlineData("S01-0004", 1, 0)]
    [InlineData("S01-0110", 0, 0)]
    [Trait("L12Evidence", "entry:simple-death-draw-recipient")]
    public void MandatorySingleDrawUsesTheStructuredRecipient(string cardId, int controller, int recipient)
    {
        var game = Create(10000 + controller);
        var drawn = Card("S01-0002", $"mandatory-draw-{cardId}", recipient);
        game.State.Players[recipient].Library.Add(drawn);
        QueueDeath(game, cardId, controller, owner: recipient);

        Assert.Empty(game.State.PendingActivations);
        Assert.Single(game.State.EffectStack);
        var spec = L12SimpleDrawTriggerEffects.Find(cardId, "death")!;
        Assert.Single(game.State.Events, entry => entry.Type == "effect-trigger"
            && entry.EffectText == spec.SettlementText
            && entry.Cards.Any(card => card.CardId == cardId));
        PassResponses(game);

        Assert.Contains(drawn, game.State.Players[recipient].Hand);
        Assert.DoesNotContain(drawn, game.State.Players[1 - recipient].Hand);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.EffectText == spec.SettlementText
            && entry.Cards.Any(card => card.CardId == cardId));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0309")]
    [Trait("L12Evidence", "entry:simple-death-draw-condition-lock")]
    public void BrunhildeChecksTheConditionAtTriggerTimeAndLocksItForSettlement()
    {
        var game = Create(10009);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        player.Hp = 6;
        opponent.Hp = 6;
        var drawn = Card("S01-0002", "brunhilde-locked-draw");
        player.Library.Add(drawn);
        QueueDeath(game, "S01-0309");
        Assert.Equal("true", Assert.Single(game.State.PendingTriggerStackCandidates)
            .Data.GetValueOrDefault("verifiedAtomicConditionLocked"));

        player.Hp = 8;
        opponent.Hp = 6;
        ResolveChoice(game, "mode:use");
        PassResponses(game);
        Assert.Contains(drawn, player.Hand);

        var blocked = Create(10010);
        blocked.State.Players[0].Hp = 8;
        blocked.State.Players[1].Hp = 6;
        blocked.State.Players[0].Library.Add(Card("S01-0002", "brunhilde-blocked-draw"));
        QueueDeath(blocked, "S01-0309");
        Assert.Empty(blocked.State.PendingActivations);
        Assert.Empty(blocked.State.PendingTriggerStackCandidates);
        Assert.Empty(blocked.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-death-draw-reconnect")]
    public void AcceptedSingleDrawRestoresDuringResponseAndSettlesExactlyOnce()
    {
        var game = Create(10012);
        var drawn = Card("S01-0002", "simple-draw-restored-card");
        game.State.Players[0].Library.Add(drawn);
        QueueDeath(game, "S02-0203");
        ResolveChoice(game, "mode:use");
        var oldResponse = Assert.Single(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");

        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        PassResponses(game);

        Assert.Single(game.State.Players[0].Hand,
            card => card.InstanceId == drawn.InstanceId);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.CardId == "S02-0203"));
        Assert.False(game.Handle(oldResponse.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: oldResponse.PromptId,
                Choice: "pass")).Accepted);
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-death-draw-empty-library")]
    public void ChosenDrawFromAnEmptyLibraryUsesTheSharedLossRule()
    {
        var game = Create(10011);
        QueueDeath(game, "S02-0402");
        ResolveChoice(game, "mode:use");
        PassResponses(game);

        Assert.Equal(L12Phase.GameOver, game.State.Phase);
        Assert.Equal(1, game.State.Winner);
    }
}
