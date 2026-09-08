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
        var frozen = string.IsNullOrWhiteSpace(sceneId)
            ? null
            : State.EffectPresentationSnapshot?.FirstOrDefault(scene =>
                string.Equals(scene.SceneId, sceneId, StringComparison.Ordinal));
        AddEventCore(type, playerIndex, text, frozen?.Text, cards);
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
        var candidateAbilities = card.Abilities
            .Where(ability => string.Equals(ability.Trigger, trigger, StringComparison.OrdinalIgnoreCase))
            .ToArray();
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
            .Where(scene => scene.EventType == "effect")
            .ToArray();

        if (candidates.Length == 1) return candidates[0].SceneId;
        var normalized = L12EffectPresentationText.Normalize(resolvedDefault);
        var exact = candidates.Where(scene => string.Equals(scene.DefaultText, normalized,
            StringComparison.Ordinal)).ToArray();
        return exact.Length == 1 ? exact[0].SceneId : null;
    }
}
