using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

// Synthetic, deterministic authority fixtures; no real match or player data.
public sealed class LibraryReturnVisibilityAndBatchFactsTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void GraveBatchKeepsEveryMovementAndCountsOnlyActualDistinctReturns(int count)
    {
        var game = Create();
        var player = game.State.Players[0];
        var cards = Enumerable.Range(0, count).Select(index => Card("S01-0003", $"grave-{index}", 0)).ToArray();
        player.Graveyard.AddRange(cards);
        var absent = Card("S01-0001", "not-in-grave", 0);
        Invoke(game, "MoveGraveToLibraryBottom", player, cards.Concat(cards).Append(absent));
        var returned = game.State.Events.Where(entry => entry.PlayerLibraryReturn is not null).ToArray();
        Assert.Equal(count, returned.Length);
        Assert.Equal(cards.Select(card => card.InstanceId), returned.Select(entry => Assert.Single(entry.Cards).InstanceId));
        if (count == 0) return;
        Assert.Single(returned.Select(entry => entry.PlayerLibraryReturn!.OperationId).Distinct());
        foreach (var entry in returned)
        {
            var fact = entry.PlayerLibraryReturn!;
            Assert.Equal(1, fact.BottomCount); Assert.Equal(0, fact.TopCount); Assert.True(fact.IdentitiesPublic);
            Assert.Matches("^library-return:0:[1-9][0-9]*$", fact.OperationId);
        }
        AssertProjectionAndReplay(game);
        Export($"grave-multi-{count}", game);
    }

    [Fact]
    public void IndependentReturnsDoNotReuseAnEffectOrCostWideOperation()
    {
        var game = Create(); var player = game.State.Players[0];
        var first = Card("S01-0003", "cost-return", 0); var second = Card("S01-0003", "effect-return", 0);
        player.Graveyard.AddRange([first, second]);
        Invoke(game, "MoveGraveToLibraryBottom", player, new[] { first }.AsEnumerable());
        Invoke(game, "MoveGraveToLibraryBottom", player, new[] { second }.AsEnumerable());
        var receipts = game.State.Events.Where(entry => entry.PlayerLibraryReturn is not null).ToArray();
        Assert.Equal(2, receipts.Select(entry => entry.PlayerLibraryReturn!.OperationId).Distinct().Count());
        Export("independent-cost-effect", game);
    }

    [Fact]
    public void ReceiptUpdatesLastActionJournalTailAndCachedHashTogetherAndRejectsSummaryEvents()
    {
        var game = Create();
        var sequence = (long)Invoke(game, "AddEvent", "return", 0, "真实返回", Array.Empty<L12CardInstance>())!;
        var before = game.ComputeStateHash();
        Invoke(game, "AttachLibraryReturnFact", sequence, 0, 1, 0, 0, false, null);
        Assert.NotEqual(before, game.ComputeStateHash());
        Assert.Equal(game.State.Events.Single(entry => entry.Sequence == sequence), game.State.LastAction);
        AssertProjectionAndReplay(game);
        var cost = (long)Invoke(game, "AddEvent", "cost", 0, "费用汇总", Array.Empty<L12CardInstance>())!;
        var rejected = Assert.Throws<TargetInvocationException>(() =>
            Invoke(game, "AttachLibraryReturnFact", cost, 0, 1, 0, 0, true, null));
        Assert.IsType<InvalidOperationException>(rejected.InnerException);
        Assert.Null(game.State.LastAction!.PlayerLibraryReturn);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SingleFieldLeaveUsesActualOwnerAndVisibilityBeforeReset(bool hidden, bool top)
    {
        var game = Create();
        var card = Card("S01-0003", "single-field-secret", 1); card.Hidden = hidden;
        game.State.Players[0].Field[0][0] = card;
        Assert.True((bool)Invoke(game, "MoveFieldCardToZone", game.State.Players[0], card,
            top ? "library-top" : "library-bottom", "返回牌库", true)!);
        var receipt = Assert.Single(game.State.Events, entry => entry.PlayerLibraryReturn is not null);
        Assert.Equal("leave", receipt.Type); Assert.Equal(0, receipt.PlayerIndex);
        Assert.Equal(1, receipt.PlayerLibraryReturn!.LibraryOwner);
        Assert.Equal(!hidden, receipt.PlayerLibraryReturn.IdentitiesPublic);
        Assert.Equal(top ? 1 : 0, receipt.PlayerLibraryReturn.TopCount);
        Assert.Equal(top ? 0 : 1, receipt.PlayerLibraryReturn.BottomCount);
        Assert.Contains(card, game.State.Players[1].Library);
        AssertMemberVisibility(game, card, hidden, receipt.Sequence);
        AssertProjectionAndReplay(game);
        Export($"field-single-{hidden}-{top}", game);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void PairedReturnAndMixedPromotionFoundationsNeverBorrowEachOthersVisibility(
        bool hostHidden, bool foundationHidden, bool differentOwner)
    {
        var game = Create();
        var host = Card("S02-0501", "mixed-host", 1); host.Hidden = hostHidden;
        var foundation = Card("S02-0502", "mixed-foundation", differentOwner ? 0 : 1);
        foundation.Hidden = foundationHidden;
        host.AttachedCards.Add(foundation);
        game.State.Players[0].Field[0][0] = host;
        object?[] args = [0, host.InstanceId, "返回所有者牌库顶部", null, null];
        Assert.True((bool)Invoke(game, "TryMoveAuthoritativeCardToOwnerLibraryTop", args)!);
        var returned = Assert.Single(game.State.Events, entry => entry.Type == "return");
        var leave = Assert.Single(game.State.Events, entry => entry.Type == "leave");
        Assert.Null(leave.PlayerLibraryReturn);
        Assert.Equal(leave.PlayerLogGroupId, returned.PlayerLibraryReturn!.OperationId);
        Assert.Equal(0, leave.PlayerIndex); Assert.Equal(1, returned.PlayerIndex);
        var bottom = Assert.Single(game.State.Events, entry => entry.Type == "promotion-stack-leave");
        Assert.NotNull(bottom.PlayerLibraryReturn);
        Assert.Equal(!foundationHidden, bottom.PlayerLibraryReturn!.IdentitiesPublic);
        Assert.Equal(differentOwner ? 0 : 1, bottom.PlayerLibraryReturn.LibraryOwner);
        Assert.Equal(!differentOwner, bottom.PlayerLibraryReturn.OperationId == returned.PlayerLibraryReturn.OperationId);
        Assert.Equal(2, game.State.Events.Sum(entry => entry.PlayerLibraryReturn?.TopCount ?? 0));
        AssertMemberVisibility(game, host, hostHidden, returned.Sequence);
        AssertMemberVisibility(game, host, hostHidden, leave.Sequence);
        AssertMemberVisibility(game, foundation, foundationHidden, bottom.Sequence);
        AssertProjectionAndReplay(game);
        Export($"field-paired-{hostHidden}-{foundationHidden}-{differentOwner}", game);
    }

    [Fact]
    public void DepartureReplacementAndMissingOrDuplicateInstancePublishNoFalseReturn()
    {
        var game = Create();
        var token = Card("S01-0003", "vanishing", 0, "token");
        game.State.Players[0].Field[0][0] = token;
        object?[] args = [0, token.InstanceId, "请求回顶", null, null];
        Assert.True((bool)Invoke(game, "TryMoveAuthoritativeCardToOwnerLibraryTop", args)!);
        Assert.DoesNotContain(game.State.Events, entry => entry.PlayerLibraryReturn is not null || entry.Type == "return");
        Assert.DoesNotContain(token, game.State.Players[0].Library);
        var duplicate = Card("S01-0003", "duplicate", 0);
        game.State.Players[0].Hand.Add(duplicate); game.State.Players[0].Graveyard.Add(duplicate);
        foreach (var id in new[] { "missing", duplicate.InstanceId })
        {
            args = [0, id, "无效请求", null, null];
            Assert.False((bool)Invoke(game, "TryMoveAuthoritativeCardToOwnerLibraryTop", args)!);
        }
        Assert.DoesNotContain(game.State.Events, entry => entry.PlayerLibraryReturn is not null);
    }

    [Theory]
    [InlineData("relic", false)]
    [InlineData("relic", true)]
    [InlineData("graveyard", false)]
    [InlineData("hand", false)]
    [InlineData("library", false)]
    public void AuthoritativeReturnKeepsPublicOriginsAndProtectsPrivateOrigins(string zone, bool hidden)
    {
        var game = Create(); var owner = game.State.Players[1];
        var card = Card("S01-0417", $"authority-{zone}-{hidden}", 1); card.Hidden = hidden;
        var host = game.State.Players[0];
        switch (zone)
        {
            case "relic": host.Relic = card; break;
            case "graveyard": host.Graveyard.Add(card); break;
            case "hand": host.Hand.Add(card); break;
            default: host.Library.Add(card); break;
        }
        object?[] args = [0, card.InstanceId, "真实来源返回牌库顶部", null, null];
        Assert.True((bool)Invoke(game, "TryMoveAuthoritativeCardToOwnerLibraryTop", args)!);
        var receipt = Assert.Single(game.State.Events, entry => entry.PlayerLibraryReturn is not null);
        var identityHidden = hidden || zone is "hand" or "library";
        Assert.Equal(!identityHidden, receipt.PlayerLibraryReturn!.IdentitiesPublic);
        AssertMemberVisibility(game, card, identityHidden, receipt.Sequence);
        Assert.Same(card, owner.Library[0]);
        foreach (var leave in game.State.Events.Where(entry => entry.Type == "leave"))
        {
            Assert.Equal(receipt.PlayerLibraryReturn.OperationId, leave.PlayerLogGroupId);
            Assert.Null(leave.PlayerLibraryReturn);
            AssertMemberVisibility(game, card, identityHidden, leave.Sequence);
        }
        AssertProjectionAndReplay(game);
        Export($"authoritative-{zone}-{hidden}", game);
    }

    [Fact]
    public void SingleLeaveAndPromotionFoundationKeepOneOperationDespiteOriginalActorsDiffering()
    {
        var game = Create(); var host = Card("S02-0501", "single-promoted-host", 1);
        var foundation = Card("S02-0502", "single-promoted-foundation", 1); foundation.Hidden = true;
        host.AttachedCards.Add(foundation); game.State.Players[0].Field[0][0] = host;
        Assert.True((bool)Invoke(game, "MoveFieldCardToZone", game.State.Players[0], host,
            "library-bottom", "返回牌库底部", true)!);
        var receipts = game.State.Events.Where(entry => entry.PlayerLibraryReturn is not null).ToArray();
        Assert.Equal(2, receipts.Length);
        Assert.Single(receipts.Select(entry => entry.PlayerLibraryReturn!.OperationId).Distinct());
        Assert.Equal(2, receipts.Select(entry => entry.PlayerIndex).Distinct().Count());
        AssertMemberVisibility(game, host, false, receipts.Single(entry => entry.Type == "leave").Sequence);
        AssertMemberVisibility(game, foundation, true, receipts.Single(entry => entry.Type == "promotion-stack-leave").Sequence);
        Export("single-leave-mixed-actors-promotion", game);
    }

    [Fact]
    public void CorruptedPairedMemberCannotBorrowAnotherPublicMembersIdentityPermission()
    {
        var game = Create(); var host = Card("S02-0501", "corrupt-hidden-host", 1); host.Hidden = true;
        host.AttachedCards.Add(Card("S02-0502", "corrupt-public-foundation", 1));
        game.State.Players[0].Field[0][0] = host;
        object?[] args = [0, host.InstanceId, "返回牌库顶部", null, null];
        Assert.True((bool)Invoke(game, "TryMoveAuthoritativeCardToOwnerLibraryTop", args)!);
        var hostReturn = game.State.Events.Single(entry => entry.Type == "return");
        game.State.Events.Remove(hostReturn);
        var leave = game.State.Events.Single(entry => entry.Type == "leave");
        Assert.Empty(game.SnapshotForSpectator().RecentEvents.Single(entry => entry.Sequence == leave.Sequence).Cards);
        Assert.DoesNotContain(host.InstanceId, JsonSerializer.Serialize(
            game.SnapshotForReferee().RecentEvents.Single(entry => entry.Sequence == leave.Sequence), Wire));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 3)]
    public void ActualPublicRevealAndPrivateInspectionKeepDifferentRemainderVisibility(bool publiclyRevealed, int count)
    {
        var game = Create();
        var source = publiclyRevealed ? "S01-02D1" : "S01-0315";
        var item = Stack(game, source);
        var cards = Enumerable.Range(0, count).Select(index => Card("S01-0003", $"inspect-{index}", 0)).ToArray();
        game.State.Players[0].Library.AddRange(cards);
        Invoke(game, "BeginFactionTopSearch", item, 3,
            publiclyRevealed ? "taiyangcheng" : "asgard", string.Empty,
            publiclyRevealed ? "sun-divinity" : "ivar-search");
        game = Restore(game);
        if (count > 1)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            var command = new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                BottomCardInstanceIds: cards.Reverse().Select(card => card.InstanceId).ToList());
            Assert.False(game.Handle(0, command with { BottomCardInstanceIds = [cards[0].InstanceId] }).Accepted);
            Assert.DoesNotContain(game.State.Events, entry => entry.PlayerLibraryReturn is not null);
            Assert.True(game.Handle(0, command).Accepted);
            var factsBeforeRetry = game.State.Events.Count(entry => entry.PlayerLibraryReturn is not null);
            Assert.False(game.Handle(0, command).Accepted);
            Assert.Equal(factsBeforeRetry, game.State.Events.Count(entry => entry.PlayerLibraryReturn is not null));
        }
        var receipts = game.State.Events.Where(entry => entry.PlayerLibraryReturn is not null).ToArray();
        if (count == 0) { Assert.Empty(receipts); return; }
        var receipt = Assert.Single(receipts);
        Assert.Equal("reorder", receipt.Type);
        Assert.Equal(count, receipt.PlayerLibraryReturn!.BottomCount);
        Assert.Equal(publiclyRevealed, receipt.PlayerLibraryReturn.IdentitiesPublic);
        foreach (var view in Views(game))
        {
            var projected = Assert.Single(view.RecentEvents, entry => entry.PlayerLibraryReturn is not null);
            if (publiclyRevealed) Assert.Equal(count, projected.Cards.Length);
            else Assert.Empty(projected.Cards);
        }
        AssertProjectionAndReplay(game);
        Export($"inspection-{publiclyRevealed}-{count}", game);
    }

    [Fact]
    public void CorruptedMultiCardLeaveCannotBorrowPublicSingletonReturnReceipt()
    {
        var game = Create();
        var secret = Card("S01-0003", "mixed-leave-hidden", 0); secret.Hidden = true;
        var visible = Card("S01-0001", "mixed-leave-public", 0);
        var first = ++game.State.EventSequence;
        var operation = $"library-return:0:{first}";
        game.State.Events.Add(new(first, "leave", 0, "损坏的混合离场记录", [secret, visible])
            { PlayerLogGroupId = operation });
        game.State.Events.Add(new(++game.State.EventSequence, "return", 0, "公开单卡回库", [visible])
        {
            PlayerLogGroupId = operation,
            PlayerLibraryReturn = new(operation, 0, 1, 0, 0, true),
        });
        foreach (var view in Views(game))
        {
            var leave = view.RecentEvents.Single(entry => entry.Sequence == first);
            Assert.Empty(leave.Cards);
            Assert.DoesNotContain(secret.InstanceId, JsonSerializer.Serialize(leave, Wire));
        }
    }

    [Theory]
    [InlineData("field")]
    [InlineData("relic")]
    public void MissingPairedReceiptNeverExposesIdentityResetByTheActualReturn(string zone)
    {
        var game = Create();
        var secret = Card("S01-0003", "missing-receipt-secret", 0); secret.Hidden = true;
        if (zone == "field") game.State.Players[0].Field[0][0] = secret;
        else game.State.Players[0].Relic = secret;
        object?[] args = [0, secret.InstanceId, "返回所有者牌库顶部", null, null];
        Assert.True((bool)Invoke(game, "TryMoveAuthoritativeCardToOwnerLibraryTop", args)!);
        var leave = Assert.Single(game.State.Events, entry => entry.Type == "leave");
        Assert.False(Assert.Single(leave.Cards).Hidden);
        game.State.Events.RemoveAll(entry => entry.PlayerLibraryReturn?.OperationId == leave.PlayerLogGroupId);
        Assert.DoesNotContain(game.State.Events, entry => entry.PlayerLibraryReturn is not null);
        foreach (var view in Views(game))
        {
            var projected = view.RecentEvents.Single(entry => entry.Sequence == leave.Sequence);
            Assert.Empty(projected.Cards);
            Assert.DoesNotContain(secret.InstanceId, JsonSerializer.Serialize(projected, Wire));
        }
    }

    [Theory]
    [InlineData("top")]
    [InlineData("bottom")]
    public void LiJingRevealedSingletonRetainsPublicNameAndDirection(string destination)
    {
        var game = Create(); var item = Stack(game, "S01-0103");
        var card = Card("S01-0003", "lijing-public", 0);
        game.State.Players[0].Library.Add(card);
        Invoke(game, "BeginLiJingEffect", item);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: destination)).Accepted);
        var receipt = Assert.Single(game.SnapshotForSpectator().RecentEvents, entry => entry.PlayerLibraryReturn is not null);
        Assert.True(receipt.PlayerLibraryReturn!.IdentitiesPublic);
        Assert.Single(receipt.Cards, shown => shown.InstanceId == card.InstanceId);
        Assert.Equal(destination == "top" ? 1 : 0, receipt.PlayerLibraryReturn.TopCount);
        Assert.Equal(destination == "bottom" ? 1 : 0, receipt.PlayerLibraryReturn.BottomCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void LakeLadyPublishesActualPublicBatchAsShuffledNotTopOrBottom(int count)
    {
        var game = Create(); var item = Stack(game, "S02-06S3");
        item.Data["trialCompletionPlan"] = "lake-lady"; item.Data["trialSegment"] = "1";
        var cards = Enumerable.Range(0, count).Select(index => Card("S02-0601", $"arthur-{index}", 0)).ToArray();
        game.State.Players[0].Graveyard.AddRange(cards);
        Invoke(game, "ResolveTrialCompletionTriggerEffect", item);
        var receipts = game.State.Events.Where(entry => entry.PlayerLibraryReturn is not null).ToArray();
        Assert.Equal(count, receipts.Length);
        foreach (var receipt in receipts)
        {
            Assert.Equal(1, receipt.PlayerLibraryReturn!.ShuffledCount);
            Assert.Equal(0, receipt.PlayerLibraryReturn.TopCount + receipt.PlayerLibraryReturn.BottomCount);
            Assert.True(receipt.PlayerLibraryReturn.IdentitiesPublic);
        }
        if (count > 0) Assert.Single(receipts.Select(entry => entry.PlayerLibraryReturn!.OperationId).Distinct());
        AssertProjectionAndReplay(game);
    }

    [Fact]
    public void ScoutHiddenHandReturnPublishesShuffleButNeverAttackerIdentity()
    {
        var game = Create(); var item = Stack(game, "S01-0013");
        var card = Card("S01-0003", "scout-secret", 1); game.State.Players[1].Hand.Add(card);
        var prompt = new L12Prompt { PromptId = "scout", PlayerIndex = 0, Kind = "hand-card", Text = "合成侦查",
            ValidChoices = [card.InstanceId], Continuation = "card-effect", Data = new() { ["action"] = "scout-shuffle" } };
        Invoke(game, "TryContinueS1Extended", item, prompt, new List<string> { card.InstanceId }, new L12Command("resolvePrompt"));
        var receipt = Assert.Single(game.State.Events, entry => entry.PlayerLibraryReturn is not null);
        Assert.Equal(1, receipt.PlayerLibraryReturn!.ShuffledCount);
        Assert.Equal(1, receipt.PlayerLibraryReturn.LibraryOwner); Assert.False(receipt.PlayerLibraryReturn.IdentitiesPublic);
        AssertMemberVisibility(game, card, true, receipt.Sequence);
        AssertProjectionAndReplay(game);
    }

    [Fact]
    public void ApocalypseKeepsTwoOwnersSeparateAndPublishesOnlyHandCountsAndBottom()
    {
        var game = Create(); var item = Stack(game, "S01-DS07");
        for (var owner = 0; owner < 2; owner++)
        {
            var player = game.State.Players[owner];
            player.Hand.AddRange(Enumerable.Range(0, owner + 2)
                .Select(index => Card("S01-0003", $"apocalypse-hand-{owner}-{index}", owner)));
            player.Library.AddRange(Enumerable.Range(0, 8)
                .Select(index => Card("S01-0001", $"apocalypse-deck-{owner}-{index}", owner)));
            item.Data[$"apocalypse-hand-order:{owner}"] = string.Join('|', player.Hand.Select(card => card.InstanceId));
        }
        Invoke(game, "CompleteApocalypseHands", item);
        var receipts = game.State.Events.Where(entry => entry.PlayerLibraryReturn is not null).ToArray();
        Assert.Equal(5, receipts.Length);
        Assert.Equal(2, receipts.Select(entry => entry.PlayerLibraryReturn!.OperationId).Distinct().Count());
        foreach (var owner in new[] { 0, 1 })
        {
            var owned = receipts.Where(entry => entry.PlayerLibraryReturn!.LibraryOwner == owner).ToArray();
            Assert.Equal(owner + 2, owned.Length);
            Assert.All(owned, entry => AssertMemberVisibility(game,
                game.State.Players[owner].Library.Single(card => card.InstanceId == entry.Cards.Single().InstanceId),
                true, entry.Sequence));
        }
        AssertProjectionAndReplay(game);
        Export("apocalypse-both-owners", game);
    }

    [Fact]
    public void BlindSupplyReturnNeverTreatsAttackerAsIdentityOwner()
    {
        var game = Create(); var item = Stack(game, "S02-0017");
        var card = Card("S01-0003", "blind-hand", 1); game.State.Players[1].Hand.Add(card);
        item.Data["affectedPlayer"] = "1"; item.Data["declared:handTarget"] = card.InstanceId;
        Invoke(game, "ResolveS2CounterEffect", item);
        var receipt = Assert.Single(game.State.Events, entry => entry.PlayerLibraryReturn is not null);
        Assert.Equal(1, receipt.PlayerLibraryReturn!.LibraryOwner);
        Assert.Equal(1, receipt.PlayerLibraryReturn.TopCount);
        Assert.False(receipt.PlayerLibraryReturn.IdentitiesPublic);
        foreach (var view in new[] { game.SnapshotFor(0), game.SnapshotForSpectator(), game.SnapshotForReferee() })
        {
            var visible = view.RecentEvents.Single(entry => entry.Sequence == receipt.Sequence);
            Assert.Empty(visible.Cards); Assert.DoesNotContain(card.InstanceId, JsonSerializer.Serialize(visible, Wire));
        }
        AssertProjectionAndReplay(game);
    }

    [Fact]
    public void AbsentCapabilityPreservesLegacyCheckpointAndDoesNotAppendNewFacts()
    {
        var modern = Create();
        var root = JsonNode.Parse(modern.SerializeFullState())!.AsObject();
        Assert.True(root.Remove("LibraryReturnFactsProtocolEnabled"));
        var legacy = L12GameEngine.RestoreCheckpoint(Catalog, root.ToJsonString(), modern.RandomState!.Value,
            modern.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        Assert.False(legacy.State.LibraryReturnFactsProtocolEnabled);
        var before = legacy.SerializeFullState();
        var roundtrip = Restore(legacy);
        Assert.Equal(before, roundtrip.SerializeFullState());
        var card = Card("S01-0003", "legacy-grave", 0); legacy.State.Players[0].Graveyard.Add(card);
        Invoke(legacy, "MoveGraveToLibraryBottom", legacy.State.Players[0], new[] { card }.AsEnumerable());
        Assert.Null(Assert.Single(legacy.State.Events, entry => entry.Type == "return").PlayerLibraryReturn);
        Assert.DoesNotContain("PlayerLibraryReturn", legacy.SerializeFullState(), StringComparison.Ordinal);
        Assert.DoesNotContain("LibraryReturnFactsProtocolEnabled", legacy.SerializeFullState(), StringComparison.Ordinal);
    }

    private static void AssertMemberVisibility(L12GameEngine game, L12CardInstance card, bool wasHidden, long sequence)
    {
        var ownerEvent = game.SnapshotFor(card.OwnerIndex!.Value).RecentEvents.Single(entry => entry.Sequence == sequence);
        Assert.Contains(ownerEvent.Cards, shown => shown.InstanceId == card.InstanceId);
        foreach (var view in new[] { game.SnapshotFor(1 - card.OwnerIndex.Value), game.SnapshotForSpectator(), game.SnapshotForReferee() })
        {
            var entry = view.RecentEvents.Single(entry => entry.Sequence == sequence);
            if (!wasHidden) { Assert.Contains(entry.Cards, shown => shown.InstanceId == card.InstanceId); continue; }
            var json = JsonSerializer.Serialize(entry, Wire);
            Assert.Empty(entry.Cards);
            Assert.DoesNotContain(card.InstanceId, json, StringComparison.Ordinal);
            Assert.DoesNotContain(card.CardId, json, StringComparison.Ordinal);
            Assert.DoesNotContain(card.Name, json, StringComparison.Ordinal);
        }
    }

    private static void AssertProjectionAndReplay(L12GameEngine game)
    {
        var unpersisted = (IReadOnlyList<L12ActionEvent>)typeof(L12GameEngine)
            .GetProperty("UnpersistedEvents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        foreach (var receipt in game.State.Events.Where(entry => entry.PlayerLibraryReturn is not null
                     && unpersisted.Any(pending => pending.Sequence == entry.Sequence)))
            Assert.Equal(JsonSerializer.Serialize(receipt, Wire), JsonSerializer.Serialize(unpersisted.Single(entry => entry.Sequence == receipt.Sequence), Wire));
        var restored = Restore(game);
        Assert.Equal(game.SerializeFullState(), restored.SerializeFullState());
        var recorder = typeof(L12GameEngine).Assembly.GetType("TwelveLegions.Server.MatchRecorder")!;
        var projection = recorder.GetMethod("ProjectRecordedDisastersAndEvents", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var viewer in new[] { 0, 1, -1 })
        {
            var replay = new JsonObject(); projection.Invoke(null, [replay, game.State, viewer]);
            var replayEvents = replay["Events"]!.Deserialize<L12ActionEvent[]>()!;
            var liveEvents = viewer < 0 ? game.SnapshotForSpectator().RecentEvents : game.SnapshotFor(viewer).RecentEvents;
            Assert.Equal(JsonSerializer.Serialize(liveEvents, Wire), JsonSerializer.Serialize(replayEvents, Wire));
        }
    }

    internal static void Export(string name, L12GameEngine game)
    {
        var root = Environment.GetEnvironmentVariable("L12_LIBRARY_RETURN_FIXTURE_ROOT");
        if (string.IsNullOrEmpty(root)) return;
        // Only enabled by the governed D-root focused test entry; never default to cwd.
        if (!Path.IsPathFullyQualified(root) || Path.GetPathRoot(root)!.StartsWith("C:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fixture output requires the explicitly governed D/E root.");
        Directory.CreateDirectory(root);
        var fixture = new { scenario = name, synthetic = true, authority = game.State.Events,
            player0 = game.SnapshotFor(0).RecentEvents, player1 = game.SnapshotFor(1).RecentEvents,
            spectator = game.SnapshotForSpectator().RecentEvents, referee = game.SnapshotForReferee().RecentEvents };
        File.WriteAllText(Path.Combine(root, name + ".json"), JsonSerializer.Serialize(fixture, Wire));
    }

    private static IEnumerable<L12GameSnapshot> Views(L12GameEngine game)
        => [game.SnapshotFor(0), game.SnapshotFor(1), game.SnapshotForSpectator(), game.SnapshotForReferee()];
    private static L12GameEngine Restore(L12GameEngine game) => L12GameEngine.RestoreCheckpoint(Catalog,
        game.SerializeFullState(), game.RandomState!.Value, game.CardFactSignalSequence,
        autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
    private static L12GameEngine Create()
    {
        var game = new L12GameEngine(Catalog, "return-facts-synthetic", "RETURN", 20261023, ["甲", "乙"], [0, 0],
            skipPreparation: true, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActiveDisaster = null; game.State.Phase = L12Phase.Main; game.State.ActivePlayer = 0;
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.Hand.Clear(); player.Library.Clear(); player.Graveyard.Clear(); player.Morale.Clear();
            player.Relic = null; player.ExtraRelics.Clear();
        }
        game.State.Events.Clear();
        return game;
    }
    private static L12StackItem Stack(L12GameEngine game, string source)
    {
        var item = new L12StackItem { StackItemId = "synthetic-stack", Controller = 0, SourceInstanceId = "source",
            SourceCardId = source, SourceName = Catalog.Cards[source].NameZh, Trigger = "active", Text = "合成回库验证" };
        game.State.EffectStack.Add(item); game.State.IsResolvingStack = true; return item;
    }
    private static object? Invoke(object target, string name, params object?[] arguments)
        => target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(method => method.Name == name && method.GetParameters().Length == arguments.Length).Invoke(target, arguments);
    private static L12CardInstance Card(string id, string instance, int owner, string? cardType = null)
    {
        var definition = Catalog.Cards[id];
        return new L12CardInstance { CardId = id, InstanceId = instance, OwnerIndex = owner, Name = definition.NameZh,
            CardType = cardType ?? definition.CardType, Faction = definition.Faction, ImageUrl = definition.ImageUrl,
            EffectText = definition.Effect, Cost = definition.Cost ?? 0, HasPrintedCost = definition.Cost is not null,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0, Traits = [.. definition.Traits],
            Profession = definition.Profession };
    }
}
