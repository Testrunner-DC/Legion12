using System.Security.Cryptography;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    // Internal properties are deliberately not JSON payloads. Never put a prepared
    // credential into a command envelope, persisted result, exception, or audit.
    internal sealed class PreparedAdminPassword
    {
        internal L12PlatformStore Owner { get; }
        internal object Snapshot { get; }
        internal L12AccountView Actor { get; }
        internal bool Disabled { get; }
        internal bool Deleted { get; }
        internal string Role { get; }
        internal string Action { get; }
        internal string? CommandId { get; }
        internal L12PasswordCredentialFields Credential { get; }
        internal string? TemporaryPassword { get; }
        internal PreparedAdminPassword(L12PlatformStore owner, object snapshot, L12AccountView actor,
            bool disabled, bool deleted, string role, string action, string? commandId,
            L12PasswordCredentialFields credential, string? temporaryPassword)
        {
            Owner = owner; Snapshot = snapshot; Actor = actor;
            Disabled = disabled; Deleted = deleted; Role = role; Action = action; CommandId = commandId;
            Credential = credential; TemporaryPassword = temporaryPassword;
        }
        public override string ToString() => nameof(PreparedAdminPassword);
    }

    internal PreparedAdminPassword PrepareAdminPassword(L12AccountView actor, string accountId,
        string reason, string action, string? commandId = null)
    {
        RequirePasswordWorkOutsideTransaction();
        if (action is not ("reset" or "delete")) throw new ArgumentException("Unsupported credential action.");
        PasswordSnapshot snapshot;
        bool disabled, deleted;
        string role;
        lock (_gate)
        {
            EnsureAccountAdministrationAllowed(actor, accountId, reason, action);
            if (action == "delete")
                DeleteAccountPersonalDataCore(actor, accountId, reason, new("credential-preparation"), false, null);
            var row = _data.Accounts.First(item => item.Id == accountId);
            snapshot = PasswordSnapshot.Capture(row);
            disabled = row.Disabled; deleted = row.Deleted; role = row.Role;
        }
        L12PasswordCredentialFields credential;
        string? temporaryPassword;
        using (PasswordWorkBudget.Acquire())
        {
            BeforeAdminPasswordWorkForTesting?.Invoke(action);
            var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(action == "reset" ? 16 : 32));
            credential = L12PasswordCredential.Create(raw);
            temporaryPassword = action == "reset" ? raw : null;
        }
        return new(this, snapshot, actor, disabled, deleted, role, action, commandId, credential, temporaryPassword);
    }

    internal void ValidatePreparedAdminPassword(L12AccountView actor, string accountId, string reason,
        string action, PreparedAdminPassword prepared, string? commandId = null)
    {
        lock (_gate)
        {
            var row = _data.Accounts.FirstOrDefault(item => item.Id == accountId);
            if (row is null || !ReferenceEquals(prepared.Owner, this) || prepared.Action != action
                || prepared.CommandId != commandId || prepared.Actor.Id != actor.Id
                || prepared.Actor.PermissionVersion != actor.PermissionVersion
                || prepared.Actor.Role != actor.Role
                || prepared.Snapshot is not PasswordSnapshot snapshot || !snapshot.MatchesValues(row)
                || prepared.Disabled != row.Disabled || prepared.Deleted != row.Deleted || prepared.Role != row.Role)
                throw new L12SecurityPolicyException("account_changed", "账号信息已更新，请刷新后重试");
            EnsureAccountAdministrationAllowed(actor, accountId, reason, action);
            if (action == "delete")
                DeleteAccountPersonalDataCore(actor, accountId, reason, new("credential-validation"), false, null);
        }
    }

    internal L12AdminPasswordResetView ApplyAdminResetPassword(L12AccountView actor, string accountId,
        string reason, L12AdminAuditContext context, PreparedAdminPassword prepared)
        => AdminResetPasswordCore(actor, accountId, reason, context, true, prepared);

    internal L12AccountDeletionView ApplyAccountDeletion(L12AccountView actor, string accountId,
        string reason, L12AdminAuditContext context, PreparedAdminPassword prepared)
        => DeleteAccountPersonalDataCore(actor, accountId, reason, context, true, prepared);
}
