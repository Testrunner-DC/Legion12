namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    internal L12PasswordWorkBudget PasswordWorkBudget { get; set; } = L12PasswordWorkBudget.Shared;
    internal Action? BeforePasswordChangeVerificationForTesting { get; set; }
    internal Action? BeforeRegistrationPasswordWorkForTesting { get; set; }
    internal Action? BeforeUsernameChangePasswordVerificationForTesting { get; set; }
    internal Action<string>? BeforeAdminPasswordWorkForTesting { get; set; }

    // Values only: no mutable account row may escape the platform gate for password work.
    private sealed record PasswordSnapshot(string Id, string Username, string Hash, string Salt,
        string? Algorithm, int? Iterations, int? Version, long PermissionVersion,
        bool MustChangePassword, bool MustChangeUsername)
    {
        internal static PasswordSnapshot Capture(AccountRow row) => new(row.Id, row.Username,
            row.PasswordHash, row.Salt, row.PasswordAlgorithm, row.PasswordIterations,
            row.PasswordCredentialVersion, row.PermissionVersion, row.MustChangePassword, row.MustChangeUsername);

        internal bool Matches(AccountRow row) => !row.Disabled && !row.Deleted
            && MatchesValues(row);

        internal bool MatchesValues(AccountRow row) => Id == row.Id && Username == row.Username && Hash == row.PasswordHash && Salt == row.Salt
            && Algorithm == row.PasswordAlgorithm && Iterations == row.PasswordIterations
            && Version == row.PasswordCredentialVersion && PermissionVersion == row.PermissionVersion
            && MustChangePassword == row.MustChangePassword && MustChangeUsername == row.MustChangeUsername;

        internal bool Verify(string password)
        {
            try
            {
                return L12PasswordCredential.Verify(password, Hash, Salt, Algorithm, Iterations, Version,
                    L12PasswordCredential.ActiveIterations);
            }
            catch { return false; }
        }
    }

    internal void RequirePasswordWorkOutsideTransaction()
    {
        if (Monitor.IsEntered(_gate))
            throw new InvalidOperationException("Password work cannot run inside a platform transaction.");
    }

    private L12AuthenticationResult LoginThrottled(string normalizedUsername,
        L12LoginAttemptContext context, int retryAfter)
    {
        PruneLoginThrottles(DateTimeOffset.UtcNow);
        AddAuthenticationAudit(null, normalizedUsername, "rate-limited", context, "denied");
        Save(false);
        return new(false, "登录尝试过多，请稍后重试", null, null, "login_rate_limited", retryAfter);
    }
}
