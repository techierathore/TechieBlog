using BlogModels;
using System.Text;
using Xunit;

namespace TechieBlog.Tests.Security;

/// <summary>
/// Unit tests for <see cref="PasswordHasher"/> (REQ-NFR-002, BRD-79).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Locks down the storage format, the salt behaviour and — most
/// importantly — the re-hash-on-next-login migration path that keeps pre-existing accounts
/// working after the move off the hand-rolled MD5 scheme.</para>
/// <para><b>Dependencies:</b> xUnit; no database or host required.</para>
/// </remarks>
public class PasswordHasherTests
{
    private const string SamplePassword = "Str0ngPassword!";

    /// <summary>
    /// Hashing a password produces the documented
    /// "PBKDF2-SHA256$iterations$salt$subkey" envelope with the configured iteration count.
    /// </summary>
    [Fact]
    public void HashProducesDocumentedFormat()
    {
        var hash = PasswordHasher.HashPassword(SamplePassword);

        var parts = hash.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal(PasswordHasher.HashPrefix, parts[0]);
        Assert.Equal(PasswordHasher.IterationCount.ToString(), parts[1]);
        Assert.Equal(PasswordHasher.SaltByteSize, Convert.FromBase64String(parts[2]).Length);
        Assert.Equal(PasswordHasher.KeyByteSize, Convert.FromBase64String(parts[3]).Length);
    }

    /// <summary>
    /// Hashing the same password twice yields different strings, proving the salt is random
    /// per call rather than the single fixed salt the retired scheme used.
    /// </summary>
    [Fact]
    public void HashUsesFreshSaltPerCall()
    {
        var first = PasswordHasher.HashPassword(SamplePassword);
        var second = PasswordHasher.HashPassword(SamplePassword);

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// An empty password is rejected outright rather than hashed into a usable credential.
    /// </summary>
    [Fact]
    public void HashRejectsEmptyPassword()
    {
        Assert.Throws<ArgumentException>(() => PasswordHasher.HashPassword(string.Empty));
    }

    /// <summary>
    /// Verifying the correct password against a current hash succeeds and does not ask for a
    /// re-hash, because the stored work factor already matches the configured one.
    /// </summary>
    [Fact]
    public void VerifyAcceptsCorrectPassword()
    {
        var hash = PasswordHasher.HashPassword(SamplePassword);

        Assert.Equal(PasswordVerifyResult.Success, PasswordHasher.Verify(SamplePassword, hash));
    }

    /// <summary>
    /// Verifying a wrong password against a current hash fails.
    /// </summary>
    [Fact]
    public void VerifyRejectsWrongPassword()
    {
        var hash = PasswordHasher.HashPassword(SamplePassword);

        Assert.Equal(PasswordVerifyResult.Failed, PasswordHasher.Verify("wrong-password", hash));
    }

    /// <summary>
    /// Null or empty inputs fail closed instead of throwing out of the login path.
    /// </summary>
    [Fact]
    public void VerifyRejectsEmptyInputs()
    {
        var hash = PasswordHasher.HashPassword(SamplePassword);

        Assert.Equal(PasswordVerifyResult.Failed, PasswordHasher.Verify(null!, hash));
        Assert.Equal(PasswordVerifyResult.Failed, PasswordHasher.Verify(SamplePassword, null!));
        Assert.Equal(PasswordVerifyResult.Failed, PasswordHasher.Verify(string.Empty, string.Empty));
    }

    /// <summary>
    /// A credential still stored as the retired MD5 digest authenticates, but reports that the
    /// record must be re-hashed — this is the migration path for existing accounts.
    /// </summary>
    [Fact]
    public void VerifyMigratesLegacyDigestCredential()
    {
        var legacyDigest = ComputeRetiredDigest(SamplePassword);

        Assert.Equal(
            PasswordVerifyResult.SuccessNeedsRehash,
            PasswordHasher.Verify(SamplePassword, legacyDigest));
    }

    /// <summary>
    /// A wrong password is still refused when the stored credential is a legacy MD5 digest.
    /// </summary>
    [Fact]
    public void VerifyRejectsWrongPasswordAgainstLegacyDigest()
    {
        var legacyDigest = ComputeRetiredDigest(SamplePassword);

        Assert.Equal(PasswordVerifyResult.Failed, PasswordHasher.Verify("wrong-password", legacyDigest));
    }

    /// <summary>
    /// A credential left in plain text by the pre-REQ-NFR-023 seed authenticates once and asks
    /// to be re-hashed, so an existing installation is repaired at the owner's next sign-in.
    /// </summary>
    [Fact]
    public void VerifyMigratesLegacyPlaintextCredential()
    {
        Assert.Equal(
            PasswordVerifyResult.SuccessNeedsRehash,
            PasswordHasher.Verify("admin_password", "admin_password"));
    }

    /// <summary>
    /// A correct password stored with a lower work factor than the current one authenticates
    /// but requests a re-hash, so the iteration count can be raised without a password reset.
    /// </summary>
    [Fact]
    public void VerifyRequestsRehashForStaleWorkFactor()
    {
        var current = PasswordHasher.HashPassword(SamplePassword);
        var parts = current.Split('$');
        var weakened = BuildWeakenedHash(SamplePassword, Convert.FromBase64String(parts[2]));

        Assert.Equal(PasswordVerifyResult.SuccessNeedsRehash, PasswordHasher.Verify(SamplePassword, weakened));
    }

    /// <summary>
    /// A malformed stored value — wrong field count, non-numeric iterations or invalid base64 —
    /// fails verification instead of throwing.
    /// </summary>
    [Theory]
    [InlineData("PBKDF2-SHA256$210000$only-three-parts")]
    [InlineData("PBKDF2-SHA256$notanumber$AAAAAAAAAAAAAAAAAAAAAA==$AAAA")]
    [InlineData("PBKDF2-SHA256$210000$not-base64!!$AAAA")]
    public void VerifyRejectsMalformedHash(string storedHash)
    {
        Assert.Equal(PasswordVerifyResult.Failed, PasswordHasher.Verify(SamplePassword, storedHash));
    }

    /// <summary>
    /// Hashing with an explicit salt is deterministic, which is what makes the hash literal
    /// embedded in the seed migration reproducible.
    /// </summary>
    [Fact]
    public void HashWithSaltIsDeterministic()
    {
        var salt = Encoding.UTF8.GetBytes("TechieBlogSeed01");

        var first = PasswordHasher.HashPasswordWithSalt(SamplePassword, salt);
        var second = PasswordHasher.HashPasswordWithSalt(SamplePassword, salt);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// A salt shorter than the configured size is rejected, so a seed script cannot weaken the
    /// scheme by accident.
    /// </summary>
    [Fact]
    public void HashWithSaltRejectsShortSalt()
    {
        Assert.Throws<ArgumentException>(
            () => PasswordHasher.HashPasswordWithSalt(SamplePassword, new byte[] { 1, 2, 3 }));
    }

    /// <summary>
    /// The exact hash literal seeded by 003-SeedData.sql and repaired by
    /// 017-SecurityAndTokenPersistence.sql still verifies the documented bootstrap password,
    /// so the migration cannot silently lock the administrator out (REQ-NFR-023).
    /// </summary>
    [Fact]
    public void SeededAdminHashVerifiesDocumentedPassword()
    {
        const string seededHash =
            "PBKDF2-SHA256$210000$VGVjaGllQmxvZ1NlZWQwMQ==$m3BUDC+/QWc38+4jGaLfRF6VDV/ksim4+JCoOJJZjw4=";

        Assert.Equal(PasswordVerifyResult.Success, PasswordHasher.Verify("admin_password", seededHash));
        Assert.Equal(PasswordVerifyResult.Failed, PasswordHasher.Verify("not_the_password", seededHash));
    }

    /// <summary>
    /// <see cref="PasswordHasher.IsCurrentFormat"/> recognises current hashes and rejects the
    /// legacy shapes a migration still has to repair.
    /// </summary>
    [Fact]
    public void IsCurrentFormatDistinguishesLegacyCredentials()
    {
        Assert.True(PasswordHasher.IsCurrentFormat(PasswordHasher.HashPassword(SamplePassword)));
        Assert.False(PasswordHasher.IsCurrentFormat("admin_password"));
        Assert.False(PasswordHasher.IsCurrentFormat(ComputeRetiredDigest(SamplePassword)));
        Assert.False(PasswordHasher.IsCurrentFormat(null!));
    }

    /// <summary>
    /// Reproduces the retired MD5 + fixed-salt digest exactly as the pre-REQ-NFR-002 code did,
    /// so the migration path is tested against a genuine legacy value rather than a stand-in.
    /// </summary>
    /// <param name="password">The plaintext password.</param>
    /// <returns>The lowercase hexadecimal MD5 digest.</returns>
    private static string ComputeRetiredDigest(string password)
    {
        const string legacySalt = "TeleM3t3IS@lt";
        var bytes = System.Security.Cryptography.MD5.HashData(
            Encoding.UTF32.GetBytes(legacySalt + password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Builds a correctly formatted hash that uses a deliberately low iteration count, standing
    /// in for a credential written before the work factor was raised.
    /// </summary>
    /// <param name="password">The plaintext password.</param>
    /// <param name="salt">The salt to reuse.</param>
    /// <returns>The encoded low-iteration hash.</returns>
    private static string BuildWeakenedHash(string password, byte[] salt)
    {
        const int weakIterations = 1000;
        var subkey = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            weakIterations,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            PasswordHasher.KeyByteSize);

        return string.Join(
            '$',
            PasswordHasher.HashPrefix,
            weakIterations.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(subkey));
    }
}
