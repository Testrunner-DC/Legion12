namespace TwelveLegions.Server;

public sealed partial class L12RoomManager
{
    private IReadOnlyList<L12FrozenEffectPresentation> CaptureEffectPresentationSnapshot()
        => _platform?.CaptureEffectPresentationSnapshot(_catalog.AtomicEffects)
            ?? L12EffectPresentationSceneCatalog.Freeze(_catalog.AtomicEffects.All);
}
