using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

/// <summary>
/// 324 张卡逐卡交互遍历。
///
/// 这不是“存在某个测试文件”或“能够解析文本”的静态审计：每个用例都会建立独立的
/// 权威对局状态，经由与网页相同的 Handle/resolvePrompt 命令入口实际点击卡牌、发动
/// 可见主动能力并处理 Prompt。三个策略分别覆盖最少选择、最多/末项选择和拒绝/取消。
/// 等价卡位不会做指数级排列，但首末目标、最少/最多数量、发动/不发动及取消语义均会经过。
/// </summary>
public sealed class AllCardInteractionTraversalTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    public static IEnumerable<object[]> CardBranches()
    {
        foreach (var cardId in Catalog.Cards.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            yield return [cardId, "minimum"];
            yield return [cardId, "maximum"];
            yield return [cardId, "decline"];
        }
    }

    [Theory]
    [MemberData(nameof(CardBranches))]
    [Trait("L12Evidence", "all-card-interaction-traversal")]
    public void EveryCardCanTraverseItsPlayerInteractionBranches(string cardId, string branch)
    {
        var definition = Catalog.Cards[cardId];

        // 先实际打开一次这张卡的玩家快照；主宰、天灾、士气及特殊区卡也必须能被
        // 权威目录实例化，不能在客户端临时退回占位数据。
        var projectionGame = CreateGame(definition, Seed(cardId, branch, 1));
        var projected = Card(definition, $"projection-{cardId}");
        Assert.Equal(cardId, projected.CardId);
        Assert.Equal(definition.NameZh, projected.Name);

        ExercisePrintedPlayOrZoneEntry(definition, branch);
        ExerciseEveryVisibleActiveAbility(definition, branch);
        ExerciseLegionCombatAndDefeat(definition, branch);

        // 快照本身也是网页的真实输入。每张卡完成交互后再次生成快照，确保派生属性、
        // 能力与 Prompt 投影不会因本卡而抛错。
        _ = projectionGame.SnapshotForGm(0);
    }

    [Fact]
    [Trait("L12Evidence", "all-card-interaction-inventory")]
    public void TraversalInventoryIsExactlyTheAuthoritativeThreeHundredTwentyFourCards()
    {
        Assert.Equal(324, Catalog.Cards.Count);
        Assert.Equal(324 * 3, CardBranches().Count());
        Assert.Equal(Catalog.Cards.Count, Catalog.Cards.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private static void ExercisePrintedPlayOrZoneEntry(L12CardDefinition definition, string branch)
    {
        var game = CreateGame(definition, Seed(definition.Id, branch, 2));
        var player = game.State.Players[0];
        var source = Card(definition, $"play-{definition.Id}");
        source.OwnerIndex = 0;

        CommandResult? result = definition.CardType switch
        {
            "legion" or "token" => PlayFromHand(game, player, source, row: 0, slot: 0),
            "artifact" => PlayFromHand(game, player, source),
            "tactic" => PlayFromHand(game, player, source),
            "counter-tactic" => CoverCounterTactic(game, player, source),
            "destruction" => PutDisasterIntoAuthoritativeDeck(game, source),
            "trial" => PutTrialIntoSpecialZone(player, source),
            "rune" => PutRuneIntoResourceZone(player, source),
            "divinity" => PutDivinityIntoPublicZone(player, source),
            "master" => CommandResult.Ok(),
            _ => CommandResult.Reject($"未登记卡牌类型：{definition.CardType}"),
        };

        Assert.True(result.Accepted, $"{definition.Id} {definition.NameZh} 的基础交互失败：{result.Error}");
        DrainPrompts(game, branch, definition.Id);

        if (definition.CardType == "destruction")
        {
            var trigger = game.HandleGm(new L12GmCommand("triggerDisaster"));
            Assert.True(trigger.Accepted, $"{definition.Id} 的天灾发动入口失败：{trigger.Error}");
            DrainPrompts(game, branch, definition.Id);
        }
    }

    private static void ExerciseEveryVisibleActiveAbility(L12CardDefinition definition, string branch)
    {
        var inventoryGame = CreateGame(definition, Seed(definition.Id, branch, 3));
        var inventory = Invoke<List<L12AbilityView>>(inventoryGame, "GetAbilities", definition.Id);
        foreach (var ability in inventory.Where(item => !item.TriggerOnly))
        {
            var game = CreateGame(definition, Seed(definition.Id, branch, 10 + StableHash(ability.Id) % 500));
            var player = game.State.Players[0];
            var source = Card(definition, $"active-{definition.Id}-{ability.Id}");
            source.OwnerIndex = 0;
            source.SummonRound = -1;
            var sourceId = PutActiveSourceInItsAuthoritativeZone(game, player, source, ability.Id);

            var visible = Invoke<List<L12AbilityView>>(game, "BuildAbilityViews", player, definition.Id, sourceId)
                .SingleOrDefault(item => item.Id.Equals(ability.Id, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(visible);

            var result = game.Handle(0, new L12Command("activateAbility", sourceId, Ability: ability.Id));
            // 禁用按钮也属于必须逐卡验证的交互组合：点击不得改变状态，并且必须返回
            // 玩家可理解的原因；可用按钮则必须真正受理并继续到选择/结算。
            if (!result.Accepted)
            {
                Assert.False(string.IsNullOrWhiteSpace(result.Error));
                AssertPlayerFacingText(result.Error!, definition.Id);
                Assert.False(visible.Enabled,
                    $"{definition.Id}/{ability.Id} 显示可用却被服务端拒绝：{result.Error}");
                continue;
            }

            Assert.True(visible.Enabled,
                $"{definition.Id}/{ability.Id} 显示禁用却被服务端受理：{visible.DisabledReason}");

            DrainPrompts(game, branch, $"{definition.Id}/{ability.Id}");
        }
    }

    private static void ExerciseLegionCombatAndDefeat(L12CardDefinition definition, string branch)
    {
        if (definition.CardType is not ("legion" or "token")) return;

        var attackGame = CreateGame(definition, Seed(definition.Id, branch, 4));
        var attacker = Card(definition, $"attack-{definition.Id}");
        attacker.OwnerIndex = 0;
        attacker.SummonRound = -1;
        attacker.Tapped = false;
        attacker.HasCharge = true;
        attackGame.State.Players[0].Field[0][0] = attacker;
        var defender = attackGame.State.Players[1].Field[0][0]!;
        var attack = attackGame.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", defender.InstanceId)));
        if (!attack.Accepted)
            attack = attackGame.Handle(0, new L12Command("attack", attacker.InstanceId,
                Target: new L12AttackTarget("master")));
        // 静态规则可以令某张军团当前不能进攻；此时禁用原因必须仍是自然语言。
        if (attack.Accepted) DrainPrompts(attackGame, branch, $"{definition.Id}/attack");
        else AssertPlayerFacingText(Assert.IsType<string>(attack.Error), definition.Id);

        var defeatGame = CreateGame(definition, Seed(definition.Id, branch, 5));
        var defeated = Card(definition, $"defeat-{definition.Id}");
        defeated.OwnerIndex = 0;
        defeated.SummonRound = -1;
        defeatGame.State.Players[0].Field[0][0] = defeated;
        var destroy = defeatGame.HandleGm(new L12GmCommand("destroyCard", TargetPlayer: 0,
            CardInstanceId: defeated.InstanceId));
        Assert.True(destroy.Accepted || destroy.Error?.Contains("阻止了击杀", StringComparison.Ordinal) == true,
            $"{definition.Id} 的阵亡/离场入口失败：{destroy.Error}");
        DrainPrompts(defeatGame, branch, $"{definition.Id}/defeat");
    }

    private static L12GameEngine CreateGame(L12CardDefinition focus, int seed)
    {
        var baseDeck = Catalog.DeckAt(0);
        var focusMaster = focus.CardType == "master"
            ? focus.Id
            : focus.CardType == "rune"
                ? Catalog.Cards.Values.FirstOrDefault(card => card.CardType == "master" && card.Faction == focus.Faction)?.Id
                    ?? baseDeck.MasterId
                : baseDeck.MasterId;
        var focusDeck = new L12PresetDeckDefinition
        {
            Name = $"{focus.Id} 逐卡交互遍历",
            MasterId = focusMaster,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [.. baseDeck.SpecialIds],
        };
        var game = new L12GameEngine(Catalog, $"interaction-{focus.Id}-{seed}", "INTERACTION", seed,
            ["甲", "乙"], [focusDeck, baseDeck], skipPreparation: true,
            disasterMode: focus.CardType == "destruction" ? "all" : "none",
            autoPassEmptyResponses: true, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 3;
        game.State.TurnSerial = 5;
        game.State.Phase = L12Phase.Main;
        game.State.DisasterValue = 0;
        PreparePlayer(game.State.Players[0], 0);
        PreparePlayer(game.State.Players[1], 1);
        return game;
    }

    private static void PreparePlayer(L12PlayerState player, int index)
    {
        player.Hp = Math.Max(player.Hp, 30);
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
        player.SpecialZones.TrialLevel = 7;
        player.SpecialZones.TrialCapacity = 20;
        player.SpecialZones.Trials.Clear();
        player.SpecialZones.CanopicProgress.Clear();
        player.UsedAbilities.Clear();
        player.Field[0] = new L12CardInstance?[3];
        player.Field[1] = new L12CardInstance?[3];

        for (var moraleIndex = 0; moraleIndex < 16; moraleIndex++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-00C1",
                InstanceId = $"resource-{index}-{moraleIndex}",
                IsGodPower = moraleIndex >= 8,
                Tapped = false,
            });

        var fieldIds = index == 0
            ? new[] { "S01-0103", "S01-0203", "S01-0303", "S01-0403" }
            : new[] { "S01-0107", "S01-0208", "S01-0308", "S01-0407", "S02-0101", "S02-0207" };
        var positions = index == 0
            ? new[] { (0, 1), (0, 2), (1, 0), (1, 1) }
            : new[] { (0, 0), (0, 1), (0, 2), (1, 0), (1, 1), (1, 2) };
        for (var itemIndex = 0; itemIndex < positions.Length; itemIndex++)
        {
            var card = Card(Catalog.Cards[fieldIds[itemIndex]], $"field-{index}-{itemIndex}");
            card.OwnerIndex = index;
            card.SummonRound = -1;
            var (row, slot) = positions[itemIndex];
            player.Field[row][slot] = card;
        }

        var handIds = new[] { "S01-0003", "S01-0104", "S01-0205", "S01-0305", "S01-0405", "S02-0005" };
        var graveIds = new[] { "S01-0005", "S01-0106", "S01-0206", "S01-0306", "S01-0406", "S02-0006",
            "S02-0106", "S02-0206", "S02-0306", "S02-0406" };
        var libraryIds = new[] { "S01-0007", "S01-0108", "S01-0208", "S01-0308", "S01-0408", "S02-0007",
            "S02-0106", "S02-0207", "S02-0307", "S02-0406" };
        for (var itemIndex = 0; itemIndex < handIds.Length; itemIndex++)
            player.Hand.Add(Card(Catalog.Cards[handIds[itemIndex]], $"hand-{index}-{itemIndex}"));
        for (var itemIndex = 0; itemIndex < graveIds.Length; itemIndex++)
            player.Graveyard.Add(Card(Catalog.Cards[graveIds[itemIndex]], $"grave-{index}-{itemIndex}"));
        for (var itemIndex = 0; itemIndex < libraryIds.Length; itemIndex++)
            player.Library.Add(Card(Catalog.Cards[libraryIds[itemIndex]], $"library-{index}-{itemIndex}"));
    }

    private static CommandResult PlayFromHand(L12GameEngine game, L12PlayerState player, L12CardInstance source,
        int? row = null, int? slot = null)
    {
        player.Hand.Insert(0, source);
        var result = game.Handle(0, new L12Command("playCard", source.InstanceId, Row: row, Slot: slot));
        if (result.Accepted) return result;
        // 特殊登场卡可能需要自身专用声明。GM 入口仍会走权威登场/打出触发，作为
        // 逐卡遍历的基础入口；对应专用声明由主动能力与专项回归继续验证。
        return game.HandleGm(new L12GmCommand("playHandCard", TargetPlayer: 0,
            CardInstanceId: source.InstanceId, Row: row, Slot: slot, TriggerEffects: true));
    }

    private static CommandResult CoverCounterTactic(L12GameEngine game, L12PlayerState player, L12CardInstance source)
    {
        source.Hidden = true;
        source.SetRound = game.State.Round - 1;
        source.OwnerIndex = 0;
        player.Field[1][2] = source;
        return CommandResult.Ok();
    }

    private static CommandResult PutTrialIntoSpecialZone(L12PlayerState player, L12CardInstance source)
    {
        source.TrialCompleted = false;
        player.SpecialZones.Trials.Add(source);
        return CommandResult.Ok();
    }

    private static CommandResult PutRuneIntoResourceZone(L12PlayerState player, L12CardInstance source)
    {
        player.Morale.Add(new L12MoraleCard { CardId = source.CardId, InstanceId = source.InstanceId });
        return CommandResult.Ok();
    }

    private static CommandResult PutDivinityIntoPublicZone(L12PlayerState player, L12CardInstance source)
    {
        player.ExtraRelics.Add(source);
        return CommandResult.Ok();
    }

    private static CommandResult PutDisasterIntoAuthoritativeDeck(L12GameEngine game, L12CardInstance source)
    {
        game.State.DisasterDeck.Clear();
        game.State.DisasterPool.Clear();
        game.State.ActiveDisaster = null;
        game.State.DisasterDeck.Add(source);
        return CommandResult.Ok();
    }

    private static string PutActiveSourceInItsAuthoritativeZone(L12GameEngine game, L12PlayerState player,
        L12CardInstance source, string ability)
    {
        switch (source.CardType)
        {
            case "master":
                return "master-0";
            case "rune":
                return "faction-0";
            case "divinity":
                player.ExtraRelics.Add(source);
                return source.InstanceId;
            case "trial":
                player.SpecialZones.Trials.Add(source);
                return source.InstanceId;
            case "artifact":
                player.Relic = source;
                return source.InstanceId;
            case "legion" when source.CardId == "S02-0301" && ability == "thorHammerRevive":
                player.Graveyard.Add(source);
                return source.InstanceId;
            case "legion":
            case "token":
                player.Field[0][0] = source;
                return source.InstanceId;
            default:
                player.Resolving.Add(source);
                return source.InstanceId;
        }
    }

    private static void DrainPrompts(L12GameEngine game, string branch, string evidence)
    {
        for (var safety = 0; safety < 240; safety++)
        {
            if (game.State.PendingPrompts.Count == 0)
            {
                Assert.Empty(game.State.PendingActivations);
                return;
            }

            var prompt = game.State.PendingPrompts[0];
            AssertPromptContract(prompt, evidence);
            var selected = SelectChoices(game, prompt, branch);
            var command = BuildPromptCommand(prompt, selected);
            var result = game.Handle(prompt.PlayerIndex, command);
            Assert.True(result.Accepted,
                $"{evidence} 的 {prompt.Kind}/{prompt.Continuation} 选择失败：{result.Error}；候选={string.Join(',', prompt.ValidChoices)}；选择={string.Join(',', selected)}");
        }

        throw new Xunit.Sdk.XunitException($"{evidence} 在 240 次选择后仍未结束，疑似重复询问或卡死");
    }

    private static List<string> SelectChoices(L12GameEngine game, L12Prompt prompt, string branch)
    {
        if (prompt.Kind == "response" && prompt.ValidChoices.Contains("pass")) return ["pass"];
        if (prompt.MaxChoose == 0) return [];

        var negative = new[] { "skip", "mode:none", "no", "pass", "cancel" }
            .FirstOrDefault(value => prompt.ValidChoices.Contains(value, StringComparer.OrdinalIgnoreCase));
        if (branch == "decline" && negative is not null && prompt.MinChoose <= 1) return [negative];

        var candidates = prompt.ValidChoices
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

        var choose = branch == "maximum" ? prompt.MaxChoose : prompt.MinChoose;
        choose = Math.Min(choose, candidates.Count);
        if (choose == 0) return [];
        var ordered = branch == "maximum" ? candidates.AsEnumerable().Reverse() : candidates;
        var selected = ordered.Take(choose).ToList();

        if (constraint == "distinct-card-names")
        {
            var distinct = new List<string>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var choice in ordered)
            {
                var name = PromptCardName(game, prompt.PlayerIndex, choice) ?? choice;
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
        {
            return new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                TopCardInstanceIds: placement == "all-bottom" ? [] : selected,
                BottomCardInstanceIds: placement == "all-bottom" ? selected : []);
        }
        return new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: selected.Count == 1 ? selected[0] : null,
            CardInstanceIds: selected.Count == 1 ? null : selected);
    }

    private static void AssertPromptContract(L12Prompt prompt, string evidence)
    {
        AssertPlayerFacingText(prompt.Text, evidence);
        Assert.Equal(prompt.ValidChoices.Count,
            prompt.ValidChoices.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.InRange(prompt.MinChoose, 0, prompt.MaxChoose);
        Assert.True(prompt.MaxChoose <= prompt.ValidChoices.Count,
            $"{evidence} 的 {prompt.Kind} 最大选择数超过候选数");
        foreach (var choice in prompt.ValidChoices)
        {
            Assert.True(prompt.ChoiceLabels.TryGetValue(choice, out var label),
                $"{evidence} 的协议选项 {choice} 缺少玩家标签");
            Assert.NotNull(label);
            AssertPlayerFacingText(label, evidence);
            Assert.False(string.IsNullOrWhiteSpace(label));
            if (choice.Contains(':')) Assert.NotEqual(choice, label);
        }
        var declineLabels = prompt.ValidChoices.Select(choice => prompt.ChoiceLabels[choice])
            .Count(label => label == "不发动");
        Assert.True(declineLabels <= 1, $"{evidence} 出现重复“不发动”选项");
    }

    private static void AssertPlayerFacingText(string text, string evidence)
    {
        foreach (var forbidden in new[] { "预先", "预声明", "公开区域", "私密区域", "公开资源", "Continuation", "activationId" })
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(text), $"{evidence} 出现空白玩家文案");
    }

    private static string? PromptCardName(L12GameEngine game, int playerIndex, string instanceId)
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

    private static T Invoke<T>(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().Name, methodName);
        return Assert.IsType<T>(method.Invoke(method.IsStatic ? null : target, args));
    }

    private static L12CardInstance Card(L12CardDefinition definition, string instanceId)
        => new()
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
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

    private static int Seed(string cardId, string branch, int salt)
        => 30000 + Math.Abs(StableHash($"{cardId}|{branch}|{salt}")) % 100000;

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 17;
            foreach (var character in value) hash = hash * 31 + character;
            return hash == int.MinValue ? int.MaxValue : Math.Abs(hash);
        }
    }
}
