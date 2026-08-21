using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private readonly L12Catalog _catalog;
    private readonly Random _random;

    public L12GameState State { get; }

    public L12GameEngine(
        L12Catalog catalog,
        string matchId,
        string roomCode,
        int seed,
        string[] playerNames,
        int[] deckIndexes,
        bool skipPreparation = false,
        string disasterMode = "all")
        : this(catalog, matchId, roomCode, seed, playerNames,
            deckIndexes.Select(catalog.DeckAt).ToArray(), skipPreparation, disasterMode)
    {
    }

    public L12GameEngine(
        L12Catalog catalog,
        string matchId,
        string roomCode,
        int seed,
        string[] playerNames,
        L12PresetDeckDefinition[] decks,
        bool skipPreparation = false,
        string disasterMode = "all")
    {
        if (playerNames.Length != 2 || decks.Length != 2)
            throw new ArgumentException("十二军团对战需要两名玩家和两副牌库");
        _catalog = catalog;
        _random = new Random(seed);
        State = new L12GameState
        {
            MatchId = matchId,
            RoomCode = roomCode,
            Seed = seed,
            DisasterMode = NormalizeDisasterMode(disasterMode),
            ActivePlayer = 0,
            FirstPlayer = 0,
            Players =
            [
                BuildPlayer(0, playerNames[0], decks[0]),
                BuildPlayer(1, playerNames[1], decks[1]),
            ],
        };

        RollInitiative();
        if (skipPreparation)
        {
            State.FirstPlayer = State.DiceWinner;
            State.ActivePlayer = State.FirstPlayer;
            PrepareLibrariesAndHands(applyOptionalSetupDefaults: true);
            State.Phase = L12Phase.Mulligan;
            AddEvent("match-created", null, $"对局创建，玩家 {State.FirstPlayer + 1} 先手（测试准备捷径）");
        }
        else
        {
            if (State.DisasterMode != "none") BuildDisasterPool();
            State.Phase = L12Phase.Initiative;
            AddEvent("initiative", State.DiceWinner,
                $"掷骰结果：{State.Players[0].Name} {State.InitiativeRolls[0]}，{State.Players[1].Name} {State.InitiativeRolls[1]}；{State.Players[State.DiceWinner].Name} 选择先后手");
            CreatePrompt(State.DiceWinner, "initiative", "选择先攻或后攻", ["first", "second"], 1, 1,
                "setup-initiative", isPrivate: false);
        }
    }

    public CommandResult Handle(int playerIndex, L12Command command)
    {
        if (playerIndex is < 0 or > 1) return CommandResult.Reject("无效玩家");
        if (State.Phase == L12Phase.GameOver) return CommandResult.Reject("对局已经结束");

        // Capture this before resolving the command: a disaster prompt/stack item may be
        // removed during resolution, but deaths caused by that disaster must still not
        // create death/leave triggers.
        var suppressStateDeathTriggers = State.Phase == L12Phase.Disaster
            || State.EffectStack.Any(item => item.Trigger == "disaster")
            || (command.Type == "resolvePrompt"
                && State.PendingPrompts.Any(prompt => prompt.PromptId == command.PromptId
                    && prompt.Continuation == "disaster-effect"));

        var result = command.Type switch
        {
            "resolvePrompt" => ResolvePrompt(playerIndex, command),
            "mulligan" => Mulligan(playerIndex, command.CardInstanceIds ?? []),
            "advancePhase" => CommandResult.Reject("触发天灾至主要阶段由服务器自动结算"),
            "playCard" => PlayCard(playerIndex, command),
            "attack" => Attack(playerIndex, command),
            "resolveDefense" => ResolveDefense(playerIndex, command),
            "move" => Move(playerIndex, command),
            "cavalryMove" => CavalryMove(playerIndex, command),
            "activateAbility" => ActivateAbility(playerIndex, command),
            "flipHidden" => FlipHidden(playerIndex, command.CardInstanceId),
            "endTurn" => EndTurn(playerIndex),
            "surrender" => Surrender(playerIndex),
            _ => CommandResult.Reject("未知操作"),
        };

        if (result.Accepted)
        {
            ResolveStateBasedLegionDeaths(suppressStateDeathTriggers);
            State.Revision++;
            CheckWinner();
        }
        return result;
    }

    public L12GameSnapshot SnapshotFor(int viewer) => SnapshotForInternal(viewer, spectator: false, revealAllDisasters: false);

    public L12GameSnapshot SnapshotForSpectator() => SnapshotForInternal(-1, spectator: true, revealAllDisasters: false);

    public L12GameSnapshot SnapshotForReferee() => SnapshotForInternal(-1, spectator: true, revealAllDisasters: true);

    private L12GameSnapshot SnapshotForInternal(int viewer, bool spectator, bool revealAllDisasters)
    {
        RecalculateContinuousTroops();
        var players = State.Players.Select((player, index) => !spectator && index == viewer
            ? (object)new
            {
                player.PlayerIndex, player.Name, player.DeckName, player.Faction,
                master = MasterSnapshot(player),
                factionEffect = FactionEffectSnapshot(player),
                libraryCount = player.Library.Count, libraryTop = State.ActiveDisaster?.CardId == "S02-DS01" ? player.Library.FirstOrDefault() : null,
                hand = SnapshotHand(index), player.MoraleDeck, player.Morale,
                field = SnapshotField(player, revealCounters: true), player.Relic, player.ExtraRelics, player.Resolving, player.Graveyard, player.Removed, specialZones = SpecialZonesSnapshot(player, index, viewer, revealAllDisasters),
                player.TemporaryMorale, player.NextLegionChargeMaxCost, player.NextS2PromotionGodPowerDiscount, player.MulliganDone,
            }
            : new
            {
                player.PlayerIndex, player.Name, player.DeckName, player.Faction,
                master = MasterSnapshot(player),
                factionEffect = FactionEffectSnapshot(player),
                libraryCount = player.Library.Count, libraryTop = State.ActiveDisaster?.CardId == "S02-DS01" ? player.Library.FirstOrDefault() : null,
                handCount = player.Hand.Count,
                moraleDeckCount = player.MoraleDeck.Count, player.Morale,
                field = SnapshotField(player, revealCounters: false), player.Relic, player.ExtraRelics, player.Resolving, player.Graveyard, graveyardCount = player.Graveyard.Count,
                removedCount = player.Removed.Count, specialZones = SpecialZonesSnapshot(player, index, viewer, revealAllDisasters), player.TemporaryMorale, player.NextLegionChargeMaxCost, player.NextS2PromotionGodPowerDiscount, player.MulliganDone,
            }).ToArray();

        var prompts = State.PendingPrompts
            .Where(prompt => !spectator && prompt.PlayerIndex == viewer)
            .Select(prompt => (object)new
            {
                prompt.PromptId, prompt.PlayerIndex, prompt.Kind, prompt.Text, prompt.ValidChoices,
                prompt.MinChoose, prompt.MaxChoose, prompt.Data,
            }).ToArray();
        // 对手正在处理任何选择时都给出不泄露私密候选内容的等待状态。
        var waitingPromptSource = State.PendingPrompts.FirstOrDefault(prompt => spectator || prompt.PlayerIndex != viewer);
        object? waitingPrompt = waitingPromptSource is null ? null : new
        {
            waitingPromptSource.PlayerIndex,
            playerName = State.Players[waitingPromptSource.PlayerIndex].Name,
            waitingPromptSource.Kind,
        };
        var stack = State.EffectStack.Select(item =>
        {
            var privateHandCard = item.Data.GetValueOrDefault("eventType") == "effect-hand-add"
                && !spectator && item.Controller != viewer;
            return (object)new
            {
                item.StackItemId, item.Controller,
                SourceInstanceId = privateHandCard ? string.Empty : item.SourceInstanceId,
                SourceCardId = privateHandCard ? string.Empty : item.SourceCardId,
                SourceName = privateHandCard ? "加入手牌的牌" : item.SourceName,
                item.Trigger,
                Text = privateHandCard ? $"{State.Players[item.Controller].Name}因效果将1张牌加入手牌" : item.Text,
                item.Negated, Targets = privateHandCard ? [] : item.Targets,
            };
        }).ToArray();

        var recentEvents = State.Events
            .Select(actionEvent => FilterDisasterEvent(actionEvent, viewer, revealAllDisasters))
            .ToArray();
        var lastAction = State.LastAction is null
            ? null
            : FilterDisasterEvent(State.LastAction, viewer, revealAllDisasters);

        return new L12GameSnapshot(
            State.MatchId, State.RoomCode, spectator ? 0 : viewer, State.Revision, State.ActivePlayer,
            State.FirstPlayer, State.DiceWinner, State.InitiativeRolls, State.Phase, State.Round, State.TurnSerial,
            State.DisasterMode, State.DisasterValue, State.ActiveDisaster, State.DisasterDeck.Select(CardBackSnapshot).ToArray(),
            State.BannedDisasters.Cast<object>().ToArray(), State.RemovedDisasters.Cast<object>().ToArray(),
            State.RevealedDisasters.Cast<object>().ToArray(), BuildChosenDisasterSnapshot(viewer, revealAllDisasters),
            BuildSessionDisasterSnapshot(viewer, revealAllDisasters),
            State.DisasterPreparationStep,
            waitingPrompt, prompts, stack, State.PendingDefense, State.Winner, State.WinnerReason, players, lastAction,
            recentEvents, spectator ? [] : BuildLegalAttackTargets(viewer), ComputeStateHash());
    }

    private object[] BuildChosenDisasterSnapshot(int viewer, bool revealAll)
        => State.ChosenDisasters.Select(card => DisasterVisibilitySnapshot(card, viewer, revealAll)).ToArray();

    private object[] BuildSessionDisasterSnapshot(int viewer, bool revealAll)
    {
        // 这一区域只表达“玩家目前知道哪些牌”，绝不能用亮/暗位置泄露洗混后的牌序。
        // 前三格先紧凑排列已知的非最终天灾，再补未知牌背；公开的最终天灾固定在第四格。
        var all = State.RevealedDisasters.Concat(State.ChosenDisasters)
            .Concat(State.DisasterDeck).Concat(State.RemovedDisasters)
            .Append(State.ActiveDisaster)
            .Where(card => card is not null)
            .Cast<L12CardInstance>()
            .DistinctBy(card => card.InstanceId)
            .ToArray();
        var visible = all.Where(card => card.CardId != "S01-DS10")
            .Select(card => DisasterVisibilitySnapshot(card, viewer, revealAll))
            .Where(IsVisibleDisasterSnapshot)
            .Take(3)
            .ToList();
        while (visible.Count < 3)
            visible.Add(new { instanceId = $"unknown-disaster-{visible.Count}", hidden = true });

        var final = all.FirstOrDefault(card => card.CardId == "S01-DS10")
            ?? (_catalog.Cards.ContainsKey("S01-DS10") ? CreateCard("S01-DS10", "session-final-disaster") : null);
        if (final is not null) visible.Add(final);
        return visible.ToArray();
    }

    private static bool IsVisibleDisasterSnapshot(object snapshot) => snapshot is L12CardInstance;

    private object DisasterVisibilitySnapshot(L12CardInstance card, int viewer, bool revealAll)
    {
        var alreadyRevealed = State.ActiveDisaster?.InstanceId == card.InstanceId
            || State.RemovedDisasters.Any(item => item.InstanceId == card.InstanceId)
            || State.RevealedDisasters.Any(item => item.InstanceId == card.InstanceId);
        var owner = State.ChosenDisasterOwners.GetValueOrDefault(card.InstanceId, card.OwnerIndex ?? -1);
        if (revealAll || alreadyRevealed || (viewer >= 0 && owner == viewer)) return card;
        return new { card.InstanceId, hidden = true, ownerIndex = owner };
    }

    private L12ActionEvent FilterDisasterEvent(L12ActionEvent actionEvent, int viewer, bool revealAll)
    {
        if (revealAll || actionEvent.Type != "disaster-selected" || actionEvent.Cards.Length == 0)
            return actionEvent;

        var visibleCards = actionEvent.Cards
            .Where(card => ReferenceEquals(DisasterVisibilitySnapshot(card, viewer, revealAll), card))
            .Select(card => card.Clone())
            .ToArray();
        if (visibleCards.Length == actionEvent.Cards.Length) return actionEvent;

        var playerName = actionEvent.PlayerIndex is >= 0 and <= 1
            ? State.Players[actionEvent.PlayerIndex.Value].Name
            : "玩家";
        return new L12ActionEvent(actionEvent.Sequence, actionEvent.Type, actionEvent.PlayerIndex,
            $"{playerName} 已完成天灾选择", visibleCards);
    }

    private Dictionary<string, string[]> BuildLegalAttackTargets(int viewer)
    {
        var result = new Dictionary<string, string[]>();
        if (!CanAct(viewer)) return result;
        var player = State.Players[viewer];
        var defender = State.Players[1 - viewer];
        var taunts = defender.Field[0].Where(card => card is not null && HasS1Taunt(card) && !card.Hidden)
            .Select(card => card!.InstanceId).ToHashSet();
        for (var row = 0; row < 2; row++)
        for (var slot = 0; slot < 3; slot++)
        {
            var attacker = player.Field[row][slot];
            if (attacker is null || attacker.CannotAttack || attacker.Tapped || attacker.Hidden
                || !CanAttackFromRow(attacker, row)
                || (attacker.SummonRound >= State.Round && !attacker.HasCharge)) continue;
            var targets = new List<string>();
            for (var targetRow = 0; targetRow < 2; targetRow++)
            for (var targetSlot = 0; targetSlot < 3; targetSlot++)
            {
                var target = defender.Field[targetRow][targetSlot];
                if (target is null || target.Hidden || !IsFieldLegion(target)) continue;
                if (row == 1 && targetRow != 0 && attacker.CanAttackBackAndMasterUntilTurn != State.TurnSerial) continue;
                if (row == 0 && targetRow == 1 && !HasRangeInPosition(attacker, row)) continue;
                var isRanged = row == 1 || targetRow == 1;
                if (isRanged && target.CannotBeRanged) continue;
                if (taunts.Count > 0 && !taunts.Contains(target.InstanceId)) continue;
                targets.Add(target.InstanceId);
            }
            var disasterAllowsBackMaster = player.UsedAbilities.Contains("ds01-back-master")
                && HasRangeInPosition(attacker, row);
            if (taunts.Count == 0 && (row == 0 || disasterAllowsBackMaster
                || attacker.CanAttackBackAndMasterUntilTurn == State.TurnSerial)) targets.Add("master");
            if (targets.Count > 0) result[attacker.InstanceId] = targets.ToArray();
        }
        return result;
    }

    private static object CardBackSnapshot(L12CardInstance _) => new { hidden = true };

    private object MasterSnapshot(L12PlayerState player)
    {
        _catalog.Cards.TryGetValue(player.MasterId, out var card);
        return new
        {
            player.MasterId,
            player.MasterName,
            player.MasterImageUrl,
            effectText = card?.Effect,
            tapped = player.MasterTapped,
            player.Hp,
            player.MaxHp,
            abilities = BuildAbilityViews(player, player.MasterId, $"master-{player.PlayerIndex}"),
        };
    }

    private object FactionEffectSnapshot(L12PlayerState player)
    {
        if (player.Faction == "olympus"
            && _catalog.Cards.TryGetValue("S02-05C1A", out var moraleFace)
            && _catalog.Cards.TryGetValue("S02-05C1", out var godPowerFace))
        {
            var abilities = BuildAbilityViews(player, moraleFace.Id, $"faction-{player.PlayerIndex}")
                .Concat(BuildAbilityViews(player, godPowerFace.Id, $"faction-{player.PlayerIndex}"))
                .ToArray();
            return new
            {
                cardId = moraleFace.Id,
                name = "奥林匹斯士气 / 神力",
                imageUrl = moraleFace.ImageUrl,
                effectText = $"{moraleFace.Effect}\n{godPowerFace.Effect}",
                abilities,
            };
        }
        var moraleId = player.Morale.FirstOrDefault()?.CardId ?? player.MoraleDeck.FirstOrDefault()?.CardId;
        if (moraleId is null || !_catalog.Cards.TryGetValue(moraleId, out var card))
            return new { cardId = string.Empty, name = "阵营效果", imageUrl = (string?)null, effectText = string.Empty, abilities = Array.Empty<L12AbilityView>() };
        return new { cardId = card.Id, name = card.NameZh, imageUrl = card.ImageUrl, effectText = card.Effect, abilities = BuildAbilityViews(player, card.Id, $"faction-{player.PlayerIndex}") };
    }

    private object SpecialZonesSnapshot(L12PlayerState player, int ownerIndex, int viewer, bool revealAll)
    {
        string[] canopicIds = ["S01-0216", "S01-0217", "S01-0218", "S01-0219", "S01-0220"];
        var completedCanopicIds = player.SpecialZones.CanopicProgress.Select(card => card.CardId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var canopicTrack = player.MasterId == "S01-02M1"
            ? canopicIds.Select(cardId =>
            {
                var card = CreateCard(cardId, $"canopic-track-{ownerIndex}-{cardId}");
                return new
                {
                    card.InstanceId, card.CardId, card.Name, card.CardType, card.Faction, card.ImageUrl,
                    card.EffectText, card.Cost, card.BaseTroops, card.Troops, card.DisasterLevel,
                    completed = completedCanopicIds.Contains(cardId),
                };
            }).ToArray()
            : [];
        var trials = player.SpecialZones.Trials.Select(card =>
            revealAll || viewer == ownerIndex || card.TrialCompleted
                ? (object)card
                : new { card.InstanceId, cardId = "hidden-trial", name = "未揭示试炼", cardType = "trial", hidden = true,
                    imageUrl = "/assets/l12/trial-back.png", card.TrialProgress, card.TrialCompleted }).ToArray();
        var godPower = player.Morale.Where(card => card.IsGodPower).Select(card => new
        {
            card.InstanceId,
            cardId = "S02-05C1",
            name = "神力",
            cardType = "rune",
            faction = "olympus",
            imageUrl = _catalog.Cards.GetValueOrDefault("S02-05C1")?.ImageUrl,
            tapped = card.Tapped,
        }).ToArray();
        return new
        {
            player.SpecialZones.Runes, player.SpecialZones.TrialLevel, player.SpecialZones.TrialCapacity,
            godPower, trials, canopicProgress = player.SpecialZones.CanopicProgress, canopicTrack,
        };
    }

    private List<L12AbilityView> BuildAbilityViews(L12PlayerState player, string cardId, string sourceInstanceId)
    {
        return GetAbilities(cardId).Select(view =>
        {
            if (view.TriggerOnly)
                return view with { Enabled = false, DisabledReason = view.DisabledReason ?? "仅在符合触发时点时发动" };
            if (!CanAct(player.PlayerIndex))
                return view with { Enabled = false, DisabledReason = "仅在我方主要阶段可以发动" };
            if (player.UsedAbilities.Contains($"active:{sourceInstanceId}:{view.Id}"))
                return view with { Enabled = false, DisabledReason = "该效果本回合已经发动" };
            if (view.Id == "sunDraw" && player.Hand.Count > 3)
                return view with { Enabled = false, DisabledReason = "我方手牌需不高于3张" };
            if (view.Id == "factionZeroRecovery")
                return view with { Enabled = false, DisabledReason = "我方士气为0张时触发", TriggerOnly = true };
            if (view.Id == "godPowerDraw" && !player.Morale.Any(card => card.IsGodPower && !card.Tapped))
                return view with { Enabled = false, DisabledReason = "需要1张活跃神力" };
            if (view.Id == "olympusMoraleFlip" && !player.Morale.Any(card => !card.IsGodPower))
                return view with { Enabled = false, DisabledReason = "没有可翻转为神力的士气" };
            if (view.Id == "isisVictory")
            {
                var completed = player.SpecialZones.CanopicProgress
                    .Where(card => card.CardId is "S01-0216" or "S01-0217" or "S01-0218" or "S01-0219" or "S01-0220")
                    .Select(card => card.CardId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                if (player.MasterId != "S01-02M1" || completed < 5
                    || !player.Graveyard.Any(card => card.CardId == "S01-02M2"))
                    return view with { Enabled = false, DisabledReason = "需要完成5种卡诺匹斯圣物且复苏的奥西里斯位于墓地" };
            }
            var match = System.Text.RegularExpressions.Regex.Match(view.Label, @"消耗\s*(\d+)\s*士气");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var cost) && ActiveResourceCount(player) < cost)
                return view with { Enabled = false, DisabledReason = $"需要{cost}张活跃士气" };
            return view;
        }).ToList();
    }

    private static object?[][] SnapshotField(L12PlayerState player, bool revealCounters)
        => player.Field.Select(row => row.Select(card =>
        {
            if (card is null) return null;
            if (card.CardId == "S01-0415" && card.Hidden && !revealCounters)
                return new
                {
                    card.InstanceId,
                    cardId = "hidden-card",
                    name = "覆盖的隐匿卡",
                    cardType = "covered",
                    faction = "hidden",
                    imageUrl = "/assets/l12/card-back-official.png",
                    effectText = (string?)null,
                    cost = 0,
                    baseTroops = 0,
                    troops = 0,
                    disasterLevel = 0,
                    hidden = true,
                    tapped = false,
                    summonRound = card.SummonRound,
                };
            if (revealCounters || !card.Hidden) return (object)card;
            return new
            {
                card.InstanceId,
                cardId = "hidden-card",
                name = "覆盖的卡牌",
                cardType = card.CardType,
                faction = "hidden",
                imageUrl = "/assets/l12/card-back-official.png",
                effectText = (string?)null,
                cost = 0,
                baseTroops = 0,
                troops = 0,
                disasterLevel = 0,
                hidden = true,
                tapped = false,
                summonRound = card.SummonRound,
            };
        }).ToArray()).ToArray();

    public string SerializeFullState() => JsonSerializer.Serialize(State);

    public string ComputeStateHash()
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(SerializeFullState()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private L12PlayerState BuildPlayer(int index, string name, L12PresetDeckDefinition deck)
    {
        var master = _catalog.Cards[deck.MasterId];
        var player = new L12PlayerState
        {
            PlayerIndex = index,
            Name = name,
            DeckName = deck.Name,
            Faction = master.Faction,
            MasterId = master.Id,
            MasterName = master.NameZh,
            MasterImageUrl = master.ImageUrl,
            Hp = master.Hp ?? 10,
            MaxHp = master.Hp ?? 10,
        };
        player.SpecialZones.TrialCapacity = L12SpecialDeckRules.TrialCapacity(master);
        var mainDeckIndex = 0;
        foreach (var cardId in deck.CardIds.Where(id => !id.Equals("S01-0212", StringComparison.OrdinalIgnoreCase)))
            player.Library.Add(CreateCard(cardId, $"p{index}-c{++mainDeckIndex}"));
        for (var i = 0; i < deck.MoraleIds.Count; i++)
            player.MoraleDeck.Add(new L12MoraleCard
            {
                InstanceId = $"p{index}-m{i + 1}",
                CardId = deck.MoraleIds[i],
            });
        for (var i = 0; i < deck.SpecialIds.Count; i++)
            player.SpecialZones.Trials.Add(CreateCard(deck.SpecialIds[i], $"p{index}-special-{i + 1}"));
        player.TrialOrderDone = player.SpecialZones.Trials.Count <= 1;
        if (player.Faction == "taiyangcheng" && _catalog.Cards.ContainsKey("S01-0212"))
        {
            var configuredGuards = deck.CardIds.Count(id => id.Equals("S01-0212", StringComparison.OrdinalIgnoreCase));
            var guardCount = configuredGuards == 0 ? 3 : Math.Min(3, configuredGuards);
            for (var i = 0; i < guardCount; i++) player.Graveyard.Add(CreateCard("S01-0212", $"p{index}-guard-{i + 1}"));
        }
        if (player.MasterId == "S01-02M1" && _catalog.Cards.ContainsKey("S01-02M2"))
            player.Graveyard.Add(CreateCard("S01-02M2", $"p{index}-osiris"));
        return player;
    }

    private static string NormalizeDisasterMode(string? mode) => mode switch
    {
        "random" => "random",
        "season" => "season",
        "none" => "none",
        _ => "all",
    };

    private bool DisastersEnabled => State.DisasterMode != "none";

    private void SetDisasterValue(int value, int? playerIndex = null, string? text = null)
    {
        State.DisasterValue = !DisastersEnabled || State.ActiveDisaster?.CardId == "S01-DS10"
            ? 0
            : Math.Max(0, value);
        if (!string.IsNullOrWhiteSpace(text))
            AddEvent("disaster-value", playerIndex, text.Replace("{value}", State.DisasterValue.ToString(), StringComparison.Ordinal));
    }

    private void AdjustDisasterValue(int delta, int? playerIndex = null, string? text = null)
        => SetDisasterValue(State.DisasterValue + delta, playerIndex, text);

    private L12CardInstance CreateCard(string cardId, string instanceId)
    {
        var card = _catalog.Cards[cardId];
        var instance = new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = card.Id,
            Name = card.NameZh,
            CardType = card.CardType,
            Faction = card.Faction,
            ImageUrl = card.ImageUrl,
            EffectText = card.Effect,
            Cost = card.Cost ?? 0,
            BaseTroops = card.Troops ?? 0,
            Troops = card.Troops ?? 0,
            DisasterLevel = card.DisasterLevel ?? 0,
            TrialValue = card.TrialValue ?? 0,
            Traits = [.. card.Traits],
            Profession = card.Profession,
            Abilities = GetAbilities(card.Id),
            CannotAttack = card.Id is "S02-0005" or "S02-0007" or "S02-0201" or "S02-0603",
            CannotSupport = card.Id == "S02-0201",
        };
        if (instance.TrialValue > 0 && instance.CardType == "legion"
            && instance.Abilities.All(view => view.Id != "trialAdvance"))
            instance.Abilities.Insert(0, new L12AbilityView("trialAdvance", $"发动试炼（试炼值{instance.TrialValue}）"));
        return instance;
    }

    private CommandResult Mulligan(int playerIndex, List<string> ids)
    {
        if (State.Phase != L12Phase.Mulligan) return CommandResult.Reject("当前不是调度阶段");
        var player = State.Players[playerIndex];
        if (player.MulliganDone) return CommandResult.Reject("已经完成调度");
        if (ids.Count != ids.Distinct().Count()) return CommandResult.Reject("调度卡牌重复");
        var chosen = player.Hand.Where(card => ids.Contains(card.InstanceId)).ToList();
        if (chosen.Count != ids.Count) return CommandResult.Reject("调度卡牌不在手牌中");
        foreach (var card in chosen)
        {
            player.Hand.Remove(card);
            player.Library.Add(card);
        }
        Draw(player, chosen.Count);
        Shuffle(player.Library);
        player.MulliganDone = true;
        AddEvent("mulligan", playerIndex, $"{player.Name} 完成调度（{chosen.Count} 张）");
        if (State.Players.All(candidate => candidate.MulliganDone)) RunAutomaticTurnStart();
        return CommandResult.Ok();
    }

    private void RunAutomaticTurnStart()
    {
        var playerIndex = State.ActivePlayer;
        var player = State.Players[playerIndex];
        AddEvent("turn-start", playerIndex, $"第 {State.Round} 回合 · {player.Name} 回合");

        State.Phase = L12Phase.Disaster;
        AddEvent("phase", playerIndex, "执行触发天灾");
        if (DisastersEnabled && State.ActiveDisaster?.CardId == "S01-DS10")
            DamageMasterNonLethal(0, 1, "〈堙灭〉", neutralSource: true);
        if (DisastersEnabled && State.ActiveDisaster?.CardId == "S01-DS10")
            DamageMasterNonLethal(1, 1, "〈堙灭〉", neutralSource: true);
        if (DisastersEnabled && State.DisasterValue > 8)
        {
            State.ResumeTurnStartAfterStack = true;
            BeginDisasterTrigger(opening: State.Round == 1);
            if (State.EffectStack.Count > 0 || State.PendingPrompts.Count > 0) return;
            State.ResumeTurnStartAfterStack = false;
        }
        else if (DisastersEnabled)
        {
            AddEvent("phase-detail", playerIndex, $"当前天灾值为 {State.DisasterValue}，未达到触发条件");
        }
        ContinueAutomaticTurnStart();
    }

    private void ContinueAutomaticTurnStart()
    {
        var playerIndex = State.ActivePlayer;
        var player = State.Players[playerIndex];
        if (State.Phase == L12Phase.GameOver) return;

        State.Phase = L12Phase.Reset;
        if (player.MasterId == "S02-06D1")
        {
            AdvanceTrial(playerIndex, 1, CreateCard(player.MasterId, $"master-{playerIndex}"));
            L12S2ZoneOps.GainRunes(player, 1);
            AddEvent("runes", playerIndex, "彼界 阿瓦隆在回合开始时获得1符文");
        }
        AddEvent("phase", playerIndex, "执行重置阶段");
        Untap(player);
        AddEvent("phase-detail", playerIndex, "将本回合玩家所有可以重置的士气、军团与圣物转为活跃");

        State.Phase = L12Phase.Draw;
        AddEvent("phase", playerIndex, "执行抽牌阶段");
        if (State.Round == 1 && playerIndex == State.FirstPlayer)
            AddEvent("draw-skipped", playerIndex, "先手玩家首回合不抽牌");
        else if (player.MasterId == "S01-03M1")
        {
            Mill(player, 2, "瓦尔基里的抽牌阶段替代效果");
            AddEvent("phase-detail", playerIndex, "瓦尔基里将抽牌阶段改为弃置牌库顶部2张牌");
        }
        else if (!Draw(player, 1))
        {
            SetWinner(1 - playerIndex, "抽牌阶段牌库为空");
            return;
        }
        else AddEvent("phase-detail", playerIndex, "从牌库抽取 1 张牌");

        State.Phase = L12Phase.Morale;
        AddEvent("phase", playerIndex, "执行士气阶段");
        var moraleAdded = AddMorale(player, State.Round == 1 && playerIndex == State.FirstPlayer ? 1 : 2);
        AddEvent("phase-detail", playerIndex, $"从士气牌库追加 {moraleAdded} 张士气");

        State.Phase = L12Phase.Main;
        AddEvent("phase", playerIndex, "进入主要阶段");
        if (DisastersEnabled) BeginMainPhaseDisasterEffect();
    }

    private CommandResult EndTurn(int playerIndex)
    {
        if (!CanAct(playerIndex)) return CommandResult.Reject("只能在自己的主要阶段结束回合");
        var endingPlayer = State.Players[playerIndex];
        if (endingPlayer.Relic?.CardId == "S02-0305" && endingPlayer.Hand.Count > 6)
        {
            CreatePrompt(playerIndex, "hand-cards", "安德华拉诺特：弃置手牌直至手牌数量为6张",
                endingPlayer.Hand.Select(card => card.InstanceId), endingPlayer.Hand.Count - 6, endingPlayer.Hand.Count - 6,
                "s2-ring-end-discard", isPrivate: true);
            return CommandResult.Ok();
        }
        State.Phase = L12Phase.End;
        AddEvent("phase", playerIndex, "执行结束阶段");
        if (DisastersEnabled) ResolveEndPhaseDisasterEffect(playerIndex);
        if (State.PendingPrompts.Count > 0 || State.EffectStack.Count > 0) return CommandResult.Ok();
        CompleteEndTurn(playerIndex);
        return CommandResult.Ok();
    }

    private void CompleteEndTurn(int playerIndex)
    {
        var current = State.Players[playerIndex];
        ResolveS2DelayedEndTurnCards(playerIndex);
        if (State.Phase == L12Phase.GameOver) return;
        ReturnWukongMasterLegions(current, "我方回合结束", resumeEndTurn: true);
        if (State.PendingPrompts.Any(prompt => prompt.Continuation == "s2-wukong-return-morale"
            && prompt.Data.GetValueOrDefault("resumeEndTurn") == "true")) return;
        current.NextLegionChargeMaxCost = null;
        current.FreeTacticCount = 0;
        current.TemporaryMorale = 0;
        current.BackRowCannotSupport = false;
        current.ReturnedMoraleThisTurn = 0;
        current.NextFactionLegionDiscount = 0;
        current.NextS2SunDisasterLegionDiscount = 0;
        current.NextS2OlympusLegionDiscount = 0;
        current.NextActiveTacticSurcharge = 0;
        foreach (var player in State.Players)
        {
            player.MasterDamageTakenThisTurn = 0;
            ResetTemporaryCardState(player, State.TurnSerial);
            player.UsedAbilities.Clear();
        }
        if (!DisastersEnabled || State.ActiveDisaster?.CardId == "S01-DS10")
            SetDisasterValue(0);
        else
        {
            AdjustDisasterValue(1, playerIndex, "天灾值增加至 {value}");
        }
        AddEvent("end-turn", playerIndex, $"{current.Name} 结束回合");
        if (State.ExtraTurnsForPlayer == playerIndex)
            State.ExtraTurnsForPlayer = -1;
        else
            State.ActivePlayer = 1 - playerIndex;
        State.Round++;
        State.TurnSerial++;
        RunAutomaticTurnStart();
    }

    private CommandResult Surrender(int playerIndex)
    {
        SetWinner(1 - playerIndex, $"{State.Players[playerIndex].Name} 投降");
        return CommandResult.Ok();
    }

    private bool CanAct(int playerIndex) => State.ActivePlayer == playerIndex
        && State.Phase == L12Phase.Main
        && State.PendingDefense is null
        && State.PendingPrompts.Count == 0
        && State.EffectStack.Count == 0;

    private bool Draw(L12PlayerState player, int count)
    {
        var result = L12LibraryOps.Draw(player, count);
        if (!result.Success) return false;
        if (State.IsResolvingStack || State.EffectStack.Count > 0)
            foreach (var card in result.Cards)
                NotifyCardAddedToHandByEffect(player, card, "library", $"{player.Name}因效果将{card.Name}加入手牌");
        return true;
    }

    private int AddMorale(L12PlayerState player, int count, bool tapped = false)
    {
        var added = 0;
        for (var i = 0; i < count && player.MoraleDeck.Count > 0; i++)
        {
            var card = player.MoraleDeck[0];
            player.MoraleDeck.RemoveAt(0);
            card.Tapped = tapped;
            player.Morale.Add(card);
            added++;
        }
        return added;
    }

    private int ActiveResourceCount(L12PlayerState player)
        => player.TemporaryMorale + player.Morale.Count(card => !card.Tapped)
            + ActiveTombGuardResources(player).Count();

    private static int ActiveMoraleCountWithoutTombGuards(L12PlayerState player)
        => player.TemporaryMorale + player.Morale.Count(card => !card.Tapped);

    private bool TryConsumeMorale(L12PlayerState player, int cost, bool preferTombGuards = false, bool allowTombGuards = true)
    {
        if ((allowTombGuards ? ActiveResourceCount(player) : ActiveMoraleCountWithoutTombGuards(player)) < cost) return false;
        var temporary = Math.Min(cost, player.TemporaryMorale);
        player.TemporaryMorale -= temporary;
        var remaining = cost - temporary;
        allowTombGuards = allowTombGuards && CanUseTombGuardsAsResource(player);
        if (allowTombGuards && preferTombGuards)
        {
            var guards = ActiveTombGuardResources(player).Take(remaining).ToList();
            foreach (var guard in guards) guard.Tapped = true;
            remaining -= guards.Count;
        }
        var available = player.Morale.Where(card => !card.Tapped).Take(remaining).ToList();
        foreach (var card in available) card.Tapped = true;
        remaining -= available.Count;
        if (allowTombGuards && remaining > 0)
            foreach (var guard in ActiveTombGuardResources(player).Take(remaining)) guard.Tapped = true;
        return true;
    }

    private static bool CanReturnMorale(L12PlayerState player, int count) => player.Morale.Count >= count;

    private bool ReturnMorale(L12PlayerState player, int count)
    {
        if (!CanReturnMorale(player, count)) return false;
        var returned = player.Morale.OrderByDescending(card => card.Tapped).Take(count).ToArray();
        foreach (var card in returned)
        {
            player.Morale.Remove(card);
            card.Tapped = false;
            card.IsGodPower = false;
            card.CannotUntapUntilRound = 0;
            ReturnMoraleCardToDestination(player, card);
        }
        player.ReturnedMoraleThisTurn += count;
        if (player.PlayerIndex == State.ActivePlayer && player.Faction == "tianting" && player.Morale.Count == 0
            && player.MoraleDeck.Count > 0 && !player.UsedAbilities.Contains("trigger:factionZeroRecovery"))
            player.UsedAbilities.Add("pending:factionZeroRecovery");
        return true;
    }

    private void ReturnMoraleCardToDestination(L12PlayerState player, L12MoraleCard card)
    {
        if (card.CardId == "S02-0010")
        {
            var lotus = CreateCard(card.CardId, card.InstanceId);
            player.Graveyard.Add(lotus);
            AddEvent("grave", player.PlayerIndex, "作为士气被返还的〈黑色莲花〉置入墓地", lotus);
            return;
        }
        player.MoraleDeck.Add(card);
    }

    private void DiscardAttachedCards(L12CardInstance host, string reason)
    {
        foreach (var attached in host.AttachedCards.ToArray())
        {
            var owner = attached.OwnerIndex is >= 0 and <= 1 ? attached.OwnerIndex.Value : 0;
            ResetCardAfterLeavingField(attached);
            State.Players[owner].Graveyard.Add(attached);
            AddEvent("grave", owner, $"{reason}，{attached.Name}置入所有者墓地", attached, host);
        }
        host.AttachedCards.Clear();
        host.Abilities.RemoveAll(view => view.Id == "discardHolyLock");
    }

    private void Untap(L12PlayerState player)
    {
        player.MasterTapped = false;
        foreach (var morale in player.Morale)
            if (morale.CannotUntapUntilRound < State.Round) morale.Tapped = false;
        foreach (var row in player.Field)
            foreach (var card in row)
                if (card is not null && card.CannotUntapUntilRound < State.Round) card.Tapped = false;
        if (player.Relic is not null && player.Relic.CannotUntapUntilRound < State.Round) player.Relic.Tapped = false;
    }

    private static void ResetTemporaryCardState(L12PlayerState player, int completedTurn)
    {
        foreach (var card in player.Field.SelectMany(row => row).Where(card => card is not null).Cast<L12CardInstance>())
        {
            L12DerivedStats.ResetForCompletedTurn(card, completedTurn);
            card.HasStrongAttack = false;
            card.HasSureHit = false;
            card.HasShock = false;
            card.AttacksThisTurn = 0;
            card.CanAttackBackAndMasterUntilTurn = card.CanAttackBackAndMasterUntilTurn <= completedTurn ? -1 : card.CanAttackBackAndMasterUntilTurn;
            if (card.ReadyAfterNextKillUntilTurn <= completedTurn)
            {
                card.ReadyAfterNextKillUntilTurn = -1;
                card.ReadyAfterNextKillSourceName = null;
            }
            if (card.ImmortalUntilTurn <= completedTurn) card.ImmortalUses = 0;
        }
    }

    private static L12CardInstance? FindOnField(L12PlayerState player, string? instanceId, out int row, out int slot)
    {
        for (row = 0; row < 2; row++)
            for (slot = 0; slot < 3; slot++)
                if (player.Field[row][slot]?.InstanceId == instanceId) return player.Field[row][slot];
        row = slot = -1;
        return null;
    }

    private static bool IsFieldLegion(L12CardInstance card)
        => card.CardType == "legion" || card.CardId == "S01-0417" || card.IsMasterLegion;

    private L12CardInstance? FindPublicCard(string? instanceId, out int owner)
    {
        for (owner = 0; owner < 2; owner++)
        {
            var card = FindOnField(State.Players[owner], instanceId, out _, out _);
            if (card is not null) return card;
            if (State.Players[owner].Relic?.InstanceId == instanceId) return State.Players[owner].Relic;
            card = State.Players[owner].ExtraRelics.FirstOrDefault(item => item.InstanceId == instanceId);
            if (card is not null) return card;
            card = State.Players[owner].Resolving.FirstOrDefault(item => item.InstanceId == instanceId);
            if (card is not null) return card;
        }
        owner = -1;
        return null;
    }

    private bool RemoveFromField(L12PlayerState player, L12CardInstance card, bool toGraveyard, string reason = "离场",
        bool queueDeathTrigger = true, L12FieldLeaveKind leaveKind = L12FieldLeaveKind.Defeat)
    {
        if (FindOnField(player, card.InstanceId, out var row, out var slot) is null) return false;
        var isDefeat = toGraveyard && leaveKind == L12FieldLeaveKind.Defeat;
        if (isDefeat && TryPreventS1FactionDeath(player, card)) return false;
        if (isDefeat && card.ImmortalUses > 0 && card.ImmortalUntilTurn >= State.TurnSerial)
        {
            card.ImmortalUses--;
            L12DerivedStats.SetUntilTurnEnd(card, 1000, State.TurnSerial);
            AddEvent("effect", player.PlayerIndex, $"{card.Name} 的免死生效，兵力变为 1000", card);
            if (card.Troops > 0) return false;
            AddEvent("effect", player.PlayerIndex, $"{card.Name} 在持续兵力修正重算后兵力仍不高于 0", card);
        }
        player.Field[row][slot] = null;
        if (toGraveyard)
        {
            ResetCardAfterLeavingField(card);
            player.Graveyard.Add(card);
            if (card.AttachedCards.Count > 0)
            {
                foreach (var attached in card.AttachedCards) ResetCardAfterLeavingField(attached);
                player.Graveyard.AddRange(card.AttachedCards);
                AddEvent("leave", player.PlayerIndex, $"{card.Name} 与其叠放底座一同离场", card.AttachedCards.ToArray());
                card.AttachedCards.Clear();
            }
        }
        AddEvent("leave", player.PlayerIndex, $"{card.Name}{reason}", card);
        if (queueDeathTrigger)
        {
            var candidates = BuildS1LeaveReactionCandidates(player.PlayerIndex, card).ToList();
            if (isDefeat && HasDeathTrigger(card))
                candidates.Add(CreateTriggerCandidate(player.PlayerIndex, card, "death", "【阵亡时】效果",
                    new Dictionary<string, string> { ["cause"] = State.PendingDefense is null ? "effect" : "combat" }));
            if (isDefeat)
            {
                var morrigan = BuildMorriganEnemyDeathCandidate(player.PlayerIndex);
                if (morrigan is not null) candidates.Add(morrigan);
                var nephthys = BuildNephthysOwnDeathCandidate(player.PlayerIndex, card);
                if (nephthys is not null) candidates.Add(nephthys);
                var artemis = BuildArtemisRangedDeathCandidate(player.PlayerIndex, card);
                if (artemis is not null) candidates.Add(artemis);
            }
            QueueTriggerCandidates(candidates);
        }
        RecalculateContinuousTroops();
        return true;
    }

    private bool MoveFieldCardToZone(L12PlayerState player, L12CardInstance card, string destination, string reason, bool queueLeaveTrigger = true)
    {
        if (FindOnField(player, card.InstanceId, out var row, out var slot) is null) return false;
        player.Field[row][slot] = null;

        // 规则替代：陵墓守卫不能进入手牌、牌库或移出区，以任何形式离场都改为置入所有者墓地。
        if (card.CardId == "S01-0212")
        {
            ResetCardAfterLeavingField(card);
            player.Graveyard.Add(card);
            AddEvent("replacement", player.PlayerIndex, $"{card.Name}以任何形式离场，改为置入墓地", card);
        }
        else switch (destination)
        {
            case "hand":
                ResetCardAfterLeavingField(card);
                if (State.IsResolvingStack || State.EffectStack.Count > 0)
                    AddCardToHandByEffect(player, card, "field", $"{card.Name}因效果加入手牌");
                else player.Hand.Add(card);
                break;
            case "library-top": ResetCardAfterLeavingField(card); player.Library.Insert(0, card); break;
            case "library-bottom": ResetCardAfterLeavingField(card); player.Library.Add(card); break;
            case "removed": ResetCardAfterLeavingField(card); player.Removed.Add(card); break;
            default: ResetCardAfterLeavingField(card); player.Graveyard.Add(card); break;
        }

        if (card.AttachedCards.Count > 0)
        {
            foreach (var attached in card.AttachedCards) ResetCardAfterLeavingField(attached);
            player.Graveyard.AddRange(card.AttachedCards);
            card.AttachedCards.Clear();
        }
        AddEvent("leave", player.PlayerIndex, $"{card.Name}{reason}", card);
        if (queueLeaveTrigger)
            QueueTriggerCandidates(BuildS1LeaveReactionCandidates(player.PlayerIndex, card));
        RecalculateContinuousTroops();
        return true;
    }

    private static void ResetCardAfterLeavingField(L12CardInstance card)
    {
        card.CostModifier = 0;
        card.PlayCost = null;
        card.Troops = card.BaseTroops;
        card.ContinuousTroopsModifier = 0;
        card.SetTroopsValue = null;
        card.SetTroopsUntilTurn = -1;
        card.HasCharge = false;
        card.HasStrongAttack = false;
        card.HasSureHit = false;
        card.HasShock = false;
        card.AttackNoLossUntilTurn = -1;
        card.NextAttackNoLossUses = 0;
        card.ReadyAfterNextKillUntilTurn = -1;
        card.ReadyAfterNextKillSourceName = null;
        card.SureHitAgainstLegionsUntilTurn = -1;
        card.CannotReadyByEffectUntilTurn = -1;
        card.DiscardAtEndOfTurnUntilTurn = -1;
        card.Hidden = false;
        card.Tapped = false;
        card.SummonRound = 0;
        card.LastMovedTurn = -1;
        card.LastCavalryMoveTurn = -1;
        card.CannotUntapUntilRound = 0;
        card.CannotRespondUntilRound = 0;
        card.SetRound = 0;
        card.AttacksThisTurn = 0;
        card.TrialProgress = 0;
        card.TrialCompleted = false;
        card.CannotAttack = card.CardId is "S02-0005" or "S02-0007" or "S02-0201" or "S02-0603";
        card.CannotSupport = card.CardId == "S02-0201";
        card.CanAttackBackAndMasterUntilTurn = -1;
        card.CanAttackMasterOnSummonUntilTurn = -1;
        card.CanAttackLegionsOnSummonUntilTurn = -1;
        card.ImmortalUses = 0;
        card.ImmortalUntilTurn = -1;
        card.SuppressDeathUntilTurn = -1;
        card.TimedModifiers.Clear();
    }

    private L12CardInstance[] SnapshotHand(int playerIndex)
        => State.Players[playerIndex].Hand.Select(card =>
        {
            var snapshot = card.Clone();
            var selfDamageDiscount = HasOptionalSelfDamageEntryDiscount(card) && State.Players[playerIndex].Hp > 1;
            var spentRunes = card.CardId == "S02-0622"
                ? Math.Min(State.Players[playerIndex].SpecialZones.Runes, (card.Cost + 1) / 2)
                : 0;
            snapshot.PlayCost = GetPlayCost(playerIndex, card, selfDamageDiscount, spentRunes);
            return snapshot;
        }).ToArray();

    private void ResolveStateBasedLegionDeaths(bool suppressDeathTriggers = false)
    {
        if (State.Phase == L12Phase.GameOver) return;
        suppressDeathTriggers = suppressDeathTriggers || State.Phase == L12Phase.Disaster
            || State.EffectStack.Any(item => item.Trigger == "disaster");
        for (var pass = 0; pass < 12; pass++)
        {
            RecalculateContinuousTroops();
            var defeated = State.Players.SelectMany(player => player.Field.SelectMany(row => row)
                    .Where(card => card is not null && IsFieldLegion(card) && card.Troops <= 0)
                    .Cast<L12CardInstance>()
                    .Select(card => (Controller: player.PlayerIndex, Card: card)))
                .ToArray();
            if (defeated.Length == 0) return;
            var removed = new List<(int Controller, L12CardInstance Card)>();
            foreach (var entry in defeated)
                if (RemoveFromField(State.Players[entry.Controller], entry.Card, true, "因兵力不高于0阵亡", queueDeathTrigger: false))
                    removed.Add(entry);
            if (!suppressDeathTriggers) QueueSimultaneousDeathTriggers(removed);
            if (removed.Count == 0) return;
        }
        throw new InvalidOperationException("兵力状态检查超过安全迭代次数");
    }

    private static bool CanEnterHandOrLibrary(L12CardInstance card) => card.CardId != "S01-0212";

    private void CheckWinner()
    {
        if (State.Winner is not null) return;
        if (State.Players[0].Hp <= 0) SetWinner(1, "主宰血量归零");
        else if (State.Players[1].Hp <= 0) SetWinner(0, "主宰血量归零");
    }

    private void SetWinner(int winner, string reason)
    {
        State.Winner = winner;
        State.WinnerReason = reason;
        State.Phase = L12Phase.GameOver;
        State.PendingDefense = null;
        State.PendingPrompts.Clear();
        State.EffectStack.Clear();
        State.DeferredEffectStack.Clear();
        State.IsResolvingStack = false;
        State.ResponseWindow = null;
        AddEvent("game-over", winner, $"{State.Players[winner].Name} 获胜：{reason}");
    }

    private int? ResolveDamageSourcePlayer(int? declaredSourcePlayer, bool neutralSource)
    {
        if (neutralSource) return null;
        if (declaredSourcePlayer is 0 or 1) return declaredSourcePlayer;
        var sourceItem = State.EffectStack.LastOrDefault();
        return sourceItem is null || sourceItem.Trigger == "disaster" ? null : sourceItem.Controller;
    }

    private void DamageMaster(int playerIndex, int amount, string source, int? sourcePlayer = null,
        bool neutralSource = false, bool combatDamage = false)
    {
        var player = State.Players[playerIndex];
        amount = AdjustAnderstorpRingDamage(player, amount);
        player.Hp -= amount;
        player.MasterDamageTakenThisTurn += Math.Max(0, amount);
        AddEvent("damage", playerIndex, $"{player.Name} 的主宰因{source}失去 {amount} 点血量");
        if (player.Hp <= 0)
            SetWinner(1 - playerIndex, $"{player.Name}的主宰因{source}血量降至0");
        if (State.Phase != L12Phase.GameOver)
        {
            QueueS1MasterDamageReaction(playerIndex, ResolveDamageSourcePlayer(sourcePlayer, neutralSource), !combatDamage);
        }
    }

    private void DamageMasterNonLethal(int playerIndex, int amount, string source, int? sourcePlayer = null, bool neutralSource = false)
    {
        var player = State.Players[playerIndex];
        amount = AdjustAnderstorpRingDamage(player, amount);
        var actual = Math.Min(amount, Math.Max(0, player.Hp - 1));
        if (actual == 0) return;
        player.Hp -= actual;
        player.MasterDamageTakenThisTurn += actual;
        AddEvent("damage", playerIndex, $"{player.Name} 的主宰因{source}失去 {actual} 点非致命伤害");
        QueueS1MasterDamageReaction(playerIndex, ResolveDamageSourcePlayer(sourcePlayer, neutralSource), effectDamage: true);
    }

    private void HealMaster(int playerIndex, int amount, string source, bool legionEffect = false)
    {
        var player = State.Players[playerIndex];
        if (player.MasterCannotHeal)
        {
            AddEvent("heal-prevented", playerIndex, $"{player.Name} 的主宰因〈雷神索尔〉无法因{source}增加血量");
            return;
        }
        if (legionEffect && player.LegionEffectHealForbiddenUntilTurn == State.TurnSerial)
        {
            AddEvent("heal-prevented", playerIndex, $"{player.Name} 的主宰本回合无法因军团效果〈{source}〉增加血量");
            return;
        }
        var actual = Math.Min(amount, player.MaxHp - player.Hp);
        if (actual <= 0) return;
        player.Hp += actual;
        AddEvent("heal", playerIndex, $"{player.Name} 的主宰因{source}增加 {actual} 点血量");
    }

    private void AddEvent(string type, int? playerIndex, string text, params L12CardInstance[] cards)
    {
        State.EventSequence++;
        State.LastAction = new L12ActionEvent(State.EventSequence, type, playerIndex, text, cards.Select(card => card.Clone()).ToArray());
        State.Events.Add(State.LastAction);
        State.Log.Add(text);
        if (State.Log.Count > 80) State.Log.RemoveAt(0);
    }

    private void Shuffle<T>(IList<T> list)
        => L12LibraryOps.Shuffle(list, _random);
}
