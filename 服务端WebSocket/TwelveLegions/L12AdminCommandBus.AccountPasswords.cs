namespace TwelveLegions.Server;

public sealed partial class L12AdminCommandBus
{
    // Explicitly authorized two-entry adapter. Generic Execute and its ordering remain unchanged.
    // The bearer and prepared credential live only on this stack, never in a command or audit row.
    internal L12AdminCommandResult<L12AdminPasswordResetView> ExecuteAccountPasswordReset(
        L12AdminCommandEnvelope<AccountPasswordResetCommandPayload> command,
        string? authorization, string sessionId,
        Func<L12AdminCommandEnvelope<AccountPasswordResetCommandPayload>, L12PlatformStore.PreparedAdminPassword?,
            L12AdminCommandResult<L12AdminPasswordResetView>> apply,
        Func<L12AdminCommandEnvelope<AccountPasswordResetCommandPayload>, L12AdminCommandResult<L12AdminPasswordResetView>> dryRun)
        => ExecuteAccountPassword(command, authorization, sessionId, "account.password-admin-reset", "reset",
            command.Payload.AccountId, command.Payload.Reason, apply, dryRun);

    internal L12AdminCommandResult<L12AccountDeletionView> ExecuteAccountDeletion(
        L12AdminCommandEnvelope<AccountDeletionCommandPayload> command,
        string? authorization, string sessionId,
        Func<L12AdminCommandEnvelope<AccountDeletionCommandPayload>, L12PlatformStore.PreparedAdminPassword?,
            L12AdminCommandResult<L12AccountDeletionView>> apply,
        Func<L12AdminCommandEnvelope<AccountDeletionCommandPayload>, L12AdminCommandResult<L12AccountDeletionView>> dryRun)
        => ExecuteAccountPassword(command, authorization, sessionId, "account.logical-delete", "delete",
            command.Payload.AccountId, command.Payload.Reason, apply, dryRun);

    private L12AdminCommandResult<T> ExecuteAccountPassword<TPayload, T>(
        L12AdminCommandEnvelope<TPayload> command, string? authorization, string sessionId,
        string requiredType, string action, string accountId, string reason,
        Func<L12AdminCommandEnvelope<TPayload>, L12PlatformStore.PreparedAdminPassword?, L12AdminCommandResult<T>> apply,
        Func<L12AdminCommandEnvelope<TPayload>, L12AdminCommandResult<T>> dryRun)
    {
        if (command.Type != requiredType || command.Scope != $"account:{accountId}")
            throw new InvalidOperationException("Account credential command binding mismatch.");
        _platform.RequirePasswordWorkOutsideTransaction();
        const L12Permission permission = L12Permission.AdminAccountStatusWrite;
        var actorToPrepare = _platform.ExecuteAdminTransaction(() =>
        {
            var fresh = _platform.AuthenticateSession(authorization);
            if (!IsSameSession(fresh) || !IsAuthorized(fresh!.Account) || command.DryRun) return null;
            var key = NormalizeIdempotencyKey(command.IdempotencyKey);
            if (command.IdempotencyKey is not null && key is null) return null;
            if (key is not null && _platform.FindAdminCommand(fresh.Account.Id, key) is not null) return null;
            return HasVersionConflict(command) ? null : fresh.Account;
        });

        L12PlatformStore.PreparedAdminPassword? prepared = null;
        L12AdminCommandResult<T>? preparationFailure = null;
        if (actorToPrepare is not null)
        {
            try
            {
                prepared = _platform.PrepareAdminPassword(actorToPrepare, accountId, reason, action, command.CommandId);
            }
            catch (L12SecurityPolicyException error)
            {
                preparationFailure = L12AdminCommandResult<T>.Fail(error.Code, error.Message,
                    error.Code == "security_reason_required" ? StatusCodes.Status400BadRequest : StatusCodes.Status403Forbidden);
            }
            catch (KeyNotFoundException error)
            {
                preparationFailure = L12AdminCommandResult<T>.Fail("account_not_found", error.Message, StatusCodes.Status404NotFound);
            }
            // CPU admission/storage errors propagate without creating a requested command or audit.
        }

        // Always reauthenticate under the final gate, including replay, dry-run and rejection paths.
        // Execute's original idempotency-before-version ordering decides the final durable result.
        return _platform.ExecuteAdminTransaction(() =>
        {
            var fresh = _platform.AuthenticateSession(authorization);
            if (!IsSameSession(fresh))
            {
                _platform.RecordAuthorizationDenied(fresh?.Account, command.AuditContext,
                    L12Authorization.Key(permission), "authentication-required");
                return L12AdminCommandResult<T>.Fail("authentication_required", "请先登录账号", StatusCodes.Status401Unauthorized);
            }
            if (!IsAuthorized(fresh!.Account))
            {
                _platform.RecordAuthorizationDenied(fresh.Account, command.AuditContext,
                    L12Authorization.Key(permission), "permission-denied");
                return L12AdminCommandResult<T>.Fail("permission_denied", "当前账号没有执行此操作的权限", StatusCodes.Status403Forbidden);
            }
            return Execute(command with { Actor = fresh.Account }, permission, current =>
            {
                if (preparationFailure is not null) return preparationFailure;
                if (prepared is null)
                    return L12AdminCommandResult<T>.Fail("account_changed", "账号信息已更新，请刷新后重试", StatusCodes.Status409Conflict);
                try
                {
                    // Before any deletion recorder/chat side effect, and again in Store apply.
                    _platform.ValidatePreparedAdminPassword(current.Actor, accountId, reason, action, prepared, current.CommandId);
                    return apply(current, prepared);
                }
                catch (L12SecurityPolicyException error)
                {
                    return L12AdminCommandResult<T>.Fail(error.Code, error.Message,
                        error.Code == "account_changed" ? StatusCodes.Status409Conflict
                        : error.Code == "security_reason_required" ? StatusCodes.Status400BadRequest : StatusCodes.Status403Forbidden);
                }
                catch (KeyNotFoundException error)
                {
                    return L12AdminCommandResult<T>.Fail("account_not_found", error.Message, StatusCodes.Status404NotFound);
                }
            }, dryRun);
        });

        bool IsSameSession(L12AuthenticatedSession? fresh)
            => fresh is not null && fresh.SessionId == sessionId && fresh.Account.Id == command.Actor.Id;
        static bool IsAuthorized(L12AccountView actor)
            => !actor.MustChangePassword && !actor.MustChangeUsername
                && L12Authorization.HasPermission(actor, permission);
    }
}
