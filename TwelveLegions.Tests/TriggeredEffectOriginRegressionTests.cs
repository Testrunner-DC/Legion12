using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

// Public synthetic fixtures. The transformed cases execute the real master
// ability then a real combat kill; artifact cases use the existing GM removal
// command. They are not replays of the player's private match.
public sealed class TriggeredEffectOriginRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-24T00:00:00Z");
    private const string Sword = "origin-kusanagi";
    private const string Codex = "origin-codex";
    private const string Ambush = "origin-ambush";
    private const string Fighter = "origin-fighter";

    public static IEnumerable<object[]> DepartureCases()
    {
        foreach (var owner in new[] { 0, 1 })
        foreach (var transformed in new[] { false, true })
        foreach (var recover in new[] { false, true })
            yield return [owner, transformed, recover];
    }

    [Theory]
    [MemberData(nameof(DepartureCases))]
    public void KusanagiDepartureUsesSusanooAsEffectSourceAndKeepsSwordAsSeparateTarget(
        int owner, bool transformed, bool recover)
    {
        var f = new Fixture(owner, recover);
        f.Depart(transformed);
        var declaration = Assert.Single(f.Game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Equal("S01-04M2", declaration.SourceCardId);
        Assert.Equal($"master-{owner}", declaration.SourceInstanceId);
        Assert.Contains("须佐之男", declaration.Text);
        f.Choose("mode:use");
        var item = Assert.Single(f.Game.State.EffectStack);
        Assert.Equal("S01-04M2", item.SourceCardId);
        Assert.Equal("master", item.SourceSnapshot?.CardType);
        Assert.Equal("leave", item.Trigger);
        Assert.Contains(Sword, item.Data.Values);
        f.PassResponses();
        Assert.Equal(Sword, f.Game.State.Players[owner].Library[0].InstanceId);
        Assert.Single(f.Game.State.Players[owner].Library, card => card.InstanceId == Sword);
        Assert.DoesNotContain(f.Game.State.Players[owner].Graveyard, card => card.InstanceId == Sword);
        Assert.DoesNotContain(f.Game.State.Players.SelectMany(p => p.Library), card => card.CardId == "S01-04M2");
    }

    [Theory]
    [MemberData(nameof(DepartureCases))]
    public void SusanooDepartureReturnDoesNotOfferCodexAndShowsActualTimingWhileAmbushRemainsLegal(
        int owner, bool transformed, bool recover)
    {
        var f = new Fixture(owner, recover);
        f.Depart(transformed);
        f.Choose("mode:use");
        f.PassUntilOpponent();
        var prompt = Assert.Single(f.Game.State.PendingPrompts);
        Assert.DoesNotContain(Codex, prompt.ValidChoices);
        Assert.Contains(Ambush, prompt.ValidChoices);
        Assert.Contains("须佐之男", prompt.Text);
        Assert.Contains("离场时", prompt.Text);
        Assert.Contains("牌库顶部", prompt.Text);
        Assert.DoesNotContain("战术效果发动时", prompt.Text);
        var raw = f.Game.SerializeFullState();
        var rejected = f.Game.Handle(1 - owner, Typed(prompt, Codex));
        Assert.False(rejected.Accepted);
        Assert.Equal(raw, f.Game.SerializeFullState());
        f.PassResponses();
        Assert.Equal(Sword, f.Game.State.Players[owner].Library[0].InstanceId);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)]
    [InlineData(0, true)] [InlineData(1, true)]
    public void DecliningSusanooReturnLeavesRealSwordInGraveAndDoesNotActivateAnything(int owner, bool recover)
    {
        var f = new Fixture(owner, recover);
        f.Depart(true); f.Choose("mode:none");
        Assert.Single(f.Game.State.Players[owner].Graveyard, card => card.InstanceId == Sword);
        Assert.DoesNotContain(f.Game.State.Players[owner].Library, card => card.InstanceId == Sword);
        Assert.Empty(f.Game.State.EffectStack); Assert.Empty(f.Game.State.PendingPrompts);
        Assert.True(f.Game.State.Players[1 - owner].Field[1][0]?.Hidden);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)]
    [InlineData(0, true)] [InlineData(1, true)]
    public void LegalNegationOfMasterReturnDoesNotMoveSwordOrRollBackDeparture(int owner, bool recover)
    {
        var f = new Fixture(owner, recover);
        var counter = Card("S01-0016", "origin-absolute", 1 - owner); counter.Hidden = true; counter.SetRound = 0;
        f.Game.State.Players[1 - owner].Field[1][2] = counter;
        f.Game.State.Players[1 - owner].Hand.Add(Card("S01-0003", "origin-discard", 1 - owner));
        f.Depart(false); f.Choose("mode:use"); f.PassUntilOpponent();
        Assert.Contains("origin-absolute", Assert.Single(f.Game.State.PendingPrompts).ValidChoices);
        f.Choose("origin-absolute");
        if (f.Game.State.PendingPrompts.FirstOrDefault() is { Kind: not "response" } cost)
        {
            Assert.Contains("origin-discard", cost.ValidChoices); f.Choose("origin-discard");
        }
        f.PassResponses();
        Assert.Single(f.Game.State.Players[owner].Graveyard, card => card.InstanceId == Sword);
        Assert.DoesNotContain(f.Game.State.Players[owner].Library, card => card.InstanceId == Sword);
        Assert.Empty(f.Game.State.PendingPrompts); Assert.Empty(f.Game.State.EffectStack);
    }

    [Theory]
    [InlineData(0, "enter", false)] [InlineData(1, "enter", false)]
    [InlineData(0, "active", false)] [InlineData(1, "active", false)]
    [InlineData(0, "enter", true)] [InlineData(1, "enter", true)]
    [InlineData(0, "active", true)] [InlineData(1, "active", true)]
    public void KusanagisOwnArtifactEffectsStillOfferCodex(int owner, string trigger, bool recover)
    {
        var f = new Fixture(owner, recover);
        if (trigger == "enter")
        {
            var player = f.Game.State.Players[owner];
            player.Hand.Add(player.Relic!); player.Relic = null;
            f.Game.State.Players[1 - owner].Field[0][1] = Card("S01-0116", "origin-entry-target", 1 - owner);
            f.Send(owner, new L12Command("playCard", Sword));
            f.Choose("origin-entry-target");
        }
        else
        {
            f.Send(owner, new L12Command("activateAbility", Sword, Ability: "kusanagiDebuff"));
            f.Choose(Fighter);
        }
        f.PassUntilOpponent();
        var prompt = Assert.Single(f.Game.State.PendingPrompts);
        Assert.Contains(Codex, prompt.ValidChoices);
        Assert.Equal("S01-0417", Assert.Single(f.Game.State.EffectStack).SourceCardId);
        Assert.Contains("草薙剑", prompt.Text);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)]
    [InlineData(0, true)] [InlineData(1, true)]
    public void AlreadyPersistedLegacySwordPlayItemStillSettlesUsingOnlyItsRealInstance(int owner, bool recover)
    {
        var f = new Fixture(owner, recover);
        var player = f.Game.State.Players[owner];
        var sword = player.Relic!; player.Relic = null; player.Graveyard.Add(sword);
        var data = new Dictionary<string, string> { ["declared:mode"] = "mode:use" };
        typeof(L12GameEngine).GetMethod("PushEffect", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(f.Game, [owner, sword, "play", "【离场时】效果", null, data]);
        // This verifies settlement only, not corrected eligibility/copy for an
        // arbitrarily old malformed persisted item. No historical facts migrate.
        f.PassResponses();
        Assert.Single(f.Game.State.Players[owner].Library, card => card.InstanceId == Sword);
        Assert.Equal(Sword, f.Game.State.Players[owner].Library[0].InstanceId);
    }

    [Fact]
    public void FullPoolTacticRoutesCannotMasqueradeAsAnotherCardTypesTrigger()
    {
        Assert.Equal(324, Catalog.Cards.Count);
        Assert.All(L12RuntimeEffectRoutes.AllPrograms.Where(program => program.Trigger == "play"),
            program => Assert.Equal("tactic", Catalog.Cards[program.CardId].CardType));
        Assert.NotNull(L12RuntimeEffectRoutes.FindProgram("S01-04M2", "leave"));
        Assert.Null(L12RuntimeEffectRoutes.FindProgram("S01-0417", "play"));
    }

    private static L12Command Typed(L12Prompt prompt, string choice) => new("resolvePrompt",
        PromptId: prompt.PromptId, CardInstanceIds: [choice], ActivationId: prompt.ActivationId,
        SourceInstanceId: prompt.SourceInstanceId, SourceCardId: prompt.SourceCardId,
        Step: prompt.Step, CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller);

    private static L12CardInstance Card(string id, string instance, int owner)
    {
        var d = Catalog.Cards[id];
        return new()
        {
            InstanceId = instance, CardId = d.Id, Name = d.NameZh, CardType = d.CardType,
            IsCounterTactic = d.IsCounterTactic, Faction = d.Faction, ImageUrl = d.ImageUrl,
            Cost = d.Cost ?? 0, HasPrintedCost = d.Cost.HasValue, EffectText = d.Effect,
            Traits = [.. d.Traits], Profession = d.Profession, EffectiveProfession = d.Profession,
            BaseTroops = d.Troops ?? 0, Troops = d.Troops ?? 0,
            DisasterLevel = d.DisasterLevel ?? 0, OwnerIndex = owner, SummonRound = -1,
        };
    }

    private sealed class Fixture
    {
        internal L12GameEngine Game;
        private readonly int _owner;
        private readonly bool _recover;
        internal Fixture(int owner, bool recover)
        {
            _owner = owner; _recover = recover;
            var basis = Catalog.DeckAt(0);
            var susanoo = new L12PresetDeckDefinition { Name = "合成来源牌库", MasterId = "S01-04M2",
                CardIds = [.. basis.CardIds], MoraleIds = [.. basis.MoraleIds], SpecialIds = [.. basis.SpecialIds] };
            var decks = owner == 0 ? new[] { susanoo, basis } : [basis, susanoo];
            Game = new(Catalog, "triggered-effect-origin", "SYNTHETIC", 102400 + owner,
                ["Synthetic A", "Synthetic B"], decks, skipPreparation: true, disasterMode: "none",
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
                stateFormatVersion: 2, utcNow: () => Now);
            Game.State.ActivePlayer = owner; Game.State.FirstPlayer = owner;
            Game.State.Round = 3; Game.State.TurnSerial = 10; Game.State.Phase = L12Phase.Main;
            foreach (var player in Game.State.Players)
            {
                foreach (var row in player.Field) Array.Clear(row);
                player.Hand.Clear(); player.Graveyard.Clear(); player.Resolving.Clear(); player.Removed.Clear();
                player.Relic = null; player.ExtraRelics.Clear(); player.UsedAbilities.Clear();
                player.Morale.Clear(); player.TemporaryMorale = 0;
                while (player.Morale.Count < 5)
                {
                    var morale = player.MoraleDeck[0]; player.MoraleDeck.RemoveAt(0);
                    morale.Tapped = false; player.Morale.Add(morale);
                }
            }
            Game.State.Players[owner].Relic = Card("S01-0417", Sword, owner);
            var opponent = Game.State.Players[1 - owner];
            var codex = Card("S01-0224", Codex, 1 - owner); codex.Hidden = true; codex.SetRound = 0;
            var ambush = Card("S01-0019", Ambush, 1 - owner); ambush.Hidden = true; ambush.SetRound = 0;
            opponent.Field[1][0] = codex; opponent.Field[1][1] = ambush;
            opponent.Field[0][0] = Card("S01-0001", Fighter, 1 - owner);
        }
        internal void Send(int player, L12Command command)
        {
            RoundTrip();
            var result = Game.Handle(player, command);
            Assert.True(result.Accepted, $"{command.Type}: {result.Error}");
            RoundTrip();
        }
        internal void Choose(string choice)
        {
            var prompt = Assert.Single(Game.State.PendingPrompts);
            Assert.Contains(choice, prompt.ValidChoices);
            Send(prompt.PlayerIndex, Typed(prompt, choice));
        }
        internal void PassResponses()
        {
            for (var n = 0; n < 80 && Game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; n++) Choose("pass");
        }
        internal void PassUntilOpponent()
        {
            if (Assert.Single(Game.State.PendingPrompts).PlayerIndex == _owner) Choose("pass");
            Assert.Equal(1 - _owner, Assert.Single(Game.State.PendingPrompts).PlayerIndex);
        }
        internal void Depart(bool transformed)
        {
            if (!transformed)
            {
                var result = Game.HandleGm(new L12GmCommand("destroyCard", _owner, CardInstanceId: Sword));
                Assert.True(result.Accepted, result.Error); RoundTrip(); return;
            }
            Send(_owner, new L12Command("activateAbility", $"master-{_owner}", Ability: "kusanagi"));
            Choose("0:0"); PassResponses();
            Assert.Equal(Sword, Game.State.Players[_owner].Field[0][0]?.InstanceId);
            Send(_owner, new L12Command("endTurn"));
            PassResponses();
            Assert.Equal(1 - _owner, Game.State.ActivePlayer);
            Send(1 - _owner, new L12Command("attack", Fighter, Target: new L12AttackTarget("legion", Sword)));
            for (var n = 0; n < 100; n++)
            {
                if (Game.State.PendingPrompts.FirstOrDefault() is { } prompt)
                {
                    if (prompt.Kind != "response") return;
                    Choose("pass");
                }
                else if (Game.State.PendingDefense is { Stage: L12CombatStage.DefenseChoice })
                    Send(_owner, new L12Command("resolveDefense", CardInstanceIds: []));
                else throw new Xunit.Sdk.XunitException("真实战斗未产生草薙剑离场声明");
            }
            throw new Xunit.Sdk.XunitException("真实战斗离场链未收束");
        }
        private void RoundTrip()
        {
            if (!_recover) return;
            var raw = Game.SerializeFullState(); var hash = Game.ComputeStateHash();
            var random = Game.RandomState!.Value; var facts = Game.CardFactSignalSequence;
            Game = L12GameEngine.RestoreCheckpoint(Catalog, raw, random, facts,
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, utcNow: () => Now);
            Assert.Equal(raw, Game.SerializeFullState()); Assert.Equal(hash, Game.ComputeStateHash());
        }
    }
}
