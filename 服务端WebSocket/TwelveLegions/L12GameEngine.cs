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
        bool skipPreparation = false)
        : this(catalog, matchId, roomCode, seed, playerNames,
            deckIndexes.Select(catalog.DeckAt).ToArray(), skipPreparation)
    {
    }

    public L12GameEngine(
        L12Catalog catalog,
        string matchId,
        string roomCode,
        int seed,
        string[] playerNames,
        L12PresetDeckDefinition[] decks,
        bool skipPreparation = false)
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
            PrepareLibrariesAndHands();
            State.Phase = L12Phase.Mulligan;
            AddEvent("match-created", null, $"对局创建，玩家 {State.FirstPlayer + 1} 先手（测试准备捷径）");
        }
        else
        {
            BuildDisasterPool();
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

        var result = command.Type switch
        {
            "resolvePrompt" => ResolvePrompt(playerIndex, command),
            "mulligan" => Mulligan(playerIndex, command.CardInstanceIds ?? []),
            "advancePhase" => CommandResult.Reject("触发天灾至主要阶段由服务器自动结算"),
            "playCard" => PlayCard(playerIndex, command),
            "attack" => Attack(playerIndex, command),
            "resolveDefense" => ResolveDefense(playerIndex, command),
            "move" => Move(playerIndex, command),
            "activateAbility" => ActivateAbility(playerIndex, command),
            "flipHidden" => FlipHidden(playerIndex, command.CardInstanceId),
            "endTurn" => EndTurn(playerIndex),
            "surrender" => Surrender(playerIndex),
            _ => CommandResult.Reject("未知操作"),
        };

        if (result.Accepted)
        {
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
                libraryCount = player.Library.Count, player.Hand, player.MoraleDeck, player.Morale,
                field = SnapshotField(player, revealCounters: true), player.Relic, player.ExtraRelics, player.Resolving, player.Graveyard, player.Removed, player.SpecialZones,
                player.TemporaryMorale, player.NextLegionChargeMaxCost, player.MulliganDone,
            }
            : new
            {
                player.PlayerIndex, player.Name, player.DeckName, player.Faction,
                master = MasterSnapshot(player),
                factionEffect = FactionEffectSnapshot(player),
                libraryCount = player.Library.Count, handCount = player.Hand.Count,
                moraleDeckCount = player.MoraleDeck.Count, player.Morale,
                field = SnapshotField(player, revealCounters: false), player.Relic, player.ExtraRelics, player.Resolving, player.Graveyard, graveyardCount = player.Graveyard.Count,
                removedCount = player.Removed.Count, player.SpecialZones, player.TemporaryMorale, player.NextLegionChargeMaxCost, player.MulliganDone,
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
        var stack = State.EffectStack.Select(item => (object)new
        {
            item.StackItemId, item.Controller, item.SourceInstanceId, item.SourceCardId,
            item.SourceName, item.Trigger, item.Text, item.Negated, item.Targets,
        }).ToArray();

        var recentEvents = State.Events
            .Select(actionEvent => FilterDisasterEvent(actionEvent, viewer, revealAllDisasters))
            .ToArray();
        var lastAction = State.LastAction is null
            ? null
            : FilterDisasterEvent(State.LastAction, viewer, revealAllDisasters);

        return new L12GameSnapshot(
            State.MatchId, State.RoomCode, spectator ? 0 : viewer, State.Revision, State.ActivePlayer,
            State.FirstPlayer, State.DiceWinner, State.InitiativeRolls, State.Phase, State.Round,
            State.DisasterValue, State.ActiveDisaster, State.DisasterDeck.Select(CardBackSnapshot).ToArray(),
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
        var known = new List<object>();
        known.AddRange(State.RevealedDisasters.Cast<object>());
        var chosen = State.ChosenDisasters
            .Select(card => DisasterVisibilitySnapshot(card, viewer, revealAll))
            .ToArray();
        known.AddRange(chosen.Where(IsVisibleDisasterSnapshot));
        var hidden = chosen.Where(item => !IsVisibleDisasterSnapshot(item)).ToList();
        var final = State.DisasterDeck.Concat(State.RemovedDisasters).Append(State.ActiveDisaster)
            .FirstOrDefault(card => card?.CardId == "S01-DS10");
        if (final is not null)
        {
            var finalSnapshot = DisasterVisibilitySnapshot(final, viewer, revealAll);
            (IsVisibleDisasterSnapshot(finalSnapshot) ? known : hidden).Add(finalSnapshot);
        }
        // 本局天灾已经洗混，界面只表达“已知数量”，不暴露任何牌库顺序。
        known.AddRange(hidden);
        return known.ToArray();
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
            abilities = GetAbilities(player.MasterId),
        };
    }

    private object FactionEffectSnapshot(L12PlayerState player)
    {
        var moraleId = player.Morale.FirstOrDefault()?.CardId ?? player.MoraleDeck.FirstOrDefault()?.CardId;
        if (moraleId is null || !_catalog.Cards.TryGetValue(moraleId, out var card))
            return new { cardId = string.Empty, name = "阵营效果", imageUrl = (string?)null, effectText = string.Empty, abilities = Array.Empty<L12AbilityView>() };
        return new { cardId = card.Id, name = card.NameZh, imageUrl = card.ImageUrl, effectText = card.Effect, abilities = GetAbilities(card.Id) };
    }

    private static object?[][] SnapshotField(L12PlayerState player, bool revealCounters)
        => player.Field.Select(row => row.Select(card =>
        {
            if (card is null) return null;
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

    private L12CardInstance CreateCard(string cardId, string instanceId)
    {
        var card = _catalog.Cards[cardId];
        return new L12CardInstance
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
            Abilities = GetAbilities(card.Id),
            CannotAttack = card.Id is "S02-0005" or "S02-0007" or "S02-0201" or "S02-0603",
            CannotSupport = card.Id == "S02-0201",
        };
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
        if (State.ActiveDisaster?.CardId == "S01-DS10")
            DamageMasterNonLethal(0, 1, "〈堙灭〉");
        if (State.ActiveDisaster?.CardId == "S01-DS10")
            DamageMasterNonLethal(1, 1, "〈堙灭〉");
        if (State.DisasterValue > 8)
        {
            State.ResumeTurnStartAfterStack = true;
            BeginDisasterTrigger(opening: State.Round == 1);
            if (State.EffectStack.Count > 0 || State.PendingPrompts.Count > 0) return;
            State.ResumeTurnStartAfterStack = false;
        }
        else
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
        BeginMainPhaseDisasterEffect();
    }

    private CommandResult EndTurn(int playerIndex)
    {
        if (!CanAct(playerIndex)) return CommandResult.Reject("只能在自己的主要阶段结束回合");
        State.Phase = L12Phase.End;
        AddEvent("phase", playerIndex, "执行结束阶段");
        ResolveEndPhaseDisasterEffect(playerIndex);
        if (State.PendingPrompts.Count > 0 || State.EffectStack.Count > 0) return CommandResult.Ok();
        CompleteEndTurn(playerIndex);
        return CommandResult.Ok();
    }

    private void CompleteEndTurn(int playerIndex)
    {
        var current = State.Players[playerIndex];
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
            ResetTemporaryCardState(player, State.TurnSerial);
            player.UsedAbilities.Clear();
        }
        if (State.ActiveDisaster?.CardId == "S01-DS10")
            State.DisasterValue = 0;
        else
        {
            State.DisasterValue++;
            AddEvent("disaster-value", playerIndex, $"天灾值增加至 {State.DisasterValue}");
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
            + PublicLegions(player).Count(card => card.CardId == "S01-0212" && !card.Tapped && State.ActivePlayer == player.PlayerIndex);

    private static int ActiveMoraleCountWithoutTombGuards(L12PlayerState player)
        => player.TemporaryMorale + player.Morale.Count(card => !card.Tapped);

    private bool TryConsumeMorale(L12PlayerState player, int cost, bool preferTombGuards = false, bool allowTombGuards = true)
    {
        if ((allowTombGuards ? ActiveResourceCount(player) : ActiveMoraleCountWithoutTombGuards(player)) < cost) return false;
        var temporary = Math.Min(cost, player.TemporaryMorale);
        player.TemporaryMorale -= temporary;
        var remaining = cost - temporary;
        if (allowTombGuards && preferTombGuards)
        {
            var guards = PublicLegions(player).Where(card => card.CardId == "S01-0212" && !card.Tapped).Take(remaining).ToList();
            foreach (var guard in guards) guard.Tapped = true;
            remaining -= guards.Count;
        }
        var available = player.Morale.Where(card => !card.Tapped).Take(remaining).ToList();
        foreach (var card in available) card.Tapped = true;
        remaining -= available.Count;
        if (allowTombGuards && remaining > 0)
            foreach (var guard in PublicLegions(player).Where(card => card.CardId == "S01-0212" && !card.Tapped).Take(remaining)) guard.Tapped = true;
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
            card.AttacksThisTurn = 0;
            card.CanAttackBackAndMasterUntilTurn = card.CanAttackBackAndMasterUntilTurn <= completedTurn ? -1 : card.CanAttackBackAndMasterUntilTurn;
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
        => card.CardType == "legion" || card.CardId == "S01-0417";

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

    private bool RemoveFromField(L12PlayerState player, L12CardInstance card, bool toGraveyard, string reason = "离场", bool queueDeathTrigger = true)
    {
        if (FindOnField(player, card.InstanceId, out var row, out var slot) is null) return false;
        if (toGraveyard && TryPreventS1FactionDeath(player, card)) return false;
        if (toGraveyard && card.ImmortalUses > 0 && card.ImmortalUntilTurn >= State.TurnSerial)
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
            player.Graveyard.Add(card);
            if (card.AttachedCards.Count > 0)
            {
                player.Graveyard.AddRange(card.AttachedCards);
                AddEvent("leave", player.PlayerIndex, $"{card.Name} 与其叠放底座一同离场", card.AttachedCards.ToArray());
                card.AttachedCards.Clear();
            }
        }
        AddEvent("leave", player.PlayerIndex, $"{card.Name}{reason}", card);
        if (queueDeathTrigger)
        {
            var candidates = BuildS1LeaveReactionCandidates(player.PlayerIndex, card).ToList();
            if (HasDeathTrigger(card))
                candidates.Add(CreateTriggerCandidate(player.PlayerIndex, card, "death", "【阵亡时】效果"));
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
            player.Graveyard.Add(card);
            AddEvent("replacement", player.PlayerIndex, $"{card.Name}以任何形式离场，改为置入墓地", card);
        }
        else switch (destination)
        {
            case "hand":
                if (State.IsResolvingStack || State.EffectStack.Count > 0)
                    AddCardToHandByEffect(player, card, "field", $"{card.Name}因效果加入手牌");
                else player.Hand.Add(card);
                break;
            case "library-top": player.Library.Insert(0, card); break;
            case "library-bottom": player.Library.Add(card); break;
            case "removed": player.Removed.Add(card); break;
            default: player.Graveyard.Add(card); break;
        }

        if (card.AttachedCards.Count > 0)
        {
            player.Graveyard.AddRange(card.AttachedCards);
            card.AttachedCards.Clear();
        }
        AddEvent("leave", player.PlayerIndex, $"{card.Name}{reason}", card);
        if (queueLeaveTrigger)
            QueueTriggerCandidates(BuildS1LeaveReactionCandidates(player.PlayerIndex, card));
        RecalculateContinuousTroops();
        return true;
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

    private void DamageMaster(int playerIndex, int amount, string source)
    {
        State.Players[playerIndex].Hp -= amount;
        AddEvent("damage", playerIndex, $"{State.Players[playerIndex].Name} 的主宰因{source}失去 {amount} 点血量");
        if (State.Players[playerIndex].Hp <= 0)
            SetWinner(1 - playerIndex, $"{State.Players[playerIndex].Name}的主宰因{source}血量降至0");
        if (State.Phase != L12Phase.GameOver) QueueS1MasterDamageReaction(playerIndex);
    }

    private void DamageMasterNonLethal(int playerIndex, int amount, string source)
    {
        var player = State.Players[playerIndex];
        var actual = Math.Min(amount, Math.Max(0, player.Hp - 1));
        if (actual == 0) return;
        player.Hp -= actual;
        AddEvent("damage", playerIndex, $"{player.Name} 的主宰因{source}失去 {actual} 点非致命伤害");
        QueueS1MasterDamageReaction(playerIndex);
    }

    private void HealMaster(int playerIndex, int amount, string source)
    {
        var player = State.Players[playerIndex];
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
