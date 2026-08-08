using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace BlogModels;

/// <summary>
/// The application's cryptographic secrets, supplied by configuration at startup (REQ-NFR-027).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Replaces the two secrets that used to be hard-coded literals in
/// <see cref="AppConstants"/> — the JWT signing key and the AES key behind
/// <see cref="AppEncrypt"/>. Nothing in this class carries a value; every value arrives from
/// <see cref="IConfiguration"/>, so the repository never holds a usable credential again.</para>
///
/// <para><b>Code Flow:</b> each executable head calls <see cref="Initialise"/> once, immediately
/// after its <see cref="IConfiguration"/> exists and before any service resolves. Initialise reads
/// <see cref="JwtSigningKeyPath"/> and <see cref="EncryptionKeyPath"/>, rejects anything missing,
/// blank, too short or equal to a retired literal, and only then publishes the values. A head that
/// skips the call, or supplies a bad value, fails loudly — every accessor throws
/// <see cref="InvalidOperationException"/> and there is deliberately no fallback default.</para>
///
/// <para><b>Dependencies:</b> <c>Microsoft.Extensions.Configuration.Abstractions</c> only, which
/// arrives with the <c>Microsoft.AspNetCore.App</c> framework reference already on
/// <c>BlogModels</c>. No logging dependency, because a logger would be one careless template away
/// from writing a secret to disk.</para>
///
/// <para><b>Usage:</b> Host wiring calls <c>AppSecrets.Initialise(builder.Configuration)</c>;
/// consumers read <see cref="JwtSigningKey"/> or <see cref="EncryptionKey"/>. Supply the values
/// through user secrets (<c>dotnet user-secrets set JwtSigningKey "…"</c>) or the environment
/// (<c>JwtSigningKey=…</c>, <c>AppEncryptionKey=…</c>) — never through the committed
/// <c>appsettings.json</c>.</para>
///
/// <para><b>Rotation:</b> <see cref="SessionFingerprint"/> is a short, non-reversible digest of the
/// signing key. <see cref="AppConstants.AccessKey"/> and <see cref="AppConstants.RefreshKey"/> embed
/// it in the browser storage key names, so changing the signing key changes where a session would
/// have been stored and every browser that still holds a token issued under the previous key is
/// signed out.</para>
///
/// <para><b>DANGER — rotating <see cref="EncryptionKey"/> destroys data.</b> Every value already
/// encrypted by <see cref="AppEncrypt.EncryptText"/> becomes <b>permanently undecryptable</b> the
/// moment the key changes. There is no key versioning: ciphertext carries no identifier saying which
/// key produced it, so the application cannot fall back to the previous key and no recovery path
/// exists — the affected rows have to be re-entered by hand. In this schema that means the encrypted
/// <c>SiteSetting</c> rows: the SMTP password (<c>Smtp.Password</c>) and the cloud storage access key
/// (<c>Storage.CloudAccessKey</c>). The failure is also not loud: nothing breaks at startup, and the
/// first symptom is a <see cref="System.Security.Cryptography.CryptographicException"/> the next time
/// mail is sent or a cloud upload is attempted. <b>Anyone rotating this key must plan to re-enter
/// every encrypted setting through the admin settings screen immediately afterwards</b>, and should
/// treat it as a maintenance window rather than a configuration tweak. Rotating
/// <see cref="JwtSigningKeyPath"/> is cheap by comparison — it only signs sessions out — so do not
/// assume the two keys carry the same operational risk.</para>
///
/// <para><b>Current limitation — the session fingerprint is a workaround, not a design triumph.</b>
/// A signing key normally invalidates tokens because a token minted under the old key fails signature
/// validation. That is not what happens here: <c>SvcUtils.GetUserIDFromToken</c> reads the JWT with
/// <c>JwtSecurityTokenHandler.ReadJwtToken</c>, which decodes the token <b>without validating its
/// signature</b>, and session validity is established separately by looking the token up in the
/// <c>UserLogin</c> table. The signature is therefore never actually checked on read, so changing the
/// signing key on its own would invalidate nothing. Suffixing the browser storage keys with
/// <see cref="SessionFingerprint"/> is what makes a rotation bite — it moves the slot the token was
/// stored in, so the browser simply cannot present the old token any more. That is a real and
/// effective mitigation, but it works at the storage layer rather than the cryptographic one:
/// it is defeated by anything that presents a token from outside the browser's local storage.
/// <b>Verifying the signature on read remains outstanding work</b> and should not be considered
/// covered by this mechanism.</para>
/// </remarks>
public static class AppSecrets
{
    /// <summary>
    /// Configuration key holding the symmetric JWT signing key.
    /// </summary>
    /// <remarks>
    /// Flat and PascalCase with no separators, matching the existing <c>AppDbConString</c> key, so
    /// the same name works verbatim as an environment variable per the coding standards'
    /// "Environment Variables" rule.
    /// </remarks>
    public const string JwtSigningKeyPath = "JwtSigningKey";

    /// <summary>
    /// Configuration key holding the AES key used by <see cref="AppEncrypt"/>.
    /// </summary>
    /// <remarks>Named to the same convention as <see cref="JwtSigningKeyPath"/>.</remarks>
    public const string EncryptionKeyPath = "AppEncryptionKey";

    /// <summary>
    /// Shortest signing key accepted, in characters. HMAC-SHA256 needs at least 256 bits of key
    /// material, so anything shorter is rejected rather than silently weakening every token.
    /// </summary>
    public const int MinimumJwtSigningKeyLength = 32;

    /// <summary>
    /// Shortest encryption key accepted, in characters.
    /// </summary>
    public const int MinimumEncryptionKeyLength = 16;

    /// <summary>
    /// SHA-256 digests of the two secrets that shipped in source control before REQ-NFR-027.
    /// </summary>
    /// <remarks>
    /// Stored as digests rather than as the values themselves: the point of the requirement is that
    /// no usable secret remains anywhere in this repository, and a blocklist written in plaintext
    /// would put both of them straight back. The digests are enough to recognise and refuse either
    /// value if an operator pastes it into configuration to "keep things working".
    /// </remarks>
    private static readonly string[] RetiredSecretDigests =
    {
        "a87dd0cb0fd27f719ee8976418b4577fd9d75f5cb813769941c0a2483159ce26",
        "d26af67a5af0a46c59733eece0bb793338199b7532ab1e93a69eaeca252e5417"
    };

    private static string? jwtSigningKey;
    private static string? encryptionKey;
    private static string? sessionFingerprint;

    /// <summary>
    /// Whether <see cref="Initialise"/> has completed successfully in this process.
    /// </summary>
    public static bool IsInitialised => jwtSigningKey is not null;

    /// <summary>
    /// The symmetric key used to sign and, in future, validate JWTs.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Initialise"/> has not run in this process.
    /// </exception>
    public static string JwtSigningKey =>
        jwtSigningKey ?? throw BuildNotInitialisedError();

    /// <summary>
    /// The key from which <see cref="SaltEncryption"/> derives its AES key.
    /// </summary>
    /// <remarks>
    /// <b>Effectively permanent.</b> Changing this value renders every existing ciphertext
    /// undecryptable for good — there is no key versioning and no fallback to a previous key. See the
    /// rotation warning in the remarks on <see cref="AppSecrets"/> before changing it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Initialise"/> has not run in this process.
    /// </exception>
    public static string EncryptionKey =>
        encryptionKey ?? throw BuildNotInitialisedError();

    /// <summary>
    /// Eight lowercase hex characters derived from the signing key, safe to expose publicly.
    /// </summary>
    /// <remarks>
    /// A SHA-256 digest truncated to four bytes: it identifies which key generation is in force
    /// without revealing the key. Used to namespace the browser storage keys so a rotation signs
    /// every existing session out.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Initialise"/> has not run in this process.
    /// </exception>
    public static string SessionFingerprint =>
        sessionFingerprint ?? throw BuildNotInitialisedError();

    /// <summary>
    /// Loads both secrets from configuration, refusing to start when either is unusable.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Reads the two configuration paths, validates each one, computes
    /// <see cref="SessionFingerprint"/> and publishes the values. Validation rejects a missing or
    /// whitespace value, a value shorter than the minimum length for its purpose, and any of the
    /// literals that were previously committed to source control. There is no default: the
    /// requirement is that an absent secret stops the host rather than quietly weakening it.</para>
    /// <para><b>Flow:</b> read both paths → validate each → SHA-256 the signing key and take four
    /// bytes → assign the three static values as one group.</para>
    /// <para><b>Side Effects:</b> Sets process-wide state, but only after BOTH values have passed
    /// validation — a rejected configuration therefore leaves any previously loaded secrets intact
    /// rather than half-replacing them. Calling it a second time with good values replaces them,
    /// which is the in-process rotation path. Nothing is logged, and no exception message contains
    /// a secret — only the configuration key that failed.</para>
    /// </remarks>
    /// <param name="configuration">The host configuration to read the secrets from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Either secret is missing, blank, shorter than its minimum length, or a retired literal.
    /// </exception>
    public static void Initialise(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var signingKeyValue = Validate(
            configuration[JwtSigningKeyPath], JwtSigningKeyPath, MinimumJwtSigningKeyLength);
        var encryptionKeyValue = Validate(
            configuration[EncryptionKeyPath], EncryptionKeyPath, MinimumEncryptionKeyLength);

        jwtSigningKey = signingKeyValue;
        encryptionKey = encryptionKeyValue;
        sessionFingerprint = BuildFingerprint(signingKeyValue);
    }

    /// <summary>
    /// Derives the public fingerprint of any signing key, without touching process state.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The same one-way digest <see cref="Initialise"/> stores in
    /// <see cref="SessionFingerprint"/>. Exposed so callers — and the tests that prove a rotation
    /// really does move the browser storage keys — can compare two key generations without
    /// installing either of them.</para>
    /// <para><b>Flow:</b> forward to the private digest helper.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="signingKey">The key to fingerprint. Must be non-empty.</param>
    /// <returns>Eight lowercase hexadecimal characters.</returns>
    /// <exception cref="ArgumentException"><paramref name="signingKey"/> is null or empty.</exception>
    public static string ComputeFingerprint(string signingKey)
    {
        if (string.IsNullOrEmpty(signingKey))
            throw new ArgumentException("Signing key must have a value.", nameof(signingKey));

        return BuildFingerprint(signingKey);
    }

    /// <summary>
    /// Rejects a configured secret that cannot be used safely.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Enforces presence, a minimum length and the retired-literal
    /// blocklist. The message names the configuration path and how to supply it, never the value.</para>
    /// <para><b>Flow:</b> blank check → retired-literal check → length check → return trimmed value.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="value">The raw configuration value.</param>
    /// <param name="configurationPath">The path it was read from, used in the error message.</param>
    /// <param name="minimumLength">The shortest acceptable length for this secret.</param>
    /// <returns>The validated secret.</returns>
    /// <exception cref="InvalidOperationException">The value is unusable.</exception>
    private static string Validate(string? value, string configurationPath, int minimumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Required secret '{configurationPath}' is not configured. Set it through user " +
                $"secrets, an environment-specific settings file that is not committed, or the " +
                $"environment before starting the host. There is no default value by design.");
        }

        var trimmed = value.Trim();

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trimmed)))
            .ToLowerInvariant();
        if (RetiredSecretDigests.Contains(digest, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Required secret '{configurationPath}' is set to a value that was previously " +
                $"committed to source control and is therefore public. Generate a new random key " +
                $"and supply that instead.");
        }

        if (trimmed.Length < minimumLength)
        {
            throw new InvalidOperationException(
                $"Required secret '{configurationPath}' is too short: it must be at least " +
                $"{minimumLength} characters. Supply a randomly generated value of at least that " +
                $"length.");
        }

        return trimmed;
    }

    /// <summary>
    /// Builds the short public digest that identifies the current signing-key generation.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> SHA-256 over the UTF-8 bytes of the key, truncated to the first
    /// four bytes and rendered as lowercase hex. One-way and short enough to sit inside a browser
    /// storage key name.</para>
    /// <para><b>Flow:</b> UTF-8 encode → hash → take four bytes → hex → lowercase.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="value">The signing key to fingerprint.</param>
    /// <returns>Eight lowercase hexadecimal characters.</returns>
    private static string BuildFingerprint(string value)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(digest, 0, 4).ToLowerInvariant();
    }

    /// <summary>
    /// Builds the exception thrown when an accessor is used before the secrets are loaded.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Names both configuration paths and the wiring call that is
    /// missing, so a head that forgot to initialise says exactly what to add.</para>
    /// <para><b>Flow:</b> format the message.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>The exception to throw.</returns>
    private static InvalidOperationException BuildNotInitialisedError() =>
        new($"Application secrets have not been loaded. Call AppSecrets.Initialise(configuration) " +
            $"during host startup, with '{JwtSigningKeyPath}' and '{EncryptionKeyPath}' supplied " +
            $"by configuration.");
}
