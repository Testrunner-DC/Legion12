using System.Security.Cryptography;
using System.Text;

namespace TwelveLegions.Server;

internal readonly record struct L12PasswordCredentialFields(
    string PasswordHash,
    string Salt,
    string Algorithm,
    int Iterations,
    int Version);

internal static class L12PasswordCredential
{
    internal const string Pbkdf2Sha256 = "pbkdf2-sha256";
    internal const int FormatVersion = 1;
    internal const int LegacyIterations = 120_000;
    internal const int ActiveIterations = LegacyIterations;
    internal const int RecognizedUpgradeIterations = 600_000;
    // Reader compatibility is independent of the cost used for new credentials.
    internal const int MaximumReadIterations = RecognizedUpgradeIterations;
    internal const int SaltBytes = 16;
    internal const int HashBytes = 32;

    internal static L12PasswordCredentialFields Create(string password, int iterations = ActiveIterations)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (!IsRecognizedIterations(iterations))
            throw new ArgumentOutOfRangeException(nameof(iterations), "Password cost is not recognized.");

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? hash = null;
        try
        {
            hash = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
            return new(Convert.ToBase64String(hash), Convert.ToBase64String(salt), Pbkdf2Sha256,
                iterations, FormatVersion);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (hash is not null) CryptographicOperations.ZeroMemory(hash);
        }
    }

    internal static bool Verify(string password, string? passwordHash, string? salt,
        string? algorithm, int? iterations, int? version, int maximumActivatedIterations)
    {
        if (password is null || !IsRecognizedIterations(maximumActivatedIterations)) return false;

        var hasExplicitMetadata = algorithm is not null || iterations.HasValue || version.HasValue;
        var credentialIterations = LegacyIterations;
        if (hasExplicitMetadata)
        {
            if (algorithm is null || !iterations.HasValue || !version.HasValue
                || !string.Equals(algorithm, Pbkdf2Sha256, StringComparison.Ordinal)
                || version.Value != FormatVersion
                || !IsRecognizedIterations(iterations.Value)
                || iterations.Value > maximumActivatedIterations)
                return false;
            credentialIterations = iterations.Value;
        }

        if (!TryDecodeExact(salt, SaltBytes, out var saltBytes)
            || !TryDecodeExact(passwordHash, HashBytes, out var expected))
            return false;

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? actual = null;
        try
        {
            actual = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, saltBytes, credentialIterations,
                HashAlgorithmName.SHA256, HashBytes);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (actual is not null) CryptographicOperations.ZeroMemory(actual);
        }
    }

    private static bool IsRecognizedIterations(int iterations)
        => iterations is LegacyIterations or RecognizedUpgradeIterations;

    private static bool TryDecodeExact(string? value, int expectedBytes, out byte[] decoded)
    {
        decoded = [];
        if (value is null || value.Length != ((expectedBytes + 2) / 3) * 4) return false;
        var candidate = new byte[expectedBytes];
        if (!Convert.TryFromBase64String(value, candidate, out var written)
            || written != expectedBytes
            || !string.Equals(Convert.ToBase64String(candidate), value, StringComparison.Ordinal))
            return false;
        decoded = candidate;
        return true;
    }
}
