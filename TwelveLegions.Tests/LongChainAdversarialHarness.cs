using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit.Sdk;

namespace TwelveLegions.Tests;

public sealed record LongChainScenario(
    string Id,
    string CardId,
    int Seed,
    string[] RiskFamilies,
    string Setup,
    string Action,
    string ExpectedEvidence,
    string Mechanism = "attack");

internal sealed record LongChainEvidence(
    string ScenarioId,
    string CardId,
    int Seed,
    string FinalStateHash,
    string Player0ProjectionHash,
    string Player1ProjectionHash,
    string SpectatorProjectionHash,
    string RefereeProjectionHash,
    string[] Commands,
    string[] Cutpoints,
    IReadOnlyDictionary<string, int> EvidenceCounts,
    string[] Postconditions,
    int RejectedCommands,
    int StateBytes,
    int MaximumProjectionBytes,
    double SnapshotP95Milliseconds,
    double RestoreP95Milliseconds);

/// <summary>
/// LC-01 的最小确定性编排器。它只调用现有命令入口、Journal V2、检查点恢复与各接收者
/// 投影，不复制任何规则、序列化器或恢复协议。
/// </summary>
internal static class LongChainAdversarialHarness
{
    internal const string AttachmentRisk = "叠放、转移与最后已知信息";
    internal const string TimedRisk = "限时与持续效果源失效";
    internal const string ResponseRisk = "响应、无效化与同时触发";
    internal const string PrivateRisk = "手牌、牌库、检索与顺序";
    internal const string DisasterTrialRisk = "天灾与试炼";
    internal const string CleanupRisk = "跨回合清理、次数与阵亡替代";

    internal const string AttachmentEvidence = "attachment-committed";
    internal const string TimedSourceEvidence = "timed-modifier-survives-source-exit";
    internal const string ResponseEvidence = "response-stack-and-negation";
    internal const string PrivateOrderEvidence = "private-library-order-change";
    internal const string DisasterTrialEvidence = "disaster-and-trial-committed";
    internal const string CleanupEvidence = "cross-turn-cleanup";

    internal static readonly IReadOnlyDictionary<string, string> RequiredEvidenceByRisk =
        new Dictionary<string, string>
        {
            [AttachmentRisk] = AttachmentEvidence,
            [TimedRisk] = TimedSourceEvidence,
            [ResponseRisk] = ResponseEvidence,
            [PrivateRisk] = PrivateOrderEvidence,
            [DisasterTrialRisk] = DisasterTrialEvidence,
            [CleanupRisk] = CleanupEvidence,
        };

    internal static readonly string[] RequiredRiskFamilies =
    [
        AttachmentRisk, TimedRisk, ResponseRisk, PrivateRisk, DisasterTrialRisk, CleanupRisk,
    ];

    internal static readonly LongChainScenario[] Scenarios =
    [
        Scenario("lc01-hanxin", "S01-0104", 410104, ResponseRisk, PrivateRisk,
            "韩信前排、对方盖伏反击战术、显式响应窗口", "进攻并支付返还士气，响应中发动反击战术",
            "支付后/结算前恢复；响应栈和无效结果可见", "response"),
        Scenario("lc01-guanyu", "S01-0106", 410106, CleanupRisk, ResponseRisk),
        Scenario("lc01-thutmose", "S01-0201", 410201, PrivateRisk, ResponseRisk),
        Scenario("lc01-ay", "S01-0208", 410208, TimedRisk, PrivateRisk),
        Scenario("lc01-beowulf", "S01-0301", 410301, CleanupRisk, ResponseRisk),
        Scenario("lc01-gustav", "S01-0311", 410311, PrivateRisk, CleanupRisk,
            "墓地至少十张卡、牌库有稳定底部", "进攻并将墓地两张卡按选择顺序送回牌库底",
            "私密牌序变化在三线恢复后完全一致", "private-order"),
        Scenario("lc01-pelopidas", "S02-0511", 420511, ResponseRisk, CleanupRisk),
        Scenario("lc01-hannibal", "S02-0516", 420516, TimedRisk, ResponseRisk),
        Scenario("lc01-penthesilea", "S02-0517", 420517, CleanupRisk, ResponseRisk),
        Scenario("lc01-spartan", "S02-0519", 420519, TimedRisk, CleanupRisk),
        Scenario("lc01-arthur", "S02-0601", 420601, AttachmentRisk, CleanupRisk,
            "亚瑟王在手牌、战场有空位且符文充足", "从手牌登场并支付符文",
            "王者之剑真实叠放且检查点/Journal 保留附件", "attachment"),
        Scenario("lc01-percival", "S02-0606", 420606, PrivateRisk, CleanupRisk),
        Scenario("lc01-gawain", "S02-0607", 420607, TimedRisk, CleanupRisk),
        Scenario("lc01-scathach", "S02-0612", 420612, AttachmentRisk, ResponseRisk),
        Scenario("lc01-oiran", "ST04-06", 440406, TimedRisk, CleanupRisk,
            "花魁与双方军团在场、当前为主要阶段", "主动休整后移除来源并结束回合",
            "限时修正来源离场后保留，跨回合准确清理", "timed-cleanup"),
    ];

    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    internal static async Task<LongChainEvidence> RunAsync(LongChainScenario scenario)
    {
        var primary = CreateGame(scenario);
        var initialState = primary.SerializeFullState();
        var initialRandom = RequireRandom(primary, scenario, "initial");
        var checkpoint = Restore(initialState, initialRandom, primary.CardFactSignalSequence, primary);
        var journalLive = Restore(initialState, initialRandom, primary.CardFactSignalSequence, primary);
        var private0 = $"lc-private-p0-{scenario.Id}";
        var private1 = $"lc-private-p1-{scenario.Id}";
        var commands = new List<string>();
        var cutpoints = new List<string> { "initial" };
        var evidenceCounts = RequiredEvidenceByRisk.Values.Distinct(StringComparer.Ordinal)
            .ToDictionary(key => key, _ => 0, StringComparer.Ordinal);
        var postconditions = new List<string>();
        var initialGraveyardCount = primary.State.Players[0].Graveyard.Count;
        var initialLibraryCount = primary.State.Players[0].Library.Count;
        var rejected = 0;
        long sequence = 0;
        var directory = Path.Combine(Path.GetTempPath(), "l12-lc01", Guid.NewGuid().ToString("N"));
        var database = Path.Combine(directory, "matches.db");

        Directory.CreateDirectory(directory);
        try
        {
            await using var recorder = new MatchRecorder(database);
            await recorder.InitializeAsync();
            recorder.AttachCatalog(Catalog);
            await recorder.StartAsync(journalLive, "lc-01");

            async Task ApplyGmAsync(string label, L12GmCommand command, bool requireAccepted = true)
            {
                var canonical = JsonSerializer.Serialize(command);
                var a = primary.HandleGm(command);
                var b = checkpoint.HandleGm(command);
                var c = journalLive.HandleGm(command);
                AssertSameOutcome(scenario, label, a, b, c);
                if (requireAccepted && !a.Accepted)
                    Fail(scenario, commands, cutpoints, label, $"命令被拒绝：{a.Error}");
                if (!a.Accepted) rejected++;
                commands.Add($"{label}|gm|{canonical}|accepted={a.Accepted}");
                await recorder.AppendAsync(journalLive, ++sequence, -1, canonical, c,
                    $"{scenario.Id}-{sequence:D3}");
                AssertEquivalent(scenario, commands, cutpoints, label, primary, checkpoint, journalLive,
                    private0, private1);
            }

            async Task ApplyAsync(string label, int player, L12Command command, bool requireAccepted = true)
            {
                var canonical = JsonSerializer.Serialize(command);
                var beforeA = CaptureAtomicContract(primary);
                var beforeB = CaptureAtomicContract(checkpoint);
                var beforeC = CaptureAtomicContract(journalLive);
                var a = primary.Handle(player, command);
                var b = checkpoint.Handle(player, command);
                var c = journalLive.Handle(player, command);
                AssertSameOutcome(scenario, label, a, b, c);
                if (requireAccepted && !a.Accepted)
                    Fail(scenario, commands, cutpoints, label, $"命令被拒绝：{a.Error}");
                if (!a.Accepted)
                {
                    rejected++;
                    var attempted = commands.Append($"{label}|p{player}|{canonical}|accepted=False").ToList();
                    AssertRejectedAtomic(scenario, attempted, cutpoints, $"{label}/A", beforeA,
                        CaptureAtomicContract(primary));
                    AssertRejectedAtomic(scenario, attempted, cutpoints, $"{label}/B", beforeB,
                        CaptureAtomicContract(checkpoint));
                    AssertRejectedAtomic(scenario, attempted, cutpoints, $"{label}/C", beforeC,
                        CaptureAtomicContract(journalLive));
                }
                commands.Add($"{label}|p{player}|{canonical}|accepted={a.Accepted}");
                await recorder.AppendAsync(journalLive, ++sequence, player, canonical, c,
                    $"{scenario.Id}-{sequence:D3}");
                AssertEquivalent(scenario, commands, cutpoints, label, primary, checkpoint, journalLive,
                    private0, private1);
            }

            async Task ReconnectCheckpointAsync(string label)
            {
                checkpoint = Restore(primary.SerializeFullState(), RequireRandom(primary, scenario, label),
                    primary.CardFactSignalSequence, primary);
                cutpoints.Add(label);
                AssertEquivalent(scenario, commands, cutpoints, label, primary, checkpoint, journalLive,
                    private0, private1);
                await Task.CompletedTask;
            }

            await ApplyGmAsync("prelude-life-p0", new L12GmCommand("setLife", 0, Value: 29));
            await ApplyGmAsync("prelude-life-p1", new L12GmCommand("setLife", 1, Value: 28));
            var neverIssued = new L12Command("resolvePrompt", PromptId: "lc01-never-issued", Choice: "pass");
            await ApplyAsync("prelude-out-of-order-p1", 1, neverIssued, false);
            await ApplyAsync("prelude-expired-p0", 0, neverIssued, false);
            await ApplyAsync("prelude-invalid-command", 0, new L12Command("lc01-invalid-command"), false);
            await ReconnectCheckpointAsync("after-prelude");

            if (scenario.Mechanism == "timed-cleanup")
            {
                await ApplyAsync("focus-active", 0,
                    new L12Command("activateAbility", "focus", Ability: "oiranTransfer"));
            }
            else if (scenario.Mechanism == "attachment")
            {
                await ApplyAsync("focus-play-from-hand", 0,
                    new L12Command("playCard", "focus", Row: 0, Slot: 0));
            }
            else
            {
                var defender = primary.State.Players[1].Field[0][0]
                    ?? throw new XunitException($"{scenario.Id} 缺少进攻目标");
                var attack = new L12Command("attack", "focus",
                    Target: new L12AttackTarget("legion", defender.InstanceId));
                await ApplyAsync("focus-attack", 0, attack);
            }

            await DrainPromptsAsync("focus", ApplyAsync, ReconnectCheckpointAsync, scenario, primary,
                evidenceCounts, postconditions, commands, cutpoints);
            await ReconnectCheckpointAsync("after-focus");

            AssertHistoricalFocusEvidence(scenario, primary, commands, cutpoints, postconditions);
            if (scenario.Mechanism == "attachment")
            {
                var arthur = primary.State.Players[0].Field[0][0]
                    ?? throw new XunitException($"{scenario.Id} 亚瑟王没有完成登场");
                if (!arthur.AttachedCards.Any(card => card.Name == "王者之剑"))
                    Fail(scenario, commands, cutpoints, "attachment-postcondition", "王者之剑没有叠放到亚瑟王下方");
                evidenceCounts[AttachmentEvidence]++;
                cutpoints.Add("attachment-committed");
                postconditions.Add("亚瑟王下方存在王者之剑附件");
                await ReconnectCheckpointAsync("after-attachment-committed");
            }
            if (scenario.Mechanism == "private-order")
            {
                var returned = primary.State.Players[0].Library.Count - initialLibraryCount;
                var removed = initialGraveyardCount - primary.State.Players[0].Graveyard.Count;
                if (returned < 2 || removed < 2)
                    Fail(scenario, commands, cutpoints, "private-order-postcondition",
                        $"墓地回牌库顺序效果未实际发生：libraryDelta={returned}, graveDelta={removed}");
                evidenceCounts[PrivateOrderEvidence]++;
                cutpoints.Add("private-library-order-committed");
                postconditions.Add("古斯塔夫将墓地至少两张卡按声明顺序放到牌库底");
                await ReconnectCheckpointAsync("after-private-library-order");
            }

            if (scenario.Mechanism == "timed-cleanup")
            {
                var modifiedBeforeExit = CountOiranModifiers(primary);
                if (modifiedBeforeExit < 2)
                    Fail(scenario, commands, cutpoints, "timed-modifier-postcondition",
                        $"花魁没有实际生成双方限时修正：count={modifiedBeforeExit}");
                await ApplyGmAsync("remove-timed-source", new L12GmCommand("destroyCard", 0,
                    CardInstanceId: "focus"));
                await DrainPromptsAsync("source-exit", ApplyAsync, ReconnectCheckpointAsync, scenario, primary,
                    evidenceCounts, postconditions, commands, cutpoints);
                if (CountOiranModifiers(primary) != modifiedBeforeExit)
                    Fail(scenario, commands, cutpoints, "source-left-field",
                        "来源离场后本回合限时修正被提前移除");
                evidenceCounts[TimedSourceEvidence]++;
                cutpoints.Add("source-left-field");
                postconditions.Add("花魁来源离场后双方限时修正仍保留到回合结束");
                await ReconnectCheckpointAsync("after-source-left-field");

                await ApplyAsync("cross-turn-end", 0, new L12Command("endTurn"));
                await DrainPromptsAsync("cross-turn", ApplyAsync, ReconnectCheckpointAsync, scenario, primary,
                    evidenceCounts, postconditions, commands, cutpoints);
                if (CountOiranModifiers(primary) != 0)
                    Fail(scenario, commands, cutpoints, "cross-turn-cleanup", "花魁限时修正跨回合后仍残留");
                evidenceCounts[CleanupEvidence]++;
                cutpoints.Add("cross-turn-cleanup");
                postconditions.Add("回合结束后花魁限时修正已清理");
                await ReconnectCheckpointAsync("after-cross-turn-cleanup");
            }

            await ApplyGmAsync("return-to-main-phase",
                new L12GmCommand("setPhase", 0, Phase: nameof(L12Phase.Main)));
            await ApplyAsync("complete-trial", 0,
                new L12Command("activateAbility", "lc-trial", Ability: "completeTrial"));
            await DrainPromptsAsync("trial", ApplyAsync, ReconnectCheckpointAsync, scenario, primary,
                evidenceCounts, postconditions, commands, cutpoints);

            await ApplyGmAsync("trigger-disaster", new L12GmCommand("triggerDisaster"));
            await DrainPromptsAsync("disaster", ApplyAsync, ReconnectCheckpointAsync, scenario, primary,
                evidenceCounts, postconditions, commands, cutpoints);
            if (!primary.State.Players[0].SpecialZones.Trials.Single(card => card.InstanceId == "lc-trial").TrialCompleted
                || primary.State.ActiveDisaster is null)
                Fail(scenario, commands, cutpoints, "disaster-trial-postcondition",
                    "试炼完成或天灾翻开没有形成真实后置状态");
            evidenceCounts[DisasterTrialEvidence]++;
            postconditions.Add("试炼已完成且权威场上存在已翻开的天灾");
            await ReconnectCheckpointAsync("after-disaster-and-trial");

            var restoredJournal = await recorder.LoadJournalEngineAsync(primary.State.MatchId)
                ?? throw new XunitException($"{scenario.Id} Journal V2 未返回恢复状态");
            cutpoints.Add($"journal-sequence-{restoredJournal.CommandSequence}");
            AssertEquivalent(scenario, commands, cutpoints, "journal-recovery", primary, checkpoint,
                restoredJournal.Engine, private0, private1);
            if (restoredJournal.CommandSequence != sequence)
                Fail(scenario, commands, cutpoints, "journal-recovery",
                    $"日志序号不一致：expected={sequence}, actual={restoredJournal.CommandSequence}");
            if (restoredJournal.ProcessedRequests.Count != sequence)
                Fail(scenario, commands, cutpoints, "journal-recovery",
                    $"请求去重证据数量不一致：expected={sequence}, actual={restoredJournal.ProcessedRequests.Count}");

            var metrics = Measure(primary);
            var projections = ProjectionJson(primary);
            return new LongChainEvidence(
                scenario.Id,
                scenario.CardId,
                scenario.Seed,
                primary.ComputeStateHash(),
                Sha256(projections.Player0),
                Sha256(projections.Player1),
                Sha256(projections.Spectator),
                Sha256(projections.Referee),
                [.. commands],
                [.. cutpoints],
                evidenceCounts,
                [.. postconditions],
                rejected,
                Encoding.UTF8.GetByteCount(primary.SerializeFullState()),
                new[] { projections.Player0, projections.Player1, projections.Spectator, projections.Referee }
                    .Max(value => Encoding.UTF8.GetByteCount(value)),
                metrics.SnapshotP95Milliseconds,
                metrics.RestoreP95Milliseconds);
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Microsoft.Data.Sqlite 的连接池在 Windows 上可能短暂持有文件句柄。
                // 临时目录由系统回收；清理失败不能覆盖真正的长链断言结果。
            }
        }
    }

    private static async Task DrainPromptsAsync(
        string stage,
        Func<string, int, L12Command, bool, Task> apply,
        Func<string, Task> reconnect,
        LongChainScenario scenario,
        L12GameEngine primary,
        Dictionary<string, int> evidenceCounts,
        List<string> postconditions,
        List<string> commands,
        List<string> cutpoints)
    {
        for (var index = 0; index < 240; index++)
        {
            if (primary.State.PendingPrompts.Count == 0)
            {
                if (primary.State.PendingActivations.Count != 0)
                    throw new XunitException($"{scenario.Id}/{stage} Prompt 已清空但仍有 PendingActivation");
                return;
            }

            var prompt = primary.State.PendingPrompts[0];
            var hasCounterChoice = prompt.ValidChoices.Any(choice =>
                !choice.Equals("pass", StringComparison.OrdinalIgnoreCase));
            var selectCounter = scenario.Mechanism == "response"
                && prompt.Kind == "response"
                && evidenceCounts[ResponseEvidence] == 0
                && hasCounterChoice;
            if (scenario.Mechanism == "timed-cleanup"
                && prompt.Kind == "response"
                && !postconditions.Contains("花魁主动休整费用已支付", StringComparer.Ordinal))
            {
                var source = primary.State.Players[0].Field.SelectMany(row => row)
                    .SingleOrDefault(card => card?.InstanceId == "focus");
                if (source is null || !source.Tapped)
                    Fail(scenario, commands, cutpoints, $"{stage}/paid-cost-before-resolution",
                        "花魁响应窗口已建立，但主动休整费用尚未形成权威状态");
                cutpointEvidence("paid-cost-before-resolution");
                await reconnect("paid-cost-before-resolution");
                postconditions.Add("花魁主动休整费用已支付");
            }
            if (selectCounter)
            {
                if (primary.State.EffectStack.Count == 0 || primary.State.ResponseWindow is null)
                    Fail(scenario, commands, cutpoints, $"{stage}/response-stack",
                        "响应 Prompt 出现时没有权威 EffectStack/ResponseWindow");
                if (primary.State.Players[0].Morale.Count >= 16)
                    Fail(scenario, commands, cutpoints, $"{stage}/paid-cost-before-resolution",
                        "韩信响应窗口已建立，但返还士气费用尚未形成权威状态");
                cutpointEvidence("paid-cost-before-resolution");
                await reconnect("paid-cost-before-resolution");
                cutpointEvidence("response-stack");
                await reconnect("response-stack");
            }
            var selected = SelectChoices(primary, prompt, selectCounter);
            if (scenario.Mechanism == "timed-cleanup" && selected.SequenceEqual(["focus"]))
            {
                var alternative = prompt.ValidChoices.FirstOrDefault(choice => choice != "focus");
                if (alternative is not null) selected = [alternative];
            }
            if (selectCounter && selected.All(choice => choice.Equals("pass", StringComparison.OrdinalIgnoreCase)))
                Fail(scenario, commands, cutpoints, $"{stage}/response-negation",
                    $"显式响应窗口没有可提交的反击战术：{string.Join(',', prompt.ValidChoices)}");
            var valid = BuildPromptCommand(prompt, selected);
            await apply($"{stage}-prompt-{index:D2}-wrong-player", 1 - prompt.PlayerIndex, valid, false);
            await apply($"{stage}-prompt-{index:D2}-invalid", prompt.PlayerIndex,
                BuildInvalidPromptCommand(prompt), false);
            await reconnect($"{stage}-before-prompt-{index:D2}");
            await apply($"{stage}-prompt-{index:D2}-valid", prompt.PlayerIndex, valid, true);
            await apply($"{stage}-prompt-{index:D2}-expired-duplicate", prompt.PlayerIndex, valid, false);
            await reconnect($"{stage}-after-prompt-{index:D2}");

            if (selectCounter)
            {
                evidenceCounts[ResponseEvidence]++;
                postconditions.Add("响应栈中实际提交了对方盖伏反击战术，并经过恢复后继续结算");
            }
        }

        throw new XunitException($"{scenario.Id}/{stage} 在 240 次选择后仍未结束");

        void cutpointEvidence(string name)
        {
            if (!postconditions.Contains($"命中关键切点：{name}", StringComparer.Ordinal))
                postconditions.Add($"命中关键切点：{name}");
        }
    }

    private static LongChainScenario Scenario(string id, string cardId, int seed,
        string riskA, string riskB,
        string? setup = null, string? action = null, string? expectedEvidence = null,
        string mechanism = "attack")
        => new(id, cardId, seed, mechanism switch
            {
                "attachment" => [AttachmentRisk, DisasterTrialRisk],
                "timed-cleanup" => [TimedRisk, CleanupRisk, DisasterTrialRisk],
                "response" => [ResponseRisk, DisasterTrialRisk],
                "private-order" => [PrivateRisk, DisasterTrialRisk],
                _ => [DisasterTrialRisk],
            },
            setup ?? $"{cardId} 置于我方前排，双方具有充足资源和合法目标",
            action ?? "经权威 attack 与 resolvePrompt 入口发动该卡进攻时能力",
            expectedEvidence ?? "焦点卡的实际效果事件或可变状态被保留并在 A/B/C 三线一致",
            mechanism);

    private static L12GameEngine CreateGame(LongChainScenario scenario)
    {
        var baseDeck = Catalog.DeckAt(0);
        var definition = Catalog.Cards[scenario.CardId];
        var focusDeck = new L12PresetDeckDefinition
        {
            Name = $"{scenario.CardId} LC-01",
            MasterId = baseDeck.MasterId,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [.. baseDeck.SpecialIds],
        };
        var game = new L12GameEngine(Catalog, scenario.Id, "LC01", scenario.Seed,
            ["甲", "乙"], [focusDeck, baseDeck], skipPreparation: true, disasterMode: "all",
            autoPassEmptyResponses: scenario.Mechanism is not ("response" or "timed-cleanup"),
            concealHiddenResponseAvailability: false,
            stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 3;
        game.State.TurnSerial = 5;
        game.State.Phase = L12Phase.Main;
        game.State.DisasterValue = 0;
        PreparePlayer(game.State.Players[0], 0, scenario.Id);
        PreparePlayer(game.State.Players[1], 1, scenario.Id);

        var focus = Card(definition, "focus");
        focus.OwnerIndex = 0;
        focus.SummonRound = -1;
        focus.Tapped = false;
        focus.HasCharge = true;
        if (scenario.Mechanism == "attachment")
        {
            focus.SummonRound = game.State.Round;
            game.State.Players[0].Hand.Insert(0, focus);
        }
        else game.State.Players[0].Field[0][0] = focus;

        if (scenario.Mechanism == "response")
        {
            var counter = Card(Catalog.Cards["S01-0016"], "lc-response-counter");
            counter.OwnerIndex = 1;
            counter.Hidden = true;
            counter.SetRound = 0;
            game.State.Players[1].Field[1][2] = counter;
        }

        var trial = Card(Catalog.Cards["S02-06S3"], "lc-trial");
        trial.OwnerIndex = 0;
        trial.TrialProgress = 8;
        game.State.Players[0].SpecialZones.Trials.Add(trial);
        game.InitializeGmDisasters();
        return game;
    }

    private static void PreparePlayer(L12PlayerState player, int index, string scenarioId)
    {
        player.Hp = 30;
        player.MasterTapped = false;
        player.Hand.Clear();
        player.Library.Clear();
        player.Graveyard.Clear();
        player.Morale.Clear();
        player.Resolving.Clear();
        player.Removed.Clear();
        player.ExtraRelics.Clear();
        player.Relic = null;
        player.SpecialZones.Runes = 12;
        player.SpecialZones.TrialLevel = 8;
        player.SpecialZones.TrialCapacity = 20;
        player.SpecialZones.Trials.Clear();
        player.SpecialZones.CanopicProgress.Clear();
        player.UsedAbilities.Clear();
        player.Field[0] = new L12CardInstance?[3];
        player.Field[1] = new L12CardInstance?[3];

        for (var moraleIndex = 0; moraleIndex < 16; moraleIndex++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S02-05C1A",
                InstanceId = $"lc-resource-{index}-{moraleIndex}",
                IsGodPower = moraleIndex >= 8,
            });

        var fieldIds = index == 0
            ? new[] { "S01-0103", "S01-0203", "S01-0303", "S01-0403" }
            : new[] { "S01-0107", "S01-0208", "S01-0308", "S01-0407", "S02-0101", "S02-0207" };
        var positions = index == 0
            ? new[] { (0, 1), (0, 2), (1, 0), (1, 1) }
            : new[] { (0, 0), (0, 1), (0, 2), (1, 0), (1, 1), (1, 2) };
        for (var itemIndex = 0; itemIndex < positions.Length; itemIndex++)
        {
            var card = Card(Catalog.Cards[fieldIds[itemIndex]], $"lc-field-{index}-{itemIndex}");
            card.OwnerIndex = index;
            card.SummonRound = -1;
            var (row, slot) = positions[itemIndex];
            player.Field[row][slot] = card;
        }

        var handIds = new[] { "S01-0003", "S01-0205", "S01-0305", "S01-0405", "S02-0005" };
        var libraryIds = new[] { "S01-0007", "S01-0108", "S01-0208", "S01-0308", "S01-0408", "S02-0007" };
        var graveIds = new[] { "S01-0005", "S01-0106", "S01-0206", "S01-0306", "S01-0406", "S02-0006" };
        var sentinel = Card(Catalog.Cards[handIds[0]], $"lc-private-p{index}-{scenarioId}");
        sentinel.OwnerIndex = index;
        player.Hand.Add(sentinel);
        for (var itemIndex = 0; itemIndex < handIds.Length; itemIndex++)
            player.Hand.Add(Card(Catalog.Cards[handIds[itemIndex]], $"lc-hand-{index}-{itemIndex}"));
        for (var itemIndex = 0; itemIndex < libraryIds.Length; itemIndex++)
            player.Library.Add(Card(Catalog.Cards[libraryIds[itemIndex]], $"lc-library-{index}-{itemIndex}"));
        for (var itemIndex = 0; itemIndex < graveIds.Length; itemIndex++)
            player.Graveyard.Add(Card(Catalog.Cards[graveIds[itemIndex]], $"lc-grave-{index}-{itemIndex}"));
    }

    private static L12CardInstance Card(L12CardDefinition definition, string instanceId)
        => new()
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            IsCounterTactic = definition.IsCounterTactic,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
        };

    private static void AssertHistoricalFocusEvidence(LongChainScenario scenario, L12GameEngine game,
        List<string> commands, List<string> cutpoints, List<string> postconditions)
    {
        if (scenario.Mechanism is "attachment" or "timed-cleanup") return;
        var effectEvents = game.State.Events.Count(entry => entry.Cards.Any(card => card.InstanceId == "focus")
            && entry.Type is not ("attack" or "gm"));
        if (effectEvents == 0)
            Fail(scenario, commands, cutpoints, "historical-focus-postcondition",
                "焦点卡只完成了普通进攻，没有形成其卡效事件或历史可变状态证据");
        if (scenario.Mechanism == "response")
        {
            var negated = game.State.Events.Any(entry => entry.EffectResultStatus == "negated"
                && entry.Cards.Any(card => card.InstanceId == "focus"));
            if (!negated)
                Fail(scenario, commands, cutpoints, "response-negation-postcondition",
                    "反击战术已提交，但焦点效果没有留下 negated 结果");
        }
        postconditions.Add($"焦点卡 {scenario.CardId} 形成 {effectEvents} 条非普通进攻卡效事件");
    }

    private static int CountOiranModifiers(L12GameEngine game)
        => game.State.Players.SelectMany(player => player.Field.SelectMany(row => row))
            .Where(card => card is not null)
            .Sum(card => card!.TimedModifiers.Count(modifier =>
                modifier.Source.Contains("吉原的花魁", StringComparison.Ordinal)));

    private static List<string> SelectChoices(L12GameEngine game, L12Prompt prompt,
        bool selectCounterResponse = false)
    {
        if (prompt.Kind == "response" && prompt.ValidChoices.Contains("pass"))
        {
            if (selectCounterResponse)
            {
                var counter = prompt.ValidChoices.FirstOrDefault(choice =>
                    !choice.Equals("pass", StringComparison.OrdinalIgnoreCase));
                if (counter is not null) return [counter];
            }
            return ["pass"];
        }
        if (prompt.MaxChoose == 0) return [];
        var candidates = prompt.ValidChoices
            .Where(choice => !choice.StartsWith("lc-private-", StringComparison.OrdinalIgnoreCase))
            .Where(choice => !choice.Equals("skip", StringComparison.OrdinalIgnoreCase)
                && !choice.Equals("mode:none", StringComparison.OrdinalIgnoreCase)
                && !choice.Equals("no", StringComparison.OrdinalIgnoreCase)
                && !choice.Equals("cancel", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count < prompt.MinChoose) candidates = [.. prompt.ValidChoices];

        var constraint = prompt.Data.GetValueOrDefault("selectionConstraint");
        if (constraint is "one-resource-two-field-legions" or "zero-resource-two-field-legions")
        {
            var player = game.State.Players[prompt.PlayerIndex];
            var resources = candidates.Where(id => player.Morale.Any(card => card.InstanceId == id && !card.Tapped)
                || player.Field.SelectMany(row => row).Any(card => card?.InstanceId == id && card.CardId == "S01-0212"))
                .Take(constraint == "one-resource-two-field-legions" ? 1 : 0);
            var field = candidates.Where(id => player.Field.SelectMany(row => row)
                    .Any(card => card?.InstanceId == id && card.CardType == "legion"))
                .Take(2);
            return resources.Concat(field).ToList();
        }

        var choose = Math.Min(prompt.MinChoose, candidates.Count);
        var selected = candidates.Take(choose).ToList();
        if (constraint == "distinct-card-names")
        {
            var distinct = new List<string>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var choice in candidates)
            {
                var name = PromptCardName(game, choice) ?? choice;
                if (!names.Add(name)) continue;
                distinct.Add(choice);
                if (distinct.Count == choose) break;
            }
            selected = distinct;
        }
        return selected;
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

    private static L12Command BuildInvalidPromptCommand(L12Prompt prompt)
    {
        const string invalid = "__lc01_invalid_choice__";
        var placement = prompt.Data.GetValueOrDefault("placementMode");
        return placement is "split-top-bottom" or "all-top-bottom" or "all-bottom"
            ? new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                TopCardInstanceIds: [invalid], BottomCardInstanceIds: [])
            : new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: invalid);
    }

    private static string? PromptCardName(L12GameEngine game, string instanceId)
    {
        foreach (var player in game.State.Players)
        {
            var card = player.Hand.Concat(player.Library).Concat(player.Graveyard).Concat(player.Removed)
                .Concat(player.Resolving).Concat(player.ExtraRelics).Concat(player.SpecialZones.Trials)
                .Concat(player.SpecialZones.CanopicProgress)
                .FirstOrDefault(item => item.InstanceId == instanceId)
                ?? player.Field.SelectMany(row => row).FirstOrDefault(item => item?.InstanceId == instanceId)
                ?? (player.Relic?.InstanceId == instanceId ? player.Relic : null);
            if (card is not null) return card.Name;
        }
        return null;
    }

    private static void AssertSameOutcome(LongChainScenario scenario, string label,
        CommandResult a, CommandResult b, CommandResult c)
    {
        if (a.Accepted != b.Accepted || a.Accepted != c.Accepted || a.Error != b.Error || a.Error != c.Error)
            throw new XunitException($"{scenario.Id}/{label} A/B/C 命令结果不一致：" +
                $"A={a.Accepted}:{a.Error}; B={b.Accepted}:{b.Error}; C={c.Accepted}:{c.Error}");
    }

    private static void AssertEquivalent(LongChainScenario scenario, List<string> commands, List<string> cutpoints,
        string label, L12GameEngine a, L12GameEngine b, L12GameEngine c, string private0, string private1)
    {
        var stateA = a.SerializeFullState();
        var stateB = b.SerializeFullState();
        var stateC = c.SerializeFullState();
        var pa = ProjectionJson(a);
        var pb = ProjectionJson(b);
        var pc = ProjectionJson(c);
        var summary = ComparisonSummary(a, b, c, pa, pb, pc);
        AssertJsonEqual(scenario, commands, cutpoints, $"{label}/authority-state-A-B", stateA, stateB, summary);
        AssertJsonEqual(scenario, commands, cutpoints, $"{label}/authority-state-A-C", stateA, stateC, summary);
        if (a.ComputeStateHash() != b.ComputeStateHash() || a.ComputeStateHash() != c.ComputeStateHash())
            Fail(scenario, commands, cutpoints, $"{label}/authority-hash", $"A/B/C 状态哈希不一致；{summary}");
        if (a.RandomState != b.RandomState || a.RandomState != c.RandomState
            || a.RandomDrawCount != b.RandomDrawCount || a.RandomDrawCount != c.RandomDrawCount)
            Fail(scenario, commands, cutpoints, $"{label}/authority-random",
                $"A/B/C 随机状态或抽取计数不一致；{summary}");
        if (a.State.Revision != b.State.Revision || a.State.Revision != c.State.Revision
            || a.State.EventSequence != b.State.EventSequence || a.State.EventSequence != c.State.EventSequence
            || a.CardFactSignalSequence != b.CardFactSignalSequence
            || a.CardFactSignalSequence != c.CardFactSignalSequence)
            Fail(scenario, commands, cutpoints, $"{label}/authority-sequence",
                $"A/B/C revision、事件或卡牌事实序号不一致；{summary}");

        AssertJsonEqual(scenario, commands, cutpoints, $"{label}/projection.player0-A-B", pa.Player0, pb.Player0, summary);
        AssertJsonEqual(scenario, commands, cutpoints, $"{label}/projection.player0-A-C", pa.Player0, pc.Player0, summary);
        AssertJsonEqual(scenario, commands, cutpoints, $"{label}/projection.player1-A-B", pa.Player1, pb.Player1, summary);
        AssertJsonEqual(scenario, commands, cutpoints, $"{label}/projection.player1-A-C", pa.Player1, pc.Player1, summary);
        AssertJsonEqual(scenario, commands, cutpoints, $"{label}/projection.spectator-A-B", pa.Spectator, pb.Spectator, summary);
        AssertJsonEqual(scenario, commands, cutpoints, $"{label}/projection.spectator-A-C", pa.Spectator, pc.Spectator, summary);
        AssertJsonEqual(scenario, commands, cutpoints, $"{label}/projection.referee-A-B", pa.Referee, pb.Referee, summary);
        AssertJsonEqual(scenario, commands, cutpoints, $"{label}/projection.referee-A-C", pa.Referee, pc.Referee, summary);
        AssertPrivateSentinels(scenario, commands, cutpoints, label, a, pa, private0, private1);
    }

    private static void AssertPrivateSentinels(LongChainScenario scenario, List<string> commands,
        List<string> cutpoints, string label, L12GameEngine game, ProjectionSet projection,
        string private0, string private1)
    {
        if (game.State.Players[0].Hand.Any(card => card.InstanceId == private0))
        {
            if (!VisibleHandIds(projection.Player0, 0).Contains(private0)
                || HasHandField(projection.Player1, 0)
                || HasHandField(projection.Spectator, 0)
                || HasHandField(projection.Referee, 0))
                Fail(scenario, commands, cutpoints, $"{label}/privacy-p0",
                    "玩家1手牌结构字段或私密哨兵在接收者投影中的授权不符合合同");
        }
        if (game.State.Players[1].Hand.Any(card => card.InstanceId == private1))
        {
            if (!VisibleHandIds(projection.Player1, 1).Contains(private1)
                || HasHandField(projection.Player0, 1)
                || HasHandField(projection.Spectator, 1)
                || HasHandField(projection.Referee, 1))
                Fail(scenario, commands, cutpoints, $"{label}/privacy-p1",
                    "玩家2手牌结构字段或私密哨兵在接收者投影中的授权不符合合同");
        }
    }

    private static HashSet<string> VisibleHandIds(string projectionJson, int playerIndex)
    {
        using var document = JsonDocument.Parse(projectionJson);
        var player = document.RootElement.GetProperty("players")[playerIndex];
        if (!player.TryGetProperty("hand", out var hand)) return [];
        return hand.EnumerateArray()
            .Select(card => card.GetProperty("instanceId").GetString())
            .Where(id => id is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool HasHandField(string projectionJson, int playerIndex)
    {
        using var document = JsonDocument.Parse(projectionJson);
        var player = document.RootElement.GetProperty("players")[playerIndex];
        return player.TryGetProperty("hand", out _);
    }

    private static ProjectionSet ProjectionJson(L12GameEngine game)
        => new(
            JsonSerializer.Serialize(game.SnapshotFor(0), WireJson),
            JsonSerializer.Serialize(game.SnapshotFor(1), WireJson),
            JsonSerializer.Serialize(game.SnapshotForSpectator(), WireJson),
            JsonSerializer.Serialize(game.SnapshotForReferee(), WireJson));

    private static string ComparisonSummary(L12GameEngine a, L12GameEngine b, L12GameEngine c,
        ProjectionSet pa, ProjectionSet pb, ProjectionSet pc)
        => $"authorityHash[A={a.ComputeStateHash()},B={b.ComputeStateHash()},C={c.ComputeStateHash()}]; " +
           $"projectionHash[p0={Sha256(pa.Player0)}/{Sha256(pb.Player0)}/{Sha256(pc.Player0)}," +
           $"p1={Sha256(pa.Player1)}/{Sha256(pb.Player1)}/{Sha256(pc.Player1)}," +
           $"spectator={Sha256(pa.Spectator)}/{Sha256(pb.Spectator)}/{Sha256(pc.Spectator)}," +
           $"referee={Sha256(pa.Referee)}/{Sha256(pb.Referee)}/{Sha256(pc.Referee)}]";

    private static AtomicContract CaptureAtomicContract(L12GameEngine game)
    {
        var projections = ProjectionJson(game);
        return new AtomicContract(
            game.SerializeFullState(),
            game.ComputeStateHash(),
            JsonSerializer.Serialize(game.RandomState),
            game.RandomDrawCount,
            game.State.Revision,
            game.State.EventSequence,
            game.CardFactSignalSequence,
            JsonSerializer.Serialize(game.State.Events, WireJson),
            JsonSerializer.Serialize(game.UnpersistedEvents, WireJson),
            projections.Player0,
            projections.Player1,
            projections.Spectator,
            projections.Referee);
    }

    private static void AssertRejectedAtomic(LongChainScenario scenario, List<string> commands,
        List<string> cutpoints, string label, AtomicContract before, AtomicContract after)
    {
        if (before == after) return;
        var fields = typeof(AtomicContract).GetProperties()
            .Where(property => !Equals(property.GetValue(before), property.GetValue(after)))
            .Select(property => property.Name)
            .ToArray();
        Fail(scenario, commands, cutpoints, $"{label}/rejected-atomic",
            $"拒绝命令改变了合同字段：{string.Join(',', fields)}");
    }

    private static (double SnapshotP95Milliseconds, double RestoreP95Milliseconds) Measure(L12GameEngine game)
    {
        var snapshotSamples = new List<double>();
        var restoreSamples = new List<double>();
        for (var index = 0; index < 12; index++)
        {
            var watch = Stopwatch.StartNew();
            _ = game.SnapshotFor(index % 2);
            watch.Stop();
            snapshotSamples.Add(watch.Elapsed.TotalMilliseconds);

            var state = game.SerializeFullState();
            var random = game.RandomState ?? throw new XunitException("LC-01 缺少确定性随机状态");
            watch.Restart();
            _ = L12GameEngine.RestoreCheckpoint(Catalog, state, random, game.CardFactSignalSequence,
                game.AutoPassEmptyResponses, game.ConcealHiddenResponseAvailability);
            watch.Stop();
            restoreSamples.Add(watch.Elapsed.TotalMilliseconds);
        }
        return (P95(snapshotSamples), P95(restoreSamples));
    }

    private static double P95(List<double> samples)
    {
        samples.Sort();
        return samples[(int)Math.Ceiling(samples.Count * 0.95) - 1];
    }

    private static L12GameEngine Restore(string state, L12RandomState random, long cardFactSignalSequence,
        L12GameEngine source)
        => L12GameEngine.RestoreCheckpoint(Catalog, state, random, cardFactSignalSequence,
            source.AutoPassEmptyResponses, source.ConcealHiddenResponseAvailability);

    private static L12RandomState RequireRandom(L12GameEngine game, LongChainScenario scenario, string label)
        => game.RandomState ?? throw new XunitException($"{scenario.Id}/{label} 缺少确定性随机状态");

    private static void AssertJsonEqual(LongChainScenario scenario, List<string> commands, List<string> cutpoints,
        string label, string expected, string actual, string comparisonSummary)
    {
        if (expected == actual) return;
        var difference = FirstJsonDifference(expected, actual);
        Fail(scenario, commands, cutpoints, label, $"{difference}; {comparisonSummary}");
    }

    private static string FirstJsonDifference(string expected, string actual)
    {
        try
        {
            using var expectedDocument = JsonDocument.Parse(expected);
            using var actualDocument = JsonDocument.Parse(actual);
            return FirstJsonDifference(expectedDocument.RootElement, actualDocument.RootElement, "$")
                ?? "JSON 文本不同但结构比较未定位差异";
        }
        catch (JsonException)
        {
            var index = Enumerable.Range(0, Math.Min(expected.Length, actual.Length))
                .FirstOrDefault(position => expected[position] != actual[position]);
            return $"首个文本差异 index={index}";
        }
    }

    private static string? FirstJsonDifference(JsonElement expected, JsonElement actual, string path)
    {
        if (expected.ValueKind != actual.ValueKind)
            return $"首个差异 {path}: kind expected={expected.ValueKind}, actual={actual.ValueKind}";
        if (expected.ValueKind == JsonValueKind.Object)
        {
            var left = expected.EnumerateObject().ToDictionary(item => item.Name, item => item.Value);
            var right = actual.EnumerateObject().ToDictionary(item => item.Name, item => item.Value);
            foreach (var name in left.Keys.Union(right.Keys).Order(StringComparer.Ordinal))
            {
                if (!left.TryGetValue(name, out var l)) return $"首个差异 {path}.{name}: expected missing";
                if (!right.TryGetValue(name, out var r)) return $"首个差异 {path}.{name}: actual missing";
                var nested = FirstJsonDifference(l, r, $"{path}.{name}");
                if (nested is not null) return nested;
            }
            return null;
        }
        if (expected.ValueKind == JsonValueKind.Array)
        {
            var left = expected.EnumerateArray().ToArray();
            var right = actual.EnumerateArray().ToArray();
            if (left.Length != right.Length)
                return $"首个差异 {path}.length: expected={left.Length}, actual={right.Length}";
            for (var index = 0; index < left.Length; index++)
            {
                var nested = FirstJsonDifference(left[index], right[index], $"{path}[{index}]");
                if (nested is not null) return nested;
            }
            return null;
        }
        return expected.GetRawText() == actual.GetRawText()
            ? null
            : $"首个差异 {path}: expected={expected.GetRawText()}, actual={actual.GetRawText()}";
    }

    private static void Fail(LongChainScenario scenario, List<string> commands, List<string> cutpoints,
        string label, string reason)
        => throw new XunitException($"""
            LC-01 长链对抗失败
            scenario={scenario.Id}
            card={scenario.CardId}
            seed={scenario.Seed}
            setup={scenario.Setup}
            action={scenario.Action}
            expectedEvidence={scenario.ExpectedEvidence}
            stage={label}
            cutpoints={string.Join(" -> ", cutpoints)}
            reason={reason}
            minimalCommandPrefixCount={commands.Count}
            minimalCommandPrefix:
            {string.Join(Environment.NewLine, commands)}
            """);

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record ProjectionSet(string Player0, string Player1, string Spectator, string Referee);
    private sealed record AtomicContract(
        string StateJson,
        string StateHash,
        string RandomStateJson,
        long RandomDrawCount,
        long Revision,
        long EventSequence,
        long CardFactSequence,
        string EventsJson,
        string AuditJson,
        string Player0Projection,
        string Player1Projection,
        string SpectatorProjection,
        string RefereeProjection);
}
