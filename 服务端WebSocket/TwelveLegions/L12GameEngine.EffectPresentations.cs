namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private void AddPresentationEvent(string type, int? playerIndex, string text,
        string producerCardId, string sceneKey, IReadOnlyDictionary<string, string>? values,
        params L12CardInstance[] cards)
    {
        var effectText = ResolveFrozenPresentation(producerCardId, sceneKey, values);
        AddEventCore(type, playerIndex, text, effectText, cards);
    }

    private void AddPresentationEvent(string type, int? playerIndex, string text,
        string producerCardId, string sceneKey, params L12CardInstance[] cards)
    {
        IReadOnlyDictionary<string, string>? values = cards.Length == 0
            ? null
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cardName"] = cards[0].Name,
            };
        AddPresentationEvent(type, playerIndex, text, producerCardId, sceneKey, values, cards);
    }

    private void AddPresentationEventById(string type, int? playerIndex, string text,
        string? sceneId, params L12CardInstance[] cards)
    {
        var configured = FindEffectPresentationScene(cards.FirstOrDefault()?.CardId, sceneId);
        var frozen = string.IsNullOrWhiteSpace(sceneId)
            ? null
            : State.EffectPresentationSnapshot?.FirstOrDefault(scene =>
                string.Equals(scene.SceneId, sceneId, StringComparison.Ordinal));
        // Text is the established audit/event narration supplied by the caller.  Variant
        // presentation copy belongs in EffectText so adding a branch never rewrites the
        // historical event shape.  Legacy scenes keep their previous frozen-only behavior;
        // only the new Flow scenes contribute a default EffectText without an override.
        var effectText = frozen?.Text
            ?? (configured?.Flow is not null ? configured.DefaultText : null);
        if (configured is { EventType: "effect", Flow: null }
            && _catalog.AtomicEffects.Find(configured.CardId)?.Abilities.Any(ability =>
                ability.Presentations.Any(scene => scene.SceneId == configured.SceneId)
                && ability.Presentations.Any(scene => scene.EventType == "effect" && scene.Flow is not null)) == true
            && type is "effect-trigger" or "effect-activation" or "effect-response")
        {
            // Keep the historical declaration/audit event, but only the actual segment or
            // chosen branch may enter the card animation queue.
            type = "effect-announced";
        }
        AddEventCore(type, playerIndex, text, effectText, cards);
    }

    internal string? ResolveFrozenPresentation(string cardId, string sceneKey,
        IReadOnlyDictionary<string, string>? values = null)
    {
        var frozen = State.EffectPresentationSnapshot?.Where(scene =>
                string.Equals(scene.CardId, cardId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(scene.SceneKey, sceneKey, StringComparison.Ordinal))
            .ToArray();
        if (frozen is not { Length: 1 }) return null;
        return L12EffectPresentationText.Render(frozen[0].Text,
            values ?? new Dictionary<string, string>(StringComparer.Ordinal));
    }

    internal string? ResolveEffectPresentationSceneId(L12CardInstance source, string trigger,
        IReadOnlyDictionary<string, string>? data, string resolvedDefault)
    {
        var card = _catalog.AtomicEffects.Find(source.CardId);
        if (card is null) return null;
        var triggerAbilities = card.Abilities
            .Where(ability => string.Equals(ability.Trigger, trigger, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // CompositeFirstSegmentData/next-segment data is complete before the stack item is
        // published. Select against that final public declaration so a stale pre-declaration
        // scene can never leak into the event or frozen replay.
        if (TryResolveSegmentScene(card, trigger, data, out var segmentSceneId, out var segmentAmbiguous))
            return segmentSceneId;
        if (segmentAmbiguous)
            System.Diagnostics.Trace.TraceError(
                $"Effect presentation branch ambiguity failed closed for {source.CardId}/{trigger}.");

        var candidateAbilities = triggerAbilities;
        var declaredAbilityId = data?.GetValueOrDefault("ability");
        if (!string.IsNullOrWhiteSpace(declaredAbilityId))
        {
            var direct = candidateAbilities.FirstOrDefault(ability =>
                string.Equals(ability.AbilityId, declaredAbilityId, StringComparison.OrdinalIgnoreCase));
            if (direct is not null) candidateAbilities = [direct];
            else
            {
                var declared = GetAbilities(source.CardId).FirstOrDefault(ability =>
                    string.Equals(ability.Id, declaredAbilityId, StringComparison.OrdinalIgnoreCase));
                if (declared is not null)
                {
                    var declaredText = L12EffectPresentationText.Normalize(declared.Label);
                    var labeled = candidateAbilities.Where(ability => ability.Presentations.Any(scene =>
                        scene.EventType == "effect" && string.Equals(scene.DefaultText, declaredText,
                            StringComparison.Ordinal))).ToArray();
                    if (labeled.Length == 1) candidateAbilities = labeled;
                }
            }
        }
        var candidates = candidateAbilities.SelectMany(ability => ability.Presentations)
            .Where(scene => scene.EventType == "effect" && scene.Flow is null)
            .ToArray();

        if (candidates.Length == 1) return candidates[0].SceneId;
        var normalized = L12EffectPresentationText.Normalize(resolvedDefault);
        var exact = candidates.Where(scene => string.Equals(scene.DefaultText, normalized,
            StringComparison.Ordinal)).ToArray();
        return exact.Length == 1 ? exact[0].SceneId : null;
    }

    private void RefreshDeclaredPresentationSceneId(L12TriggerCandidate candidate,
        L12CardInstance source)
    {
        var fallback = candidate.Data.GetValueOrDefault("triggerEffectText")
            ?? source.EffectText ?? candidate.SourceName;
        var sceneId = ResolveEffectPresentationSceneId(source, candidate.Trigger,
            candidate.Data, fallback);
        if (string.IsNullOrWhiteSpace(sceneId)) candidate.Data.Remove("presentationSceneId");
        else candidate.Data["presentationSceneId"] = sceneId;
    }

    private static void DeclarePresentationBranch(Dictionary<string, string> data,
        string flow, string declarationKey, string? publicChoice)
    {
        if (string.IsNullOrWhiteSpace(publicChoice)) return;
        data["presentationFlow"] = flow;
        data[$"declared:{declarationKey}"] = publicChoice;
    }

    private bool TryResolveSegmentScene(L12AtomicCardEffect card, string trigger,
        IReadOnlyDictionary<string, string>? data, out string? sceneId, out bool ambiguous)
    {
        sceneId = null;
        ambiguous = false;
        if (data is null) return false;
        var flow = data.GetValueOrDefault("presentationFlow")
            ?? data.GetValueOrDefault("atomicFlow");
        if (string.IsNullOrWhiteSpace(flow)) return false;
        var hasSegmentIndex = data.TryGetValue("compositeSegment", out var rawIndex);
        var zeroBasedIndex = -1;
        if (hasSegmentIndex && (!int.TryParse(rawIndex, out zeroBasedIndex) || zeroBasedIndex < 0))
            return false;

        IEnumerable<L12AtomicAbility> scopedAbilities = card.Abilities;
        var compositePlan = data.GetValueOrDefault("compositePlan");
        var planPrefix = string.IsNullOrWhiteSpace(compositePlan)
            ? null
            : L12EffectPresentationVariants.SceneKeyPrefix(compositePlan);
        if (planPrefix is null)
        {
            var triggerScoped = card.Abilities.Where(ability => ability.Trigger.Equals(trigger,
                StringComparison.OrdinalIgnoreCase)).ToArray();
            if (triggerScoped.Length > 0) scopedAbilities = triggerScoped;
        }

        var candidates = scopedAbilities.SelectMany(ability => ability.Presentations)
            .Where(scene => scene.EventType == "effect"
                && string.Equals(scene.Flow, flow, StringComparison.OrdinalIgnoreCase)
                && (hasSegmentIndex ? scene.SegmentIndex == zeroBasedIndex + 1 : scene.SegmentIndex is null))
            .Where(scene => planPrefix is null || scene.Trigger.StartsWith(planPrefix,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length == 0) return false;

        var specific = candidates.Where(scene => scene.RequiredChoices is { Count: > 0 }
            && RequiredChoicesMatch(scene.RequiredChoices, data)).ToArray();
        if (specific.Length == 1)
        {
            sceneId = specific[0].SceneId;
            return true;
        }
        if (specific.Length > 1)
        {
            ambiguous = true;
            return false;
        }

        var fallback = candidates.Where(scene => scene.RequiredChoices is not { Count: > 0 }).ToArray();
        if (fallback.Length == 1)
        {
            sceneId = fallback[0].SceneId;
            return true;
        }
        if (fallback.Length > 1) ambiguous = true;
        return false;
    }

    private static bool RequiredChoicesMatch(IReadOnlyDictionary<string, string> required,
        IReadOnlyDictionary<string, string> data)
        => required.All(pair => data.GetValueOrDefault($"declared:{pair.Key}", string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(pair.Value, StringComparer.OrdinalIgnoreCase));

    private L12EffectPresentationScene? FindEffectPresentationScene(string? cardId, string? sceneId)
    {
        if (string.IsNullOrWhiteSpace(cardId) || string.IsNullOrWhiteSpace(sceneId)) return null;
        var card = _catalog.AtomicEffects.Find(cardId);
        if (card is null) return null;
        var matches = card.Abilities.SelectMany(ability => ability.Presentations)
            .Where(scene => string.Equals(scene.SceneId, sceneId, StringComparison.Ordinal))
            .Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
