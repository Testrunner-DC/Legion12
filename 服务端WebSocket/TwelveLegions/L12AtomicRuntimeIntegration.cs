namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private bool TryResolveVerifiedAtomicProgram(L12StackItem item)
    {
        var program = L12VerifiedAtomicPrograms.Find(item.SourceCardId, item.Trigger);
        if (program is null) return false;
        var source = FindSource(item);
        if (source is null)
        {
            FinishStackItem(item);
            return true;
        }

        var controller = State.Players[item.Controller];
        var opponent = State.Players[1 - item.Controller];
        for (var atomIndex = item.Step; atomIndex < program.Atoms.Count; atomIndex++)
        {
            var atom = program.Atoms[atomIndex];
            item.Step = atomIndex + 1;
            switch (atom.Kind)
            {
                case L12AtomKinds.Trigger:
                    break;
                case L12AtomKinds.Condition:
                    if (!CheckVerifiedAtomicCondition(atom.Parameters.GetValueOrDefault("expression"), item, source, controller, opponent))
                    {
                        FinishStackItem(item);
                        return true;
                    }
                    break;
                case L12AtomKinds.Optional:
                    if (PublicTriggerDeclared(item, "mode") == "mode:use")
                        break;
                    CreatePrompt(item.Controller, "optional", atom.Parameters.GetValueOrDefault("prompt") ?? atom.Label,
                        ["yes", "no"], 1, 1, "card-effect", item.StackItemId,
                        data: new Dictionary<string, string>
                        {
                            ["action"] = "verified-atomic-optional",
                            ["yes"] = atom.Parameters.GetValueOrDefault("yes") ?? "发动",
                            ["no"] = atom.Parameters.GetValueOrDefault("no") ?? "不发动",
                        });
                    return true;
                case L12AtomKinds.SetState:
                    if (atom.Parameters.GetValueOrDefault("key") == "controller.nextLegionChargeMaxCost")
                        controller.NextLegionChargeMaxCost = AtomicInt(atom, "value");
                    else if (atom.Parameters.GetValueOrDefault("key") == "opponent.nextActiveTacticSurcharge"
                        && atom.Parameters.GetValueOrDefault("operation") == "increment")
                        opponent.NextActiveTacticSurcharge += AtomicInt(atom, "value");
                    else if (atom.Parameters.GetValueOrDefault("key") == "controller.freeTacticCount"
                        && atom.Parameters.GetValueOrDefault("operation") == "increment")
                        controller.FreeTacticCount += AtomicInt(atom, "value");
                    else if (atom.Parameters.GetValueOrDefault("key") == "source.hidden")
                        source.Hidden = bool.Parse(atom.Parameters.GetValueOrDefault("value") ?? "false");
                    else if (atom.Parameters.GetValueOrDefault("key") == "source.canAttackLegionsOnSummonUntilTurn"
                        && atom.Parameters.GetValueOrDefault("value") == "current-turn")
                        source.CanAttackLegionsOnSummonUntilTurn = State.TurnSerial;
                    else if (atom.Parameters.GetValueOrDefault("key") == "source.canAttackMasterOnSummonUntilTurn"
                        && atom.Parameters.GetValueOrDefault("value") == "current-turn")
                        source.CanAttackMasterOnSummonUntilTurn = State.TurnSerial;
                    else
                        throw new InvalidOperationException($"Unsupported verified atomic state key: {atom.Parameters.GetValueOrDefault("key")}");
                    EmitVerifiedAtomicEvent(atom, item.Controller, source);
                    break;
                case L12AtomKinds.Keyword:
                    if (atom.Parameters.GetValueOrDefault("keyword") == "charge") source.HasCharge = true;
                    else if (atom.Parameters.GetValueOrDefault("keyword") == "strong-attack") GrantStrongAttack(source);
                    else throw new InvalidOperationException($"Unsupported verified atomic keyword: {atom.Parameters.GetValueOrDefault("keyword")}");
                    EmitVerifiedAtomicEvent(atom, item.Controller, source);
                    break;
                case L12AtomKinds.AddMorale:
                {
                    var added = AddMorale(controller, AtomicInt(atom, "amount"),
                        tapped: atom.Parameters.GetValueOrDefault("tapped") == "true");
                    EmitVerifiedAtomicEvent(atom, item.Controller, source, added);
                    break;
                }
                case L12AtomKinds.GainRune:
                {
                    var before = controller.SpecialZones.Runes;
                    L12S2ZoneOps.GainRunes(controller, AtomicInt(atom, "amount"));
                    EmitVerifiedAtomicEvent(atom, item.Controller, source, controller.SpecialZones.Runes - before);
                    break;
                }
                case L12AtomKinds.AdvanceTrial:
                    AdvanceTrial(item.Controller, AtomicInt(atom, "amount"), source);
                    break;
                case L12AtomKinds.Draw:
                {
                    var amount = AtomicInt(atom, "amount");
                    var succeeded = Draw(controller, amount);
                    if (!succeeded)
                    {
                        SetWinner(1 - item.Controller, atom.Parameters.GetValueOrDefault("emptyLossReason") ?? $"{source.Name}效果抽牌时牌库为空");
                    }
                    if (succeeded) EmitVerifiedAtomicEvent(atom, item.Controller, source, amount);
                    break;
                }
                case L12AtomKinds.HealMaster:
                    var healTargets = atom.Parameters.GetValueOrDefault("target") == "both"
                        ? new[] { 0, 1 }
                        : new[] { item.Controller };
                    foreach (var target in healTargets)
                        HealMaster(target, AtomicInt(atom, "amount"), atom.Parameters.GetValueOrDefault("reason") ?? source.Name,
                            legionEffect: source.CardType == "legion");
                    break;
                case L12AtomKinds.DamageMaster when atom.Parameters.GetValueOrDefault("target") == "both"
                    && atom.Parameters.GetValueOrDefault("lethal") == "false":
                    DamageMasterNonLethal(0, AtomicInt(atom, "amount"), atom.Parameters.GetValueOrDefault("reason") ?? source.Name,
                        neutralSource: atom.Parameters.GetValueOrDefault("neutralSource") == "true");
                    DamageMasterNonLethal(1, AtomicInt(atom, "amount"), atom.Parameters.GetValueOrDefault("reason") ?? source.Name,
                        neutralSource: atom.Parameters.GetValueOrDefault("neutralSource") == "true");
                    break;
                case L12AtomKinds.DamageMaster when atom.Parameters.GetValueOrDefault("target") == "opponent":
                    DamageMaster(1 - item.Controller, AtomicInt(atom, "amount"), atom.Parameters.GetValueOrDefault("reason") ?? source.Name,
                        sourcePlayer: item.Controller);
                    break;
                case L12AtomKinds.ModifyTroops when atom.Parameters.GetValueOrDefault("operation") == "set":
                    source.Troops = AtomicInt(atom, "value");
                    EmitVerifiedAtomicEvent(atom, item.Controller, source);
                    break;
                case L12AtomKinds.CompositeFlow:
                    item.Data["atomicFlow"] = atom.Parameters.GetValueOrDefault("flow") ?? source.Name;
                    ResolveStructuredCompositeFlow(item);
                    return true;
                default:
                    throw new InvalidOperationException($"Verified atomic program {program.CardId}/{program.Trigger} contains unsupported atom {atom.Kind}.");
            }
        }
        FinishStackItem(item);
        return true;
    }

    private bool CheckVerifiedAtomicCondition(string? expression, L12StackItem item, L12CardInstance source,
        L12PlayerState controller, L12PlayerState opponent)
        => expression switch
        {
            "controller.hand<=5" => controller.Hand.Count <= 5,
            "controller.hand<=4" => controller.Hand.Count <= 4,
            "controller.hand<=opponent.hand" => controller.Hand.Count <= opponent.Hand.Count,
            "controller.morale<=7" => controller.Morale.Count <= 7,
            "controller.hp<=opponent.hp" => controller.Hp <= opponent.Hp,
            "controller.hp<=7" => controller.Hp <= 7,
            "controller.hp<=6" => controller.Hp <= 6,
            "opponent.hp>controller.hp" => opponent.Hp > controller.Hp,
            "controller.front.other-legions=0" => controller.Field[0].All(candidate => candidate is null
                || candidate.InstanceId == source.InstanceId || !IsFieldLegion(candidate)),
            "source.row=back" => FindOnField(controller, source.InstanceId, out var row, out _) is not null && row == 1,
            "source.hidden=true" => source.Hidden,
            "item.killed=true" => item.Data.GetValueOrDefault("killed") == "true",
            _ => throw new InvalidOperationException($"Unsupported verified atomic condition: {expression}"),
        };

    private void ContinueVerifiedAtomicOptional(L12StackItem item, string choice)
    {
        if (!choice.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            FinishStackItem(item);
            return;
        }
        TryResolveVerifiedAtomicProgram(item);
    }

    private static int AtomicInt(L12EffectAtom atom, string key)
        => int.TryParse(atom.Parameters.GetValueOrDefault(key), out var value)
            ? value
            : throw new InvalidOperationException($"Atomic parameter {key} is not an integer for {atom.Kind}.");

    private void EmitVerifiedAtomicEvent(L12EffectAtom atom, int playerIndex, L12CardInstance source, int value = 0)
    {
        if (!atom.Parameters.TryGetValue("event", out var message) || string.IsNullOrWhiteSpace(message)) return;
        AddEvent(atom.Parameters.GetValueOrDefault("eventType") ?? "effect", playerIndex,
            message.Replace("{source}", source.Name).Replace("{value}", value.ToString()), source);
    }
}
