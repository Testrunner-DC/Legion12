using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PasswordCredentialCompatibilityTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Password = "password-123";

    [Theory]
    [InlineData(8, 32)]
    [InlineData(16, 16)]
    [InlineData(16, 64)]
    public void LegacyCredentialRejectsNonHistoricalBinarySizes(int saltBytes, int hashBytes)
    {
        var root = TempRoot();
        try
        {
            var username = Username();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var registered = store.Register(username, Password);
            Assert.True(registered.Success, registered.Message);
            var row = Account(store, registered.Account!.Id);
            var salt = Enumerable.Range(1, saltBytes).Select(value => (byte)value).ToArray();
            var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(Password), salt, 120_000,
                HashAlgorithmName.SHA256, hashBytes);
            SetCredential(row, Convert.ToBase64String(hash), Convert.ToBase64String(salt), null, null, null);

            Assert.False(store.Login(username, Password).Success);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LegacyCredentialHasOneFixedHistoricalDefinition()
    {
        var fields = L12PasswordCredential.Create(Password);

        Assert.True(L12PasswordCredential.Verify(Password, fields.PasswordHash, fields.Salt,
            null, null, null, L12PasswordCredential.ActiveIterations));
        Assert.True(L12PasswordCredential.Verify(Password, fields.PasswordHash, fields.Salt,
            null, null, null, L12PasswordCredential.RecognizedUpgradeIterations));
        Assert.False(L12PasswordCredential.Verify("wrong-password", fields.PasswordHash, fields.Salt,
            null, null, null, L12PasswordCredential.ActiveIterations));
    }

    [Fact]
    public void InvalidBase64AndWrongEncodedLengthsAreRejected()
    {
        var fields = L12PasswordCredential.Create(Password);
        const string base64Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        var nonCanonicalSalt = fields.Salt.ToCharArray();
        var saltPaddingValue = base64Alphabet.IndexOf(nonCanonicalSalt[^3]);
        Assert.Equal(0, saltPaddingValue & 0b001111);
        nonCanonicalSalt[^3] = base64Alphabet[(saltPaddingValue & 0b110000) | 1];
        var nonCanonicalSaltText = new string(nonCanonicalSalt);
        var nonCanonicalHash = fields.PasswordHash.ToCharArray();
        var hashPaddingValue = base64Alphabet.IndexOf(nonCanonicalHash[^2]);
        Assert.Equal(0, hashPaddingValue & 0b000011);
        nonCanonicalHash[^2] = base64Alphabet[(hashPaddingValue & 0b111100) | 1];
        var nonCanonicalHashText = new string(nonCanonicalHash);

        Assert.Equal(Convert.FromBase64String(fields.Salt), Convert.FromBase64String(nonCanonicalSaltText));
        Assert.Equal(Convert.FromBase64String(fields.PasswordHash),
            Convert.FromBase64String(nonCanonicalHashText));

        Assert.False(L12PasswordCredential.Verify(Password, new string('!', 44), fields.Salt,
            null, null, null, L12PasswordCredential.ActiveIterations));
        Assert.False(L12PasswordCredential.Verify(Password, fields.PasswordHash, new string('!', 24),
            null, null, null, L12PasswordCredential.ActiveIterations));
        Assert.False(L12PasswordCredential.Verify(Password, fields.PasswordHash + " ", fields.Salt,
            null, null, null, L12PasswordCredential.ActiveIterations));
        Assert.False(L12PasswordCredential.Verify(Password, fields.PasswordHash, fields.Salt + " ",
            null, null, null, L12PasswordCredential.ActiveIterations));
        Assert.False(L12PasswordCredential.Verify(Password, fields.PasswordHash, nonCanonicalSaltText,
            null, null, null, L12PasswordCredential.ActiveIterations));
        Assert.False(L12PasswordCredential.Verify(Password, nonCanonicalHashText, fields.Salt,
            null, null, null, L12PasswordCredential.ActiveIterations));
    }

    [Fact]
    public void ExplicitMetadataMustBeCompleteAndKnown()
    {
        var fields = L12PasswordCredential.Create(Password);

        Assert.False(L12PasswordCredential.Verify(Password, fields.PasswordHash, fields.Salt,
            null, fields.Iterations, fields.Version, L12PasswordCredential.ActiveIterations));
        Assert.False(L12PasswordCredential.Verify(Password, fields.PasswordHash, fields.Salt,
            fields.Algorithm, null, fields.Version, L12PasswordCredential.ActiveIterations));
        Assert.False(L12PasswordCredential.Verify(Password, fields.PasswordHash, fields.Salt,
            fields.Algorithm, fields.Iterations, null, L12PasswordCredential.ActiveIterations));
        Assert.False(L12PasswordCredential.Verify(Password, fields.PasswordHash, fields.Salt,
            string.Empty, fields.Iterations, fields.Version, L12PasswordCredential.ActiveIterations));
        Assert.False(L12PasswordCredential.Verify(Password, fields.PasswordHash, fields.Salt,
            "pbkdf2-sha512", fields.Iterations, fields.Version, L12PasswordCredential.ActiveIterations));
        Assert.False(L12PasswordCredential.Verify(Password, fields.PasswordHash, fields.Salt,
            fields.Algorithm, 120_001, fields.Version, L12PasswordCredential.ActiveIterations));
        Assert.False(L12PasswordCredential.Verify(Password, fields.PasswordHash, fields.Salt,
            fields.Algorithm, fields.Iterations, 2, L12PasswordCredential.ActiveIterations));
    }

    [Fact]
    public void ActiveCredentialIsExplicitAndRemainsReadableByTheOldFixedCostAlgorithm()
    {
        var fields = L12PasswordCredential.Create(Password);
        var salt = Convert.FromBase64String(fields.Salt);
        var expected = Convert.FromBase64String(fields.PasswordHash);
        var rollbackReader = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(Password), salt, 120_000,
            HashAlgorithmName.SHA256, 32);

        Assert.Equal(L12PasswordCredential.Pbkdf2Sha256, fields.Algorithm);
        Assert.Equal(L12PasswordCredential.ActiveIterations, fields.Iterations);
        Assert.Equal(L12PasswordCredential.FormatVersion, fields.Version);
        Assert.Equal(16, salt.Length);
        Assert.Equal(32, expected.Length);
        Assert.True(CryptographicOperations.FixedTimeEquals(rollbackReader, expected));
        Assert.True(L12PasswordCredential.Verify(Password, fields.PasswordHash, fields.Salt,
            fields.Algorithm, fields.Iterations, fields.Version, L12PasswordCredential.ActiveIterations));
        Assert.True(L12PasswordCredential.Verify(Password, fields.PasswordHash, fields.Salt,
            fields.Algorithm, fields.Iterations, fields.Version,
            L12PasswordCredential.RecognizedUpgradeIterations));
    }

    [Fact]
    public void StoreWritesAndReloadsOnlyTheActiveExplicitCredential()
    {
        var root = TempRoot();
        try
        {
            var path = Path.Combine(root, "platform.json");
            var username = Username();
            var store = new L12PlatformStore(path);
            var registered = store.Register(username, Password);
            Assert.True(registered.Success, registered.Message);
            var row = Account(store, registered.Account!.Id);

            Assert.Equal(L12PasswordCredential.Pbkdf2Sha256, Property<string>(row, "PasswordAlgorithm"));
            Assert.Equal(L12PasswordCredential.ActiveIterations, Property<int?>(row, "PasswordIterations"));
            Assert.Equal(L12PasswordCredential.FormatVersion, Property<int?>(row, "PasswordCredentialVersion"));
            Assert.True(new L12PlatformStore(path).Login(username, Password).Success);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void StoreStillReadsLegacyCredentialWhenAllMetadataIsAbsent()
    {
        var root = TempRoot();
        try
        {
            var username = Username();
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path);
            var registered = store.Register(username, Password);
            Assert.True(registered.Success, registered.Message);
            var row = Account(store, registered.Account!.Id);
            SetCredential(row, Property<string>(row, "PasswordHash")!, Property<string>(row, "Salt")!,
                null, null, null);

            Assert.True(store.Login(username, Password).Success);
            var reloaded = new L12PlatformStore(path);
            Assert.True(reloaded.Login(username, Password).Success);
            var reloadedRow = Account(reloaded, registered.Account!.Id);
            Assert.Null(Property<string>(reloadedRow, "PasswordAlgorithm"));
            Assert.Null(Property<int?>(reloadedRow, "PasswordIterations"));
            Assert.Null(Property<int?>(reloadedRow, "PasswordCredentialVersion"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RecognizedUpgradeCostIsPureHelperOnlyUntilTheStoreActivatesIt()
    {
        var future = L12PasswordCredential.Create(Password, L12PasswordCredential.RecognizedUpgradeIterations);
        Assert.True(L12PasswordCredential.Verify(Password, future.PasswordHash, future.Salt,
            future.Algorithm, future.Iterations, future.Version,
            L12PasswordCredential.RecognizedUpgradeIterations));
        Assert.False(L12PasswordCredential.Verify(Password, future.PasswordHash, future.Salt,
            future.Algorithm, future.Iterations, future.Version, L12PasswordCredential.ActiveIterations));

        var root = TempRoot();
        try
        {
            var username = Username();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var registered = store.Register(username, Password);
            Assert.True(registered.Success, registered.Message);
            SetCredential(Account(store, registered.Account!.Id), future.PasswordHash, future.Salt,
                future.Algorithm, future.Iterations, future.Version);

            Assert.False(store.Login(username, Password).Success);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static string TempRoot()
        => Path.Combine(Path.GetTempPath(), $"l12-password-credential-{Guid.NewGuid():N}");

    private static string Username()
        => "u" + Guid.NewGuid().ToString("N")[..10];

    private static object Account(L12PlatformStore store, string accountId)
    {
        var data = typeof(L12PlatformStore).GetProperty("_data", Private)!.GetValue(store)!;
        return ((IEnumerable)data.GetType().GetProperty("Accounts")!.GetValue(data)!).Cast<object>()
            .Single(row => (string)row.GetType().GetProperty("Id")!.GetValue(row)! == accountId);
    }

    private static T? Property<T>(object row, string name)
        => (T?)row.GetType().GetProperty(name)!.GetValue(row);

    private static void SetCredential(object row, string passwordHash, string salt,
        string? algorithm, int? iterations, int? version)
    {
        row.GetType().GetProperty("PasswordHash")!.SetValue(row, passwordHash);
        row.GetType().GetProperty("Salt")!.SetValue(row, salt);
        row.GetType().GetProperty("PasswordAlgorithm")!.SetValue(row, algorithm);
        row.GetType().GetProperty("PasswordIterations")!.SetValue(row, iterations);
        row.GetType().GetProperty("PasswordCredentialVersion")!.SetValue(row, version);
    }
}
