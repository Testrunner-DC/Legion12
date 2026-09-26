using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit.Sdk;

namespace TwelveLegions.Tests;

public sealed record LongChainAllCardCase(
    string CardId,
    int Seed,
    int Shard,
    string[] ProfileIds);

internal sealed record LongChainAllCardEvidence(
    string CardId,
    int Seed,
    int Shard,
    string? ProfileId,
    int AcceptedSteps,
    int RejectedSteps,
    string[] Cutpoints,
    string FocusEvidence,
    int StateBytes,
    int MaximumProjectionBytes);

/// <summary>
/// LC-02 的全卡轻量 A/B 与生命周期代表深度 A/B/C 编排器。
/// A 连续执行；B 在真实切点从检查点重建；C 仅用于 78 个生命周期代表的 Journal V2 重建。
/// </summary>
internal static class LongChainAllCardHarness
{
    internal const int ShardCount = 8;
    private const string SeedDomain = "LC-02";
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    internal static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    internal static IReadOnlyList<LongChainAllCardCase> Inventory()
    {
        var profiles = EffectLifecycleProfiles.Read(Catalog);
        return Catalog.Cards.Keys.Order(StringComparer.Ordinal).Select(cardId =>
            new LongChainAllCardCase(cardId, StableSeed(cardId), StableShard(cardId),
                profiles.Where(pair => Ability(pair.Key).CardId == cardId)
                    .Select(pair => pair.Value.Id).Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal).ToArray())).ToArray();
    }

    internal static IReadOnlyList<(string ProfileId, L12AtomicAbility Ability)> ProfileRepresentatives()
    {
        var bindings = EffectLifecycleProfiles.Read(Catalog);
        return bindings.Select(pair => (Profile: pair.Value, Ability: Ability(pair.Key)))
            .GroupBy(item => item.Profile.Id, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var representative = group.OrderBy(item => item.Ability.AbilityId, StringComparer.Ordinal)
                    .ThenBy(item => item.Ability.CardId, StringComparer.Ordinal).First();
                return (group.Key, representative.Ability);
            }).ToArray();
    }

    internal static int StableSeed(string cardId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(cardId + SeedDomain));
        return 1 + (int)(BinaryPrimitives.ReadUInt32BigEndian(hash) % int.MaxValue);
    }

    internal static int StableShard(string cardId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(cardId));
        return (int)(BinaryPrimitives.ReadUInt32BigEndian(hash) % ShardCount);
    }

    internal static async Task<LongChainAllCardEvidence> RunAsync(
        LongChainAllCardCase testCase, string? profileId = null, bool journal = false)
    {
        var definition = Catalog.Cards[testCase.CardId];
        var primary = CreateGame(definition, testCase.Seed);
        var checkpoint = Restore(primary);
        L12GameEngine? journalLive = journal ? Restore(primary) : null;
        var commands = new List<string>();
        var cutpoints = new List<string> { "initial" };
        var accepted = 0;
        var rejected = 0;
        long sequence = 0;
        var tempDirectory = Path.Combine(Path.GetTempPath(), "l12-lc02", Guid.NewGuid().ToString("N"));
        MatchRecorder? recorder = null;

        try
        {
            if (journal)
            {
                Directory.CreateDirectory(tempDirectory);
                recorder = new MatchRecorder(Path.Combine(tempDirectory, "matches.db"));
                await recorder.InitializeAsync();
                recorder.AttachCatalog(Catalog);
                await recorder.StartAsync(journalLive!, "lc-02");
            }

            async Task ApplyGm(string label, L12GmCommand command, bool requireAccepted = true)
            {
                var canonical = JsonSerializer.Serialize(command);
                var beforeA = Atomic(primary);
                var beforeB = Atomic(checkpoint);
                var beforeC = journalLive is null ? null : Atomic(journalLive);
                var a = primary.HandleGm(command);
                var b = checkpoint.HandleGm(command);
                var c = journalLive?.HandleGm(command);
                SameOutcome(label, a, b, c);
                commands.Add($"{label}|gm|{canonical}|accepted={a.Accepted}");
                if (requireAccepted && !a.Accepted) Fail(label, $"命令被拒绝：{a.Error}");
                if (a.Accepted) accepted++; else
                {
                    rejected++;
                    RejectedAtomic(label + "/A", beforeA, Atomic(primary));
                    RejectedAtomic(label + "/B", beforeB, Atomic(checkpoint));
                    if (journalLive is not null) RejectedAtomic(label + "/C", beforeC!, Atomic(journalLive));
                }
                if (recorder is not null)
                    await recorder.AppendAsync(journalLive!, ++sequence, -1, canonical, c!,
                        $"lc02-{testCase.CardId}-{sequence:D3}");
                Equivalent(label);
            }

            async Task ApplyPlayer(string label, int player, L12Command command, bool requireAccepted = true)
            {
                var canonical = JsonSerializer.Serialize(command);
                var beforeA = Atomic(primary);
                var beforeB = Atomic(checkpoint);
                var beforeC = journalLive is null ? null : Atomic(journalLive);
                var a = primary.Handle(player, command);
                var b = checkpoint.Handle(player, command);
                var c = journalLive?.Handle(player, command);
                SameOutcome(label, a, b, c);
                commands.Add($"{label}|p{player}|{canonical}|accepted={a.Accepted}");
                if (requireAccepted && !a.Accepted) Fail(label, $"命令被拒绝：{a.Error}");
                if (a.Accepted) accepted++; else
                {
                    rejected++;
                    RejectedAtomic(label + "/A", beforeA, Atomic(primary));
                    RejectedAtomic(label + "/B", beforeB, Atomic(checkpoint));
                    if (journalLive is not null) RejectedAtomic(label + "/C", beforeC!, Atomic(journalLive));
                }
                if (recorder is not null)
                    await recorder.AppendAsync(journalLive!, ++sequence, player, canonical, c!,
                        $"lc02-{testCase.CardId}-{sequence:D3}");
                Equivalent(label);
            }

            void Reconnect(string label)
            {
                checkpoint = Restore(primary);
                cutpoints.Add(label);
                Equivalent(label);
            }

            void Equivalent(string label)
            {
                CompareEngines(label, primary, checkpoint);
                if (journalLive is not null) CompareEngines(label, primary, journalLive);
            }

            void SameOutcome(string label, CommandResult a, CommandResult b, CommandResult? c)
            {
                if (a.Accepted != b.Accepted || a.Error != b.Error
                    || c is not null && (a.Accepted != c.Accepted || a.Error != c.Error))
                    Fail(label, $"A/B/C 命令结果不一致：A={a.Accepted}:{a.Error}; B={b.Accepted}:{b.Error}; C={c?.Accepted}:{c?.Error}");
            }

            void RejectedAtomic(string label, AtomicContract before, AtomicContract after)
            {
                if (before == after) return;
                var changed = typeof(AtomicContract).GetProperties()
                    .Where(property => !Equals(property.GetValue(before), property.GetValue(after)))
                    .Select(property => property.Name);
                Fail(label, "拒绝命令产生副作用：" + string.Join(',', changed));
            }

            void CompareEngines(string label, L12GameEngine expected, L12GameEngine actual)
            {
                Compare(label + "/authority", expected.SerializeFullState(), actual.SerializeFullState());
                if (expected.ComputeStateHash() != actual.ComputeStateHash())
                    Fail(label + "/stateHash", "状态哈希不一致");
                if (expected.RandomState != actual.RandomState || expected.RandomDrawCount != actual.RandomDrawCount)
                    Fail(label + "/random", "随机状态或抽取计数不一致");
                if (expected.State.Revision != actual.State.Revision
                    || expected.State.EventSequence != actual.State.EventSequence
                    || expected.CardFactSignalSequence != actual.CardFactSignalSequence)
                    Fail(label + "/sequence", "revision、eventSequence 或 cardFactSequence 不一致");
                var left = Projections(expected);
                var right = Projections(actual);
                Compare(label + "/player0", left.Player0, right.Player0);
                Compare(label + "/player1", left.Player1, right.Player1);
                Compare(label + "/spectator", left.Spectator, right.Spectator);
                Compare(label + "/referee", left.Referee, right.Referee);
            }

            void Compare(string label, string expected, string actual)
            {
                if (expected == actual) return;
                Fail(label, FirstJsonDifference(expected, actual));
            }

            void Fail(string label, string reason)
            {
                var firstDiff = reason.Contains("首个差异", StringComparison.Ordinal) ? reason : "n/a";
                throw new XunitException($"""
                    LC-02 全卡长链失败
                    cardId={testCase.CardId}
                    profileId={profileId ?? string.Join(',', testCase.ProfileIds)}
                    seed={testCase.Seed}
                    shard={testCase.Shard}
                    stage={label}
                    cutpoints={string.Join(" -> ", cutpoints)}
                    firstDiffPath={firstDiff}
                    reason={reason}
                    commandPrefixCount={commands.Count}
                    commandPrefix:
                    {string.Join(Environment.NewLine, commands)}
                    """);
            }

            await ApplyGm("prelude-life-p0", new L12GmCommand("setLife", 0, Value: 29));
            await ApplyGm("prelude-life-p1", new L12GmCommand("setLife", 1, Value: 28));
            Reconnect("after-prelude");

            var focusEvidence = await ExerciseFocus(definition, primary, ApplyGm, ApplyPlayer);
            await DrainPrompts(primary, ApplyPlayer, "focus");
            ValidateFocusPostcondition(definition, primary);
            Reconnect("after-focus");

            var stale = new L12Command("resolvePrompt", PromptId: "lc02-never-issued", Choice: "pass");
            await ApplyPlayer("rejected-out-of-order", 1, stale, false);
            await ApplyPlayer("rejected-expired", 0, stale, false);
            await ApplyPlayer("rejected-duplicate", 0, stale, false);
            await ApplyGm("continue-life-p0", new L12GmCommand("setLife", 0, Value: 28));
            await ApplyGm("continue-life-p1", new L12GmCommand("setLife", 1, Value: 27));
            await ApplyGm("continue-phase", new L12GmCommand("setPhase", 0, Phase: nameof(L12Phase.Main)));
            await ApplyGm("continue-life-p0-final", new L12GmCommand("setLife", 0, Value: 29));
            Reconnect("final-checkpoint");

            if (accepted < 6) Fail("accepted-step-budget", $"accepted={accepted}, required>=6");
            if (cutpoints.Count < 3) Fail("restore-cutpoint-budget", $"cutpoints={cutpoints.Count - 1}, required>=2");
            AssertPrivacy(primary, testCase, profileId, commands, cutpoints);

            if (recorder is not null)
            {
                var recovered = await recorder.LoadJournalEngineAsync(primary.State.MatchId)
                    ?? throw new XunitException("LC-02 Journal V2 未返回恢复状态");
                cutpoints.Add($"journal-sequence-{recovered.CommandSequence}");
                CompareEngines("journal-recovery", primary, recovered.Engine);
                if (recovered.CommandSequence != sequence || recovered.ProcessedRequests.Count != sequence)
                    Fail("journal-recovery", $"sequence={recovered.CommandSequence}/{sequence}, requests={recovered.ProcessedRequests.Count}/{sequence}");
            }

            var projections = Projections(primary);
            var stateBytes = Encoding.UTF8.GetByteCount(primary.SerializeFullState());
            var projectionBytes = new[] { projections.Player0, projections.Player1, projections.Spectator, projections.Referee }
                .Max(item => Encoding.UTF8.GetByteCount(item));
            if (stateBytes > 2 * 1024 * 1024) Fail("state-budget", $"stateBytes={stateBytes}");
            if (projectionBytes > 1024 * 1024) Fail("projection-budget", $"maximumProjectionBytes={projectionBytes}");
            return new(testCase.CardId, testCase.Seed, testCase.Shard, profileId, accepted, rejected,
                [.. cutpoints], focusEvidence, stateBytes, projectionBytes);
        }
        catch (XunitException error) when (!error.Message.StartsWith("LC-02 全卡长链失败", StringComparison.Ordinal))
        {
            throw new XunitException($"""
                LC-02 全卡长链失败
                cardId={testCase.CardId}
                profileId={profileId ?? string.Join(',', testCase.ProfileIds)}
                seed={testCase.Seed}
                shard={testCase.Shard}
                stage=unexpected-contract-failure
                cutpoints={string.Join(" -> ", cutpoints)}
                firstDiffPath=n/a
                reason={error.Message}
                commandPrefixCount={commands.Count}
                commandPrefix:
                {string.Join(Environment.NewLine, commands)}
                """, error);
        }
        finally
        {
            if (recorder is not null) await recorder.DisposeAsync();
            try
            {
                if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, recursive: true);
            }
            catch (IOException)
            {
                // SQLite 的 Windows 连接池可能短暂持有测试临时库；不覆盖真正的长链断言。
            }
        }

    }

    private static async Task<string> ExerciseFocus(L12CardDefinition definition, L12GameEngine game,
        Func<string, L12GmCommand, bool, Task> gm,
        Func<string, int, L12Command, bool, Task> player)
    {
        switch (definition.CardType)
        {
            case "legion":
            case "token":
                if (definition.Id == "S02-06S1")
                {
                    RequireProjectionContains(game, definition.Id);
                    return "authority-rune-resource-snapshot-derivation";
                }
                if (definition.Id == "S02-06S2")
                {
                    RequireProjectionContains(game, definition.Id);
                    return "authority-attached-zone-continuous-snapshot-derivation";
                }
                await gm("focus-zone-entry", new L12GmCommand("playHandCard", 0,
                    CardInstanceId: "focus", Row: 0, Slot: 0, TriggerEffects: true), true);
                await DrainPrompts(game, player, "focus-entry");
                var source = FindCard(game, "focus");
                if (source is not null && game.State.Players[0].Field.SelectMany(row => row).Contains(source))
                {
                    await gm("focus-combat", new L12GmCommand("startAttack", 0,
                        CardInstanceId: "focus", TargetInstanceId: "lc02-field-1"), false);
                }
                return "authority-zone-entry-and-combat-rule";
            case "artifact":
            case "tactic":
                if (definition.IsCounterTactic)
                {
                    await gm("focus-counter-response-window", new L12GmCommand("startAttack", 1,
                        CardInstanceId: "lc02-field-1", TargetInstanceId: "lc02-field-0"), true);
                    return "authority-counter-cover-and-response-window";
                }
                await gm("focus-play", new L12GmCommand("playHandCard", 0,
                    CardInstanceId: "focus", TriggerEffects: true), true);
                return definition.IsCounterTactic ? "authority-counter-cover-or-response-entry" : "authority-play-resolution";
            case "destruction":
                await gm("focus-disaster-reveal", new L12GmCommand("triggerDisaster"), true);
                return "authority-disaster-deck-reveal";
            case "trial":
                await player("focus-complete-trial", 0,
                    new L12Command("activateAbility", "focus", Ability: "completeTrial"), true);
                return "authority-trial-zone-completion";
            case "rune":
                RequireProjectionContains(game, definition.Id);
                return "authority-morale-zone-snapshot-derivation";
            case "divinity":
                RequireProjectionContains(game, definition.Id);
                return "authority-divinity-zone-snapshot-derivation";
            case "master":
                RequireProjectionContains(game, definition.Id);
                return "authority-master-zone-snapshot-derivation";
            default:
                throw new XunitException($"LC-02 未登记卡牌类型：{definition.CardType}/{definition.Id}");
        }
    }

    private static async Task DrainPrompts(L12GameEngine game,
        Func<string, int, L12Command, bool, Task> apply, string prefix)
    {
        for (var safety = 0; safety < 240 && game.State.PendingPrompts.Count > 0; safety++)
        {
            var prompt = game.State.PendingPrompts[0];
            var selected = SelectChoices(game, prompt);
            await apply($"{prefix}-prompt-{safety:D3}", prompt.PlayerIndex, BuildPromptCommand(prompt, selected), true);
        }
        if (game.State.PendingPrompts.Count > 0)
            throw new XunitException($"LC-02 {prefix} 在 240 次选择后仍未结束");
    }

    private static List<string> SelectChoices(L12GameEngine game, L12Prompt prompt)
    {
        if (prompt.Kind == "response" && prompt.ValidChoices.Contains("focus")) return ["focus"];
        if (prompt.Kind == "response" && prompt.ValidChoices.Contains("pass")) return ["pass"];
        if (prompt.MaxChoose == 0) return [];
        var candidates = prompt.ValidChoices.Where(choice => !choice.StartsWith("lc02-private-", StringComparison.Ordinal))
            .Where(choice => choice is not ("skip" or "mode:none" or "no" or "cancel")).ToList();
        if (candidates.Count < prompt.MinChoose) candidates = [.. prompt.ValidChoices];
        var constraint = prompt.Data.GetValueOrDefault("selectionConstraint");
        if (constraint is "one-resource-two-field-legions" or "zero-resource-two-field-legions")
        {
            var owner = game.State.Players[prompt.PlayerIndex];
            var resources = candidates.Where(id => owner.Morale.Any(card => card.InstanceId == id && !card.Tapped))
                .Take(constraint == "one-resource-two-field-legions" ? 1 : 0);
            var field = candidates.Where(id => owner.Field.SelectMany(row => row)
                .Any(card => card?.InstanceId == id && card.CardType == "legion")).Take(2);
            return resources.Concat(field).ToList();
        }
        return candidates.Take(Math.Min(prompt.MinChoose, candidates.Count)).ToList();
    }

    private static L12Command BuildPromptCommand(L12Prompt prompt, List<string> selected)
    {
        var placement = prompt.Data.GetValueOrDefault("placementMode");
        if (placement is "split-top-bottom" or "all-top-bottom" or "all-bottom")
            return new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                TopCardInstanceIds: placement == "all-bottom" ? [] : selected,
                BottomCardInstanceIds: placement == "all-bottom" ? selected : []);
        return new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: selected.Count == 1 ? selected[0] : null,
            CardInstanceIds: selected.Count == 1 ? null : selected);
    }

    private static L12GameEngine CreateGame(L12CardDefinition focus, int seed)
    {
        var baseDeck = Catalog.DeckAt(0);
        var focusMaster = focus.CardType == "master" ? focus.Id
            : focus.CardType == "rune"
                ? Catalog.Cards.Values.FirstOrDefault(card => card.CardType == "master" && card.Faction == focus.Faction)?.Id
                    ?? baseDeck.MasterId
                : baseDeck.MasterId;
        var focusDeck = new L12PresetDeckDefinition
        {
            Name = $"{focus.Id} LC-02",
            MasterId = focusMaster,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [.. baseDeck.SpecialIds],
        };
        var game = new L12GameEngine(Catalog, $"lc02-{focus.Id}-{seed}", "LC02", seed,
            ["甲", "乙"], [focusDeck, baseDeck], skipPreparation: true,
            disasterMode: focus.CardType == "destruction" ? "all" : "none",
            autoPassEmptyResponses: true, concealHiddenResponseAvailability: false,
            stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 3;
        game.State.TurnSerial = 5;
        game.State.Phase = L12Phase.Main;
        PreparePlayer(game.State.Players[0], 0);
        PreparePlayer(game.State.Players[1], 1);
        PlaceFocus(game, focus);
        return game;
    }

    private static void PlaceFocus(L12GameEngine game, L12CardDefinition definition)
    {
        var player = game.State.Players[0];
        var focus = Card(definition, "focus");
        focus.OwnerIndex = 0;
        switch (definition.CardType)
        {
            case "legion": case "token": case "artifact": case "tactic":
                if (definition.IsCounterTactic)
                {
                    focus.Hidden = true; focus.SetRound = game.State.Round - 1;
                    player.Field[1][0] = focus;
                }
                else if (definition.Id == "S02-06S1")
                    player.Morale.Insert(0, new L12MoraleCard { CardId = focus.CardId, InstanceId = focus.InstanceId });
                else if (definition.Id == "S02-06S2")
                {
                    var arthur = Card(Catalog.Cards["S02-0601"], "lc02-arthur");
                    arthur.OwnerIndex = 0; arthur.SummonRound = -1; arthur.AttachedCards.Add(focus);
                    player.Field[0][0] = arthur;
                }
                else player.Hand.Insert(0, focus);
                break;
            case "destruction":
                game.State.DisasterDeck.Clear(); game.State.DisasterPool.Clear();
                game.State.ActiveDisaster = null; game.State.DisasterDeck.Add(focus); break;
            case "trial":
                focus.TrialProgress = 8; focus.TrialCompleted = false;
                player.SpecialZones.Trials.Add(focus); break;
            case "rune":
                player.Morale.Insert(0, new L12MoraleCard { CardId = focus.CardId, InstanceId = focus.InstanceId }); break;
            case "divinity": player.ExtraRelics.Add(focus); break;
            case "master": break;
        }
    }

    private static void PreparePlayer(L12PlayerState player, int index)
    {
        player.Hp = 30; player.MasterTapped = false;
        player.Hand.Clear(); player.Library.Clear(); player.Graveyard.Clear(); player.Morale.Clear();
        player.Resolving.Clear(); player.Removed.Clear(); player.ExtraRelics.Clear(); player.Relic = null;
        player.SpecialZones.Runes = 12; player.SpecialZones.TrialLevel = 8; player.SpecialZones.TrialCapacity = 20;
        player.SpecialZones.Trials.Clear(); player.SpecialZones.CanopicProgress.Clear(); player.UsedAbilities.Clear();
        player.Field[0] = new L12CardInstance?[3]; player.Field[1] = new L12CardInstance?[3];
        for (var i = 0; i < 16; i++)
            player.Morale.Add(new L12MoraleCard { CardId = "S02-05C1A", InstanceId = $"lc02-resource-{index}-{i}", IsGodPower = i >= 8 });
        var defender = Card(Catalog.Cards[index == 0 ? "S01-0103" : "S01-0107"], $"lc02-field-{index}");
        defender.OwnerIndex = index; defender.SummonRound = -1; player.Field[0][index == 0 ? 1 : 0] = defender;
        var sentinel = Card(Catalog.Cards["S01-0003"], $"lc02-private-{index}");
        sentinel.OwnerIndex = index; player.Hand.Add(sentinel);
        foreach (var id in new[] { "S01-0007", "S01-0108", "S01-0208", "S01-0308" })
            player.Library.Add(Card(Catalog.Cards[id], $"lc02-library-{index}-{player.Library.Count}"));
    }

    private static void ValidateFocusPostcondition(L12CardDefinition definition, L12GameEngine game)
    {
        var player = game.State.Players[0];
        switch (definition.CardType)
        {
            case "legion" or "token" when definition.Id == "S02-06S1":
                if (!player.Morale.Any(card => card.InstanceId == "focus"))
                    throw new XunitException("焦点符文没有保留在权威士气区");
                break;
            case "legion" or "token" when definition.Id == "S02-06S2":
                if (!player.Field.SelectMany(row => row).Where(card => card is not null)
                        .Any(card => card!.AttachedCards.Any(attached => attached.InstanceId == "focus")))
                    throw new XunitException("王者之剑没有保留在权威叠放区");
                break;
            case "legion" or "token":
                if (player.Hand.Any(card => card.InstanceId == "focus"))
                    throw new XunitException("焦点军团没有完成权威区域进入");
                break;
            case "artifact":
                if (player.Hand.Any(card => card.InstanceId == "focus"))
                    throw new XunitException("焦点圣物没有完成权威区域进入");
                break;
            case "tactic" when definition.IsCounterTactic:
                var covered = player.Field.SelectMany(row => row).FirstOrDefault(card => card?.InstanceId == "focus");
                if (covered is not null && (!covered.Hidden || covered.SetRound >= game.State.Round))
                    throw new XunitException("焦点反击战术完成资格判定后没有保持合法盖伏状态");
                break;
            case "tactic":
                if (player.Hand.Any(card => card.InstanceId == "focus"))
                    throw new XunitException("焦点战术没有完成权威打出结算");
                break;
            case "destruction":
                if (game.State.ActiveDisaster?.CardId != definition.Id)
                    throw new XunitException("焦点天灾没有成为权威翻开天灾");
                break;
            case "trial":
                if (!player.SpecialZones.Trials.Any(card => card.InstanceId == "focus" && card.TrialCompleted))
                    throw new XunitException("焦点试炼没有完成权威翻面");
                break;
            case "rune":
                if (!player.Morale.Any(card => card.InstanceId == "focus"))
                    throw new XunitException("焦点符文没有保留在权威士气区");
                break;
            case "divinity":
                if (!player.ExtraRelics.Any(card => card.InstanceId == "focus"))
                    throw new XunitException("焦点神力没有保留在权威公开区");
                break;
        }
    }

    private static L12CardInstance Card(L12CardDefinition definition, string instanceId) => new()
    {
        InstanceId = instanceId, CardId = definition.Id, Name = definition.NameZh,
        CardType = definition.CardType, IsCounterTactic = definition.IsCounterTactic,
        Faction = definition.Faction, ImageUrl = definition.ImageUrl, Cost = definition.Cost ?? 0,
        HasPrintedCost = definition.Cost.HasValue, EffectText = definition.Effect,
        BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
        DisasterLevel = definition.DisasterLevel ?? 0, TrialValue = definition.TrialValue ?? 0,
        Traits = [.. definition.Traits], Profession = definition.Profession, EffectiveProfession = definition.Profession,
    };

    private static L12CardInstance? FindCard(L12GameEngine game, string instanceId)
        => game.State.Players.SelectMany(player => player.Hand.Concat(player.Library).Concat(player.Graveyard)
            .Concat(player.Resolving).Concat(player.Removed).Concat(player.ExtraRelics).Concat(player.SpecialZones.Trials)
            .Concat(player.Field.SelectMany(row => row).Where(card => card is not null).Cast<L12CardInstance>()))
            .FirstOrDefault(card => card.InstanceId == instanceId);

    private static L12AtomicAbility Ability(string abilityId)
        => Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Single(ability => ability.AbilityId == abilityId);

    private static L12GameEngine Restore(L12GameEngine source)
        => L12GameEngine.RestoreCheckpoint(Catalog, source.SerializeFullState(),
            source.RandomState ?? throw new XunitException("LC-02 缺少随机状态"), source.CardFactSignalSequence,
            source.AutoPassEmptyResponses, source.ConcealHiddenResponseAvailability);

    private static ProjectionSet Projections(L12GameEngine game) => new(
        JsonSerializer.Serialize(game.SnapshotFor(0), WireJson),
        JsonSerializer.Serialize(game.SnapshotFor(1), WireJson),
        JsonSerializer.Serialize(game.SnapshotForSpectator(), WireJson),
        JsonSerializer.Serialize(game.SnapshotForReferee(), WireJson));

    private static void RequireProjectionContains(L12GameEngine game, string cardId)
    {
        var projection = Projections(game).Player0;
        if (!projection.Contains(cardId, StringComparison.Ordinal))
            throw new XunitException($"LC-02 权威区域卡没有进入玩家派生快照：{cardId}");
    }

    private static void AssertPrivacy(L12GameEngine game, LongChainAllCardCase testCase, string? profileId,
        List<string> commands, List<string> cutpoints)
    {
        var projections = Projections(game);
        var p0Private = game.State.Players[0].Hand.Any(card => card.InstanceId == "lc02-private-0");
        var p1Private = game.State.Players[1].Hand.Any(card => card.InstanceId == "lc02-private-1");
        if (p0Private && (!VisibleHandIds(projections.Player0, 0).Contains("lc02-private-0")
                || HasHandField(projections.Player1, 0)
                || HasHandField(projections.Spectator, 0)
                || HasHandField(projections.Referee, 0))
            || p1Private && (!VisibleHandIds(projections.Player1, 1).Contains("lc02-private-1")
                || HasHandField(projections.Player0, 1)
                || HasHandField(projections.Spectator, 1)
                || HasHandField(projections.Referee, 1)))
            throw new XunitException($"LC-02 投影隐私失败 cardId={testCase.CardId} profileId={profileId} seed={testCase.Seed} shard={testCase.Shard} cutpoints={string.Join(" -> ", cutpoints)} commands={commands.Count}");
    }

    private static HashSet<string> VisibleHandIds(string projectionJson, int playerIndex)
    {
        using var document = JsonDocument.Parse(projectionJson);
        var player = document.RootElement.GetProperty("players")[playerIndex];
        if (!player.TryGetProperty("hand", out var hand)) return [];
        return hand.EnumerateArray().Select(card => card.GetProperty("instanceId").GetString())
            .Where(id => id is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);
    }

    private static bool HasHandField(string projectionJson, int playerIndex)
    {
        using var document = JsonDocument.Parse(projectionJson);
        return document.RootElement.GetProperty("players")[playerIndex].TryGetProperty("hand", out _);
    }

    private static AtomicContract Atomic(L12GameEngine game)
    {
        var projections = Projections(game);
        return new(game.SerializeFullState(), game.ComputeStateHash(), JsonSerializer.Serialize(game.RandomState),
            game.RandomDrawCount, game.State.Revision, game.State.EventSequence, game.CardFactSignalSequence,
            JsonSerializer.Serialize(game.State.Events, WireJson), JsonSerializer.Serialize(game.UnpersistedEvents, WireJson),
            projections.Player0, projections.Player1, projections.Spectator, projections.Referee);
    }

    private static string FirstJsonDifference(string expected, string actual)
    {
        using var left = JsonDocument.Parse(expected);
        using var right = JsonDocument.Parse(actual);
        return Difference(left.RootElement, right.RootElement, "$") ?? "JSON 文本不同但结构未定位差异";
    }

    private static string? Difference(JsonElement expected, JsonElement actual, string path)
    {
        if (expected.ValueKind != actual.ValueKind) return $"首个差异 {path}: kind";
        if (expected.ValueKind == JsonValueKind.Object)
        {
            var left = expected.EnumerateObject().ToDictionary(item => item.Name, item => item.Value);
            var right = actual.EnumerateObject().ToDictionary(item => item.Name, item => item.Value);
            foreach (var name in left.Keys.Union(right.Keys).Order(StringComparer.Ordinal))
            {
                if (!left.TryGetValue(name, out var l) || !right.TryGetValue(name, out var r)) return $"首个差异 {path}.{name}: missing";
                var nested = Difference(l, r, $"{path}.{name}"); if (nested is not null) return nested;
            }
            return null;
        }
        if (expected.ValueKind == JsonValueKind.Array)
        {
            var left = expected.EnumerateArray().ToArray(); var right = actual.EnumerateArray().ToArray();
            if (left.Length != right.Length) return $"首个差异 {path}.length: {left.Length}/{right.Length}";
            for (var i = 0; i < left.Length; i++) { var nested = Difference(left[i], right[i], $"{path}[{i}]"); if (nested is not null) return nested; }
            return null;
        }
        return expected.GetRawText() == actual.GetRawText() ? null : $"首个差异 {path}: {expected.GetRawText()}/{actual.GetRawText()}";
    }

    private sealed record ProjectionSet(string Player0, string Player1, string Spectator, string Referee);
    private sealed record AtomicContract(string StateJson, string StateHash, string RandomStateJson,
        long RandomDrawCount, long Revision, long EventSequence, long CardFactSequence,
        string EventsJson, string AuditJson, string Player0Projection, string Player1Projection,
        string SpectatorProjection, string RefereeProjection);
}
