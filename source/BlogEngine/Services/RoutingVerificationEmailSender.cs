namespace BlogEngine.Services;

/// <summary>
/// Chooses, at every send, between mailing a confirmation link and only logging it.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The registered <see cref="IVerificationEmailSender"/> (REQ-FN-048). It follows
/// the same rule as <see cref="RoutingEmailService"/>, from <see cref="IMailServerSource"/>, so the two
/// can never disagree about whether mail is really sent.</para>
/// <para><b>Code Flow:</b> read the mail server → <see cref="VerificationEmailSender"/> when one is set,
/// else <see cref="LoggingVerificationEmailSender"/>.</para>
/// <para><b>Dependencies:</b> <see cref="IMailServerSource"/> and the two senders.</para>
/// <para><b>Usage:</b> In practice the logging branch is not reached from the public forms: the rating,
/// comment and subscribe services ask <c>IEmailVerificationService.IsMailUnavailableAsync</c> first and
/// tell the reader that confirmation is not available, instead of parking a submission behind a link
/// nobody will receive.</para>
/// </remarks>
public class RoutingVerificationEmailSender : IVerificationEmailSender
{
    private readonly IMailServerSource mailServerSource;
    private readonly VerificationEmailSender mailingSender;
    private readonly LoggingVerificationEmailSender loggingSender;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoutingVerificationEmailSender"/> class.
    /// </summary>
    /// <param name="mailServerSource">Answers which mail server to use now.</param>
    /// <param name="mailingSender">Mails the link.</param>
    /// <param name="loggingSender">Logs the link.</param>
    public RoutingVerificationEmailSender(
        IMailServerSource mailServerSource,
        VerificationEmailSender mailingSender,
        LoggingVerificationEmailSender loggingSender)
    {
        this.mailServerSource = mailServerSource;
        this.mailingSender = mailingSender;
        this.loggingSender = loggingSender;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Mails when a mail server is set; otherwise logs.</para>
    /// <para><b>Flow:</b> read the mail server → delegate.</para>
    /// <para><b>Side Effects:</b> Sends mail or writes a log entry. A failed mail throws, so the caller
    /// can undo the pending submission.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The confirmation mail could not be delivered.</exception>
    public async Task SendVerificationEmailAsync(string toEmail, string displayName, string purpose, string verificationUrl)
    {
        var choice = await mailServerSource.GetCurrentAsync().ConfigureAwait(false);
        IVerificationEmailSender sender = choice.IsAvailable ? mailingSender : loggingSender;
        await sender.SendVerificationEmailAsync(toEmail, displayName, purpose, verificationUrl).ConfigureAwait(false);
    }
}
