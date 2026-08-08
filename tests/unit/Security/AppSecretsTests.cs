using BlogModels;
using Microsoft.Extensions.Configuration;

namespace TechieBlog.Tests.Security;

/// <summary>
/// Covers the configuration-supplied cryptographic secrets that replaced the committed literals
/// (REQ-NFR-027).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Proves the three properties the requirement asks for that can be asserted
/// in-process: nothing usable is left in source, an absent or unusable value stops the host instead
/// of falling back to a default, and rotating the signing key moves the browser storage keys so the
/// sessions minted under the old key can no longer be presented.</para>
///
/// <para><b>Code Flow:</b> every failure case calls the real
/// <see cref="AppSecrets.Initialise(IConfiguration)"/> with a deliberately bad configuration. That
/// is safe to do while other test classes run in parallel because Initialise validates both values
/// before it assigns either, so a rejected configuration cannot disturb the secrets that
/// <see cref="TestSecretsBootstrap"/> installed.</para>
///
/// <para><b>Dependencies:</b> <see cref="AppSecrets"/>, <see cref="AppConstants"/> and an in-memory
/// <see cref="IConfiguration"/>.</para>
///
/// <para><b>Usage:</b> <c>dotnet test</c>.</para>
/// </remarks>
public class AppSecretsTests
{
    /// <summary>
    /// The signing key and the AES key that were hard-coded in AppConstants before REQ-NFR-027 are
    /// no longer present anywhere in BlogModels, so a copy of the repository no longer hands an
    /// attacker the ability to forge an Admin token.
    /// </summary>
    [Fact]
    public void CommittedLiteralsAreGoneFromAppConstants()
    {
        var members = typeof(AppConstants)
            .GetFields()
            .Select(field => field.Name)
            .Concat(typeof(AppConstants).GetProperties().Select(property => property.Name))
            .ToArray();

        Assert.DoesNotContain("JWTTokenGenKey", members);
        Assert.DoesNotContain("AppSalt", members);
    }

    /// <summary>
    /// The secrets are read from the configuration keys the host documents, so supplying them
    /// through user secrets or the environment actually reaches the crypto code.
    /// </summary>
    [Fact]
    public void ConfiguredSecretsAreExposedThroughTheAccessors()
    {
        Assert.True(AppSecrets.IsInitialised);
        Assert.Equal(TestSecretsBootstrap.TestSigningKey, AppSecrets.JwtSigningKey);
        Assert.Equal(TestSecretsBootstrap.TestEncryptionKey, AppSecrets.EncryptionKey);
    }

    /// <summary>
    /// A missing signing key stops startup with a message naming the configuration key, rather than
    /// quietly falling back to a built-in default.
    /// </summary>
    [Fact]
    public void MissingSigningKeyFailsLoudly()
    {
        var configuration = BuildConfiguration(null, "a-perfectly-valid-encryption-key");

        var error = Assert.Throws<InvalidOperationException>(() => AppSecrets.Initialise(configuration));

        Assert.Contains(AppSecrets.JwtSigningKeyPath, error.Message);
        Assert.Contains("no default", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A missing AES key stops startup the same way the missing signing key does.
    /// </summary>
    [Fact]
    public void MissingEncryptionKeyFailsLoudly()
    {
        var configuration = BuildConfiguration(TestSecretsBootstrap.TestSigningKey, null);

        var error = Assert.Throws<InvalidOperationException>(() => AppSecrets.Initialise(configuration));

        Assert.Contains(AppSecrets.EncryptionKeyPath, error.Message);
    }

    /// <summary>
    /// A whitespace-only value counts as absent, so a half-filled settings file cannot start the
    /// host with an effectively empty key.
    /// </summary>
    [Fact]
    public void BlankSigningKeyFailsLoudly()
    {
        var configuration = BuildConfiguration("   ", "a-perfectly-valid-encryption-key");

        Assert.Throws<InvalidOperationException>(() => AppSecrets.Initialise(configuration));
    }

    /// <summary>
    /// The exact literal that used to sit in AppConstants is refused even when an operator pastes it
    /// into configuration, because it is public knowledge to anyone holding the repository.
    /// </summary>
    [Fact]
    public void RetiredCommittedSigningKeyIsRefused()
    {
        var configuration = BuildConfiguration(
            "Xp@ns@JwTokenBieSR@viKum@r2025!Secure", "a-perfectly-valid-encryption-key");

        var error = Assert.Throws<InvalidOperationException>(() => AppSecrets.Initialise(configuration));

        Assert.Contains("source control", error.Message);
    }

    /// <summary>
    /// The retired AES literal is refused for the same reason as the retired signing key.
    /// </summary>
    [Fact]
    public void RetiredCommittedEncryptionKeyIsRefused()
    {
        var configuration = BuildConfiguration(TestSecretsBootstrap.TestSigningKey, "Xp@ns@r");

        var error = Assert.Throws<InvalidOperationException>(() => AppSecrets.Initialise(configuration));

        Assert.Contains("source control", error.Message);
    }

    /// <summary>
    /// A signing key shorter than the 256 bits HMAC-SHA256 needs is refused, so a weak key cannot be
    /// configured by accident.
    /// </summary>
    [Fact]
    public void ShortSigningKeyIsRefused()
    {
        var configuration = BuildConfiguration("too-short", "a-perfectly-valid-encryption-key");

        var error = Assert.Throws<InvalidOperationException>(() => AppSecrets.Initialise(configuration));

        Assert.Contains(AppSecrets.MinimumJwtSigningKeyLength.ToString(), error.Message);
    }

    /// <summary>
    /// No rejection message ever echoes the value it rejected, so a startup failure written to a log
    /// or a console cannot leak the secret it was complaining about.
    /// </summary>
    [Fact]
    public void RejectionMessagesNeverEchoTheValue()
    {
        const string secret = "this-is-a-secret-value-that-is-far-too-obvious";
        var configuration = BuildConfiguration(secret, null);

        var error = Assert.Throws<InvalidOperationException>(() => AppSecrets.Initialise(configuration));

        Assert.DoesNotContain(secret, error.Message);
    }

    /// <summary>
    /// The published fingerprint is a one-way digest: it is stable for a given key, differs for a
    /// different key, and reveals none of the key material itself.
    /// </summary>
    [Fact]
    public void FingerprintIsStableAndDoesNotRevealTheKey()
    {
        var first = AppSecrets.ComputeFingerprint(TestSecretsBootstrap.TestSigningKey);
        var second = AppSecrets.ComputeFingerprint(TestSecretsBootstrap.TestSigningKey);
        var rotated = AppSecrets.ComputeFingerprint("a-completely-different-signing-key-value");

        Assert.Equal(first, second);
        Assert.NotEqual(first, rotated);
        Assert.Equal(8, first.Length);
        Assert.Matches("^[0-9a-f]{8}$", first);
    }

    /// <summary>
    /// The fingerprint currently in force is the one derived from the configured signing key, so the
    /// storage keys below are tied to the real key generation rather than to a constant.
    /// </summary>
    [Fact]
    public void SessionFingerprintMatchesTheConfiguredSigningKey()
    {
        Assert.Equal(
            AppSecrets.ComputeFingerprint(TestSecretsBootstrap.TestSigningKey),
            AppSecrets.SessionFingerprint);
    }

    /// <summary>
    /// Both browser storage keys carry the signing-key fingerprint, which is what makes a key
    /// rotation sign every existing session out: a browser holding a token issued under the previous
    /// key looks in a slot that no longer exists, so that token can never be presented again.
    /// </summary>
    [Fact]
    public void StorageKeysCarryTheFingerprintSoRotationInvalidatesSessions()
    {
        var currentFingerprint = AppSecrets.SessionFingerprint;
        var rotatedFingerprint = AppSecrets.ComputeFingerprint("a-completely-different-signing-key-value");

        Assert.Equal($"AccessToken-{currentFingerprint}", AppConstants.AccessKey);
        Assert.Equal($"RefreshToken-{currentFingerprint}", AppConstants.RefreshKey);
        Assert.NotEqual($"AccessToken-{rotatedFingerprint}", AppConstants.AccessKey);
        Assert.NotEqual($"RefreshToken-{rotatedFingerprint}", AppConstants.RefreshKey);
    }

    /// <summary>
    /// The reversible encryption helpers now key on the configured secret, and a value encrypted
    /// under a different key cannot be read back — the property that makes an AES-key rotation
    /// meaningful.
    /// </summary>
    [Fact]
    public void EncryptionUsesTheConfiguredKey()
    {
        const string plaintext = "Ravi@techieblog.com";

        var underConfiguredKey = AppEncrypt.EncryptText(plaintext);
        var underRotatedKey = plaintext.Encrypt("a-completely-different-encryption-key");

        Assert.Equal(plaintext, AppEncrypt.DecryptText(underConfiguredKey));

        // A wrong key almost always throws on the PKCS7 padding check, but roughly one time in 256
        // the padding is accidentally well formed and garbage comes back instead. Both outcomes mean
        // the same thing - the plaintext is unrecoverable - so assert that rather than the throw.
        string? recovered = null;
        try
        {
            recovered = AppEncrypt.DecryptText(underRotatedKey);
        }
        catch (Exception)
        {
            recovered = null;
        }

        Assert.NotEqual(plaintext, recovered);
    }

    /// <summary>
    /// Builds an in-memory configuration containing whichever of the two secrets the caller supplied.
    /// </summary>
    /// <param name="signingKey">The signing key value, or <c>null</c> to leave the key absent.</param>
    /// <param name="encryptionKey">The AES key value, or <c>null</c> to leave the key absent.</param>
    /// <returns>The configuration to hand to <see cref="AppSecrets.Initialise(IConfiguration)"/>.</returns>
    private static IConfiguration BuildConfiguration(string? signingKey, string? encryptionKey)
    {
        var entries = new Dictionary<string, string?>();
        if (signingKey is not null)
            entries[AppSecrets.JwtSigningKeyPath] = signingKey;
        if (encryptionKey is not null)
            entries[AppSecrets.EncryptionKeyPath] = encryptionKey;

        return new ConfigurationBuilder().AddInMemoryCollection(entries).Build();
    }
}
