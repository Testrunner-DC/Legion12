using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class Batch299EffectRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static L12GameEngine Create(string master = "S01-04M1", int stateFormatVersion = 0)
    {
        var basis = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition { Name = "回归", MasterId = master,
            CardIds = [.. basis.CardIds], MoraleIds = [.. basis.MoraleIds], SpecialIds = [] };
        var game = new L12GameEngine(Catalog, "batch299", "B299", 299,
            ["甲", "乙"], [deck, deck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: stateFormatVersion);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear(); player.Graveyard.Clear(); player.Resolving.Clear();
            player.Library.Clear(); player.Morale.Clear();
        }
        return game;
    }

    private static object? Call(L12GameEngine game, string name, params object?[] args)
        => typeof(L12GameEngine).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == name && method.GetParameters().Length == args.Length)
            .Invoke(game, args);
    private static L12CardInstance Card(L12GameEngine game, string id, string instance)
        => (L12CardInstance)Call(game, "CreateCard", id, instance)!;
    private static void Resolve(L12GameEngine game, params string[] choices)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt",
            PromptId: prompt.PromptId, CardInstanceIds: choices.ToList()));
        Assert.True(result.Accepted, result.Error);
    }
    private static void Pass(L12GameEngine game)
    {
        for (var count = 0; count < 60 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; count++)
            Resolve(game, "pass");
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
    }

    [Fact]
    public void AmaterasuBuffTracksFrontPositionAndLaterEntriesUntilTurnEnds()
    {
        var game = Create();
        var owner = game.State.Players[0];
        var discard = Card(game, "S01-0003", "cost");
        var legion = Card(game, "S01-0404", "front");
        owner.Hand.Add(discard); owner.Field[0][0] = legion;
        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "amaterasuReady")).Accepted);
        Resolve(game, discard.InstanceId);
        if (game.State.PendingPrompts.FirstOrDefault()?.Kind != "response") Resolve(game);
        Pass(game);
        Assert.Equal(legion.BaseTroops + 1000, legion.Troops);
        owner.Field[0][0] = null; owner.Field[1][0] = legion;
        Call(game, "RecalculateContinuousTroops");
        Assert.Equal(legion.BaseTroops, legion.Troops);
        var late = Card(game, "S01-0404", "late"); owner.Field[0][1] = late;
        Call(game, "RecalculateContinuousTroops");
        Assert.Equal(late.BaseTroops + 1000, late.Troops);
        game.State.TurnSerial++;
        Call(game, "RecalculateContinuousTroops");
        Assert.Equal(late.BaseTroops, late.Troops);
    }

    [Fact]
    public void YingzhengWithoutEightCostOnlyRevealsAndDoesNotKillReturnOrRestrict()
    {
        var game = Create(); var owner = game.State.Players[0];
        var emperor = Card(game, "S02-0101", "emperor"); owner.Field[0][0] = emperor;
        var enemy = Card(game, "S01-0003", "enemy"); game.State.Players[1].Field[0][0] = enemy;
        owner.Hand.Add(Card(game, "S01-0003", "not-eight"));
        owner.Morale.Add(new L12MoraleCard { InstanceId = "morale", CardId = "S01-01C1" });
        Call(game, "BeginYingzhengEnterActivation", 0, emperor);
        Pass(game);
        Assert.Same(enemy, game.State.Players[1].Field[0][0]);
        Assert.Single(owner.Morale);
        Assert.Contains(game.State.Events, entry => entry.Text.Contains("未满足发动条件"));
        Assert.DoesNotContain(game.State.Events, entry => entry.Text.Contains("并限制本回合"));
        Assert.Empty(game.State.PendingPrompts);
    }

    [Fact]
    public void SuccessfulCounterNegationOnOpponentTurnTriggersAngusTrial()
    {
        var game = Create("S02-06M2"); game.State.ActivePlayer = 1;
        var owner = game.State.Players[0];
        owner.SpecialZones.Trials.Add(Card(game, "S02-06S4", "trial"));
        var counter = Card(game, "S01-0020", "counter"); owner.Resolving.Add(counter);
        var item = new L12StackItem { StackItemId = "counter-stack", Controller = 0,
            SourceInstanceId = counter.InstanceId, SourceCardId = counter.CardId,
            SourceName = counter.Name, Trigger = "response-negate", Text = "无效" };
        game.State.EffectStack.Add(item);
        Call(game, "FinishStackItem", item);
        Assert.True(game.State.PendingPrompts.Count > 0 || game.State.EffectStack.Any(stack => stack.SourceCardId == "S02-06M2"),
            "成功结算的反击战术必须产生安格斯的试炼推进");
    }

    [Fact]
    public void ScoutShowsHandBeforeOfferingOptionalPaymentAndDoesNotRevealToSpectators()
    {
        var game = Create(); var owner = game.State.Players[0];
        var scout = Card(game, "S01-0013", "scout"); owner.Hand.Add(scout);
        var hidden = Card(game, "S01-0003", "hidden-opponent-hand");
        game.State.Players[1].Hand.Add(hidden);
        for (var i = 0; i < 4; i++) owner.Morale.Add(new L12MoraleCard { InstanceId = $"m{i}", CardId = "S01-01C1" });
        var played = game.Handle(0, new L12Command("playCard", scout.InstanceId));
        Assert.True(played.Accepted, played.Error);
        Pass(game);
        var preview = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("scout-view-confirm", preview.Data["action"]);
        Assert.Equal(hidden.InstanceId, preview.Data["displayCardIds"]);
        var paidBefore = owner.Morale.Count(morale => morale.Tapped);
        Assert.DoesNotContain(game.SnapshotForSpectator().RecentEvents,
            entry => entry.Cards.Any(card => card.InstanceId == hidden.InstanceId));
        Resolve(game, "confirm");
        var optional = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:none", optional.ValidChoices);
        Assert.Contains("mode:use", optional.ValidChoices);
        Assert.Equal(paidBefore, owner.Morale.Count(morale => morale.Tapped));
        Resolve(game, "mode:none");
        Assert.Empty(game.State.PendingPrompts);
        Assert.Same(hidden, Assert.Single(game.State.Players[1].Hand));
        Assert.Equal(paidBefore, owner.Morale.Count(morale => morale.Tapped));
    }

    [Fact]
    public void MulanDeathWithoutRestedOpponentMoraleDoesNotLeavePendingWork()
    {
        var game = Create(); game.State.ActivePlayer = 1;
        var mulan = Card(game, "S01-0108", "mulan"); game.State.Players[0].Field[0][0] = mulan;
        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0, CardInstanceId: mulan.InstanceId)).Accepted);
        Pass(game);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.EffectStack);
        Assert.Contains(game.State.Players[0].Graveyard, card => card.InstanceId == mulan.InstanceId);
    }

    [Theory]
    [InlineData(1, 0, true)]
    [InlineData(2, 0, true)]
    [InlineData(0, 2, false)]
    [InlineData(0, 3, true)]
    public void ThorHammerButtonUsesRepresentedGraveCost(int warriors, int ordinary, bool enabled)
    {
        var game = Create("S02-03M1");
        var owner = game.State.Players[0];
        var hammer = Card(game, "S02-0301", "hammer");
        owner.Graveyard.Add(hammer);
        for (var i = 0; i < warriors; i++) owner.Graveyard.Add(Card(game, "ST03-08", $"warrior-{i}"));
        for (var i = 0; i < ordinary; i++) owner.Graveyard.Add(Card(game, "S01-0003", $"ordinary-{i}"));
        var views = (List<L12AbilityView>)Call(game, "BuildAbilityViews", owner, hammer.CardId, hammer.InstanceId)!;
        Assert.Equal(enabled, Assert.Single(views, view => view.Id == "thorHammerRevive").Enabled);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public void AristotleDiscountAccumulatesOnlySuccessfulEffects(bool negateSecond, int expected)
    {
        var game = Create(); var owner = game.State.Players[0];
        for (var i = 0; i < 2; i++)
        {
            var source = Card(game, "S02-0513", $"aristotle-{i}"); owner.Field[0][i] = source;
            var result = game.Handle(0, new L12Command("activateAbility", source.InstanceId, Ability: "aristotleDiscount"));
            Assert.True(result.Accepted, result.Error);
            if (i == 1 && negateSecond) Assert.Single(game.State.EffectStack).Negated = true;
            Pass(game);
        }
        Assert.Equal(expected, owner.NextS2OlympusLegionDiscount);
    }

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(1, 1, true)]
    [InlineData(1, -1, false)]
    public void MedjedDamageCandidateRequiresOpponentTurnAndOpponentSource(int active, int source, bool expected)
    {
        var game = Create("S01-02M3"); game.State.ActivePlayer = active;
        game.State.Players[0].Graveyard.Add(Card(game, "S01-0212", "guard"));
        Call(game, "QueueS1MasterDamageReaction", 0, source < 0 ? null : (int?)source, true);
        Assert.Equal(expected, game.State.PendingPrompts.Count > 0);
        if (expected)
        {
            Resolve(game, "mode:none");
            Assert.DoesNotContain("trigger:medjedDamageResponse", game.State.Players[0].UsedAbilities);
            Assert.Empty(game.State.PendingActivations);
            Assert.Empty(game.State.PendingPrompts);
        }
    }

    [Theory]
    [InlineData("card")]
    [InlineData("target-morale")]
    [InlineData("slot")]
    public void MandatoryDeclarationWithNoRemainingCandidatesDoesNotCreateAnImpossiblePrompt(string kind)
    {
        var game = Create();
        var activation = new L12PendingActivation
        {
            ActivationId = "empty-required", Controller = 0, SourceCardId = "S01-0108", Text = "必发选择", ValidChoices = [],
            SourceInstanceId = "mulan", Ability = "public-trigger-declaration", TriggerCandidateId = "required",
            SelectionSteps = [new L12ActivationSelectionStep
            {
                Kind = kind, Text = "必发选择", MinChoose = 1, MaxChoose = 1,
                ValidChoices = [], CancellationPolicy = L12ActivationCancellationPolicy.NotAllowed,
            }],
        };
        game.State.PendingActivations.Add(activation);
        Call(game, "CreateActivationStepPrompt", activation);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
    }

    [Theory]
    [InlineData("mengpoMorale", "mengpoSilence")]
    [InlineData("mengpoSilence", "mengpoMorale")]
    public void MengpoBranchesShareOneActivationPerTurn(string first, string second)
    {
        var game = Create("S01-01M2"); var owner = game.State.Players[0];
        owner.Morale.Add(new L12MoraleCard { InstanceId = "own-morale", CardId = "S01-01C1" });
        for (var i = 0; i < 3; i++) game.State.Players[1].Morale.Add(new L12MoraleCard { InstanceId = $"enemy-morale-{i}", CardId = "S01-01C1" });
        owner.Hand.Add(Card(game, "S01-0003", "discard"));
        owner.Library.Add(Card(game, "S01-0003", "draw"));
        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: first)).Accepted);
        if (first == "mengpoMorale") Resolve(game, "discard");
        else Resolve(game);
        Pass(game);
        if (game.State.PendingPrompts.FirstOrDefault()?.ValidChoices.Contains("mode:none") == true)
            Resolve(game, "mode:none");
        var result = game.Handle(0, new L12Command("activateAbility", "master-0", Ability: second));
        Assert.False(result.Accepted);
        Assert.Empty(game.State.PendingPrompts);
    }

    [Fact]
    public void XishiCanDeclareHerOwnFrontSlotUnderCorruptEarth()
    {
        var game = Create(); var owner = game.State.Players[0];
        game.State.ActiveDisaster = Card(game, "S01-DS03", "disaster");
        var xishi = Card(game, "S01-0116", "xishi"); owner.Field[0][0] = xishi;
        owner.Field[0][1] = Card(game, "S01-0003", "occupied-1");
        owner.Field[0][2] = Card(game, "S01-0003", "occupied-2");
        var targetId = Catalog.Cards.Values.First(card => card.CardType == "legion" && card.Troops == 2000
            && card.Id is not ("S01-0004" or "S01-0116")).Id;
        var summon = Card(game, targetId, "summon"); owner.Hand.Add(summon);
        owner.Morale.Add(new L12MoraleCard { InstanceId = "morale", CardId = "S01-01C1" });
        Assert.True(game.Handle(0, new L12Command("activateAbility", xishi.InstanceId, Ability: "xishiExchange")).Accepted);
        Assert.Contains(summon.InstanceId, Assert.Single(game.State.PendingPrompts).ValidChoices);
        Resolve(game, summon.InstanceId);
        if (Assert.Single(game.State.PendingPrompts).ValidChoices.Contains("battlefield:0")) Resolve(game, "battlefield:0");
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("0:0", slot.ValidChoices);
        Assert.DoesNotContain(slot.ValidChoices, choice => choice.StartsWith("1:"));
        Resolve(game, "0:0");
        Pass(game);
        Assert.True(ReferenceEquals(summon, owner.Field[0][0]),
            string.Join(";", game.State.PendingPrompts.Select(prompt => prompt.Text)) + " / "
            + string.Join(";", game.State.Events.TakeLast(6).Select(entry => entry.Text)));
        Assert.Contains(xishi, owner.Graveyard);
    }

    [Fact]
    public void MercenaryDiscardsAtDeclarationAndNegationDoesNotRefund()
    {
        var game = Create(); var owner = game.State.Players[1];
        var mercenary = Card(game, "S01-0002", "mercenary"); owner.Hand.Add(mercenary);
        Call(game, "CommitMercenaryResponse", 1, mercenary, "attack-target");
        Assert.DoesNotContain(mercenary, owner.Hand);
        Assert.Same(mercenary, Assert.Single(owner.Graveyard));
        Assert.Single(game.State.EffectStack).Negated = true;
        Pass(game);
        Assert.Same(mercenary, Assert.Single(owner.Graveyard));
        Assert.Empty(game.State.EffectStack);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void YangjianReturnIsPrivateOnBothSubmissionPaths(bool direct)
    {
        var game = Create(stateFormatVersion: 2); var owner = game.State.Players[0];
        var hidden = Card(game, "S01-0003", "private-return-identity"); owner.Hand.Add(hidden);
        var item = new L12StackItem { StackItemId = "return-effect", Controller = 0,
            SourceInstanceId = "source", SourceCardId = "S01-0103", SourceName = "杨戬", Trigger = "active", Text = "抽一放一" };
        game.State.EffectStack.Add(item);
        if (direct) Call(game, "CompleteYangJianReturn", item, hidden.InstanceId, "top");
        else
        {
            item.Data["return-card"] = hidden.InstanceId;
            Call(game, "CompleteYangJianReturn", item, "top");
        }
        Assert.Contains(game.SnapshotFor(0).RecentEvents,
            entry => entry.Type == "return" && entry.Cards.Any(card => card.InstanceId == hidden.InstanceId));
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, JsonSerializer.Serialize(game.State),
            game.RandomState!.Value, 0, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        Assert.Contains(restored.SnapshotFor(0).RecentEvents,
            entry => entry.Type == "return" && entry.Cards.Any(card => card.InstanceId == hidden.InstanceId));
        foreach (var snapshot in new[] { game.SnapshotFor(1), game.SnapshotForSpectator(), game.SnapshotForReferee(),
                     restored.SnapshotFor(1), restored.SnapshotForSpectator(), restored.SnapshotForReferee() })
        {
            var returned = Assert.Single(snapshot.RecentEvents, entry => entry.Type == "return");
            Assert.Empty(returned.Cards);
            Assert.DoesNotContain(hidden.Name, returned.Text);
            Assert.Null(returned.EffectText);
        }
    }

    [Fact]
    public void ZhugeSeesPrivateDisasterBeforeChoosingAdjustment()
    {
        var game = Create(); var owner = game.State.Players[0];
        var source = Card(game, "S01-0111", "zhuge"); owner.Field[0][0] = source;
        game.State.DisasterDeck.Clear();
        var disaster = Card(game, "S01-DS01", "secret-disaster"); game.State.DisasterDeck.Add(disaster);
        Call(game, "QueueOrPushTriggeredEffect", 0, source, "enter", "登场时效果", null, new Dictionary<string, string>());
        Pass(game);
        Assert.True(game.State.PendingPrompts.Count > 0, string.Join(";", game.State.Events.TakeLast(10).Select(entry => entry.Text)));
        var preview = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("private-view-confirm", preview.Data.GetValueOrDefault("action"));
        Assert.Equal(disaster.InstanceId, preview.Data["displayCardIds"]);
        Assert.DoesNotContain(game.SnapshotFor(1).RecentEvents, entry => entry.Cards.Any(card => card.InstanceId == disaster.InstanceId));
        var before = game.State.DisasterValue;
        Resolve(game, "confirm");
        Assert.Contains("mode:use", Assert.Single(game.State.PendingPrompts).ValidChoices);
        Assert.Equal(before, game.State.DisasterValue);
        Resolve(game, "mode:use");
        Resolve(game, "1");
        Pass(game);
        Assert.Equal(before + 1, game.State.DisasterValue);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void KusanagiExcludesCostlessButIncludesRealCostBoundary(int cost, bool legal)
    {
        var game = Create();
        var sword = Card(game, "S01-0417", "sword");
        game.State.Players[0].Relic = sword;
        var dog = Card(game, "S02-01S1", "costless");
        Assert.False(dog.HasPrintedCost);
        var target = Card(game, "S01-0003", "priced");
        target.CostModifier = cost - target.Cost;
        game.State.Players[1].Field[0][0] = dog;
        game.State.Players[1].Field[0][1] = target;
        Call(game, "QueueOrPushTriggeredEffect", 0, sword, "enter", "登场时效果", null, new Dictionary<string, string>());
        var choices = game.State.PendingPrompts.SelectMany(prompt => prompt.ValidChoices).ToArray();
        Assert.DoesNotContain(dog.InstanceId, choices);
        Assert.Equal(legal, choices.Contains(target.InstanceId));
    }

    [Theory]
    [InlineData("ds01-free-tactic", 0)]
    [InlineData("ds01-back-master", 4)]
    [InlineData("", 4)]
    public void MorningStarHeavenlyPunishmentUsesOnlyFreeBranch(string state, int expectedCost)
    {
        var game = Create(); var owner = game.State.Players[0];
        if (state.Length > 0) owner.UsedAbilities.Add(state);
        for (var i = 0; i < 4; i++) owner.Morale.Add(new L12MoraleCard { InstanceId = $"morale-{i}", CardId = "S01-04C1" });
        var tactic = Card(game, "S01-0418", "heavenly-punishment"); owner.Hand.Add(tactic);
        var target = Card(game, "S01-0003", "target"); game.State.Players[1].Field[0][0] = target;
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(expectedCost, snapshot.GetProperty("players")[0].GetProperty("hand").EnumerateArray()
            .Single(card => card.GetProperty("instanceId").GetString() == tactic.InstanceId).GetProperty("playCost").GetInt32());
        Assert.True(game.Handle(0, new L12Command("playCard", tactic.InstanceId)).Accepted);
        Resolve(game, target.InstanceId);
        Assert.Equal(expectedCost, owner.Morale.Count(card => card.Tapped));
    }

    [Fact]
    public void MorningStarFreeBranchSurvivesMultipleActiveTacticsAndExpiresWithTurn()
    {
        var game = Create(); var owner = game.State.Players[0];
        owner.UsedAbilities.Add("ds01-free-tactic");
        for (var i = 0; i < 2; i++)
        {
            var tactic = Card(game, "S01-0418", $"free-tactic-{i}"); owner.Hand.Add(tactic);
            var target = Card(game, "S01-0003", $"free-target-{i}");
            game.State.Players[1].Field[0][0] = target;
            var result = game.Handle(0, new L12Command("playCard", tactic.InstanceId));
            Assert.True(result.Accepted, result.Error);
            Resolve(game, target.InstanceId);
            Pass(game);
            Assert.Contains("ds01-free-tactic", owner.UsedAbilities);
            Assert.Empty(owner.Morale);
        }
        Call(game, "CompleteEndTurn", 0);
        Assert.DoesNotContain("ds01-free-tactic", owner.UsedAbilities);
    }

    [Theory]
    [InlineData(true, true, 1)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 2)]
    [InlineData(false, false, 2)]
    public void DisasterDamageIgnoresRingButPlayerDamageStillUsesIt(bool neutral, bool nonLethal, int expected)
    {
        var game = Create(); var owner = game.State.Players[0];
        game.State.ActivePlayer = 1;
        owner.Relic = Card(game, "S02-0305", "ring");
        owner.Hp = 8; owner.MasterDamageTakenThisTurn = 0;
        if (nonLethal) Call(game, "DamageMasterNonLethal", 0, 1, "test", 1, neutral);
        else Call(game, "DamageMaster", 0, 1, "test", 1, neutral, false);
        Assert.Equal(8 - expected, owner.Hp);
        Assert.Equal(expected, owner.MasterDamageTakenThisTurn);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RunePowerOnlyAsksPaymentWhenOutcomesDiffer(bool mixed)
    {
        var game = Create(); var owner = game.State.Players[0];
        owner.UsedAbilities.Add("ds01-free-tactic"); // 免费打出不免除效果正文的士气费用。
        var source = Card(game, "S02-0620", "runes"); owner.Resolving.Add(source);
        for (var i = 0; i < 3; i++) owner.Morale.Add(new L12MoraleCard { InstanceId = $"rune-morale-{i}", CardId = "S01-04C1" });
        if (mixed) owner.TemporaryMorale = 1;
        owner.Library.Add(Card(game, "S02-0609", "private-pick"));
        var item = new L12StackItem { StackItemId = "rune-stack", Controller = 0, SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId, SourceName = source.Name, Trigger = "play", Text = "符文之力" };
        game.State.EffectStack.Add(item);
        var prompt = new L12Prompt { PromptId = "mode", PlayerIndex = 0, Kind = "option", Text = "可选择",
            ValidChoices = ["mode:search"], Continuation = "card-effect", Data = new Dictionary<string, string> { ["action"] = "s2-rune-power-mode" } };
        Call(game, "TryContinueS2Faction", item, prompt, new List<string> { "mode:search" }, new L12Command("resolvePrompt"));
        Assert.Equal(mixed, game.State.PendingPrompts.Any(p => p.Kind == "resource-payment"));
        Assert.Equal(mixed ? 0 : 1, owner.Morale.Count(card => card.Tapped));
    }

    [Theory]
    [InlineData("ordinary", true)]
    [InlineData("temporary", true)]
    [InlineData("lotus", true)]
    [InlineData("mixed", false)]
    public void DeclarationAutomaticallySelectsOnlyEquivalentPaymentOutcomes(string kind, bool automatic)
    {
        var game = Create(); var owner = game.State.Players[0];
        var choices = new List<string>();
        for (var i = 1; i <= 3; i++)
        {
            var id = kind == "temporary" ? $"temporary-morale:{i}" : $"payment-{i}";
            choices.Add(id);
            if (kind == "temporary") owner.TemporaryMorale++;
            else owner.Morale.Add(new L12MoraleCard { InstanceId = id, CardId = kind == "lotus" ? "S02-0010" : "S01-04C1" });
        }
        if (kind == "mixed") { owner.TemporaryMorale = 1; choices.Add("temporary-morale:1"); }
        var step = new L12ActivationSelectionStep { Kind = "resource-payment", Text = "支付1士气",
            MinChoose = 1, MaxChoose = 1, ValidChoices = choices, AutoSelectEquivalentOrdinaryMorale = true };
        var activation = new L12PendingActivation { ActivationId = "payment", Controller = 0, SourceCardId = "S02-0620",
            SourceInstanceId = "rune", Ability = "test-payment", Text = "支付", ValidChoices = choices, SelectionSteps = [step] };
        Assert.Equal(automatic, Call(game, "DeterministicCostSelection", activation, step) is not null);
    }

    [Theory]
    [InlineData(true, "all", 3)]
    [InlineData(false, "all", 3)]
    [InlineData(true, "skip-change", 2)]
    [InlineData(false, "skip-change", 2)]
    [InlineData(true, "skip-faction", 1)]
    [InlineData(false, "skip-faction", 1)]
    [InlineData(true, "negate-change", 2)]
    [InlineData(false, "negate-change", 2)]
    [InlineData(true, "negate-faction", 1)]
    [InlineData(false, "negate-faction", 1)]
    public void ChangeAndTiantingReturnEventOfferBothOrders(bool changeFirst, string outcome, int expectedMorale)
    {
        var game = Create("ST01-M1"); var owner = game.State.Players[0];
        Call(game, "RegisterReturnedMorale", owner, 1);
        Call(game, "FlushStarterResourceTriggerBatches");
        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-batch-order", order.Continuation);
        var candidates = game.State.PendingTriggerBatches.SelectMany(batch => batch.Candidates).ToArray();
        Assert.Equal(2, candidates.Length);
        var ids = candidates.OrderBy(candidate => (candidate.SourceCardId == "ST01-M1") == changeFirst ? 0 : 1)
            .Select(candidate => candidate.CandidateId).ToArray();
        Resolve(game, ids);
        for (var i = 0; i < 20 && game.State.PendingPrompts.Count > 0; i++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            foreach (var pendingEffect in game.State.EffectStack)
                if (outcome == (pendingEffect.SourceCardId == "ST01-M1" ? "negate-change" : "negate-faction"))
                    pendingEffect.Negated = true;
            if (prompt.Kind == "response")
            {
                Resolve(game, "pass");
            }
            else
            {
                var activation = game.State.PendingActivations.Single(a => a.ActivationId == prompt.ActivationId);
                var skip = outcome == (activation.SourceCardId == "ST01-M1" ? "skip-change" : "skip-faction");
                Resolve(game, skip ? "mode:none" : "mode:use");
            }
        }
        Assert.Empty(game.State.PendingPrompts);
        Assert.Equal(expectedMorale, owner.Morale.Count);
        Assert.All(owner.Morale, morale => Assert.True(morale.Tapped));
        Call(game, "FlushStarterResourceTriggerBatches");
        Assert.Empty(game.State.PendingPrompts);
        Assert.Equal(expectedMorale, owner.Morale.Count);
    }
}
