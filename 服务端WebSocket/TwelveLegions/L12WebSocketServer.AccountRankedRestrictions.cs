using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace TwelveLegions.Server;

public sealed partial class L12WebSocketServer
{
    private sealed record AccountRankedRestrictionRequest(int? RestrictionDays, string? Reason,
        string? IdempotencyKey, long? ExpectedVersion, bool DryRun = false);

    private void MapAccountRankedRestrictionEndpoints()
    {
        var app = _app ?? throw new InvalidOperationException("WebSocket server app is not initialized");
        const string route = "/api/admin/accounts/{id}/ranked-restriction";
        app.MapGet(route, (HttpRequest request, string id) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminAccountsRead, out var auth, out var failure))
                return failure;
            try { return Results.Ok(_platform.AccountRankedRestriction(auth.Account, id)); }
            catch (KeyNotFoundException error)
            { return ApiError(request, "account_not_found", error.Message, 404); }
        });
        IResult Mutate(HttpRequest request, string id, AccountRankedRestrictionRequest body, bool clear)
        {
            const L12Permission permission = L12Permission.AdminAccountStatusWrite;
            if (!TryAuthorize(request, permission, out var auth, out var failure)) return failure;
            if (!TrySecurityCommandOptions(request, auth.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expectedVersion, out failure))
                return failure;
            var payload = new L12AccountRankedRestrictionPayload(id, body.RestrictionDays,
                body.Reason ?? string.Empty, clear);
            // A dedicated route and command type cannot fall back to account disable
            // or an integrity decision on an older service. Use the existing global
            // platform-version fallback, never the login PermissionVersion.
            var command = CommandEnvelope(request, auth.Account, permission,
                clear ? "ranked.account-restriction.clear" : "ranked.account-restriction.set",
                $"account:{id}", payload, key, expectedVersion, body.DryRun, body.Reason);
            L12AdminCommandResult<L12AccountRankedRestrictionOperation> Execute(bool apply)
            {
                try
                {
                    var result = _platform.SetAccountRankedRestriction(auth.Account, payload,
                        command.AuditContext, apply);
                    return L12AdminCommandResult<L12AccountRankedRestrictionOperation>.Ok(result,
                        apply ? "手工排位禁赛已更新；登录和进行中的对局不受影响" : "干运行验证通过");
                }
                catch (ArgumentException error)
                { return L12AdminCommandResult<L12AccountRankedRestrictionOperation>.Fail("ranked_restriction_invalid", error.Message, 400); }
                catch (KeyNotFoundException error)
                { return L12AdminCommandResult<L12AccountRankedRestrictionOperation>.Fail("account_not_found", error.Message, 404); }
                catch (UnauthorizedAccessException error)
                { return L12AdminCommandResult<L12AccountRankedRestrictionOperation>.Fail("ranked_restriction_forbidden", error.Message, 403); }
            }
            var outcome = _adminCommands.Execute(command, permission, _ => Execute(true), _ => Execute(false));
            return AdminCommandResponse(request, command, outcome);
        }
        app.MapPut(route, (HttpRequest request, string id, AccountRankedRestrictionRequest body)
            => Mutate(request, id, body, clear: false));
        app.MapPost(route + "/clear", (HttpRequest request, string id, AccountRankedRestrictionRequest body)
            => Mutate(request, id, body, clear: true));
    }
}
