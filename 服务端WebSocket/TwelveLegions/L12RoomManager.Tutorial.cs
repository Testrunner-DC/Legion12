using System.Text.Json;
using System.Text.Encodings.Web;
using System.Security.Cryptography;
using System.Text;

namespace TwelveLegions.Server;

public sealed partial class L12RoomManager
{
    // Local teaching is opt-in. Deployment policy keeps this flag off outside the test service.
    public bool TutorialAvailable { get; }
    private sealed record TutorialCreateReceipt(string RoomCode, string MatchId, string LessonId);
    private sealed class TutorialDeckSnapshot
    {
        public int SchemaVersion { get; init; }
        public List<TutorialDeckEntry> Decks { get; init; } = [];
    }
    private sealed class TutorialDeckEntry
    {
        public string Role { get; init; } = string.Empty;
        public string PublicCode { get; init; } = string.Empty;
        public int PublicationVersion { get; init; }
        public string NormalizedContentSha256 { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string MasterId { get; init; } = string.Empty;
        public List<string> CardIds { get; init; } = [];
        public List<string> MoraleIds { get; init; } = [];
        public List<string> SpecialIds { get; init; } = [];
    }

    private sealed class TutorialContext(string lessonId)
    {
        public string LessonId { get; } = lessonId;
        public string StepId { get; set; } = lessonId switch
            {
                "combat" => "attack",
                "response" => "response-artifact",
                "preparation" => "preparation-initiative",
                "finale" => "finale-attack",
                _ => lessonId,
            };
        public string PracticeInstanceId { get; set; } = string.Empty;
        public string ArtifactInstanceId { get; set; } = string.Empty;
        public string CounterInstanceId { get; set; } = string.Empty;
        public string CostCardInstanceId { get; set; } = string.Empty;
        public string LegionInstanceId { get; set; } = string.Empty;
        public string AttackerInstanceId { get; set; } = string.Empty;
        public List<string> FinaleAttackerInstanceIds { get; set; } = [];
        public List<string> InitialMoraleInstanceIds { get; set; } = [];
        public List<string> CounterPaymentInstanceIds { get; set; } = [];
        public int ResponseInitialHp { get; set; }
        public bool LegionRevealed { get; set; }
        public bool HiddenDeclarationStarted { get; set; }
        public int PracticeTroops { get; set; }
        public bool Frozen { get; set; }
    }

    private static object? TutorialView(Room room)
    {
        if (room.Tutorial is not { } lesson) return null;
        var status = lesson.StepId == "completed" ? "completed" : "active";
        if (lesson.LessonId != "response")
            return new { lessonId = lesson.LessonId, version = 1, stepId = lesson.StepId, status };

        var state = room.Game!.State;
        var own = state.Players[0];
        var prompt = state.PendingPrompts.FirstOrDefault(candidate => candidate.PlayerIndex == 0);
        string? KnownOwnCard(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return null;
            return own.Hand.Concat(own.Resolving).Concat(own.Graveyard).Concat(own.Removed)
                       .Concat(own.Field.SelectMany(row => row).OfType<L12CardInstance>())
                       .Concat(own.ExtraRelics).Append(own.Relic).OfType<L12CardInstance>()
                       .Any(card => card.InstanceId == instanceId)
                ? instanceId : null;
        }
        var promptActivation = prompt is null ? null : TutorialPromptActivation(state, prompt);
        var activationStep = promptActivation is null ? null
            : promptActivation.SelectionSteps.ElementAtOrDefault(promptActivation.CurrentStep);
        var cost = promptActivation is { Ability: "artifactSearch" }
                   && promptActivation.SourceInstanceId == lesson.ArtifactInstanceId
                   && activationStep is { IsCostSelection: true }
                   && prompt!.ValidChoices.Contains(lesson.CostCardInstanceId, StringComparer.OrdinalIgnoreCase)
            ? KnownOwnCard(lesson.CostCardInstanceId) : null;
        var legion = lesson.LegionRevealed ? KnownOwnCard(lesson.LegionInstanceId) : null;
        if (legion is null && prompt is { Kind: "search" }
            && prompt.Data.GetValueOrDefault("action") == "shanhe-search-pick"
            && prompt.ValidChoices.Contains(lesson.LegionInstanceId, StringComparer.OrdinalIgnoreCase))
            legion = lesson.LegionInstanceId;
        return new
        {
            lessonId = lesson.LessonId,
            version = 2,
            recipeId = "public-response-v1",
            stepId = lesson.StepId,
            status,
            focus = new
            {
                artifactInstanceId = KnownOwnCard(lesson.ArtifactInstanceId),
                counterInstanceId = KnownOwnCard(lesson.CounterInstanceId),
                costCardInstanceId = cost,
                legionInstanceId = legion,
            },
        };
    }

    public async Task<IReadOnlyList<OutgoingMessage>> CreateTutorialAsync(Guid sessionId,
        string lessonId, string? requestId)
    {
        requestId = NormalizeActionRequestId(requestId);
        if (!TutorialAvailable) return Error(sessionId, "教学尚未开放", "tutorialUnavailable", requestId);
        if (requestId is null || lessonId is not
            ("deploy" or "move" or "combat" or "response" or "preparation" or "finale"))
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

            var disasterMode = lessonId == "preparation" ? "all" : "none";
            var policy = currentPolicy.ForSandbox(disasterMode);
            var (studentDeck, opponentDeck) = LoadTutorialDecks();
            if (!L12DeckValidator.TryValidatePreset(_catalog, studentDeck, out _)
                || !L12DeckValidator.TryValidatePreset(_catalog, opponentDeck, out _))
                return Error(sessionId, "当前规则与教学配方不兼容", "tutorialUnavailable", requestId);
            var room = ReserveTutorialRoom(policy, lessonId);
            var opponentId = Guid.NewGuid();
            var opponent = new Session { Id = opponentId, Name = "教学对手", IsVirtual = true,
                RoomCode = room.Code, PlayerIndex = 1, CustomDeck = opponentDeck };
            var initializationStage = "bootstrap";
            try
            {
                room.Game = BuildTutorialGame(room, session.Name, opponent.Name, studentDeck, opponentDeck, policy);
                // Exactly the trusted, legally reached state is checkpoint zero. All subsequent
                // student and scripted-opponent commands use the existing durable Journal path.
                initializationStage = "start-recording";
                await _recorder.StartAsync(room.Game, "sandbox", session.AccountId, null, [studentDeck, opponentDeck]);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"Tutorial unavailable stage={initializationStage} exception={error.GetType().Name} detail={error.Message}");
                ((ICollection<KeyValuePair<string, Room>>)_rooms).Remove(new(room.Code, room));
                return Error(sessionId, "教学配方或记录暂时不可用，未进入关卡", "tutorialUnavailable", requestId);
            }
            room.Sessions.Add(sessionId);
            room.Sessions.Add(opponentId);
            room.Ready[0] = room.Ready[1] = true;
            _sessions[opponentId] = opponent;
            session.RoomCode = room.Code;
            session.PlayerIndex = 0;
            session.CustomDeck = studentDeck;
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
                    HandVisibility = "request", DisasterMode = lessonId == "preparation" ? "all" : "none",
                    UseCardRestrictions = false } };
        }
        while (!_rooms.TryAdd(room.Code, room));
        return room;
    }

    private static readonly JsonSerializerOptions TutorialDeckJson = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static (L12PresetDeckDefinition Student, L12PresetDeckDefinition Opponent) LoadTutorialDecks()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data", "tutorial-decks.v1.json");
        var snapshot = JsonSerializer.Deserialize<TutorialDeckSnapshot>(File.ReadAllText(path), TutorialDeckJson)
            ?? throw new InvalidDataException("教学公开构筑快照不可读取");
        if (snapshot.SchemaVersion != 1 || snapshot.Decks.Count != 2)
            throw new InvalidDataException("教学公开构筑快照版本不兼容");
        var student = snapshot.Decks.Single(deck => deck.Role == "student");
        var opponent = snapshot.Decks.Single(deck => deck.Role == "opponent");
        VerifyTutorialDeck(student, "J2DR72B52KQQ", 1,
            "E93351066B33A5BD2E8044DE02329F59870C4E1EE66F2725058B42B62F151738");
        VerifyTutorialDeck(opponent, "RGKZ73S3NHMQ", 1,
            "2FB47E86AEF1A16F3340176ACD4469B39FA42BA350F0DA1DD6F3C682E43937BD");
        return (ToPreset(student), ToPreset(opponent));
    }

    private static void VerifyTutorialDeck(TutorialDeckEntry deck, string publicCode, int version, string expectedHash)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            deck.Name, deck.MasterId, deck.CardIds, deck.MoraleIds, deck.SpecialIds,
        }, TutorialDeckJson);
        var actualHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        if (deck.PublicCode != publicCode || deck.PublicationVersion != version
            || deck.NormalizedContentSha256 != expectedHash || actualHash != expectedHash)
            throw new InvalidDataException("教学公开构筑快照校验失败");
    }

    private static L12PresetDeckDefinition ToPreset(TutorialDeckEntry deck) => new()
    {
        Name = deck.Name, MasterId = deck.MasterId, CardIds = [.. deck.CardIds],
        MoraleIds = [.. deck.MoraleIds], SpecialIds = [.. deck.SpecialIds],
    };

    private L12GameEngine BuildTutorialGame(Room room, string studentName, string opponentName,
        L12PresetDeckDefinition studentDeck, L12PresetDeckDefinition opponentDeck,
        L12OperationsPolicySnapshot policy)
    {
        var response = room.Tutorial!.LessonId == "response";
        var disasterMode = room.Tutorial.LessonId == "preparation" ? "all" : "none";
        var firstSeed = response ? 1713 : 1701;
        var lastSeed = response ? firstSeed : 1956;
        var lastFailure = "none";
        for (var seed = firstSeed; seed <= lastSeed; seed++)
        {
            var candidate = new L12GameEngine(_catalog, Guid.NewGuid().ToString("N"), room.Code, seed,
                [studentName, opponentName], [studentDeck, opponentDeck], disasterMode: disasterMode,
                operationsPolicy: policy, stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion,
                effectPresentationSnapshot: CaptureEffectPresentationSnapshot(), utcNow: _utcNow,
                responseModes: [L12GameEngine.DefaultResponseMode, L12GameEngine.DefaultResponseMode]);
            var candidateLesson = new TutorialContext(room.Tutorial.LessonId);
            try { BootstrapTutorial(candidate, candidateLesson); }
            catch (InvalidOperationException error) when (!response) { lastFailure = error.Message; continue; }
            if (room.Tutorial.LessonId is not ("response" or "preparation" or "finale")
                && candidate.State.Players.Any(player => player.Hand.Count > 8)) continue;
            room.Tutorial.PracticeInstanceId = candidateLesson.PracticeInstanceId;
            room.Tutorial.ArtifactInstanceId = candidateLesson.ArtifactInstanceId;
            room.Tutorial.CounterInstanceId = candidateLesson.CounterInstanceId;
            room.Tutorial.CostCardInstanceId = candidateLesson.CostCardInstanceId;
            room.Tutorial.LegionInstanceId = candidateLesson.LegionInstanceId;
            room.Tutorial.AttackerInstanceId = candidateLesson.AttackerInstanceId;
            room.Tutorial.FinaleAttackerInstanceIds = [.. candidateLesson.FinaleAttackerInstanceIds];
            room.Tutorial.InitialMoraleInstanceIds = [.. candidateLesson.InitialMoraleInstanceIds];
            room.Tutorial.CounterPaymentInstanceIds = [.. candidateLesson.CounterPaymentInstanceIds];
            room.Tutorial.ResponseInitialHp = candidateLesson.ResponseInitialHp;
            room.Tutorial.LegionRevealed = candidateLesson.LegionRevealed;
            room.Tutorial.PracticeTroops = candidateLesson.PracticeTroops;
            return candidate;
        }
        throw new InvalidOperationException($"教学有限种子范围内没有少手牌合法场面；最后失败：{lastFailure}");
    }

    private static void BootstrapTutorial(L12GameEngine game, TutorialContext lesson)
    {
        var initiative = game.State.PendingPrompts.Single();
        if (lesson.LessonId == "preparation")
        {
            if (initiative.PlayerIndex != 0 || initiative.Continuation != "setup-initiative")
                throw new InvalidOperationException("准备教学必须由学员作出真实先后手选择");
            return;
        }
        BootstrapCommand(game, initiative.PlayerIndex, new("resolvePrompt", PromptId: initiative.PromptId,
            CardInstanceIds: [initiative.PlayerIndex == 0 ? "first" : "second"]));
        if (lesson.LessonId == "finale")
        {
            BootstrapCommand(game, 0, new("mulligan", CardInstanceIds: game.State.Players[0].Hand
                .Where(card => card.CardId != "ST01-05").Select(card => card.InstanceId).ToList()));
            var keptBoar = game.State.Players[1].Hand.FirstOrDefault(card => card.CardId == "ST04-09");
            BootstrapCommand(game, 1, new("mulligan", CardInstanceIds: game.State.Players[1].Hand
                .Where(card => card != keptBoar).Select(card => card.InstanceId).ToList()));
            BootstrapFinaleTutorial(game, lesson);
            return;
        }
        if (lesson.LessonId == "response")
        {
            var student = game.State.Players[0];
            var opponent = game.State.Players[1];
            var keptShanhe = student.Hand.FirstOrDefault(card => card.CardId == "S01-0117");
            var keptHiddenPass = student.Hand.FirstOrDefault(card => card.CardId == "ST01-10");
            var keptBoar = opponent.Hand.FirstOrDefault(card => card.CardId == "ST04-09");
            BootstrapCommand(game, 0, new("mulligan", CardInstanceIds: student.Hand
                .Where(card => card != keptShanhe && card != keptHiddenPass)
                .Select(card => card.InstanceId).ToList()));
            BootstrapCommand(game, 1, new("mulligan", CardInstanceIds: opponent.Hand
                .Where(card => card != keptBoar).Select(card => card.InstanceId).ToList()));
            BootstrapResponseTutorial(game, lesson);
            return;
        }
        BootstrapCommand(game, 0, new("mulligan", CardInstanceIds: game.State.Players[0].Hand
            .Where(card => card.CardId != "ST01-05").Select(card => card.InstanceId).ToList()));
        BootstrapCommand(game, 1, new("mulligan", CardInstanceIds: game.State.Players[1].Hand
            .Where(card => card.CardId != "ST04-09").Select(card => card.InstanceId).ToList()));
        var combat = lesson.LessonId == "combat";
        // A bounded named-seed search surrounds this legal command prefix. It never mutates a
        // draw order, hidden zone, resource, turn or battlefield through a teaching side door.
        BootstrapLowHandTutorial(game, combat ? 2 : 1);
        lesson.PracticeInstanceId = game.State.Players[0].Hand.First(card => card.CardId == "ST01-05").InstanceId;
        if (lesson.LessonId == "deploy") return;
        BootstrapPlay(game, 0, lesson.PracticeInstanceId, combat ? 0 : 1, combat ? 0 : 1);
        if (combat)
        {
            BootstrapCommand(game, 0, new("endTurn"));
            BootstrapCommand(game, 1, new("endTurn"));
            if (game.State.ActivePlayer != 0 || game.State.Players[0].Hand.All(card => card.CardId != "ST01-05")
                || game.State.Players[0].Morale.Count(card => !card.Tapped) < 3)
                throw new InvalidOperationException("教学第二张军团合法前缀不兼容");
            var defender = game.State.Players[0].Hand.First(card => card.CardId == "ST01-05");
            BootstrapPlay(game, 0, defender.InstanceId, 0, 2);
        }
        BootstrapCommand(game, 0, new("endTurn"));
        BootstrapCommand(game, 1, new("endTurn"));
    }

    private static void BootstrapResponseTutorial(L12GameEngine game, TutorialContext lesson)
    {
        var student = game.State.Players[0];
        var opponent = game.State.Players[1];
        var shanhe = TutorialSingleCard(student.Hand, "S01-0117", "开局山河社稷图");
        var hiddenPass = TutorialSingleCard(student.Hand, "ST01-10", "开局暗度陈仓");
        var openingBlockers = student.Hand.Where(card => card != shanhe && card != hiddenPass).ToArray();
        if (openingBlockers.Length != 4 || openingBlockers.Any(card => card.CardType != "legion"))
            throw new InvalidOperationException("响应教学开局阻挡军团不兼容");
        var boar = TutorialSingleCard(opponent.Hand, "ST04-09", "开局野猪王");

        BootstrapCommand(game, 0, new("endTurn"));
        BootstrapPlay(game, 1, boar.InstanceId, 0, 0);
        BootstrapResponseAttack(game, boar);
        BootstrapResponseDefense(game, openingBlockers.Select(card => card.InstanceId).ToList());
        if (student.Hand.Count != 2 || !student.Hand.Contains(shanhe) || !student.Hand.Contains(hiddenPass))
            throw new InvalidOperationException("响应教学第一次真实阻挡没有留下两张配方牌");
        BootstrapCommand(game, 1, new("endTurn"));

        var firstDraw = TutorialSingleOtherHandCard(student, shanhe, hiddenPass, "山河登场回合抽牌");
        if (firstDraw.CardType != "legion")
            throw new InvalidOperationException("响应教学第二次真实阻挡没有合法军团");
        BootstrapPlay(game, 0, shanhe.InstanceId, null, null);
        if (student.Relic != shanhe || shanhe.Tapped || student.Hand.Count != 2)
            throw new InvalidOperationException("响应教学山河社稷图没有合法活跃登场");
        BootstrapCommand(game, 0, new("endTurn"));

        BootstrapResponseAttack(game, boar);
        BootstrapResponseDefense(game, [firstDraw.InstanceId]);
        if (student.Hand.Count != 1 || student.Hand.Single() != hiddenPass)
            throw new InvalidOperationException("响应教学第二次真实阻挡没有只留下暗度陈仓");
        BootstrapCommand(game, 1, new("endTurn"));

        if (game.State.ActivePlayer != 0 || game.State.Phase != L12Phase.Main || shanhe.Tapped)
            throw new InvalidOperationException("响应教学没有回到学员的山河主动效果回合");
        var searchPayment = TutorialSingleOtherHandCard(student, hiddenPass, null, "山河搜索费用");
        if (student.Hand.Count != 2 || TutorialFieldLegions(student).Count != 0
            || game.State.PendingDefense is not null || game.State.PendingPrompts.Count != 0
            || game.State.PendingActivations.Count != 0 || game.State.EffectStack.Count != 0
            || game.State.DeferredEffectStack.Count != 0)
            throw new InvalidOperationException("响应教学低手牌检查点不兼容");

        lesson.ArtifactInstanceId = shanhe.InstanceId;
        lesson.CounterInstanceId = hiddenPass.InstanceId;
        lesson.CostCardInstanceId = searchPayment.InstanceId;
        lesson.AttackerInstanceId = boar.InstanceId;
        lesson.InitialMoraleInstanceIds = student.Morale.Select(card => card.InstanceId).ToList();
        lesson.ResponseInitialHp = student.Hp;
    }

    private static void BootstrapLowHandTutorial(L12GameEngine game, int reservedPandas)
    {
        for (var budget = 0; budget < 48; budget++)
        {
            if (game.State.Phase != L12Phase.Main || game.State.PendingDefense is not null
                || game.State.PendingPrompts.Count != 0 || game.State.PendingActivations.Count != 0
                || game.State.EffectStack.Count != 0 || game.State.DeferredEffectStack.Count != 0)
                throw new InvalidOperationException("第一章低手牌合法前缀没有停在普通主阶段边界");
            var student = game.State.Players[0];
            var opponent = game.State.Players[1];
            var boar = TutorialFieldLegions(opponent).FirstOrDefault(card => card.CardId == "ST04-09");
            if (game.State.ActivePlayer == 0)
            {
                if (student.Hand.Count <= 3
                    && student.Hand.Count(card => card.CardId == "ST01-05") >= reservedPandas
                    && student.Morale.Count(card => !card.Tapped) >= 3 && boar is not null)
                    return;
            }
            else
            {
                if (boar is null
                    && opponent.Hand.FirstOrDefault(card => card.CardId == "ST04-09") is { } entrant
                    && opponent.Morale.Count(card => !card.Tapped) >= entrant.CurrentCost)
                {
                    var slot = opponent.Field[0][1] is null ? 1
                        : Array.FindIndex(opponent.Field[0], card => card is null);
                    if (slot < 0) throw new InvalidOperationException("第一章野猪王没有合法前排空位");
                    BootstrapPlay(game, 1, entrant.InstanceId, 0, slot);
                    boar = entrant;
                }
                if (boar is { Tapped: false })
                {
                    var reserved = student.Hand.Where(card => card.CardId == "ST01-05")
                        .Take(reservedPandas).Select(card => card.InstanceId)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var blockers = student.Hand.Where(card => card.CardType == "legion"
                            && !reserved.Contains(card.InstanceId))
                        .Select(card => card.InstanceId).ToList();
                    BootstrapFinaleAttack(game, 1, boar, blockers);
                }
            }
            if (game.State.Phase == L12Phase.GameOver)
                throw new InvalidOperationException("第一章低手牌合法前缀在检查点前结束对局");
            BootstrapCommand(game, game.State.ActivePlayer, new("endTurn"));
        }
        throw new InvalidOperationException("第一章低手牌合法前缀超出四十八条回合命令预算");
    }

    private static void BootstrapFinaleTutorial(L12GameEngine game, TutorialContext lesson)
    {
        for (var budget = 0; budget < 80; budget++)
        {
            if (game.State.Phase != L12Phase.Main || game.State.PendingDefense is not null
                || game.State.PendingPrompts.Count != 0 || game.State.PendingActivations.Count != 0
                || game.State.EffectStack.Count != 0 || game.State.DeferredEffectStack.Count != 0)
                throw new InvalidOperationException($"终章合法前缀没有停在普通主阶段边界："
                    + $"phase={game.State.Phase},active={game.State.ActivePlayer},"
                    + $"prompts={game.State.PendingPrompts.Count},activations={game.State.PendingActivations.Count},"
                    + $"effects={game.State.EffectStack.Count},deferred={game.State.DeferredEffectStack.Count},"
                    + $"defense={game.State.PendingDefense is not null},round={game.State.Round},"
                    + $"studentHand={game.State.Players[0].Hand.Count},studentLibrary={game.State.Players[0].Library.Count},"
                    + $"pandas={TutorialFieldLegions(game.State.Players[0]).Count(card => card.CardId == "ST01-05")},"
                    + $"opponentHp={game.State.Players[1].Hp},opponentLibrary={game.State.Players[1].Library.Count}");

            var student = game.State.Players[0];
            var opponent = game.State.Players[1];
            var attackers = TutorialFieldLegions(student)
                .Where(card => card.CardId == "ST01-05").ToList();
            if (game.State.ActivePlayer == 0)
            {
                if (attackers.Count < 2
                    && student.Hand.FirstOrDefault(card => card.CardId == "ST01-05") is { } entrant
                    && student.Morale.Count(card => !card.Tapped) >= entrant.CurrentCost)
                {
                    var slot = Array.FindIndex(student.Field[0], card => card is null);
                    if (slot < 0) throw new InvalidOperationException("终章食铁兽没有合法前排空位");
                    BootstrapPlay(game, 0, entrant.InstanceId, 0, slot);
                    attackers = TutorialFieldLegions(student)
                        .Where(card => card.CardId == "ST01-05").ToList();
                }

                if (opponent.Hp == 2 && student.Hand.Count <= 3 && attackers.Count == 2
                    && attackers.All(card => !card.Tapped && !card.CannotAttack
                        && card.SummonRound < game.State.Round))
                {
                    lesson.FinaleAttackerInstanceIds = attackers.Select(card => card.InstanceId).ToList();
                    return;
                }

                foreach (var attacker in attackers.Where(card => !card.Tapped && !card.CannotAttack
                             && card.SummonRound < game.State.Round).ToArray())
                {
                    if (opponent.Hp <= 2) break;
                    BootstrapFinaleAttack(game, 0, attacker, []);
                }
                if (opponent.Hp < 2 || game.State.Phase == L12Phase.GameOver)
                    throw new InvalidOperationException("终章合法前缀越过了两点生命检查点");
            }
            else
            {
                var boar = TutorialFieldLegions(opponent)
                    .FirstOrDefault(card => card.CardId == "ST04-09");
                if (boar is null
                    && opponent.Hand.FirstOrDefault(card => card.CardId == "ST04-09") is { } entrant
                    && opponent.Morale.Count(card => !card.Tapped) >= entrant.CurrentCost)
                {
                    var slot = Array.FindIndex(opponent.Field[0], card => card is null);
                    if (slot < 0) throw new InvalidOperationException("终章野猪王没有合法前排空位");
                    BootstrapPlay(game, 1, entrant.InstanceId, 0, slot);
                    boar = entrant;
                }
                if (boar is { Tapped: false })
                {
                    var pandasNeeded = Math.Max(0, 2 - TutorialFieldLegions(student)
                        .Count(card => card.CardId == "ST01-05"));
                    var reserved = student.Hand.Where(card => card.CardId == "ST01-05")
                        .Take(pandasNeeded).Select(card => card.InstanceId)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var blockers = student.Hand.Where(card => card.CardType == "legion"
                            && !reserved.Contains(card.InstanceId))
                        .Select(card => card.InstanceId).ToList();
                    BootstrapFinaleAttack(game, 1, boar, blockers);
                }
            }
            BootstrapCommand(game, game.State.ActivePlayer, new("endTurn"));
        }
        throw new InvalidOperationException("终章合法前缀超出八十条回合命令预算");
    }

    private static void BootstrapFinaleAttack(L12GameEngine game, int actor,
        L12CardInstance attacker, List<string> blockers)
    {
        BootstrapCommand(game, actor, new("attack", attacker.InstanceId, Target: new("master")));
        var defenseDeclared = false;
        for (var budget = 0; budget < 24; budget++)
        {
            if (game.State.PendingPrompts.FirstOrDefault() is { } prompt)
            {
                List<string> choices;
                if (prompt.ValidChoices.Contains("mode:none", StringComparer.OrdinalIgnoreCase))
                    choices = ["mode:none"];
                else if (prompt.Kind == "response"
                         && prompt.ValidChoices.Contains("pass", StringComparer.OrdinalIgnoreCase))
                    choices = ["pass"];
                else if (prompt.Continuation == "trigger-batch-order")
                    choices = [.. prompt.ValidChoices];
                else
                    throw new InvalidOperationException(
                        $"终章进攻遇到未声明询问：{prompt.Kind}/{prompt.Continuation}");
                BootstrapCommand(game, prompt.PlayerIndex,
                    new("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: choices));
                continue;
            }
            if (!defenseDeclared && game.State.PendingDefense is
                { Stage: L12CombatStage.DefenseChoice } defense && defense.AttackerPlayer == actor)
            {
                BootstrapCommand(game, 1 - actor, new("resolveDefense", CardInstanceIds: blockers));
                defenseDeclared = true;
                continue;
            }
            if (game.State.PendingDefense is null && game.State.SuspendedCombatContexts.Count == 0
                && game.State.PendingActivations.Count == 0 && game.State.EffectStack.Count == 0
                && game.State.DeferredEffectStack.Count == 0
                && game.State.PendingTriggerBatches.Count == 0
                && game.State.PendingTriggerStackCandidates.Count == 0
                && game.State.ResponseWindow is null && !game.State.IsResolvingStack)
                return;
        }
        throw new InvalidOperationException("终章进攻结算超出命令预算");
    }

    private static L12CardInstance TutorialSingleCard(IEnumerable<L12CardInstance> cards,
        string cardId, string stage)
    {
        var matches = cards.Where(card => card.CardId == cardId).ToArray();
        return matches.Length == 1 ? matches[0]
            : throw new InvalidOperationException($"{stage}要求恰好一张{cardId}，实际{matches.Length}张");
    }

    private static L12CardInstance TutorialSingleOtherHandCard(L12PlayerState player,
        L12CardInstance first, L12CardInstance? second, string stage)
    {
        var others = player.Hand.Where(card => card != first && card != second).ToArray();
        return others.Length == 1 ? others[0]
            : throw new InvalidOperationException($"{stage}要求恰好一张其他手牌，实际{others.Length}张");
    }

    private static List<L12CardInstance> TutorialFieldLegions(L12PlayerState player)
        => player.Field.SelectMany(row => row).Where(card => card?.CardType == "legion")
            .Cast<L12CardInstance>().ToList();

    private static void BootstrapResponseAttack(L12GameEngine game, L12CardInstance boar)
    {
        BootstrapCommand(game, 1, new("attack", boar.InstanceId, Target: new("master")));
        for (var budget = 0; game.State.PendingPrompts.FirstOrDefault() is { } prompt; budget++)
        {
            if (budget >= 12) throw new InvalidOperationException("响应教学进攻询问超出预算");
            if (prompt.ValidChoices.Contains("mode:none"))
                BootstrapCommand(game, prompt.PlayerIndex,
                    new("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: ["mode:none"]));
            else if (prompt.Kind == "response" && prompt.ValidChoices.Contains("pass"))
                BootstrapCommand(game, prompt.PlayerIndex,
                    new("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: ["pass"]));
            else
                throw new InvalidOperationException("响应教学进攻遇到未声明询问");
        }
        if (game.State.PendingDefense is not { Stage: L12CombatStage.DefenseChoice, AttackerPlayer: 1 })
            throw new InvalidOperationException("响应教学野猪王没有进入真实主宰防御选择");
    }

    private static void BootstrapResponseDefense(L12GameEngine game, List<string> blockers)
    {
        BootstrapCommand(game, 0, new("resolveDefense", CardInstanceIds: blockers));
        if (blockers.Count == 0) return;
        for (var budget = 0; game.State.PendingPrompts.FirstOrDefault() is { } prompt; budget++)
        {
            if (budget >= 12) throw new InvalidOperationException("响应教学阻挡结算超出预算");
            if (prompt.Kind == "response" && prompt.ValidChoices.Contains("pass"))
                BootstrapCommand(game, prompt.PlayerIndex,
                    new("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: ["pass"]));
            else if (prompt.ValidChoices.Contains("mode:none"))
                BootstrapCommand(game, prompt.PlayerIndex,
                    new("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: ["mode:none"]));
            else
                throw new InvalidOperationException("响应教学阻挡遇到未声明询问");
        }
        if (game.State.PendingDefense is not null)
            throw new InvalidOperationException("响应教学真实阻挡没有结算完成");
    }

    private static void BootstrapPlay(L12GameEngine game, int actor, string instanceId, int? row, int? slot)
    {
        var cost = game.State.Players[actor].Hand.Single(card => card.InstanceId == instanceId).CurrentCost;
        BootstrapCommand(game, actor, new("playCard", CardInstanceId: instanceId, Row: row, Slot: slot,
            CardInstanceIds: game.State.Players[actor].Morale.Where(card => !card.Tapped)
                .Take(cost).Select(card => card.InstanceId).ToList()));
        BootstrapPassResponses(game);
    }

    private static void BootstrapPassResponses(L12GameEngine game)
    {
        for (var budget = 0; game.State.PendingPrompts.Count != 0; budget++)
        {
            if (budget >= 8) throw new InvalidOperationException("教学合法初始化响应超出预算");
            var prompt = game.State.PendingPrompts[0];
            if (prompt.Continuation != "stack-response" || !prompt.ValidChoices.Contains("pass"))
                throw new InvalidOperationException("教学合法初始化遇到未声明询问");
            BootstrapCommand(game, prompt.PlayerIndex,
                new("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: ["pass"]));
        }
    }

    private static void BootstrapCommand(L12GameEngine game, int actor, L12Command command)
    {
        if (!game.Handle(actor, command).Accepted)
            throw new InvalidOperationException("教学合法初始化命令被规则拒绝");
    }

    private static L12PendingActivation? TutorialPromptActivation(L12GameState state, L12Prompt prompt)
        => prompt.ActivationId is null ? null : state.PendingActivations
            .SingleOrDefault(candidate => candidate.ActivationId == prompt.ActivationId);

    private static string? TutorialResponsePromptStep(TutorialContext lesson,
        L12GameState state, L12Prompt prompt)
    {
        if (prompt.PlayerIndex != 0) return null;
        if (prompt.Continuation.StartsWith("stack-response", StringComparison.Ordinal)
            && prompt.ValidChoices.Count == 1
            && prompt.ValidChoices[0].Equals("pass", StringComparison.OrdinalIgnoreCase))
            return "response-pass";
        if (prompt.Kind == "search" && prompt.Continuation == "card-effect"
            && prompt.Data.GetValueOrDefault("action") == "shanhe-search-pick")
            return "response-search-pick";
        if (prompt.Kind == "order" && prompt.Continuation == "card-effect"
            && prompt.Data.GetValueOrDefault("placementMode") == "all-top-bottom")
            return "response-search-order";

        var activation = TutorialPromptActivation(state, prompt);
        if (activation is null) return null;
        var step = activation.SelectionSteps.ElementAtOrDefault(activation.CurrentStep);
        if (activation.SourceInstanceId == lesson.ArtifactInstanceId
            && activation.SourceCardId == "S01-0117" && activation.Ability == "artifactSearch"
            && step is { IsCostSelection: true })
            return "response-search-cost";
        if (activation.SourceInstanceId != lesson.CounterInstanceId
            || activation.SourceCardId != "ST01-10" || activation.Ability != "public-trigger-declaration")
            return null;
        return step?.DeclarationKey switch
        {
            "mode" => "response-choose",
            "returnCost" => "response-return",
            "entryCard" => "response-summon",
            "entrySlot" => "response-position",
            _ => null,
        };
    }

    private static string? TutorialPreparationPromptStep(L12Prompt prompt)
        => prompt.Continuation switch
        {
            "setup-initiative" => "preparation-initiative",
            "setup-ban" => "preparation-ban",
            "setup-public-confirm" => "preparation-reveal",
            "setup-first-pick" or "setup-second-pick" => "preparation-select",
            _ => null,
        };

    private static L12Command TutorialPreparationOpponentCommand(L12Prompt prompt)
    {
        if (TutorialPreparationPromptStep(prompt) is null)
            throw new InvalidOperationException("准备教学脚本遇到未声明询问");
        var choices = prompt.Continuation == "setup-public-confirm"
            ? [] : prompt.ValidChoices.Take(prompt.MinChoose).ToList();
        if (choices.Count != prompt.MinChoose)
            throw new InvalidOperationException("准备教学脚本没有足够合法选项");
        return new("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: choices);
    }

    private static string? TutorialCommandError(Room room, L12Command command)
    {
        if (room.Tutorial is not { } lesson) return null;
        if (lesson.Frozen) return "本关记录未能完成，请退出后重开本关";
        if (command.Type == "surrender") return null;
        if (lesson.LessonId == "response")
        {
            UpdateResponseTutorialStep(room);
            var state = room.Game!.State;
            var prompt = command.Type == "resolvePrompt" && command.PromptId is not null
                ? state.PendingPrompts.SingleOrDefault(candidate => candidate.PlayerIndex == 0
                    && candidate.PromptId == command.PromptId)
                : null;
            var promptStep = prompt is null ? null : TutorialResponsePromptStep(lesson, state, prompt);
            var responseAllowed = lesson.StepId switch
            {
                "response-artifact" => command.Type == "activateAbility"
                    && command.CardInstanceId == lesson.ArtifactInstanceId && command.Ability == "artifactSearch",
                "response-search-cost" or "response-search-pick" or "response-search-order"
                    or "response-pass" or "response-choose" or "response-return"
                    or "response-summon" or "response-position" => command.Type == "resolvePrompt"
                        && promptStep == lesson.StepId,
                "response-cover" => command.Type == "playCard"
                    && command.CardInstanceId == lesson.CounterInstanceId && command.Row == 1,
                "response-end-turn" => command.Type == "endTurn",
                "response-defend" => command.Type == "resolveDefense",
                _ => false,
            };
            return responseAllowed ? null : "请按本关当前目标操作；需要重新开始时请退出本关";
        }
        if (lesson.LessonId == "preparation")
        {
            UpdatePreparationTutorialStep(room);
            var state = room.Game!.State;
            var prompt = command.Type == "resolvePrompt" && command.PromptId is not null
                ? state.PendingPrompts.SingleOrDefault(candidate => candidate.PlayerIndex == 0
                    && candidate.PromptId == command.PromptId)
                : null;
            var preparationAllowed = prompt is not null
                ? TutorialPreparationPromptStep(prompt) == lesson.StepId
                : command.Type == "mulligan" && lesson.StepId == "preparation-mulligan"
                  && state.Phase == L12Phase.Mulligan && !state.Players[0].MulliganDone;
            return preparationAllowed ? null : "请按本关当前目标操作；需要重新开始时请退出本关";
        }
        if (lesson.LessonId == "finale")
        {
            UpdateFinaleTutorialStep(room);
            var finaleAllowed = lesson.StepId == "finale-attack" && command.Type == "attack"
                && command.CardInstanceId is not null
                && lesson.FinaleAttackerInstanceIds.Contains(command.CardInstanceId,
                    StringComparer.OrdinalIgnoreCase)
                && command.Target?.Type == "master";
            return finaleAllowed ? null : "请按本关当前目标操作；需要重新开始时请退出本关";
        }
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
            _ => false,
        };
        return allowed ? null : "请按本关当前目标操作；需要重新开始时请退出本关";
    }

    private static bool IsTutorialKagutsuchiDecline(L12GameState state,
        TutorialContext lesson, L12Prompt prompt)
    {
        if (prompt.PlayerIndex != 1 || prompt.Kind != "option"
            || !prompt.ValidChoices.Contains("mode:none", StringComparer.OrdinalIgnoreCase)) return false;
        var activation = TutorialPromptActivation(state, prompt);
        if (activation is null || activation.Controller != 1 || activation.SourceInstanceId != "master-1"
            || activation.SourceCardId != state.Players[1].MasterId
            || activation.Ability != "public-trigger-declaration" || activation.TriggerCandidateId is null)
            return false;
        var candidate = state.PendingTriggerStackCandidates.SingleOrDefault(item =>
            item.CandidateId == activation.TriggerCandidateId);
        return candidate is { Controller: 1, SourceInstanceId: "master-1", Trigger: "legion-attack-timing" }
               && (lesson.LessonId != "response"
                   || candidate.Data.GetValueOrDefault("target") == lesson.AttackerInstanceId);
    }

    private static bool IsResponseAftermathDecline(L12GameState state,
        TutorialContext lesson, L12Prompt prompt)
    {
        if (lesson.LessonId != "response" || lesson.StepId != "response-position"
            || prompt.PlayerIndex != 0 || prompt.Kind != "option"
            || !prompt.ValidChoices.Contains("mode:none", StringComparer.OrdinalIgnoreCase)) return false;
        var activation = TutorialPromptActivation(state, prompt);
        if (activation is null || activation.Controller != 0 || activation.SourceInstanceId != "master-0"
            || activation.SourceCardId != state.Players[0].MasterId
            || activation.Ability != "public-trigger-declaration" || activation.TriggerCandidateId is null)
            return false;
        var candidate = state.PendingTriggerStackCandidates.SingleOrDefault(item =>
            item.CandidateId == activation.TriggerCandidateId);
        return candidate is { Controller: 0, SourceInstanceId: "master-0", Trigger: "morale-return" };
    }

    // Invoked only after the student's Append has committed, under the same room gate.
    // Read/projection/recovery paths never run the opponent or advance a step.
    private async Task<bool> AdvanceTutorialAsync(Room room)
    {
        if (room.Tutorial is not { } lesson) return true;
        if (room.Game!.State.Phase == L12Phase.GameOver)
        {
            UpdateTutorialStep(room);
            return true;
        }
        try
        {
            for (var budget = 0; budget < 8; budget++)
            {
                UpdateTutorialStep(room);
                var state = room.Game.State;
                L12Command? scripted = null;
                var scriptedActor = 1;
                var prompt = state.PendingPrompts.FirstOrDefault(p => p.PlayerIndex == 1);
                if (prompt is not null)
                {
                    if (lesson.LessonId == "preparation")
                        scripted = TutorialPreparationOpponentCommand(prompt);
                    else if (prompt.Continuation == "stack-response" && prompt.ValidChoices.Contains("pass"))
                        scripted = new("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: ["pass"]);
                    else if (lesson.LessonId is "combat" or "response" or "finale"
                             && IsTutorialKagutsuchiDecline(state, lesson, prompt))
                        scripted = new("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: ["mode:none"]);
                    else
                        throw new InvalidOperationException("教学脚本遇到未声明询问");
                }
                else if (lesson.LessonId == "preparation" && state.Phase == L12Phase.Mulligan
                         && !state.Players[1].MulliganDone)
                    scripted = new("mulligan", CardInstanceIds: []);
                else if (state.PendingPrompts.FirstOrDefault(p => p.PlayerIndex == 0) is { } studentPrompt
                         && IsResponseAftermathDecline(state, lesson, studentPrompt))
                {
                    scriptedActor = 0;
                    scripted = new("resolvePrompt", PromptId: studentPrompt.PromptId,
                        CardInstanceIds: ["mode:none"]);
                }
                else if (state.PendingDefense is { AttackerPlayer: 0, Stage: L12CombatStage.DefenseChoice }
                         && state.PendingPrompts.Count == 0 && state.EffectStack.Count == 0)
                    scripted = new("resolveDefense", CardInstanceIds: []);
                else if (lesson.StepId == "end-turn" && state.ActivePlayer == 1
                         && state.Phase == L12Phase.Main && state.PendingDefense is null)
                {
                    var attacker = state.Players[1].Field.SelectMany(row => row)
                        .Single(card => card?.CardId == "ST04-09");
                    scripted = new("attack", CardInstanceId: attacker!.InstanceId, Target: new("master"));
                }
                else if (lesson.StepId == "response-end-turn" && state.ActivePlayer == 1
                         && state.Phase == L12Phase.Main && state.PendingDefense is null
                         && state.PendingPrompts.Count == 0 && state.EffectStack.Count == 0
                         && state.DeferredEffectStack.Count == 0
                         && state.Players[1].Field.SelectMany(row => row)
                             .Any(card => card?.InstanceId == lesson.AttackerInstanceId))
                    scripted = new("attack", CardInstanceId: lesson.AttackerInstanceId,
                        Target: new("master"));
                if (scripted is null)
                {
                    UpdateTutorialStep(room);
                    return true;
                }
                var result = room.Game.Handle(scriptedActor, scripted);
                if (!result.Accepted) throw new InvalidOperationException("教学对手合法命令被拒绝");
                room.CommandSequence++;
                await _recorder.AppendAsync(room.Game, room.CommandSequence, scriptedActor,
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
        if (lesson.LessonId == "preparation")
        {
            UpdatePreparationTutorialStep(room);
            return;
        }
        if (lesson.LessonId == "finale")
        {
            UpdateFinaleTutorialStep(room);
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

    private static void UpdatePreparationTutorialStep(Room room)
    {
        var lesson = room.Tutorial!;
        if (lesson.StepId is "completed" or "preparation-retry") return;
        var state = room.Game!.State;
        if (state.Phase == L12Phase.GameOver)
        {
            lesson.StepId = "preparation-retry";
            return;
        }
        if (state.PendingPrompts.FirstOrDefault(prompt => prompt.PlayerIndex == 0) is { } prompt)
        {
            lesson.StepId = TutorialPreparationPromptStep(prompt) ?? "preparation-retry";
            return;
        }
        if (state.Phase == L12Phase.Mulligan && !state.Players[0].MulliganDone)
        {
            lesson.StepId = "preparation-mulligan";
            return;
        }
        if (state.Phase == L12Phase.Main && state.Players.All(player => player.MulliganDone)
            && state.PendingPrompts.Count == 0 && state.EffectStack.Count == 0
            && state.DeferredEffectStack.Count == 0)
            lesson.StepId = "completed";
    }

    private static void UpdateFinaleTutorialStep(Room room)
    {
        var lesson = room.Tutorial!;
        if (lesson.StepId is "completed" or "finale-retry") return;
        var state = room.Game!.State;
        if (state.Phase == L12Phase.GameOver)
        {
            lesson.StepId = state.Winner == 0 ? "completed" : "finale-retry";
            return;
        }
        lesson.StepId = "finale-attack";
    }

    private static void UpdateResponseTutorialStep(Room room)
    {
        var lesson = room.Tutorial!;
        var state = room.Game!.State;
        if (lesson.StepId is "completed" or "response-retry") return;
        if (state.Phase == L12Phase.GameOver)
        {
            lesson.StepId = "response-retry";
            return;
        }
        var own = state.Players[0];
        var prompt = state.PendingPrompts.FirstOrDefault(p => p.PlayerIndex == 0);
        if (prompt is not null)
        {
            var next = TutorialResponsePromptStep(lesson, state, prompt);
            if (next == "response-search-pick")
            {
                var matches = prompt.ValidChoices.Select(id => own.Library
                        .FirstOrDefault(card => card.InstanceId == id && card.CardId == "ST01-08"))
                    .OfType<L12CardInstance>().ToArray();
                if (matches.Length != 1)
                {
                    lesson.StepId = "response-retry";
                    return;
                }
                lesson.LegionInstanceId = matches[0].InstanceId;
                lesson.LegionRevealed = true;
            }
            if (next is "response-choose" or "response-return" or "response-summon" or "response-position")
                lesson.HiddenDeclarationStarted = true;
            if (next is null && IsResponseAftermathDecline(state, lesson, prompt)) return;
            lesson.StepId = next ?? "response-retry";
            return;
        }
        if (state.PendingPrompts.Count != 0 || state.PendingActivations.Count != 0
            || state.EffectStack.Count != 0 || state.DeferredEffectStack.Count != 0
            || state.PendingTriggerBatches.Count != 0 || state.PendingTriggerStackCandidates.Count != 0
            || state.ResponseWindow is not null || state.IsResolvingStack) return;
        if (state.PendingDefense is { AttackerPlayer: 1, Stage: L12CombatStage.DefenseChoice })
        {
            lesson.StepId = "response-defend";
            return;
        }
        if (state.PendingDefense is not null || state.SuspendedCombatContexts.Count != 0) return;
        if (ResponseTutorialCompleted(state, lesson))
        {
            lesson.StepId = "completed";
            return;
        }

        var artifact = own.Relic?.InstanceId == lesson.ArtifactInstanceId ? own.Relic : null;
        var counter = own.Field.SelectMany(row => row)
            .FirstOrDefault(card => card?.InstanceId == lesson.CounterInstanceId);
        var searchPaid = artifact is { Tapped: true }
            && own.Graveyard.Any(card => card.InstanceId == lesson.CostCardInstanceId)
            && lesson.LegionRevealed;
        if (searchPaid && counter is null && state.ActivePlayer == 0
            && own.Hand.Count == 2
            && own.Hand.Any(card => card.InstanceId == lesson.CounterInstanceId)
            && own.Hand.Any(card => card.InstanceId == lesson.LegionInstanceId))
        {
            lesson.StepId = "response-cover";
            return;
        }
        if (searchPaid && counter is { Hidden: true } && state.ActivePlayer == 0
            && own.Hand.Count == 1 && own.Hand[0].InstanceId == lesson.LegionInstanceId)
        {
            if (lesson.CounterPaymentInstanceIds.Count == 0)
                lesson.CounterPaymentInstanceIds = own.Morale
                    .Where(card => card.Tapped && lesson.InitialMoraleInstanceIds.Contains(card.InstanceId))
                    .Select(card => card.InstanceId).ToList();
            lesson.StepId = lesson.CounterPaymentInstanceIds.Count == counter.CurrentCost
                ? "response-end-turn" : "response-retry";
            return;
        }
        if (lesson.StepId == "response-end-turn" && state.ActivePlayer == 1) return;
        if (lesson.StepId is "response-defend" or "response-choose" or "response-return"
            or "response-summon" or "response-position" or "response-pass"
            || artifact is { Tapped: true } || lesson.HiddenDeclarationStarted)
            lesson.StepId = "response-retry";
    }

    private static bool ResponseTutorialCompleted(L12GameState state, TutorialContext lesson)
    {
        var own = state.Players[0];
        var fieldLegions = TutorialFieldLegions(own);
        var returnedInitialMorale = lesson.InitialMoraleInstanceIds.Count(id =>
            own.MoraleDeck.Any(card => card.InstanceId == id));
        var returnedInitialMoraleId = lesson.InitialMoraleInstanceIds.FirstOrDefault(id =>
            own.MoraleDeck.Any(card => card.InstanceId == id));
        var remainingInitialMorale = lesson.InitialMoraleInstanceIds.Count(id =>
            own.Morale.Any(card => card.InstanceId == id));
        return lesson.LegionRevealed && !string.IsNullOrWhiteSpace(lesson.LegionInstanceId)
               && own.Hp == lesson.ResponseInitialHp - 1
               && own.Relic is { Tapped: true } artifact
               && artifact.InstanceId == lesson.ArtifactInstanceId
               && own.Graveyard.Any(card => card.InstanceId == lesson.CostCardInstanceId)
               && own.Graveyard.Any(card => card.InstanceId == lesson.CounterInstanceId)
               && fieldLegions.Count == 1 && fieldLegions[0].InstanceId == lesson.LegionInstanceId
               && !fieldLegions[0].Tapped
               && lesson.CounterPaymentInstanceIds.Count > 0
               && lesson.CounterPaymentInstanceIds.All(id => id == returnedInitialMoraleId
                   || own.Morale.Any(card => card.InstanceId == id && card.Tapped))
               && returnedInitialMorale == 1
               && remainingInitialMorale == lesson.InitialMoraleInstanceIds.Count - 1
               && state.PendingDefense is null && state.SuspendedCombatContexts.Count == 0
               && state.PendingPrompts.Count == 0 && state.PendingActivations.Count == 0
               && state.EffectStack.Count == 0 && state.DeferredEffectStack.Count == 0
               && state.PendingTriggerBatches.Count == 0 && state.PendingTriggerStackCandidates.Count == 0
               && state.ResponseWindow is null && !state.IsResolvingStack;
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
