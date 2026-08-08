using BlogEngine.Services;
using BlogModels;

namespace TechieBlog.Tests.TestDoubles;

/// <summary>
/// In-memory <see cref="IEmailService"/> that captures every message instead of sending it.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets newsletter tests assert what was actually addressed and what each
/// message carried — above all the unsubscribe link that must appear in every message.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>The service under test calls <see cref="SendAsync"/>.</item>
///   <item>The message is appended to <see cref="SentMessages"/>.</item>
///   <item><see cref="FailForAddress"/>, when set, makes that one address fail so a partial-failure
///         run can be exercised.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <c>BlogEngine.Services.IEmailService</c>.</para>
///
/// <para><b>Usage:</b> Construct, pass to the service, then assert over
/// <see cref="SentMessages"/>.</para>
/// </remarks>
public class RecordingEmailService : IEmailService
{
    /// <summary>
    /// Every message handed to the transport, in send order.
    /// </summary>
    public List<EmailMessage> SentMessages { get; } = new List<EmailMessage>();

    /// <summary>
    /// When set, sends to this address return a failure instead of succeeding.
    /// </summary>
    public string FailForAddress { get; set; } = string.Empty;

    /// <summary>
    /// When true, every send fails.
    /// </summary>
    public bool DoesEverySendFail { get; set; }

    /// <inheritdoc />
    public Task SendPasswordResetEmail(string email, string resetUrl)
    {
        SentMessages.Add(new EmailMessage { ToAddress = email, Subject = "Reset your password", TextBody = resetUrl });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Result> SendAsync(EmailMessage message)
    {
        SentMessages.Add(message);

        if (DoesEverySendFail)
            return Task.FromResult(Result.Failure("Simulated transport failure."));

        var isFailingAddress = !string.IsNullOrEmpty(FailForAddress)
            && string.Equals(FailForAddress, message.ToAddress, StringComparison.OrdinalIgnoreCase);

        return Task.FromResult(isFailingAddress
            ? Result.Failure("Simulated transport failure.")
            : Result.Success());
    }
}
