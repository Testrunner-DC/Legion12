namespace TwelveLegions.Server;

/// <summary>
/// 由一个已完成效果生成的新互动。父效果必须先从堆叠移除，之后才可声明并建立
/// 新效果；这样不会把生成效果的目标选择误当成父效果的结算期 Prompt。
/// </summary>
public sealed partial class L12GameEngine
{
    private bool TryBeginPostResolutionGeneratedInteraction(L12StackItem completed)
    {
        if (completed.Data.GetValueOrDefault("postResolutionGenerated") == "ptolemy-repeat"
            && completed.Data.GetValueOrDefault("repeatCardId") is { Length: > 0 } repeatedCardId)
        {
            BeginPtolemyRepeatedTacticEffect(completed.Controller, repeatedCardId);
            return true;
        }
        if (completed.Data.GetValueOrDefault("postResolutionGenerated") == "faith-zealot-master")
        {
            BeginFaithZealotMasterChoice(completed);
            return true;
        }
        return false;
    }

    private void BeginPtolemyRepeatedTacticEffect(int controller, string repeatedCardId)
    {
        if (!_catalog.Cards.TryGetValue(repeatedCardId, out var definition)
            || definition.CardType != "tactic" || IsCounterTactic(repeatedCardId))
        {
            AddEvent("effect-cancelled", controller, "托勒密十三世没有可再次发动的主动战术效果");
            ResumeAfterPostResolutionGeneratedInteraction();
            return;
        }
        var source = CreateCard(repeatedCardId, $"repeat-effect-{++State.StackSequence}");
        if (L12CompositeEffectPlans.HasHandPlayPlan(repeatedCardId))
        {
            var result = BeginRepeatedCompositeEffectDeclaration(controller, source);
            if (!result.Accepted)
            {
                AddEvent("effect-cancelled", controller,
                    $"〈{source.Name}〉的重复效果没有足够的合法模式或目标；未建立效果", source);
                ResumeAfterPostResolutionGeneratedInteraction();
            }
            return;
        }
        if (L12StructuredCardRules.RequiresPreStackHandPlayTarget(repeatedCardId))
        {
            var targets = PublicLegions(State.Players[1 - controller]).Select(card => card.InstanceId).ToArray();
            if (targets.Length == 0)
            {
                AddEvent("effect-cancelled", controller, $"〈{source.Name}〉的重复效果没有合法目标", source);
                ResumeAfterPostResolutionGeneratedInteraction();
                return;
            }
            _ = BeginPendingActivationSequence(controller, source, "repeated-tactic-effect",
            [
                new L12ActivationSelectionStep
                {
                    Kind = "enemy-legion",
                    Text = $"托勒密十三世：为再次发动的〈{source.Name}〉效果声明目标",
                    ValidChoices = targets.ToList(),
                    DeclarationKey = "target",
                    CancellationPolicy = L12ActivationCancellationPolicy.NotAllowed,
                }
            ], triggerCandidateId: null, playCardInstanceId: source.InstanceId,
                responseTargetStackItemId: null);
            return;
        }
        PushRepeatedTacticEffect(controller, source, null);
    }

    private void CompleteRepeatedSimpleTacticEffect(L12PendingActivation activation)
    {
        var source = CreateCard(activation.SourceCardId, activation.SourceInstanceId);
        var target = activation.DeclaredValues.GetValueOrDefault("target", []).SingleOrDefault();
        if (L12StructuredCardRules.RequiresPreStackHandPlayTarget(source.CardId)
            && DeclaredEnemyTarget(activation.Controller, target) is null)
        {
            AddEvent("effect-cancelled", activation.Controller,
                $"〈{source.Name}〉的重复效果目标失效；未建立效果", source);
            ResumeAfterPostResolutionGeneratedInteraction();
            return;
        }
        PushRepeatedTacticEffect(activation.Controller, source, target);
    }

    private void PushRepeatedTacticEffect(int controller, L12CardInstance source, string? target)
    {
        var data = new Dictionary<string, string>
        {
            ["repeatedEffectOnly"] = "true",
        };
        if (target is not null) data["target"] = target;
        PushEffect(controller, source, "play", $"托勒密十三世再次发动的〈{source.Name}〉效果",
            target is null ? null : [target], data);
    }

    private void BeginFaithZealotMasterChoice(L12StackItem completed)
    {
        var player = State.Players[completed.Controller];
        var master = CreateCard(player.MasterId, $"master-{completed.Controller}");
        var abilities = GetAbilities(player.MasterId)
            .Where(view => GetActiveAbilityMoraleCost(master, view.Id) > 0)
            .ToArray();
        if (abilities.Length == 0)
        {
            ResumeAfterPostResolutionGeneratedInteraction();
            return;
        }
        var choices = abilities.Select(view => view.Id).Append("skip").ToArray();
        var data = new Dictionary<string, string>
        {
            ["action"] = "s2-faith-zealot",
            ["sourceInstanceId"] = completed.SourceInstanceId,
            ["skip"] = "不发动",
        };
        foreach (var ability in abilities) data[ability.Id] = ability.Label;
        CreatePrompt(completed.Controller, "option",
            "信仰狂热者已结算：选择1个需要消耗士气的主宰效果，无视全部消耗发动且不计入使用次数",
            choices, 1, 1, "faith-zealot-post-resolution", isPrivate: false, data: data);
    }

    private void ResolveFaithZealotPostResolutionChoice(L12Prompt prompt, string ability)
    {
        if (ability == "skip")
        {
            ResumeAfterPostResolutionGeneratedInteraction();
            return;
        }
        var player = State.Players[prompt.PlayerIndex];
        var master = CreateCard(player.MasterId, $"master-{prompt.PlayerIndex}");
        if (!GetAbilities(player.MasterId).Any(view => view.Id.Equals(ability, StringComparison.OrdinalIgnoreCase))
            || GetActiveAbilityMoraleCost(master, ability) <= 0)
        {
            AddEvent("effect-failed", prompt.PlayerIndex,
                "〈信仰狂热者〉在结算后无法建立所选主宰效果");
            ResumeAfterPostResolutionGeneratedInteraction();
            return;
        }
        State.FreeMasterActivation = new L12FreeMasterActivation
        {
            Controller = prompt.PlayerIndex,
            Ability = ability,
            SourceInstanceId = prompt.Data.GetValueOrDefault("sourceInstanceId") ?? string.Empty,
        };
        var result = BeginActiveAbility(prompt.PlayerIndex,
            new L12Command("activateAbility", CardInstanceId: master.InstanceId, Ability: ability));
        if (result.Accepted) return;
        State.FreeMasterActivation = null;
        AddEvent("effect-failed", prompt.PlayerIndex,
            $"〈信仰狂热者〉无法发动所选主宰效果：{result.Error}");
        ResumeAfterPostResolutionGeneratedInteraction();
    }

    private void ResumeAfterPostResolutionGeneratedInteraction()
    {
        if (State.PendingPrompts.Count > 0 || State.PendingActivations.Count > 0
            || State.ResponseWindow is not null) return;
        if (State.EffectStack.Count > 0)
        {
            BeginStackItem(State.EffectStack[^1]);
            return;
        }
        State.IsResolvingStack = false;
        AfterStackSettled();
    }
}
