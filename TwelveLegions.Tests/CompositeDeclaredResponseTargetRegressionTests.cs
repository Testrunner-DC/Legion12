using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

// Synthetic initial boards; every declaration, payment, repeat and free play uses
// the public command path. No private resolver or fabricated LastActiveTactic.
public sealed class CompositeDeclaredResponseTargetRegressionTests
{
    private const string Hela = "S02-0307", Fearless = "S02-0206", Glory = "S02-0521";
    private const string Source = "declared-tactic", Target = "selected-middle", Decoy = "unselected-left";
    private const string Counter = "real-absolute-defense", Milled = "hela-colon-cost";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-15T12:00:00Z");
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static IEnumerable<object[]> NewOrdinaryCases()
    {
        foreach (var card in new[] { Fearless, Glory })
        foreach (var owner in new[] { 0, 1 })
        foreach (var restore in new[] { false, true }) yield return [card, owner, restore];
    }

    public static IEnumerable<object[]> OwnerRestoreCases()
    {
        foreach (var owner in new[] { 0, 1 })
        foreach (var restore in new[] { false, true }) yield return [owner, restore];
    }

    public static IEnumerable<object[]> GeneratedCases()
    {
        foreach (var card in new[] { Hela, Fearless, Glory })
        foreach (var entry in new[] { "free-committed", "repeat" })
        foreach (var owner in new[] { 0, 1 })
        foreach (var restore in new[] { false, true }) yield return [card, entry, owner, restore];
    }

    [Theory, MemberData(nameof(NewOrdinaryCases))]
    public void NewSameTypeOrdinaryDeclarationsPublishOnlyTheirActualEffectObjects(string card, int owner, bool restore)
        => Run(card, "ordinary", owner, restore);

    [Theory, MemberData(nameof(OwnerRestoreCases))]
    public void HelaOrdinaryDeclarationKeepsTargetAndColonPaymentThroughResponseAndSettlement(int owner, bool restore)
        => Run(Hela, "ordinary", owner, restore);

    [Theory, MemberData(nameof(GeneratedCases))]
    public void RealFreeCommittedAndRealPriorTacticRepeatPreserveExplicitPublicTargets(
        string card, string entry, int owner, bool restore) => Run(card, entry, owner, restore);

    private static void Run(string card, string entry, int owner, bool restore)
    {
        var f = new Fixture(card, owner, restore);
        if (entry == "free-committed")
        {
            f.Game.State.Players[owner].Hand.Add(Card("S02-0102", "real-limu", owner));
            f.Game.State.Players[owner].Library.Insert(0, Card(card, Source, owner));
            f.Send(owner, new("playCard", "real-limu", Row: 1, Slot: 0));
            f.Drain();
            Assert.Contains(f.Game.State.Players[owner].Field[1], c => c?.InstanceId == "real-limu");
            Assert.Equal(Catalog.Cards["S02-0102"].Cost, f.PaidMorale);
        }
        else
        {
            f.Game.State.Players[owner].Hand.Add(Card(card, Source, owner));
            if (entry == "repeat")
                f.Game.State.Players[owner].Hand.Add(Card("S01-0211", "real-ptolemy", owner));
            f.Send(owner, new("playCard", Source));
            f.Drain();
            if (entry == "repeat")
            {
                var player = f.Game.State.Players[owner];
                Assert.Equal(card, player.LastActiveTacticCardId);
                Assert.Equal(f.Game.State.TurnSerial, player.LastActiveTacticTurnSerial);
                Assert.Single(player.Graveyard, c => c.InstanceId == Source);
                f.Send(owner, new("playCard", "real-ptolemy", Row: 1, Slot: 0));
                f.Drain();
                Assert.Contains(f.Game.State.Players[owner].Field[1], c => c?.InstanceId == "real-ptolemy");
                Assert.Equal(Catalog.Cards[card].Cost + Catalog.Cards["S01-0211"].Cost, f.PaidMorale);
                Assert.Single(f.Game.State.Players[owner].Graveyard, c => c.InstanceId == Source);
                Assert.DoesNotContain(f.Game.State.Players[owner].Graveyard,
                    c => c.InstanceId.StartsWith("repeat-effect-", StringComparison.Ordinal));
            }
            else Assert.Equal(Catalog.Cards[card].Cost, f.PaidMorale);
        }
        var multiplier = entry == "repeat" ? 2 : 1;
        Assert.Equal(multiplier * 2, f.TargetResponses);
        f.AssertClean();
        var actor = f.Game.State.Players[owner];
        if (card == Hela)
        {
            Assert.Equal(10000 - multiplier * 3000, f.Game.State.Players[1 - owner].Field[0][1]!.Troops);
            Assert.Equal(10000, f.Game.State.Players[1 - owner].Field[0][0]!.Troops);
            Assert.Single(actor.Graveyard, c => c.InstanceId == Milled);
        }
        else if (card == Fearless)
        {
            var target = actor.Field[0][1]!;
            Assert.Equal(10000 + multiplier * 3000, target.Troops);
            Assert.Equal(10000, actor.Field[0][0]!.Troops);
            Assert.True(target.SureHitAgainstLegionsUntilTurn >= f.Game.State.TurnSerial);
            Assert.True(target.CannotReadyByEffectUntilTurn >= f.Game.State.TurnSerial);
            Assert.True(target.DiscardAtEndOfTurnUntilTurn >= f.Game.State.TurnSerial);
            f.Send(owner, new("endTurn"));
            f.Drain();
            Assert.Null(f.Game.State.Players[owner].Field[0][1]);
            Assert.Single(f.Game.State.Players[owner].Graveyard, c => c.InstanceId == Target);
            Assert.Equal(Decoy, f.Game.State.Players[owner].Field[0][0]!.InstanceId);
            f.AssertClean();
        }
        else
        {
            Assert.Equal(multiplier, actor.Morale.Count(m => m.IsGodPower));
            Assert.All(actor.Morale.Where(m => m.IsGodPower), m => Assert.Contains(m.InstanceId, f.SelectedMorale));
        }
        if (restore) Assert.True(f.Restores >= 3);
    }

    [Theory, MemberData(nameof(OwnerRestoreCases))]
    public void GloryEffectTargetsAreNotItsLaterSearchPaymentOrPrivateLibraryChoice(int owner, bool restore)
    {
        var f = new Fixture(Glory, owner, restore) { Search = true };
        f.Game.State.Players[owner].Hand.Add(Card(Glory, Source, owner));
        f.Game.State.Players[owner].Library.Add(Card("S02-0519", "private-search-hit", owner));
        f.Send(owner, new("playCard", Source));
        f.Drain();
        Assert.Equal(2, f.TargetResponses);
        Assert.Equal(2, f.SearchResponses);
        Assert.Equal(2, f.SearchPaymentIds.Count);
        Assert.All(f.Game.State.Players[owner].Morale.Where(m => f.SearchPaymentIds.Contains(m.InstanceId)), m =>
        {
            Assert.True(m.Tapped);
            Assert.False(m.IsGodPower);
        });
        Assert.Single(f.Game.State.Players[owner].Hand, c => c.InstanceId == "private-search-hit");
        f.AssertClean();
    }

    [Theory, MemberData(nameof(OwnerRestoreCases))]
    public void GloryMayChooseZeroFlipTargetsAndStillPayForItsIndependentSearch(int owner, bool restore)
    {
        var f = new Fixture(Glory, owner, restore) { Search = true, SkipInitialFlip = true };
        var actor = f.Game.State.Players[owner];
        foreach (var resource in actor.Morale.TakeLast(2))
        {
            Assert.True(L12S2ZoneOps.FlipMoraleFace(actor, Catalog.MoraleIdentities,
                resource.InstanceId, toGodPower: true));
            f.SelectedMorale.Add(resource.InstanceId);
        }
        actor.Hand.Add(Card(Glory, Source, owner));
        actor.Library.Add(Card("S02-0519", "private-search-hit", owner));
        Assert.Equal(2, actor.Morale.Count(resource => resource.IsGodPower && !resource.Tapped));
        f.Send(owner, new("playCard", Source));
        f.Drain();
        Assert.Equal(2, f.TargetResponses);
        Assert.Equal(2, f.SearchResponses);
        Assert.Equal(2, f.SearchPaymentIds.Count);
        Assert.Equal(Catalog.Cards[Glory].Cost + 2, f.PaidMorale);
        Assert.All(f.Game.State.Players[owner].Morale.Where(m => f.SearchPaymentIds.Contains(m.InstanceId)), m =>
        {
            Assert.True(m.Tapped);
            Assert.False(m.IsGodPower);
        });
        Assert.Single(f.Game.State.Players[owner].Hand, card => card.InstanceId == "private-search-hit");
        Assert.Equal(new[] { "skipped", "resolved" }, f.Game.State.Events
            .Where(entry => entry.Type == "effect-result"
                && entry.Cards.Any(card => card.InstanceId == Source))
            .Select(entry => entry.EffectResultStatus));
        f.AssertClean();
    }

    [Theory, MemberData(nameof(OwnerRestoreCases))]
    public void HelaWithoutEnemyTargetPaysItsRealCostButDoesNotInventAPublicTarget(int owner, bool restore)
    {
        var f = new Fixture(Hela, owner, restore) { NoTarget = true };
        Array.Clear(f.Game.State.Players[1 - owner].Field[0]);
        f.Game.State.Players[owner].Hand.Add(Card(Hela, Source, owner));
        f.Send(owner, new("playCard", Source));
        f.Drain();
        Assert.Single(f.Game.State.Players[owner].Graveyard, c => c.InstanceId == Milled);
        Assert.Single(f.Game.State.Players[owner].Graveyard, c => c.InstanceId == Source);
        Assert.Equal(Catalog.Cards[Hela].Cost, f.PaidMorale);
        Assert.Equal(2, f.TargetResponses);
        f.AssertClean();
    }

    [Theory, MemberData(nameof(OwnerRestoreCases))]
    public void NonTargetModeAndRowNeverBecomeInstancePresentationTargets(int owner, bool restore)
    {
        var f = new Fixture("S02-0406", owner, restore);
        f.Game.State.Players[owner].Hand.Add(Card("S02-0406", Source, owner));
        f.Send(owner, new("playCard", Source));
        f.Drain();
        Assert.Equal(2, f.NonTargetResponses);
        Assert.All(f.Game.State.Players[1 - owner].Field[0].Where(c => c is not null), c =>
            Assert.Equal(Catalog.Cards[c!.CardId].Cost - 2, c.CurrentCost));
        f.AssertClean();
    }

    [Theory, InlineData(0), InlineData(1)]
    public void HelaMissingColonCostAndFearlessMissingLegalTargetRejectBeforePayment(int owner)
    {
        foreach (var card in new[] { Hela, Fearless })
        {
            var f = new Fixture(card, owner, restore: true);
            if (card == Hela) f.Game.State.Players[owner].Library.Clear();
            else Array.Clear(f.Game.State.Players[owner].Field[0]);
            f.Game.State.Players[owner].Hand.Add(Card(card, Source, owner));
            var result = f.Game.Handle(owner, new("playCard", Source));
            if (result.Accepted)
            {
                // A Hela target declaration can precede its final cost revalidation.
                var prompt = Assert.Single(f.Game.State.PendingPrompts);
                Assert.Equal(Hela, card);
                Assert.Contains(Target, prompt.ValidChoices);
                var answer = f.Game.Handle(owner, Typed(prompt, [Target]));
                Assert.True(answer.Accepted, answer.Error);
            }
            Assert.Equal(0, f.PaidMorale);
            Assert.Contains(f.Game.State.Players[owner].Hand, c => c.InstanceId == Source);
            f.AssertClean();
        }
    }

    private static L12CardInstance Card(string cardId, string id, int owner, int? troops = null)
    {
        var d = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = id, CardId = d.Id, Name = d.NameZh, CardType = d.CardType,
            IsCounterTactic = d.IsCounterTactic, Faction = d.Faction, ImageUrl = d.ImageUrl,
            Cost = d.Cost ?? 0, HasPrintedCost = d.Cost.HasValue, EffectText = d.Effect,
            Traits = [.. d.Traits], Profession = d.Profession, EffectiveProfession = d.Profession,
            BaseTroops = troops ?? d.Troops ?? 0, Troops = troops ?? d.Troops ?? 0,
            DisasterLevel = d.DisasterLevel ?? 0, TrialValue = d.TrialValue ?? 0,
            OwnerIndex = owner, SummonRound = -1,
        };
    }

    private static L12Command Typed(L12Prompt p, string[] choices) => new("resolvePrompt",
        PromptId: p.PromptId, CardInstanceIds: choices.ToList(),
        ActivationId: p.ActivationId, SourceInstanceId: p.SourceInstanceId,
        SourceCardId: p.SourceCardId, Step: p.Step,
        CreatedRevision: p.CreatedRevision, Controller: p.Controller);

    private sealed class Fixture
    {
        internal L12GameEngine Game;
        internal int Restores, TargetResponses, SearchResponses, NonTargetResponses;
        internal bool Search, NoTarget, SkipInitialFlip;
        internal readonly HashSet<string> SelectedMorale = [];
        internal readonly List<string> SearchPaymentIds = [];
        private readonly string _card;
        private readonly int _owner;
        private readonly bool _restore;
        private string[] _currentTargets = [];
        internal int PaidMorale => Game.State.Players[_owner].Morale.Count(m => m.Tapped);

        internal Fixture(string card, int owner, bool restore)
        {
            _card = card; _owner = owner; _restore = restore;
            var deck = Catalog.DeckAt(0);
            Game = new(Catalog, "composite-public-target-regression", "SYNTHETIC", 153300 + owner,
                ["Synthetic A", "Synthetic B"], [deck, deck], skipPreparation: true,
                disasterMode: "none", autoPassEmptyResponses: false,
                concealHiddenResponseAvailability: false, stateFormatVersion: 2, utcNow: () => Now);
            Game.State.ActivePlayer = owner; Game.State.FirstPlayer = owner;
            Game.State.Round = 3; Game.State.TurnSerial = 10; Game.State.Phase = L12Phase.Main;
            foreach (var p in Game.State.Players)
            {
                foreach (var row in p.Field) Array.Clear(row);
                p.Hand.Clear(); p.Library.Clear(); p.Graveyard.Clear(); p.Resolving.Clear();
                p.Morale.Clear(); p.MoraleDeck.Clear(); p.UsedAbilities.Clear();
                p.Relic = null; p.ExtraRelics.Clear();
                p.Hand.Add(Card("S01-0003", $"private-hand-{p.PlayerIndex}", p.PlayerIndex));
                for (var i = 0; i < 12; i++) p.Library.Add(Card("S01-0003", $"private-library-{p.PlayerIndex}-{i}", p.PlayerIndex));
            }
            for (var i = 0; i < 20; i++) Game.State.Players[owner].Morale.Add(new()
            {
                CardId = "S02-05C1A", InstanceId = $"resource-{i}", Tapped = false,
            });
            var targetOwner = card == Fearless ? owner : 1 - owner;
            foreach (var (id, slot) in new[] { (Decoy, 0), (Target, 1) })
            {
                var target = Card(card == Fearless ? "S01-0201" : "S02-0601", id, targetOwner, troops: 10000);
                Game.State.Players[targetOwner].Field[0][slot] = target;
            }
            var counter = Card("S01-0016", Counter, 1 - owner);
            counter.Hidden = true; counter.SetRound = 0;
            Game.State.Players[1 - owner].Field[1][2] = counter;
            if (card == Hela) Game.State.Players[owner].Library.Insert(0, Card("S02-0301", Milled, owner));
        }

        private void Restore()
        {
            if (!_restore) return;
            var raw = Game.SerializeFullState(); var hash = Game.ComputeStateHash();
            var random = Game.RandomState!.Value; var fact = Game.CardFactSignalSequence;
            Game = L12GameEngine.RestoreCheckpoint(Catalog, raw, random, fact,
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, utcNow: () => Now);
            Assert.Equal(raw, Game.SerializeFullState()); Assert.Equal(hash, Game.ComputeStateHash());
            Assert.Equal(JsonSerializer.Serialize(random), JsonSerializer.Serialize(Game.RandomState!.Value));
            Assert.Equal(fact, Game.CardFactSignalSequence); Restores++;
        }

        internal void Send(int actor, L12Command command)
        {
            Restore();
            var result = Game.Handle(actor, command);
            Assert.True(result.Accepted, $"{command.Type}: {result.Error}");
            Restore();
        }

        internal void Drain()
        {
            for (var guard = 0; Game.State.PendingPrompts.Count > 0; guard++)
            {
                Assert.True(guard < 80, "Public command chain did not quiesce");
                var p = Assert.Single(Game.State.PendingPrompts);
                if (p.Kind == "response")
                {
                    var top = Game.State.EffectStack.Last();
                    if (top.SourceCardId == _card)
                    {
                        var flow = top.Data.GetValueOrDefault("atomicFlow");
                        if (top.Data.GetValueOrDefault("eventType") == "effect-hand-add")
                        {
                            Assert.Empty(Ids(p));
                            Assert.DoesNotContain("private-search-hit", p.Text, StringComparison.Ordinal);
                        }
                        else if (flow == "glory-search")
                        {
                            Assert.Empty(Ids(p)); SearchResponses++;
                            Assert.DoesNotContain("private-search-hit", p.Text, StringComparison.Ordinal);
                        }
                        else if (flow == "tenka-effect")
                        {
                            Assert.Empty(Ids(p)); NonTargetResponses++;
                        }
                        else if (flow is "hela-curse" or "fearless-assassination" or "glory-flip")
                        {
                            Assert.Equal(NoTarget ? [] : _currentTargets, Ids(p));
                            Assert.DoesNotContain(Decoy, Ids(p));
                            Assert.DoesNotContain(Milled, Ids(p));
                            if (_card == Glory && _currentTargets.Length > 0)
                            {
                                using var facts = JsonDocument.Parse(top.Data["responsePublicTargetSnapshotV1"]);
                                Assert.Equal(_currentTargets, facts.RootElement.EnumerateArray()
                                    .Select(fact => fact.GetProperty("Id").GetString()).ToArray());
                                Assert.All(facts.RootElement.EnumerateArray(), fact =>
                                {
                                    Assert.Equal(_owner, fact.GetProperty("Owner").GetInt32());
                                    Assert.Equal("morale", fact.GetProperty("Zone").GetString());
                                    Assert.Equal(-1, fact.GetProperty("Row").GetInt32());
                                    Assert.Equal(-1, fact.GetProperty("Slot").GetInt32());
                                    Assert.Equal(JsonValueKind.Null, fact.GetProperty("PublicName").ValueKind);
                                    Assert.Equal(JsonValueKind.Null, fact.GetProperty("CurrentCost").ValueKind);
                                    Assert.False(fact.GetProperty("IsGodPower").GetBoolean());
                                    Assert.DoesNotContain(fact.EnumerateObject(), property =>
                                        property.Name is "CardId" or "EffectText" or "Name");
                                });
                                Assert.DoesNotContain("S02-05C1A", p.Text, StringComparison.Ordinal);
                            }
                            else if (_card == Glory)
                            {
                                Assert.False(top.Data.ContainsKey("responsePublicTargetSnapshotV1"));
                                Assert.DoesNotContain("S02-05C1A", p.Text, StringComparison.Ordinal);
                            }
                            if (!NoTarget && _card != Glory) Assert.Contains("前排中格", p.Text, StringComparison.Ordinal);
                            if (_card == Hela && top.SourceInstanceId == Source)
                                Assert.Equal("弃置我方牌库顶部1张牌", p.Data["responsePaidCostSummary"]);
                            AssertSnapshotPrivacy(p);
                            TargetResponses++;
                        }
                    }
                    Send(p.PlayerIndex, Typed(p, ["pass"]));
                    continue;
                }
                string[] choices;
                if (p.Data.GetValueOrDefault("action") == "s2-limu-tactic") choices = ["play"];
                else if (p.ValidChoices.Contains(Target)) choices = _currentTargets = [Target];
                else if (p.Kind == "target-morale" && p.MaxChoose == 3)
                {
                    choices = SkipInitialFlip ? [] : p.ValidChoices.Where(id => !SelectedMorale.Contains(id))
                        .Take(Search ? 3 : 1).ToArray();
                    Assert.Equal(SkipInitialFlip ? 0 : Search ? 3 : 1, choices.Length);
                    _currentTargets = choices; foreach (var id in choices) SelectedMorale.Add(id);
                }
                else if (p.ValidChoices.Contains("mode:row-cost")) choices = ["mode:row-cost"];
                else if (p.ValidChoices.Contains("row:0")) choices = ["row:0"];
                else if (p.ValidChoices.Contains("mode:search")) choices = [Search ? "mode:search" : "mode:none"];
                else if (Search && p.Kind == "target-morale" && p.MinChoose == 2
                    && p.ValidChoices.Any(id => SelectedMorale.Contains(id)))
                {
                    choices = p.ValidChoices.Where(id => SelectedMorale.Contains(id)).Take(2).ToArray();
                    Assert.Equal(2, choices.Length); SearchPaymentIds.AddRange(choices);
                }
                else if (p.Kind == "resource-payment")
                {
                    choices = p.ValidChoices.Where(id => Game.State.Players[p.PlayerIndex].Morale
                        .Any(m => m.InstanceId == id && !m.IsGodPower && !m.Tapped)).Take(p.MinChoose).ToArray();
                    Assert.Equal(p.MinChoose, choices.Length);
                }
                else if (p.Data.GetValueOrDefault("action") == "s2-glory-search") choices = ["private-search-hit"];
                else if (p.ValidChoices.Contains("mode:use")) choices = ["mode:use"];
                else if (p.ValidChoices.Contains("mode:none")) choices = ["mode:none"];
                else throw new InvalidOperationException($"Unexpected prompt {p.Kind}/{p.Continuation}: {p.Text}; {string.Join(',', p.ValidChoices)}");
                Send(p.PlayerIndex, Typed(p, choices));
            }
        }

        private static string[] Ids(L12Prompt p) => JsonSerializer.Deserialize<string[]>(p.Data["responseTargetIds"])!;

        private void AssertSnapshotPrivacy(L12Prompt response)
        {
            for (var viewer = 0; viewer < 2; viewer++)
            {
                var dto = JsonSerializer.SerializeToElement(Game.SnapshotFor(viewer), Web);
                var opponent = dto.GetProperty("players")[1 - viewer];
                Assert.DoesNotContain($"private-hand-{1 - viewer}", opponent.GetRawText(), StringComparison.Ordinal);
                Assert.DoesNotContain($"private-library-{1 - viewer}", opponent.GetRawText(), StringComparison.Ordinal);
                if (viewer == _owner)
                {
                    var counter = opponent.GetProperty("field")[1][2];
                    Assert.Equal(Counter, counter.GetProperty("instanceId").GetString());
                    Assert.True(counter.GetProperty("hidden").GetBoolean());
                    Assert.True(!counter.TryGetProperty("cardId", out var id) || id.GetString() != "S01-0016");
                }
                if (viewer == response.PlayerIndex)
                {
                    var prompt = Assert.Single(dto.GetProperty("prompts").EnumerateArray(), p => p.GetProperty("kind").GetString() == "response");
                    Assert.Equal(response.Data["responseTargetIds"], prompt.GetProperty("data").GetProperty("responseTargetIds").GetString());
                }
            }
        }

        internal void AssertClean()
        {
            Assert.Empty(Game.State.PendingPrompts); Assert.Empty(Game.State.PendingActivations);
            Assert.Empty(Game.State.EffectStack); Assert.Empty(Game.State.DeferredEffectStack);
            Assert.Empty(Game.State.PendingTriggerBatches); Assert.Empty(Game.State.PendingTriggerStackCandidates);
            Assert.Null(Game.State.ResponseWindow);
        }
    }
}
