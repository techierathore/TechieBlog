using BlogModels;
using Microsoft.Extensions.Configuration;

namespace TechieBlog.Tests.Security;

/// <summary>
/// Supplies the process-wide cryptographic secrets the security suites need (REQ-NFR-027).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <see cref="AppSecrets"/> deliberately has no fallback default — a head
/// that does not call <c>Initialise</c> fails loudly, which is the point of moving the keys out of
/// source. A test process is a head like any other, so the suites that build a sign-in envelope
/// through <see cref="AppEncrypt"/> have to supply their own values or every one of them dies on
/// "Application secrets have not been loaded".</para>
/// <para><b>Business Logic:</b> the values below are test fixtures, not credentials: they exist
/// only inside the test process, are never written to configuration, and are chosen to satisfy the
/// minimum lengths and to avoid the retired literals <c>AppSecrets</c> rejects.</para>
/// <para><b>Usage:</b> call <see cref="EnsureInitialised"/> from a test class constructor. It is
/// idempotent and safe under xUnit's parallel class execution, so several suites may call it.</para>
/// </remarks>
internal static class TestAppSecrets
{
    /// <summary>Guards the one-time initialisation against parallel test classes.</summary>
    private static readonly Lock InitialisationGate = new();

    /// <summary>Signing key fixture — long enough for HMAC-SHA256 and not a retired literal.</summary>
    private const string SigningKeyFixture = "TechieBlogUnitTestSigningKeyMaterial2026";

    /// <summary>Encryption key fixture — long enough for the AES derivation.</summary>
    private const string EncryptionKeyFixture = "TechieBlogUnitTestEncryptionKey2026";

    /// <summary>
    /// Loads the test secrets once per process.
    /// </summary>
    /// <remarks>
    /// <para><b>Flow:</b> fast path on the published flag → lock → re-check → build an in-memory
    /// configuration → initialise.</para>
    /// <para><b>Side Effects:</b> Sets process-wide state on <see cref="AppSecrets"/>.</para>
    /// </remarks>
    public static void EnsureInitialised()
    {
        if (AppSecrets.IsInitialised)
        {
            return;
        }

        lock (InitialisationGate)
        {
            if (AppSecrets.IsInitialised)
            {
                return;
            }

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [AppSecrets.JwtSigningKeyPath] = SigningKeyFixture,
                    [AppSecrets.EncryptionKeyPath] = EncryptionKeyFixture
                })
                .Build();

            AppSecrets.Initialise(configuration);
        }
    }
}
