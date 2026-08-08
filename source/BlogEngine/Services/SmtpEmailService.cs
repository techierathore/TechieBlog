using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Real SMTP email transport built on the .NET base class library.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Replaces <c>ConsoleEmailService</c> in any environment with mail
/// configured (REQ-FN-033), which unblocks newsletter dispatch (REQ-FN-032) and double opt-in
/// verification (REQ-FN-048).</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Settings are read once in the constructor from the <c>EmailSettings</c> configuration
///         section — never from a shipped <c>appsettings.json</c>, since this repository is a
///         clone-and-own template. Supply them through user secrets or environment configuration.</item>
///   <item>Each send builds a fresh <c>MailMessage</c> and <c>SmtpClient</c>; <c>SmtpClient</c> is
///         not thread-safe, and a per-send instance is what makes concurrent newsletter delivery
///         safe.</item>
///   <item>Every failure is logged with the recipient address before being surfaced — a bulk send
///         records the address and continues, a password reset rethrows so the caller reports it.</item>
/// </list>
///
/// <para><b>Transport choice:</b> <c>System.Net.Mail.SmtpClient</c> from the BCL, not MailKit. It
/// covers everything this project needs — host, port, STARTTLS/implicit TLS, credentials, timeout —
/// and adds no package to a template whose selling point is a short dependency list. Swap in MailKit
/// behind this same <c>IEmailService</c> only if OAuth2/XOAUTH2 authentication is ever required.</para>
///
/// <para><b>THIS ONE ACTUALLY SENDS MAIL.</b> Three <c>IEmailService</c>-shaped components exist
/// and only this one reaches a mail server. Confusing them is the single most expensive mistake
/// available in this area — a "successful" test against a development sender proves nothing about
/// deliverability, and conversely a change that looks harmless here mails real people.</para>
/// <list type="table">
///   <item>
///     <term><see cref="SmtpEmailService"/> (this class)</term>
///     <description>Opens a TCP connection to a real SMTP server and delivers. Selected whenever
///     <c>EmailSettings:SmtpHost</c> has a value — including in Development, so a developer who
///     sets a host in user secrets <b>will</b> send genuine mail to whatever addresses the
///     subscriber table holds.</description>
///   </item>
///   <item>
///     <term><c>ConsoleEmailService</c> (sibling file)</term>
///     <description>The fallback when no host is configured. Writes the message to the log and
///     returns success. Nothing leaves the machine. "Sent" in the log means <i>formatted</i>, not
///     <i>delivered</i>.</description>
///   </item>
///   <item>
///     <term><c>LoggingVerificationEmailSender</c> (sibling file)</term>
///     <description>The development stand-in for <c>IVerificationEmailSender</c>. It logs the
///     confirmation <b>link</b> so a developer can click it without a mailbox — which also means
///     that in any environment where it is registered, <b>anyone who can read the log can confirm
///     anyone else's email address</b>. It must never be registered in production.</description>
///   </item>
/// </list>
///
/// <para><b>Credentials, encryption at rest, and the key-rotation consequence.</b> Mail
/// credentials exist in two places in this codebase and they behave differently:</para>
/// <list type="bullet">
///   <item><b>Configuration (what this class reads).</b> <c>EmailSettings:Password</c> comes from
///     user secrets or environment configuration and is held in plaintext by the configuration
///     provider — never in a committed <c>appsettings.json</c>.</item>
///   <item><b>Site settings (what the admin Settings screen writes).</b> The SMTP password entered
///     there is persisted as a <c>SiteSetting</c> row and <b>encrypted at rest</b> by
///     <c>AppEncrypt</c> under the AES key configured as <c>AppEncryptionKey</c> (see
///     <c>AppSecrets</c>). <b>Rotating that key permanently destroys every existing ciphertext</b>:
///     there is no key versioning, the ciphertext carries no identifier saying which key produced
///     it, and no fallback to the previous key exists. The failure is silent at startup — the first
///     symptom is a decryption error the next time mail is sent. <b>Anyone rotating
///     <c>AppEncryptionKey</c> must re-enter the SMTP password (and the cloud storage access key)
///     through the admin Settings screen immediately afterwards</b>, and should treat the rotation
///     as a maintenance window rather than a configuration tweak.</item>
/// </list>
///
/// <para><b>Known gap — this class does not read the site settings.</b> Its constructor reads
/// <c>IConfiguration</c> only, once, at construction. The SMTP values an administrator saves on the
/// Settings screen are therefore encrypted, stored, and <i>not used by the sender</i>; changing
/// them has no effect on delivery, and only a configuration change plus a restart does. That
/// contradicts the intent documented on <c>BlogModels.Models.SmtpSettings</c> ("the sender calls
/// <c>ISiteSettingsService.GetSmtpSettingsAsync</c> per send"), which no implementation currently
/// honours. Tracked as a defect; do not assume the admin screen controls mail until it is
/// closed.</para>
///
/// <para><b>Dependencies:</b> <c>IConfiguration</c> for settings,
/// <c>ILogger&lt;SmtpEmailService&gt;</c> for the mandatory failure logging.</para>
///
/// <para><b>Usage:</b> Registered transient by <c>BlogSvcInitializer</c> only when
/// <c>EmailSettings:SmtpHost</c> has a value; otherwise the console fallback is used. Requires no
/// authorization of its own — it is infrastructure, and every caller (password reset, verification,
/// newsletter dispatch) owns the decision about whether the recipient should be mailed at
/// all.</para>
/// </remarks>
public class SmtpEmailService : IEmailService
{
    /// <summary>
    /// Configuration key whose presence switches the engine from the console fallback to SMTP.
    /// </summary>
    public const string SmtpHostKey = "EmailSettings:SmtpHost";

    private const int DefaultSmtpPort = 587;
    private const int DefaultTimeoutSeconds = 30;

    private readonly ILogger<SmtpEmailService> logger;
    private readonly string smtpHost;
    private readonly int smtpPort;
    private readonly bool isSslEnabled;
    private readonly string userName;
    private readonly string password;
    private readonly string fromAddress;
    private readonly string fromName;
    private readonly int timeoutMilliseconds;

    /// <summary>
    /// Initializes the SMTP transport from configuration.
    /// </summary>
    /// <remarks>
    /// Reads <c>EmailSettings:SmtpHost</c>, <c>SmtpPort</c>, <c>EnableSsl</c>, <c>UserName</c>,
    /// <c>Password</c>, <c>FromAddress</c>, <c>FromName</c> and <c>TimeoutSeconds</c>. Credentials
    /// must come from user secrets or environment configuration — never a committed settings file.
    /// </remarks>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="logger">Logger used for delivery diagnostics.</param>
    public SmtpEmailService(IConfiguration configuration, ILogger<SmtpEmailService> logger)
    {
        this.logger = logger;
        smtpHost = configuration[SmtpHostKey] ?? string.Empty;
        smtpPort = ReadInt(configuration, "EmailSettings:SmtpPort", DefaultSmtpPort);
        isSslEnabled = ReadBool(configuration, "EmailSettings:EnableSsl", true);
        userName = configuration["EmailSettings:UserName"] ?? string.Empty;
        password = configuration["EmailSettings:Password"] ?? string.Empty;
        fromAddress = configuration["EmailSettings:FromAddress"] ?? userName;
        fromName = configuration["EmailSettings:FromName"] ?? "TechieBlog";
        timeoutMilliseconds = ReadInt(configuration, "EmailSettings:TimeoutSeconds", DefaultTimeoutSeconds) * 1000;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Composes the reset message and delivers it. Unlike
    /// <see cref="SendAsync"/> this member <b>throws</b> on failure rather than returning a
    /// <c>Result</c>, and that asymmetry is deliberate: a password reset that silently fails leaves
    /// the user waiting forever for a mail that will never arrive, so the caller must be forced to
    /// notice. The convention in this codebase is that expected failures are returned and
    /// unexpected ones throw after being logged with context — an undeliverable reset is treated as
    /// the latter.</para>
    /// <para><b>Flow:</b> build the message → delegate to <see cref="SendAsync"/> → return on
    /// success, or log the recipient and the reason and throw.</para>
    /// <para><b>Side Effects:</b> <b>Sends a real email</b> to <paramref name="email"/> containing
    /// a working reset link. Writes an error log line on failure. Performs no rate limiting,
    /// throttling or address-existence check of its own — the calling service owns all of that, and
    /// must in particular avoid revealing through its own response whether the address is
    /// registered.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The message could not be delivered. The inner reason has already been logged with the
    /// recipient address.
    /// </exception>
    public async Task SendPasswordResetEmail(string email, string resetUrl)
    {
        var message = new EmailMessage
        {
            ToAddress = email,
            Subject = "Reset your password",
            HtmlBody = BuildPasswordResetBody(resetUrl),
            TextBody = $"Reset your password using this link: {resetUrl}"
        };

        var result = await SendAsync(message).ConfigureAwait(false);
        if (result.IsSuccess)
            return;

        logger.LogError("Password reset email to {Email} was not delivered: {Error}", email, result.ErrorMessage);
        throw new InvalidOperationException($"Password reset email could not be delivered: {result.ErrorMessage}");
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> The general-purpose send. Every failure — a rejection by the
    /// server, a timeout, a missing configuration value — comes back as a failed <c>Result</c>
    /// rather than an exception, because the main caller is newsletter dispatch, which must record
    /// one address's failure and carry on to the next rather than abandoning the run.</para>
    /// <para><b>Flow:</b> validate the message and the configuration → build a fresh
    /// <c>MailMessage</c> and <c>SmtpClient</c> → send → return success, or catch, log and convert
    /// to a failure.</para>
    /// <para><b>Side Effects:</b> <b>Sends a real email.</b> Opens an outbound TCP connection per
    /// call and disposes it; a new <c>SmtpClient</c> per send is what makes concurrent dispatch safe,
    /// since the type is not thread-safe. Logs one information line per delivery including the
    /// recipient address and subject, and an error line per failure — treat the log as containing
    /// recipient addresses when setting retention.</para>
    /// <para><b>Not retried, not queued.</b> A transient failure is reported once and forgotten;
    /// there is no outbox and no back-off. A caller that needs delivery guaranteed must implement
    /// its own retry.</para>
    /// </remarks>
    public async Task<Result> SendAsync(EmailMessage message)
    {
        var validation = Validate(message);
        if (validation.IsFailure)
            return validation;

        try
        {
            using var mailMessage = BuildMailMessage(message);
            using var client = BuildClient();
            await client.SendMailAsync(mailMessage).ConfigureAwait(false);
            logger.LogInformation("Email sent to {ToAddress} with subject {Subject}", message.ToAddress, message.Subject);
            return Result.Success();
        }
        catch (SmtpException ex)
        {
            logger.LogError(ex, "SMTP rejected the message to {ToAddress} (status {StatusCode})",
                message.ToAddress, ex.StatusCode);
            return Result.Failure($"SMTP delivery failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected failure sending email to {ToAddress}", message.ToAddress);
            return Result.Failure($"Email delivery failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Rejects a message that could never be delivered, before a connection is opened.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A recipient, a subject and a configured host and sender address
    /// are all mandatory; a missing one is a configuration error worth naming precisely.</para>
    /// <para><b>Flow:</b> null check → recipient → subject → host → from address.</para>
    /// <para><b>Side Effects:</b> Logs a warning when configuration is incomplete.</para>
    /// </remarks>
    /// <param name="message">The candidate message.</param>
    /// <returns>Success when the message can be attempted.</returns>
    private Result Validate(EmailMessage message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.ToAddress))
            return Result.Failure("A recipient address is required.");

        if (string.IsNullOrWhiteSpace(message.Subject))
            return Result.Failure("A subject is required.");

        if (string.IsNullOrWhiteSpace(smtpHost) || string.IsNullOrWhiteSpace(fromAddress))
        {
            logger.LogWarning("SMTP is not fully configured — set {HostKey} and EmailSettings:FromAddress", SmtpHostKey);
            return Result.Failure("SMTP is not configured.");
        }

        return Result.Success();
    }

    /// <summary>
    /// Builds the transport message, including the bulk-mail unsubscribe header.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> HTML is preferred when present; the plain-text body is the
    /// fallback. <c>List-Unsubscribe</c> is emitted whenever the caller supplied a URL, which every
    /// newsletter message does.</para>
    /// <para><b>Flow:</b> addresses → subject → body → optional unsubscribe header.</para>
    /// <para><b>Side Effects:</b> None beyond allocating a disposable message.</para>
    /// </remarks>
    /// <param name="message">The message to render.</param>
    /// <returns>A configured <c>MailMessage</c> the caller must dispose.</returns>
    private MailMessage BuildMailMessage(EmailMessage message)
    {
        var mailMessage = new MailMessage
        {
            From = new MailAddress(fromAddress, fromName),
            Subject = message.Subject,
            Body = message.IsHtml ? message.HtmlBody : message.TextBody,
            IsBodyHtml = message.IsHtml
        };

        mailMessage.To.Add(string.IsNullOrWhiteSpace(message.ToName)
            ? new MailAddress(message.ToAddress)
            : new MailAddress(message.ToAddress, message.ToName));

        if (!string.IsNullOrWhiteSpace(message.UnsubscribeUrl))
            mailMessage.Headers.Add("List-Unsubscribe", $"<{message.UnsubscribeUrl}>");

        return mailMessage;
    }

    /// <summary>
    /// Builds a per-send SMTP client from configuration.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Anonymous relay is supported — credentials are attached only
    /// when a user name is configured.</para>
    /// <para><b>Flow:</b> host and port → TLS → timeout → optional credentials.</para>
    /// <para><b>Side Effects:</b> None until a message is sent; the caller must dispose the client.</para>
    /// </remarks>
    /// <returns>A configured <c>SmtpClient</c>.</returns>
    private SmtpClient BuildClient()
    {
        var client = new SmtpClient(smtpHost, smtpPort)
        {
            EnableSsl = isSslEnabled,
            Timeout = timeoutMilliseconds,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (string.IsNullOrWhiteSpace(userName))
            return client;

        client.UseDefaultCredentials = false;
        client.Credentials = new NetworkCredential(userName, password);
        return client;
    }

    /// <summary>
    /// Renders the password-reset email body.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The link is shown in full so a mail client that strips anchors
    /// still leaves the user something to copy.</para>
    /// <para><b>Flow:</b> interpolate the URL into a minimal HTML fragment.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="resetUrl">The absolute reset URL.</param>
    /// <returns>The HTML body.</returns>
    private static string BuildPasswordResetBody(string resetUrl)
    {
        return $@"<p>We received a request to reset your password.</p>
<p><a href=""{resetUrl}"">Reset your password</a></p>
<p>If the link does not work, copy this address into your browser:<br />{resetUrl}</p>
<p>This link expires in 24 hours. If you did not request a reset, you can ignore this email.</p>";
    }

    /// <summary>
    /// Reads an integer setting, falling back when it is absent or unparseable.
    /// </summary>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="key">Full configuration key.</param>
    /// <param name="fallback">Value used when the key is missing or invalid.</param>
    /// <returns>The configured value, or <paramref name="fallback"/>.</returns>
    private static int ReadInt(IConfiguration configuration, string key, int fallback)
    {
        return int.TryParse(configuration[key], out var value) ? value : fallback;
    }

    /// <summary>
    /// Reads a boolean setting, falling back when it is absent or unparseable.
    /// </summary>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="key">Full configuration key.</param>
    /// <param name="fallback">Value used when the key is missing or invalid.</param>
    /// <returns>The configured value, or <paramref name="fallback"/>.</returns>
    private static bool ReadBool(IConfiguration configuration, string key, bool fallback)
    {
        return bool.TryParse(configuration[key], out var value) ? value : fallback;
    }
}
