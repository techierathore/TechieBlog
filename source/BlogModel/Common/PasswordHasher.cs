using System.Security.Cryptography;
using System.Text;

namespace BlogModels;

/// <summary>
/// Industry-standard salted password hashing and verification for TechieBlog (REQ-NFR-002, BRD-79).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Produces and verifies PBKDF2-HMAC-SHA256 password hashes with a
/// per-password cryptographic salt, replacing the hand-rolled MD5 + fixed-salt scheme that
/// <c>AppEncrypt.CreateHash</c> used before REQ-NFR-002.</para>
///
/// <para><b>Algorithm:</b> PBKDF2 (<see cref="Rfc2898DeriveBytes"/>, BCL) with HMAC-SHA256,
/// <see cref="IterationCount"/> iterations, a 128-bit random salt and a 256-bit derived key.
/// The iteration count follows the OWASP Password Storage Cheat Sheet recommendation for
/// PBKDF2-HMAC-SHA256.</para>
///
/// <para><b>Storage format:</b> a single self-describing string written to
/// <c>BlogUser.LoginPass</c>:</para>
/// <code>PBKDF2-SHA256$&lt;iterations&gt;$&lt;base64 salt&gt;$&lt;base64 subkey&gt;</code>
/// <para>Because the iteration count and salt travel with the hash, the work factor can be
/// raised later without invalidating existing credentials — <see cref="Verify"/> reports
/// <see cref="PasswordVerifyResult.SuccessNeedsRehash"/> and the caller re-hashes.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Account creation / password change calls <see cref="HashPassword"/>; the encoded
///     string is stored verbatim.</item>
///   <item>Login calls <see cref="Verify"/> with the supplied password and the stored value.</item>
///   <item>If the stored value is a legacy MD5 digest or legacy plaintext, verification still
///     succeeds when the password matches but returns
///     <see cref="PasswordVerifyResult.SuccessNeedsRehash"/> so the caller upgrades the record
///     transparently on the next successful login.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <c>System.Security.Cryptography</c> only — no third-party
/// hashing package is required.</para>
///
/// <para><b>Usage:</b></para>
/// <code>
/// var stored = PasswordHasher.HashPassword("Str0ngPassword!");
/// var outcome = PasswordHasher.Verify("Str0ngPassword!", stored);
/// if (outcome == PasswordVerifyResult.SuccessNeedsRehash)
/// {
///     userCredentialRepo.UpdatePasswordHash(userId, PasswordHasher.HashPassword(password), false);
/// }
/// </code>
/// </remarks>
public static class PasswordHasher
{
    /// <summary>
    /// Prefix identifying the current hash format. Any stored value that does not start with
    /// this prefix is treated as a legacy credential.
    /// </summary>
    public const string HashPrefix = "PBKDF2-SHA256";

    /// <summary>
    /// PBKDF2 iteration count (OWASP recommendation for PBKDF2-HMAC-SHA256).
    /// </summary>
    public const int IterationCount = 210000;

    /// <summary>
    /// Salt length in bytes (128 bits).
    /// </summary>
    public const int SaltByteSize = 16;

    /// <summary>
    /// Derived key length in bytes (256 bits).
    /// </summary>
    public const int KeyByteSize = 32;

    /// <summary>
    /// Field separator used inside the encoded hash string.
    /// </summary>
    private const char FieldSeparator = '$';

    /// <summary>
    /// Fixed salt used by the retired MD5 scheme; retained only so legacy credentials can be
    /// recognised during the re-hash-on-next-login migration.
    /// </summary>
    private const string LegacySalt = "TeleM3t3IS@lt";

    /// <summary>
    /// Hashes a plaintext password with PBKDF2-HMAC-SHA256 and a fresh random salt.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Generates a cryptographically random salt, derives a
    /// 256-bit subkey over <see cref="IterationCount"/> iterations and encodes algorithm,
    /// work factor, salt and subkey into one storable string.</para>
    /// <para><b>Flow:</b> validate input → generate salt → derive key → encode.</para>
    /// <para><b>Side Effects:</b> None. Two calls with the same password return different
    /// strings because the salt is random — never compare hashes with string equality, always
    /// use <see cref="Verify"/>.</para>
    /// </remarks>
    /// <param name="password">The plaintext password to hash.</param>
    /// <returns>The encoded hash string safe to persist in <c>BlogUser.LoginPass</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="password"/> is null or empty.</exception>
    public static string HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password must have a valid value.", nameof(password));

        var salt = RandomNumberGenerator.GetBytes(SaltByteSize);
        var subkey = DeriveKey(password, salt, IterationCount);

        return string.Join(
            FieldSeparator,
            HashPrefix,
            IterationCount.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(subkey));
    }

    /// <summary>
    /// Hashes a plaintext password with a caller-supplied salt so a deterministic value can be
    /// embedded in an idempotent seed script.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Identical to <see cref="HashPassword"/> but takes the salt
    /// instead of generating one, which lets <c>003-SeedData.sql</c> carry a fixed hash for the
    /// bootstrap administrator that re-running migrations reproduces exactly.</para>
    /// <para><b>Flow:</b> validate input → derive key with the given salt → encode.</para>
    /// <para><b>Side Effects:</b> None. Reusing a salt across accounts weakens the scheme, so
    /// this overload is for seed data only — application code calls <see cref="HashPassword"/>.</para>
    /// </remarks>
    /// <param name="password">The plaintext password to hash.</param>
    /// <param name="salt">The salt bytes to use; must be at least <see cref="SaltByteSize"/> bytes.</param>
    /// <returns>The encoded hash string.</returns>
    /// <exception cref="ArgumentException">Thrown when the password is empty or the salt is too short.</exception>
    public static string HashPasswordWithSalt(string password, byte[] salt)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password must have a valid value.", nameof(password));
        if (salt == null || salt.Length < SaltByteSize)
            throw new ArgumentException($"Salt must be at least {SaltByteSize} bytes.", nameof(salt));

        var subkey = DeriveKey(password, salt, IterationCount);

        return string.Join(
            FieldSeparator,
            HashPrefix,
            IterationCount.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(subkey));
    }

    /// <summary>
    /// Verifies a plaintext password against a stored credential, reporting whether the stored
    /// value must be upgraded to the current format.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Accepts three stored shapes — a current PBKDF2 hash, a
    /// legacy MD5 digest and legacy plaintext (the pre-REQ-NFR-023 seed). The two legacy shapes
    /// return <see cref="PasswordVerifyResult.SuccessNeedsRehash"/> on a match so callers can
    /// migrate the record without ever asking the user to re-enrol.</para>
    /// <para><b>Flow:</b> null-guard → dispatch on format → fixed-time comparison.</para>
    /// <para><b>Side Effects:</b> None; persistence of the upgraded hash is the caller's job.</para>
    /// </remarks>
    /// <param name="password">The plaintext password supplied at login.</param>
    /// <param name="storedHash">The value read from <c>BlogUser.LoginPass</c>.</param>
    /// <returns>The verification outcome.</returns>
    public static PasswordVerifyResult Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
            return PasswordVerifyResult.Failed;

        if (storedHash.StartsWith(HashPrefix + FieldSeparator, StringComparison.Ordinal))
            return VerifyPbkdf2(password, storedHash);

        return VerifyLegacy(password, storedHash);
    }

    /// <summary>
    /// Indicates whether a stored credential already uses the current hash format.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A cheap prefix test used by reporting and by migration
    /// scripts that need to skip already-upgraded rows.</para>
    /// <para><b>Flow:</b> prefix comparison.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="storedHash">The value read from <c>BlogUser.LoginPass</c>.</param>
    /// <returns><c>true</c> when the value is a current PBKDF2 hash; otherwise <c>false</c>.</returns>
    public static bool IsCurrentFormat(string storedHash)
    {
        return !string.IsNullOrEmpty(storedHash)
            && storedHash.StartsWith(HashPrefix + FieldSeparator, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reproduces the retired MD5 + fixed-salt digest so legacy credentials remain recognisable.
    /// </summary>
    /// <param name="password">The plaintext password.</param>
    /// <returns>The lowercase hexadecimal MD5 digest produced by the pre-REQ-NFR-002 scheme.</returns>
    internal static string ComputeLegacyDigest(string password)
    {
        var bytes = MD5.HashData(Encoding.UTF32.GetBytes(LegacySalt + password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Verifies a password against an encoded PBKDF2 hash.
    /// </summary>
    /// <param name="password">The plaintext password.</param>
    /// <param name="storedHash">The encoded PBKDF2 hash.</param>
    /// <returns>The verification outcome, requesting a re-hash when the work factor is stale.</returns>
    private static PasswordVerifyResult VerifyPbkdf2(string password, string storedHash)
    {
        var parts = storedHash.Split(FieldSeparator);
        if (parts.Length != 4)
            return PasswordVerifyResult.Failed;

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
            return PasswordVerifyResult.Failed;

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return PasswordVerifyResult.Failed;
        }

        var actual = DeriveKey(password, salt, iterations, expected.Length);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            return PasswordVerifyResult.Failed;

        return iterations < IterationCount || salt.Length < SaltByteSize
            ? PasswordVerifyResult.SuccessNeedsRehash
            : PasswordVerifyResult.Success;
    }

    /// <summary>
    /// Verifies a password against a pre-REQ-NFR-002 credential (MD5 digest or plaintext).
    /// </summary>
    /// <param name="password">The plaintext password.</param>
    /// <param name="storedHash">The legacy stored value.</param>
    /// <returns><see cref="PasswordVerifyResult.SuccessNeedsRehash"/> on a match; otherwise failure.</returns>
    private static PasswordVerifyResult VerifyLegacy(string password, string storedHash)
    {
        var legacyDigest = ComputeLegacyDigest(password);
        if (FixedTimeTextEquals(legacyDigest, storedHash))
            return PasswordVerifyResult.SuccessNeedsRehash;

        if (FixedTimeTextEquals(password, storedHash))
            return PasswordVerifyResult.SuccessNeedsRehash;

        return PasswordVerifyResult.Failed;
    }

    /// <summary>
    /// Derives a PBKDF2-HMAC-SHA256 subkey.
    /// </summary>
    /// <param name="password">The plaintext password.</param>
    /// <param name="salt">The salt bytes.</param>
    /// <param name="iterations">The iteration count.</param>
    /// <param name="keyLength">Derived key length in bytes; defaults to <see cref="KeyByteSize"/>.</param>
    /// <returns>The derived key bytes.</returns>
    private static byte[] DeriveKey(string password, byte[] salt, int iterations, int keyLength = KeyByteSize)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            keyLength);
    }

    /// <summary>
    /// Compares two strings without leaking their contents through timing.
    /// </summary>
    /// <param name="left">First value.</param>
    /// <param name="right">Second value.</param>
    /// <returns><c>true</c> when the byte representations match exactly.</returns>
    private static bool FixedTimeTextEquals(string left, string right)
    {
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left ?? string.Empty),
            Encoding.UTF8.GetBytes(right ?? string.Empty));
    }
}

/// <summary>
/// Outcome of a password verification attempt.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets the caller distinguish "correct password, but the stored hash is
/// obsolete" from a plain success so credentials can be upgraded silently (REQ-NFR-002).</para>
/// <para><b>Usage:</b> Treat any value other than <see cref="Failed"/> as an authenticated user.</para>
/// </remarks>
public enum PasswordVerifyResult
{
    /// <summary>The password does not match the stored credential.</summary>
    Failed = 0,

    /// <summary>The password matches and the stored hash is already current.</summary>
    Success = 1,

    /// <summary>The password matches but the stored hash must be replaced with a current one.</summary>
    SuccessNeedsRehash = 2
}
