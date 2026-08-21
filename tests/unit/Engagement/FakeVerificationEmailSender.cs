using BlogEngine.Services;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// Captures the confirmation links that would have been emailed.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets a test read the link the service built and follow it, which is how
/// the "a token works exactly once" scenario is exercised end to end.</para>
/// <para><b>Code Flow:</b> Every call appends to <see cref="SentUrls"/>.</para>
/// <para><b>Dependencies:</b> None.</para>
/// <para><b>Usage:</b> Set <see cref="IsFailing"/> to simulate a transport outage.</para>
/// </remarks>
public class FakeVerificationEmailSender : IVerificationEmailSender
{
    /// <summary>
    /// Gets the verification URLs handed to this sender, in order.
    /// </summary>
    public List<string> SentUrls { get; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether sending should throw.
    /// </summary>
    public bool IsFailing { get; set; }

    /// <inheritdoc />
    public Task SendVerificationEmailAsync(string toEmail, string displayName, string purpose, string verificationUrl)
    {
        if (IsFailing)
            throw new InvalidOperationException("The fake mail transport is unavailable.");

        SentUrls.Add(verificationUrl);
        return Task.CompletedTask;
    }
}
