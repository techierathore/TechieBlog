namespace BlogEngine.Services;

/// <summary>
/// Chooses, at every send, between really mailing and only logging.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The registered <see cref="IEmailService"/> (REQ-FN-033). The choice follows
/// one rule, <see cref="IMailServerSource"/>: a mail server on the Settings screen, else one in
/// configuration, else the log.</para>
/// <para><b>Code Flow:</b> read the mail server → <see cref="SmtpEmailService.SendViaAsync"/> with it,
/// or <see cref="ConsoleEmailService"/> when none is set.</para>
/// <para><b>Dependencies:</b> <see cref="IMailServerSource"/>, <see cref="SmtpEmailService"/>,
/// <see cref="ConsoleEmailService"/>.</para>
/// <para><b>Usage:</b> Inject <see cref="IEmailService"/>; never construct a transport directly.
/// The choice used to be made once from configuration only, so saving a mail server on the
/// Settings screen changed nothing.</para>
/// </remarks>
public class RoutingEmailService : IEmailService
{
    private readonly IMailServerSource mailServerSource;
    private readonly SmtpEmailService smtpEmailService;
    private readonly ConsoleEmailService consoleEmailService;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoutingEmailService"/> class.
    /// </summary>
    /// <param name="mailServerSource">Answers which mail server to use now.</param>
    /// <param name="smtpEmailService">The real transport.</param>
    /// <param name="consoleEmailService">The log-only transport.</param>
    public RoutingEmailService(
        IMailServerSource mailServerSource,
        SmtpEmailService smtpEmailService,
        ConsoleEmailService consoleEmailService)
    {
        this.mailServerSource = mailServerSource;
        this.smtpEmailService = smtpEmailService;
        this.consoleEmailService = consoleEmailService;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Real mail when a mail server is set; otherwise the link is logged.</para>
    /// <para><b>Flow:</b> read the mail server → delegate.</para>
    /// <para><b>Side Effects:</b> Sends mail or writes a log entry.</para>
    /// </remarks>
    public async Task SendPasswordResetEmail(string email, string resetUrl)
    {
        var choice = await mailServerSource.GetCurrentAsync().ConfigureAwait(false);
        if (!choice.IsAvailable)
        {
            await consoleEmailService.SendPasswordResetEmail(email, resetUrl).ConfigureAwait(false);
            return;
        }

        await smtpEmailService.SendPasswordResetEmail(email, resetUrl).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Real mail when a mail server is set; otherwise logged.</para>
    /// <para><b>Flow:</b> read the mail server once → send with that same answer.</para>
    /// <para><b>Side Effects:</b> Sends mail or writes a log entry.</para>
    /// </remarks>
    public async Task<Result> SendAsync(EmailMessage message)
    {
        var choice = await mailServerSource.GetCurrentAsync().ConfigureAwait(false);
        return choice.IsAvailable
            ? await smtpEmailService.SendViaAsync(message, choice).ConfigureAwait(false)
            : await consoleEmailService.SendAsync(message).ConfigureAwait(false);
    }
}
