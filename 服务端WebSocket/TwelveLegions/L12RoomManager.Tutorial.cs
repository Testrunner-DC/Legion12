using System.Text.Json;

namespace TwelveLegions.Server;

public sealed partial class L12RoomManager
{
    // Local teaching is opt-in. Deployment policy keeps this flag off outside the test service.
    public bool TutorialAvailable { get; }
    private sealed record TutorialCreateReceipt(string RoomCode, string MatchId, string LessonId);

    private sealed class TutorialContext(string lessonId)
    {
        public string LessonId { get; } = lessonId;
        public string StepId { get; set; } = lessonId switch
            { "combat" => "attack", "response" => "response-legion", _ => lessonId };
        public string PracticeInstanceId { get; set; } = string.Empty;
        public string CounterInstanceId { get; set; } = string.Empty;
        public string SiegeInstanceId { get; set; } = string.Empty;
        public int PracticeTroops { get; set; }
        public bool Frozen { get; set; }
    }

    private static object? TutorialView(Room room) => room.Tutorial is not { } lesson ? null : new
    {
        lessonId = lesson.LessonId, version = 1, stepId = lesson.StepId,
        status = lesson.StepId == "completed" ? "completed" : "active",
    };

    public async Task<IReadOnlyList<OutgoingMessage>> CreateTutorialAsync(Guid sessionId,
        string lessonId, string? requestId)
    {
        requestId = NormalizeActionRequestId(requestId);
        if (!TutorialAvailable) return Error(sessionId, "教学尚未开放", "tutorialUnavailable", requestId);
        if (requestId is null || lessonId is not ("deploy" or "move" or "combat" or "response"))
            return Error(sessionId, "教学请求无效", "tutorialRejected", requestId);
        if (!TryDeploymentGuard(admission: true, out var deploymentGuard))
            return DeploymentEntryRejected(sessionId, requestId);
        using var deployment = deploymentGuard;
        await _sessionRecoveryGate.WaitAsync();
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var session) || !session.Connected
                || _platform?.Account(session.AccountId ?? "") is not
                    { Disabled: false, Deleted: false, MustChangePassword: false, MustChangeUsername: false })
                return Error(sessionId, "请先登录有效账号", "tutorialRejected", requestId);
            if (session.TutorialRequests.TryGetValue(requestId, out var receipt))
            {
                if (receipt.LessonId != lessonId || session.RoomCode != receipt.RoomCode
                    || !_rooms.TryGetValue(receipt.RoomCode, out var prior))
                    return Error(sessionId, "该教学请求已结束或已用于其他关卡，请重新进入", "tutorialRequestExpired", requestId);
                // Do not hold the global recovery gate while waiting for one busy room. A retry
                // may read committed state only after acquiring that room's gate immediately.
                if (!prior.Gate.Wait(0))
                    return Error(sessionId, "本关正在处理操作，请稍后重试", "tutorialRejected", requestId);
                try
                {
                    if (!IsCurrentPlayerMembership(sessionId, session, prior)
                        || !session.TutorialRequests.TryGetValue(requestId, out var currentReceipt)
                        || currentReceipt != receipt || prior.Game?.State.MatchId != receipt.MatchId
                        || prior.Tutorial is not { } previousLesson || previousLesson.LessonId != lessonId)
                        return Error(sessionId, "该教学请求已结束或已用于其他关卡，请重新进入", "tutorialRequestExpired", requestId);
                    if (prior.Closed || previousLesson.Frozen)
                        return Error(sessionId, "本关记录未能完成，请退出后重开本关", "tutorialRestartRequired", requestId);
                    return BroadcastRoom(prior).Concat(BroadcastGame(prior, forceCritical: true,
                        requestSessionId: sessionId, requestId: requestId)).ToArray();
                }
                finally { prior.Gate.Release(); }
            }
            if (session.RoomCode is not null)
                return Error(sessionId, "请先退出当前房间", "tutorialRejected", requestId);
            lock (_matchmakingGate)
                if (_matchmaking.Any(entry => entry.AccountId == session.AccountId && IsQueueEntryValid(entry)))
                    return Error(sessionId, "请先取消匹配再进入教学", "tutorialRejected", requestId);
            var currentPolicy = CaptureOperationsPolicy();
            if (MaintenanceSandboxFenceActive() || currentPolicy.IsNewGameEntryBlocked(_utcNow()))
                return Error(sessionId, "维护即将开始或正在进行，暂时不能进入教学", "tutorialRejected", requestId);

            var policy = currentPolicy.ForSandbox("none");
            // These are legal published cards, not a client-provided deck or a GM checkpoint.
            // Replacing the preset's three Bai Qi with three Xi Shi leaves a legal 40-card deck.
            var preset = _catalog.DeckAt(0);
            var deck = new L12PresetDeckDefinition { Name = "教学公开构筑", MasterId = preset.MasterId,
                CardIds = preset.CardIds.Select(id => id == "S01-0109" ? "S01-0116"
                    : lessonId == "response" && id == "S01-0002" ? "S01-0008" : id).ToList(),
                MoraleIds = [.. preset.MoraleIds], SpecialIds = [.. preset.SpecialIds] };
            if (!L12DeckValidator.TryValidatePreset(_catalog, deck, out _))
                return Error(sessionId, "当前规则与教学配方不兼容", "tutorialUnavailable", requestId);
            var room = ReserveTutorialRoom(policy, lessonId);
            var opponentId = Guid.NewGuid();
            var opponent = new Session { Id = opponentId, Name = "教学对手", IsVirtual = true,
                RoomCode = room.Code, PlayerIndex = 1, CustomDeck = deck };
            var initializationStage = "bootstrap";
            try
            {
                room.Game = new L12GameEngine(_catalog, Guid.NewGuid().ToString("N"), room.Code, 1701,
                    [session.Name, opponent.Name], [deck, deck], disasterMode: "none",
                    operationsPolicy: policy, stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion,
                    effectPresentationSnapshot: CaptureEffectPresentationSnapshot(), utcNow: _utcNow,
                    responseModes: [L12GameEngine.DefaultResponseMode, L12GameEngine.DefaultResponseMode]);
                BootstrapTutorial(room);
                // Exactly the trusted, legally reached state is checkpoint zero. All subsequent
                // student and scripted-opponent commands use the existing durable Journal path.
                initializationStage = "start-recording";
                await _recorder.StartAsync(room.Game, "sandbox", session.AccountId, null, [deck, deck]);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"Tutorial unavailable stage={initializationStage} exception={error.GetType().Name}");
                ((ICollection<KeyValuePair<string, Room>>)_rooms).Remove(new(room.Code, room));
                return Error(sessionId, "教学配方或记录暂时不可用，未进入关卡", "tutorialUnavailable", requestId);
            }
            room.Sessions.Add(sessionId);
            room.Sessions.Add(opponentId);
            room.Ready[0] = room.Ready[1] = true;
            _sessions[opponentId] = opponent;
            session.RoomCode = room.Code;
            session.PlayerIndex = 0;
            session.CustomDeck = deck;
            session.TutorialRequests[requestId] = new(room.Code, room.Game!.State.MatchId, lessonId);
            if (session.TutorialRequests.Count > 32)
                session.TutorialRequests.Remove(session.TutorialRequests.Keys.First());
            room.Closed = false;
            return BroadcastRoom(room).Concat(BroadcastGame(room, forceCritical: true,
                requestSessionId: sessionId, requestId: requestId)).ToArray();
        }
        finally { _sessionRecoveryGate.Release(); }
    }

    private Room ReserveTutorialRoom(L12OperationsPolicySnapshot policy, string lessonId)
    {
        Room room;
        do
        {
            room = new Room { Code = GenerateRoomCode(), OperationsPolicy = policy, Closed = true,
                Tutorial = new TutorialContext(lessonId),
                Options = new L12RoomOptions { MatchModeId = "sandbox", Spectating = "disabled",
                    HandVisibility = "request", DisasterMode = "none", UseCardRestrictions = false } };
        }
        while (!_rooms.TryAdd(room.Code, room));
        return room;
    }

    private static void BootstrapTutorial(Room room)
    {
        var game = room.Game!;
        var lesson = room.Tutorial!;
        var initiative = game.State.PendingPrompts.Single();
        BootstrapCommand(game, initiative.PlayerIndex, new("resolvePrompt", PromptId: initiative.PromptId,
            CardInstanceIds: [initiative.PlayerIndex == 0 ? "first" : "second"]));
        BootstrapCommand(game, 0, new("mulligan", CardInstanceIds: []));
        BootstrapCommand(game, 1, new("mulligan", CardInstanceIds: []));
        if (lesson.LessonId == "response")
        {
            BootstrapResponseTutorial(game, lesson);
            return;
        }
        var combat = lesson.LessonId == "combat";
        static int Copies(L12PlayerState player) => player.Hand.Count(card => card.CardId == "S01-0116");
        // Bounded legal turn progression, never seed hunting, GM mutation, or synthetic padding.
        var turns = 0;
        while (game.State.ActivePlayer != 0 || Copies(game.State.Players[0]) < (combat ? 2 : 1)
               || game.State.Players[0].Morale.Count(card => !card.Tapped) < (combat ? 2 : 1)
               || combat && Copies(game.State.Players[1]) < 1)
        {
            if (++turns > 40) throw new InvalidOperationException("教学合法前缀超出预算");
            BootstrapCommand(game, game.State.ActivePlayer, new("endTurn"));
        }
        lesson.PracticeInstanceId = game.State.Players[0].Hand.First(card => card.CardId == "S01-0116").InstanceId;
        if (lesson.LessonId == "deploy") return;
        BootstrapPlay(game, 0, lesson.PracticeInstanceId, combat ? 0 : 1, combat ? 0 : 1);
        if (combat)
        {
            var defender = game.State.Players[0].Hand.First(card => card.CardId == "S01-0116");
            BootstrapPlay(game, 0, defender.InstanceId, 0, 2);
        }
        BootstrapCommand(game, 0, new("endTurn"));
        if (combat)
        {
            var attacker = game.State.Players[1].Hand.First(card => card.CardId == "S01-0116");
            BootstrapPlay(game, 1, attacker.InstanceId, 0, 1);
        }
        BootstrapCommand(game, 1, new("endTurn"));
    }

    private static void BootstrapResponseTutorial(L12GameEngine game, TutorialContext lesson)
    {
        static bool Hand(L12PlayerState player, string id) => player.Hand.Any(card => card.CardId == id);
        var turns = 0;
        // Same published legal recipe and fixed seed as the Journal-proved response exercise.
        // Never set a hidden card, draw order, morale, turn, or board through a teaching side door.
        while (game.State.ActivePlayer != 0 || !Hand(game.State.Players[0], "S01-0116")
               || !Hand(game.State.Players[0], "S01-0016") || !Hand(game.State.Players[1], "S01-0008")
               || game.State.Players[0].Morale.Count(card => !card.Tapped) < 3
               || game.State.Players[1].Morale.Count(card => !card.Tapped) < 2)
        {
            if (++turns > 40 || game.State.PendingPrompts.Count != 0)
                throw new InvalidOperationException("响应教学合法前缀不兼容");
            BootstrapCommand(game, game.State.ActivePlayer, new("endTurn"));
        }
        var legion = game.State.Players[0].Hand.First(card => card.CardId == "S01-0116");
        lesson.PracticeInstanceId = legion.InstanceId;
        lesson.PracticeTroops = legion.Troops;
        lesson.CounterInstanceId = game.State.Players[0].Hand.First(card => card.CardId == "S01-0016").InstanceId;
        lesson.SiegeInstanceId = game.State.Players[1].Hand.First(card => card.CardId == "S01-0008").InstanceId;
    }

    private static void BootstrapPlay(L12GameEngine game, int actor, string instanceId, int row, int slot)
        => BootstrapCommand(game, actor, new("playCard", CardInstanceId: instanceId, Row: row, Slot: slot,
            CardInstanceIds: [game.State.Players[actor].Morale.First(card => !card.Tapped).InstanceId]));

    private static void BootstrapCommand(L12GameEngine game, int actor, L12Command command)
    {
        if (!game.Handle(actor, command).Accepted)
            throw new InvalidOperationException("教学合法初始化命令被规则拒绝");
    }

    private static string? TutorialCommandError(Room room, L12Command command)
    {
        if (room.Tutorial is not { } lesson) return null;
        if (lesson.Frozen) return "本关记录未能完成，请退出后重开本关";
        if (command.Type == "surrender") return null;
        if (command.Type == "resolvePrompt" && room.Game!.State.PendingPrompts.Any(p => p.PlayerIndex == 0))
            return null;
        var allowed = lesson.StepId switch
        {
            "deploy" => command.Type == "playCard" && command.CardInstanceId == lesson.PracticeInstanceId,
            "move" => command.Type == "move" && command.CardInstanceId == lesson.PracticeInstanceId,
            "attack" => command.Type == "attack" && command.CardInstanceId == lesson.PracticeInstanceId
                && command.Target?.Type == "master",
            "end-turn" => command.Type == "endTurn",
            "defend" => command.Type == "resolveDefense",
            "response-legion" => command.Type == "playCard" && command.CardInstanceId == lesson.PracticeInstanceId
                && command.Row == 0,
            "response-cover" => command.Type == "playCard" && command.CardInstanceId == lesson.CounterInstanceId
                && command.Row == 1,
            "response-end-turn" => command.Type == "endTurn",
            _ => false,
        };
        return allowed ? null : "请按本关当前目标操作；需要重新开始时请退出本关";
    }

    // Invoked only after the student's Append has committed, under the same room gate.
    // Read/projection/recovery paths never run the opponent or advance a step.
    private async Task<bool> AdvanceTutorialAsync(Room room)
    {
        if (room.Tutorial is not { } lesson || room.Game!.State.Phase == L12Phase.GameOver) return true;
        try
        {
            for (var budget = 0; budget < 8; budget++)
            {
                UpdateTutorialStep(room);
                var state = room.Game.State;
                L12Command? scripted = null;
                var prompt = state.PendingPrompts.FirstOrDefault(p => p.PlayerIndex == 1);
                if (prompt is not null)
                {
                    if (prompt.Continuation != "stack-response" || !prompt.ValidChoices.Contains("pass"))
                        throw new InvalidOperationException("教学脚本遇到未声明询问");
                    scripted = new("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: ["pass"]);
                }
                else if (state.PendingDefense is { AttackerPlayer: 0, Stage: L12CombatStage.DefenseChoice }
                         && state.PendingPrompts.Count == 0 && state.EffectStack.Count == 0)
                    scripted = new("resolveDefense", CardInstanceIds: []);
                else if (lesson.StepId == "end-turn" && state.ActivePlayer == 1
                         && state.Phase == L12Phase.Main && state.PendingDefense is null)
                {
                    var attacker = state.Players[1].Field.SelectMany(row => row)
                        .Single(card => card?.CardId == "S01-0116");
                    scripted = new("attack", CardInstanceId: attacker!.InstanceId, Target: new("master"));
                }
                else if (lesson.StepId == "response-end-turn" && state.ActivePlayer == 1
                         && state.Phase == L12Phase.Main && state.PendingDefense is null
                         && state.PendingPrompts.Count == 0 && state.EffectStack.Count == 0
                         && state.Players[1].Hand.Any(card => card.InstanceId == lesson.SiegeInstanceId))
                    scripted = new("playCard", CardInstanceId: lesson.SiegeInstanceId,
                        CardInstanceIds: state.Players[1].Morale.Where(card => !card.Tapped)
                            .Take(2).Select(card => card.InstanceId).ToList());
                if (scripted is null)
                {
                    UpdateTutorialStep(room);
                    return true;
                }
                var result = room.Game.Handle(1, scripted);
                if (!result.Accepted) throw new InvalidOperationException("教学对手合法命令被拒绝");
                room.CommandSequence++;
                await _recorder.AppendAsync(room.Game, room.CommandSequence, 1,
                    JsonSerializer.Serialize(scripted), result);
                UpdateTutorialStep(room);
            }
            throw new InvalidOperationException("教学脚本超出命令预算");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Tutorial frozen stage=opponent-command exception={error.GetType().Name}");
            // A committed human action is never replayed to compensate for a failed opponent.
            lesson.Frozen = true;
            await TryRestoreTutorialJournalAsync(room);
            return false;
        }
    }

    private async Task<bool> TryRestoreTutorialJournalAsync(Room room)
    {
        try
        {
            if (await ReloadJournalRoomFromRecorderAsync(room)) return true;
        }
        catch (Exception) { /* Keep the last durable DB anchor; do not publish the mutated engine. */ }
        room.Closed = true;
        room.Tutorial!.Frozen = true;
        return false;
    }

    private static void UpdateTutorialStep(Room room)
    {
        var lesson = room.Tutorial!;
        var state = room.Game!.State;
        if (lesson.LessonId == "response")
        {
            UpdateResponseTutorialStep(room);
            return;
        }
        if (state.Phase == L12Phase.GameOver || state.PendingPrompts.Count != 0
            || state.EffectStack.Count != 0) return;
        var field = state.Players[0].Field;
        var card = field.SelectMany(row => row).FirstOrDefault(card => card?.InstanceId == lesson.PracticeInstanceId);
        switch (lesson.StepId)
        {
            case "deploy" when card is not null:
            case "move" when card is not null && card.LastMovedTurn >= 0:
                lesson.StepId = "completed";
                break;
            case "attack" when card is { Tapped: true } && state.PendingDefense is null:
                lesson.StepId = "end-turn";
                break;
            case "end-turn" when state.PendingDefense is { AttackerPlayer: 1 }:
                lesson.StepId = "defend";
                break;
            case "defend" when state.PendingDefense is null:
                lesson.StepId = "completed";
                break;
        }
    }

    private static void UpdateResponseTutorialStep(Room room)
    {
        var lesson = room.Tutorial!;
        var state = room.Game!.State;
        if (state.Phase == L12Phase.GameOver || lesson.StepId is "completed" or "response-retry") return;
        var own = state.Players[0];
        var prompt = state.PendingPrompts.FirstOrDefault(p => p.PlayerIndex == 0);
        if (prompt is not null)
        {
            if (prompt.Continuation == "stack-response" && prompt.ValidChoices.Contains(lesson.CounterInstanceId))
                lesson.StepId = "response-choose";
            else if (prompt.Kind == "discard-cost") lesson.StepId = "response-discard";
            return;
        }
        if (state.PendingPrompts.Count != 0 || state.EffectStack.Count != 0) return;
        var legion = own.Field.SelectMany(row => row).FirstOrDefault(card => card?.InstanceId == lesson.PracticeInstanceId);
        if (state.Players[1].Graveyard.Any(card => card.InstanceId == lesson.SiegeInstanceId))
        {
            var negated = state.Events.Count(e => e.Type == "effect-result" && e.EffectResultStatus == "negated"
                && e.Cards.FirstOrDefault()?.InstanceId == lesson.SiegeInstanceId) == 1;
            lesson.StepId = negated && legion?.Troops == lesson.PracticeTroops
                && own.Graveyard.Any(card => card.InstanceId == lesson.CounterInstanceId)
                ? "completed" : "response-retry";
        }
        else if (legion is not null && own.Field[1].Any(card => card?.InstanceId == lesson.CounterInstanceId
                     && card.Hidden)) lesson.StepId = "response-end-turn";
        else if (legion is not null) lesson.StepId = "response-cover";
    }

    private bool CanControlSandbox(Room room, Session session)
        => room.IsSandbox && room.GmControllerSessionId == session.Id
           && (room.TrustedSandboxFixture && _platform is null || HasMaintenanceSandboxPermission(session));

    internal Task<IReadOnlyList<OutgoingMessage>> CreateSandboxForTestsAsync(Guid sessionId,
        L12SandboxRequest? request)
    {
        if (_platform is not null) throw new InvalidOperationException("纯沙盒夹具不得使用平台身份上下文");
        return CreateSandboxCoreAsync(sessionId, request, trustedFixture: true);
    }
}
