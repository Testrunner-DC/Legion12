namespace TwelveLegions.Server;

public sealed record L12EffectPresentationOverrideView(
    string CardId,
    string AbilityId,
    string SceneId,
    string Text,
    string UpdatedBy,
    DateTimeOffset UpdatedAt);

public sealed partial class L12PlatformStore
{
    public L12AtomicCardEffect ApplyEffectPresentationOverrides(L12AtomicCardEffect effect)
    {
        lock (_gate) return ApplyEffectPresentationOverridesLocked(effect);
    }

    public L12AtomicEffectPage ApplyEffectPresentationOverrides(L12AtomicEffectPage page)
    {
        lock (_gate)
            return page with { Items = page.Items.Select(ApplyEffectPresentationOverridesLocked).ToArray() };
    }

    public IReadOnlyList<L12FrozenEffectPresentation> CaptureEffectPresentationSnapshot(
        L12AtomicEffectCatalog catalog)
    {
        lock (_gate)
        {
            return L12EffectPresentationSceneCatalog.Freeze(
                catalog.All.Select(ApplyEffectPresentationOverridesLocked));
        }
    }

    public L12EffectPresentationOverrideView SaveEffectPresentationOverride(L12AccountView actor,
        L12EffectPresentationScene scene, string text, L12AdminAuditContext? context = null)
    {
        var normalized = L12EffectPresentationText.Validate(text, scene.Placeholders);
        lock (_gate)
        {
            var row = _data.EffectPresentationOverrides.FirstOrDefault(item =>
                string.Equals(item.SceneId, scene.SceneId, StringComparison.Ordinal));
            var previous = row?.Text;
            if (row is null)
            {
                row = new EffectPresentationOverrideRow
                {
                    CardId = scene.CardId,
                    AbilityId = scene.AbilityId,
                    SceneId = scene.SceneId,
                };
                _data.EffectPresentationOverrides.Add(row);
            }
            row.Text = normalized;
            row.UpdatedBy = actor.Username;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            AddAdminAudit(actor, "effect-presentation", "save", scene.SceneId,
                previous ?? scene.DefaultText, normalized, null, context);
            Save();
            return ToView(row);
        }
    }

    public bool RestoreEffectPresentationDefault(L12AccountView actor, L12EffectPresentationScene scene,
        L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var row = _data.EffectPresentationOverrides.FirstOrDefault(item =>
                string.Equals(item.SceneId, scene.SceneId, StringComparison.Ordinal));
            if (row is null) return false;
            _data.EffectPresentationOverrides.Remove(row);
            AddAdminAudit(actor, "effect-presentation", "restore-default", scene.SceneId,
                row.Text, scene.DefaultText, null, context);
            Save();
            return true;
        }
    }

    private L12AtomicCardEffect ApplyEffectPresentationOverridesLocked(L12AtomicCardEffect effect)
    {
        var byScene = _data.EffectPresentationOverrides
            .Where(row => string.Equals(row.CardId, effect.CardId, StringComparison.OrdinalIgnoreCase))
            .GroupBy(row => row.SceneId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => group.OrderByDescending(row => row.UpdatedAt).First(), StringComparer.Ordinal);
        if (byScene.Count == 0) return effect;
        var abilities = effect.Abilities.Select(ability => ability with
        {
            Presentations = ability.Presentations.Select(scene => byScene.TryGetValue(scene.SceneId, out var row)
                ? scene with { OverrideText = row.Text }
                : scene).ToArray(),
        }).ToArray();
        return effect with { Abilities = abilities };
    }

    private static L12EffectPresentationOverrideView ToView(EffectPresentationOverrideRow row)
        => new(row.CardId, row.AbilityId, row.SceneId, row.Text, row.UpdatedBy, row.UpdatedAt);
}
