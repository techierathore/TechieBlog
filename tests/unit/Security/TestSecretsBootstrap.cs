using BlogModels;
using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;

namespace TechieBlog.Tests.Security;

/// <summary>
/// Installs throwaway cryptographic secrets before any test in the assembly runs (REQ-NFR-027).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <see cref="AppEncrypt"/> and <see cref="AppConstants.AccessKey"/> now read
/// <see cref="AppSecrets"/> instead of a literal, and <see cref="AppSecrets"/> deliberately has no
/// fallback default. Without this bootstrap every suite that encrypts a login envelope would fail
/// with "Application secrets have not been loaded" — which is the production behaviour working as
/// intended, not a defect to work around in each test.</para>
///
/// <para><b>Code Flow:</b> the runtime calls <see cref="Initialise"/> once, when the test assembly
/// is loaded and before the first test executes, so no test needs to remember to arrange it.</para>
///
/// <para><b>Dependencies:</b> <see cref="AppSecrets"/> and an in-memory
/// <see cref="IConfiguration"/>.</para>
///
/// <para><b>Usage:</b> Nothing calls this by hand. The values below are test fixtures with no
/// production meaning; they are not the deployment keys and must never be used as such. Tests that
/// need to reason about a different key generation use
/// <see cref="AppSecrets.ComputeFingerprint"/>, which is pure — no test replaces the installed
/// secrets, because the suite runs classes in parallel and a swap would be visible to every other
/// test in flight.</para>
/// </remarks>
public static class TestSecretsBootstrap
{
    /// <summary>
    /// The signing key installed for the duration of the test run.
    /// </summary>
    public const string TestSigningKey = "unit-test-signing-key-not-a-real-secret-0123456789";

    /// <summary>
    /// The AES key installed for the duration of the test run.
    /// </summary>
    public const string TestEncryptionKey = "unit-test-encryption-key-not-a-real-secret";

    /// <summary>
    /// Loads the test secrets into <see cref="AppSecrets"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Builds a two-entry in-memory configuration using the same keys
    /// the host reads and hands it to <see cref="AppSecrets.Initialise"/>, so the tests exercise the
    /// real loading path rather than a back door.</para>
    /// <para><b>Flow:</b> build configuration → initialise.</para>
    /// <para><b>Side Effects:</b> Sets process-wide secret state for the test assembly.</para>
    /// </remarks>
    [ModuleInitializer]
    public static void Initialise()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AppSecrets.JwtSigningKeyPath] = TestSigningKey,
                [AppSecrets.EncryptionKeyPath] = TestEncryptionKey
            })
            .Build();

        AppSecrets.Initialise(configuration);
    }
}
