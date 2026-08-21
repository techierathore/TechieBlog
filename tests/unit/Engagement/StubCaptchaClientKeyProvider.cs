using BlogEngine.Services;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// Client-key provider whose answer a test sets directly. [REQ-NFR-024]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets a test act as two different clients without standing up an HTTP
/// context, which is what proves the caps are per client rather than global.</para>
/// <para><b>Code Flow:</b> <see cref="GetClientKey"/> returns <see cref="ClientKey"/> verbatim.</para>
/// <para><b>Dependencies:</b> None.</para>
/// <para><b>Usage:</b> Assign <see cref="ClientKey"/> between calls to switch identity.</para>
/// </remarks>
public class StubCaptchaClientKeyProvider : ICaptchaClientKeyProvider
{
    /// <summary>
    /// Gets or sets the key handed to the limiter.
    /// </summary>
    public string ClientKey { get; set; } = "client-one";

    /// <inheritdoc />
    public string GetClientKey() => ClientKey;
}
