using TwelveLegions.Server;

namespace TwelveLegions.Platform.Tests;

// Existing pairing/privacy/deck fixtures intentionally include the organiser.
// Make that setup an explicit real registration, not a product side effect.
// Creator-policy regressions call CreateTournament directly and never use this helper.
internal static class TournamentTestSetup
{
    internal static L12TournamentView CreateWithRegisteredOrganizer(L12PlatformStore store,
        L12AccountView organizer, L12TournamentCreatePayload payload,
        L12AdminAuditContext context, bool apply)
    {
        if (!apply) throw new InvalidOperationException("Fixture registration requires a real isolated test tournament.");
        var created = store.CreateTournament(organizer, payload, context, true);
        return store.RegisterTournament(organizer, created.Id, new(), created.Version, context, true);
    }
}
