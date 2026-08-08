using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BlogModels;

/// <summary>
/// Reversible AES-CBC string encryption keyed by a passphrase.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Protects values that must be read back later — the opaque service token
/// built by <see cref="AppEncrypt.GetNewToken"/> is the only live consumer. This is NOT password
/// storage: passwords are one-way hashed by <see cref="PasswordHasher"/> and must never be routed
/// through here.</para>
///
/// <para><b>Code Flow:</b> The passphrase is SHA-512'd and its first 24 bytes become a 192-bit AES
/// key; a fresh random IV is generated per call and prepended to the ciphertext, so the same
/// plaintext encrypts to a different Base64 string every time. <see cref="Decrypt"/> splits the IV
/// back off the front before decrypting.</para>
///
/// <para><b>Dependencies:</b> <c>System.Security.Cryptography</c> only. The default
/// <see cref="System.Security.Cryptography.Aes"/> mode (CBC with PKCS7 padding) is used.</para>
///
/// <para><b>Usage:</b> Both methods are extension methods on <see cref="string"/>; call as
/// <c>value.Encrypt(key)</c>. Ciphertext is only portable across processes that share the same
/// passphrase — see the key-rotation warning on <see cref="AppSecrets.EncryptionKey"/>. Note the
/// construction is unauthenticated (no MAC), so it provides confidentiality but not tamper
/// detection; do not rely on a successful <see cref="Decrypt"/> as proof a value is genuine.</para>
/// </remarks>
public static class SaltEncryption
{
    /// <summary>
    /// Encrypts a string under a passphrase and returns IV-prefixed Base64 ciphertext.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Derives a 192-bit AES key from <paramref name="key"/> via
    /// SHA-512, encrypts <paramref name="text"/> under a per-call random IV, and returns
    /// <c>Base64(IV || ciphertext)</c> so <see cref="Decrypt"/> needs nothing but the same
    /// passphrase.</para>
    /// <para><b>Flow:</b> validate both arguments → SHA-512 the key, take 24 bytes → AES encrypt
    /// through a <see cref="System.Security.Cryptography.CryptoStream"/> → concatenate IV and
    /// ciphertext → Base64.</para>
    /// <para><b>Side Effects:</b> None, but the output is non-deterministic because the IV is
    /// random — never compare two ciphertexts for equality to test whether the plaintexts match.</para>
    /// </remarks>
    /// <param name="text">The plaintext to protect. Must be non-empty.</param>
    /// <param name="key">The passphrase the key is derived from. Must be non-empty.</param>
    /// <returns>Base64 of the random IV followed by the ciphertext.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> or <paramref name="text"/> is null or empty.
    /// </exception>
    public static string Encrypt(this string text, string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key must have valid value.", nameof(key));
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("The text must have valid value.", nameof(text));

        var buffer = Encoding.UTF8.GetBytes(text);
        var aesKey = new byte[24];
        using (var hash = SHA512.Create())
        {
            Buffer.BlockCopy(hash.ComputeHash(Encoding.UTF8.GetBytes(key)), 0, aesKey, 0, 24);
        }

        using var aes = Aes.Create();
        aes.Key = aesKey;

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var resultStream = new MemoryStream();
        using (var aesStream = new CryptoStream(resultStream, encryptor, CryptoStreamMode.Write))
        using (var plainStream = new MemoryStream(buffer))
        {
            plainStream.CopyTo(aesStream);
        }

        var result = resultStream.ToArray();
        var combined = new byte[aes.IV.Length + result.Length];
        Array.ConstrainedCopy(aes.IV, 0, combined, 0, aes.IV.Length);
        Array.ConstrainedCopy(result, 0, combined, aes.IV.Length, result.Length);

        return Convert.ToBase64String(combined);
    }

    /// <summary>
    /// Reverses <see cref="Encrypt"/>, recovering the plaintext from IV-prefixed Base64 ciphertext.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Re-derives the same AES key from <paramref name="key"/>, lifts
    /// the IV off the front of the decoded blob and decrypts the remainder. Only input produced by
    /// <see cref="Encrypt"/> under the identical passphrase can round-trip.</para>
    /// <para><b>Flow:</b> validate both arguments → Base64-decode → SHA-512 the key, take 24 bytes →
    /// split IV from ciphertext → AES decrypt → UTF-8 decode.</para>
    /// <para><b>Side Effects:</b> None. Because the ciphertext is unauthenticated, a wrong
    /// passphrase or a tampered blob surfaces as a padding/format exception rather than a clean
    /// "invalid" result — callers that accept untrusted input must catch, not just null-check.</para>
    /// </remarks>
    /// <param name="encryptedText">Base64 IV-prefixed ciphertext from <see cref="Encrypt"/>.</param>
    /// <param name="key">The same passphrase used to encrypt. Must be non-empty.</param>
    /// <returns>The original UTF-8 plaintext.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> or <paramref name="encryptedText"/> is null or empty.
    /// </exception>
    /// <exception cref="FormatException">
    /// <paramref name="encryptedText"/> is not valid Base64.
    /// </exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The passphrase does not match the one used to encrypt, or the ciphertext has been altered.
    /// </exception>
    public static string Decrypt(this string encryptedText, string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key must have valid value.", nameof(key));
        if (string.IsNullOrEmpty(encryptedText))
            throw new ArgumentException("The encrypted text must have valid value.", nameof(encryptedText));

        var combined = Convert.FromBase64String(encryptedText);
        var buffer = new byte[combined.Length];
        var aesKey = new byte[24];
        using (var hash = SHA512.Create())
        {
            Buffer.BlockCopy(hash.ComputeHash(Encoding.UTF8.GetBytes(key)), 0, aesKey, 0, 24);
        }

        using var aes = Aes.Create();
        aes.Key = aesKey;

        var iv = new byte[aes.IV.Length];
        var ciphertext = new byte[buffer.Length - iv.Length];

        Array.ConstrainedCopy(combined, 0, iv, 0, iv.Length);
        Array.ConstrainedCopy(combined, iv.Length, ciphertext, 0, ciphertext.Length);

        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        using var resultStream = new MemoryStream();
        using (var aesStream = new CryptoStream(resultStream, decryptor, CryptoStreamMode.Write))
        using (var plainStream = new MemoryStream(ciphertext))
        {
            plainStream.CopyTo(aesStream);
        }

        return Encoding.UTF8.GetString(resultStream.ToArray());
    }
}
/// <summary>
/// Application-wide cryptography façade: hashing for passwords, reversible encryption for tokens.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Gives callers a single entry point that already knows which key to use, so
/// no call site has to reach for <see cref="AppSecrets.EncryptionKey"/> itself. It also draws the
/// line that matters: <see cref="CreateHash"/> is one-way and is what passwords go through;
/// <see cref="EncryptText"/> is reversible and is what tokens go through.</para>
///
/// <para><b>Code Flow:</b> Every member is a thin forward — to <see cref="PasswordHasher"/> for
/// hashing, to <see cref="SaltEncryption"/> for encryption. No state is held.</para>
///
/// <para><b>Dependencies:</b> <see cref="SaltEncryption"/>, <see cref="PasswordHasher"/> and the
/// configured <see cref="AppSecrets.EncryptionKey"/> (REQ-NFR-027 — it used to be a hard-coded
/// literal in <see cref="AppConstants"/>). Every method here therefore throws
/// <see cref="InvalidOperationException"/> until the host has called
/// <see cref="AppSecrets.Initialise"/>.</para>
///
/// <para><b>Usage:</b> Never encrypt a password with <see cref="EncryptText"/> — a reversible
/// value is a stored plaintext password in every way that matters to an attacker who reaches the
/// database. Use <see cref="CreateHash"/> and verify with <see cref="PasswordHasher.Verify"/>.</para>
/// </remarks>
public static class AppEncrypt
{
    /// <summary>
    /// Encrypts a value under the application passphrase so it can be decrypted again later.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Applies <see cref="SaltEncryption.Encrypt"/> with
    /// <see cref="AppSecrets.EncryptionKey"/>. Intended for opaque tokens and similar values the
    /// application itself must read back — not for passwords, which must be one-way hashed.</para>
    /// <para><b>Flow:</b> forward to <see cref="SaltEncryption.Encrypt"/>.</para>
    /// <para><b>Side Effects:</b> None. Output differs on every call (random IV).</para>
    /// </remarks>
    /// <param name="stringToEncrypt">The plaintext to protect. Must be non-empty.</param>
    /// <returns>Base64 IV-prefixed ciphertext, decryptable by <see cref="DecryptText"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="stringToEncrypt"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="AppSecrets.Initialise"/> has not run in this process.
    /// </exception>
    public static string EncryptText(string stringToEncrypt)
    {
        return SaltEncryption.Encrypt(stringToEncrypt, AppSecrets.EncryptionKey);
    }

    /// <summary>
    /// Recovers a value previously protected by <see cref="EncryptText"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Applies <see cref="SaltEncryption.Decrypt"/> with
    /// <see cref="AppSecrets.EncryptionKey"/>. Only round-trips ciphertext produced by this same
    /// application while the key was unchanged.</para>
    /// <para><b>Flow:</b> forward to <see cref="SaltEncryption.Decrypt"/>.</para>
    /// <para><b>Side Effects:</b> None. Throws rather than returning null on bad input — see the
    /// exceptions on <see cref="SaltEncryption.Decrypt"/>.</para>
    /// </remarks>
    /// <param name="stringToDecrypt">Base64 IV-prefixed ciphertext from <see cref="EncryptText"/>.</param>
    /// <returns>The original plaintext.</returns>
    /// <exception cref="ArgumentException"><paramref name="stringToDecrypt"/> is null or empty.</exception>
    /// <exception cref="FormatException">The input is not valid Base64.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The ciphertext was produced under a different key, or has been altered.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="AppSecrets.Initialise"/> has not run in this process.
    /// </exception>
    public static string DecryptText(string stringToDecrypt)
    {
        return SaltEncryption.Decrypt(stringToDecrypt, AppSecrets.EncryptionKey);
    }

    /// <summary>
    /// Produces a storable, industry-standard salted password hash (REQ-NFR-002, BRD-79).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Delegates to <see cref="PasswordHasher.HashPassword"/>
    /// (PBKDF2-HMAC-SHA256, 210 000 iterations, 128-bit random salt). The retired MD5 +
    /// fixed-salt implementation lives on only inside <see cref="PasswordHasher"/> so existing
    /// credentials can be recognised and re-hashed on the next successful login.</para>
    /// <para><b>Flow:</b> forward to <see cref="PasswordHasher.HashPassword"/>.</para>
    /// <para><b>Side Effects:</b> None. The result differs on every call because the salt is
    /// random — verify with <see cref="PasswordHasher.Verify"/>, never with string equality.</para>
    /// </remarks>
    /// <param name="password">The plaintext password to hash.</param>
    /// <returns>The encoded PBKDF2 hash to store in <c>BlogUser.LoginPass</c>.</returns>
    public static string CreateHash(string password)
    {
        return PasswordHasher.HashPassword(password);
    }

    /// <summary>
    /// Mints an opaque, timestamp-derived service token.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Builds a deterministic seed from <paramref name="generatedOn"/> —
    /// <c>yyMMdd</c> + an invariant-culture <c>yyyyMMddTHHmmss</c> stamp + a fixed literal — and
    /// encrypts it under <see cref="AppSecrets.EncryptionKey"/>. The token therefore encodes its own
    /// issue time, which a holder of the key can read back out.</para>
    /// <para><b>Flow:</b> format the date twice → concatenate with the fixed suffix → encrypt.</para>
    /// <para><b>Side Effects:</b> None; nothing is persisted. Each call returns a different string
    /// even for the same <paramref name="generatedOn"/> because the underlying IV is random, so the
    /// result cannot be used as a lookup key.</para>
    /// <para><b>Caution:</b> the seed contains no random component beyond the IV and no user
    /// identity, so this is an identifier, not a credential — it must not be treated as proof of
    /// authentication. The JWT path in <c>AuthSvc</c> is what actually authenticates a caller.</para>
    /// </remarks>
    /// <param name="generatedOn">The issue timestamp to embed in the token.</param>
    /// <returns>Base64 IV-prefixed ciphertext of the seed.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="AppSecrets.Initialise"/> has not run in this process.
    /// </exception>
    public static string GetNewToken(DateTime generatedOn)
    {
        var tokenSeed = generatedOn.ToString("yyMMdd")
            + generatedOn.ToString("yyyyMMddTHHmmss", CultureInfo.InvariantCulture)
            + "71003502";
        return tokenSeed.Encrypt(AppSecrets.EncryptionKey);
    }
}
