using System.Net;
using System.Net.Mail;
using BlogModels.Models;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Real SMTP email transport built on the .NET base class library.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Delivers every outbound mail — password resets, reader confirmations and
/// newsletters — through the mail server set on the Settings screen (REQ-FN-033), and sends the
/// administrator's test mail (REQ-UI-072).</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><b>The mail server is read at every send</b>, through <see cref="IMailServerSource"/>: the
///         values saved on the Settings screen first, the <c>EmailSettings</c> configuration section
///         only as the fallback. A change on the Settings screen takes effect on the next mail, with no
///         restart and no deploy secret. (Until 2026-11-26 this class read configuration once, in its
///         constructor, and the Settings-screen values were stored and never used — the live site had
///         no mail server at all, and a reader's rating confirmation was never mailed.)</item>
///   <item>Each send builds a fresh <c>MailMessage</c> and <c>SmtpClient</c>; <c>SmtpClient</c> is
///         not thread-safe, and a per-send instance is what makes concurrent newsletter delivery
///         safe.</item>
///   <item>Every failure is logged with the recipient address before being surfaced.</item>
/// </list>
///
/// <para><b>Transport choice:</b> <c>System.Net.Mail.SmtpClient</c> from the BCL, not MailKit. It
/// covers host, port, STARTTLS, credentials and timeout and adds no package. Its one real limit:
/// it cannot speak SSL-from-the-first-byte on port 465, so that port is refused with a plain
/// sentence that says to use 587 — Gmail and every common host offer 587 with STARTTLS.</para>
///
/// <para><b>THIS ONE ACTUALLY SENDS MAIL.</b> <c>ConsoleEmailService</c> and
/// <c>LoggingVerificationEmailSender</c> only write to the log. Which one runs is decided per send by
/// <see cref="RoutingEmailService"/> and <see cref="RoutingVerificationEmailSender"/>, both from
/// <see cref="IMailServerSource"/>.</para>
///
/// <para><b>Credentials at rest.</b> The password entered on the Settings screen is stored encrypted
/// under <c>AppEncryptionKey</c>. Rotating that key destroys the stored ciphertext: re-enter the mail
/// password on the Settings screen right after any rotation, and use "Send test mail" to prove it.</para>
///
/// <para><b>Dependencies:</b> <see cref="IMailServerSource"/>, <c>ILogger&lt;SmtpEmailService&gt;</c>.</para>
///
/// <para><b>Usage:</b> Resolved through <see cref="IEmailService"/> (the router) for normal mail, and
/// through <see cref="IMailTestService"/> for the Settings screen's test button.</para>
/// </remarks>
public class SmtpEmailService : IEmailService, IMailTestService
{
    private const int SendTimeoutSeconds = 30;
    private const int ImplicitTlsPort = 465;

    /// <summary>
    /// Message returned when the mail server refused the message (REQ-NFR-033).
    /// </summary>
    /// <remarks>
    /// <para>An <c>SmtpException</c>'s text can carry the mail host's name and a "Username and
    /// Password not accepted" reply. <see cref="SendAsync"/> is reached from <b>anonymous</b> forms, so
    /// callers see only this sentence; the exception stays in the log. Only the admin-only
    /// <see cref="SendTestMailAsync"/> shows the server's own words.</para>
    /// </remarks>
    private const string DeliveryRejectedMessage =
        "The message could not be delivered. Please try again later.";

    /// <summary>Curated message for an unexpected send failure. See <see cref="DeliveryRejectedMessage"/>.</summary>
    private const string DeliveryFailedMessage =
        "Email delivery failed. Please try again later.";

    /// <summary>Returned when no mail server is set anywhere.</summary>
    private const string NotSetMessage = "No mail server is set.";

    /// <summary>Why port 465 is refused; shown on the Settings screen's test.</summary>
    private const string ImplicitTlsMessage =
        "Port 465 (SSL from the first byte) is not supported by this sender. " +
        "Use port 587 with \"Use SSL/TLS\" ticked — Gmail and most mail hosts offer it.";

    private readonly IMailServerSource mailServerSource;
    private readonly ILogger<SmtpEmailService> logger;

    /// <summary>
    /// Initializes the SMTP transport.
    /// </summary>
    /// <param name="mailServerSource">Answers, per send, which mail server to use.</param>
    /// <param name="logger">Logger used for delivery diagnostics.</param>
    public SmtpEmailService(IMailServerSource mailServerSource, ILogger<SmtpEmailService> logger)
    {
        this.mailServerSource = mailServerSource;
        this.logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Throws rather than returning a <c>Result</c>: a reset that silently
    /// fails leaves the user waiting for a mail that will never arrive.</para>
    /// <para><b>Flow:</b> build the message → <see cref="SendAsync"/> → return, or log and throw.</para>
    /// <para><b>Side Effects:</b> <b>Sends a real email</b> containing a working reset link.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The message could not be delivered.</exception>
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
    /// <para><b>Business Logic:</b> Reads the mail server now, then delivers. Every failure comes back
    /// as a failed <c>Result</c> carrying a curated sentence.</para>
    /// <para><b>Flow:</b> <see cref="IMailServerSource.GetCurrentAsync"/> → <see cref="SendViaAsync"/>.</para>
    /// <para><b>Side Effects:</b> <b>Sends a real email</b> when a mail server is set.</para>
    /// </remarks>
    public async Task<Result> SendAsync(EmailMessage message)
    {
        var choice = await mailServerSource.GetCurrentAsync().ConfigureAwait(false);
        return await SendViaAsync(message, choice).ConfigureAwait(false);
    }

    /// <summary>
    /// Delivers a message through a mail server choice the caller has already read.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Lets <see cref="RoutingEmailService"/> read the mail server once and
    /// use the same answer to both route and send.</para>
    /// <para><b>Flow:</b> validate → deliver → success, or log and return a curated failure.</para>
    /// <para><b>Side Effects:</b> <b>Sends a real email.</b> Logs the recipient and subject.</para>
    /// </remarks>
    /// <param name="message">The message to deliver.</param>
    /// <param name="choice">The mail server to use.</param>
    /// <returns>Success when the server accepted the message.</returns>
    public async Task<Result> SendViaAsync(EmailMessage message, MailServerChoice choice)
    {
        var validation = Validate(message, choice);
        if (validation.IsFailure)
            return validation;

        try
        {
            await DeliverAsync(message, choice.Server).ConfigureAwait(false);
            logger.LogInformation("Email sent to {ToAddress} with subject {Subject} via the {Origin} mail server",
                message.ToAddress, message.Subject, choice.Origin);
            return Result.Success();
        }
        catch (SmtpException ex)
        {
            logger.LogError(ex, "SMTP rejected the message to {ToAddress} (status {StatusCode})",
                message.ToAddress, ex.StatusCode);
            return Result.Failure(DeliveryRejectedMessage);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected failure sending email to {ToAddress}", message.ToAddress);
            return Result.Failure(DeliveryFailedMessage);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> The only place the mail server's own words reach a screen, and only
    /// the admin Settings screen calls it. That is deliberate (REQ-UI-072): "Authentication failed" or
    /// "connection refused" is exactly what an administrator needs to fix the values, and the reply
    /// names nothing the administrator did not just type.</para>
    /// <para><b>Flow:</b> validate → deliver → "accepted", or the server's reply.</para>
    /// <para><b>Side Effects:</b> Sends one real email. Never logs the password.</para>
    /// </remarks>
    public async Task<MailTestOutcome> SendTestMailAsync(SmtpSettings server, string toAddress)
    {
        var problem = CheckTestInput(server, toAddress);
        if (problem != null)
            return new MailTestOutcome { IsAccepted = false, ServerAnswer = problem };

        try
        {
            await DeliverAsync(BuildTestMessage(toAddress, server), server).ConfigureAwait(false);
            logger.LogInformation("Test mail to {ToAddress} accepted by {Host}:{Port}", toAddress, server.Host, server.Port);
            return new MailTestOutcome
            {
                IsAccepted = true,
                ServerAnswer = $"Accepted by {server.Host}:{server.Port}. The mail server answered 250 OK, " +
                               $"so the test mail is on its way to {toAddress.Trim()}."
            };
        }
        catch (Exception failure)
        {
            logger.LogWarning(failure, "Test mail to {ToAddress} via {Host}:{Port} failed", toAddress, server.Host, server.Port);
            return new MailTestOutcome { IsAccepted = false, ServerAnswer = DescribeFailure(failure, server) };
        }
    }

    /// <summary>
    /// Opens the connection and hands one message to the mail server.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Port 465 is refused before connecting (see the type remarks).
    /// <c>SmtpClient.Timeout</c> is ignored by the async send, and a cancellation token does not stop a
    /// connect to a port that a firewall silently drops — measured 2026-11-26: the send never returned.
    /// So the wait itself is bounded with <c>WaitAsync</c>, and disposing the client abandons the
    /// connection. Without this, a mail port blocked by the hosting provider would hang the reader's
    /// form and the Settings screen forever.</para>
    /// <para><b>Side Effects:</b> Network I/O; sends mail.</para>
    /// </remarks>
    /// <param name="message">The message.</param>
    /// <param name="server">The mail server values.</param>
    /// <returns>A task that completes when the server has accepted the message.</returns>
    private static async Task DeliverAsync(EmailMessage message, SmtpSettings server)
    {
        if (server.Port == ImplicitTlsPort)
            throw new NotSupportedException(ImplicitTlsMessage);

        using var mailMessage = BuildMailMessage(message, server);
        using var client = BuildClient(server);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(SendTimeoutSeconds));
        var sending = client.SendMailAsync(mailMessage, timeout.Token);

        // An abandoned send still fails later; observe it so it is not reported as unobserved.
        _ = sending.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        await sending.WaitAsync(TimeSpan.FromSeconds(SendTimeoutSeconds)).ConfigureAwait(false);
    }

    /// <summary>
    /// Rejects a message that could never be delivered, before a connection is opened.
    /// </summary>
    /// <param name="message">The candidate message.</param>
    /// <param name="choice">The mail server choice.</param>
    /// <returns>Success when the message can be attempted.</returns>
    private Result Validate(EmailMessage message, MailServerChoice choice)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.ToAddress))
            return Result.Failure("A recipient address is required.");

        if (string.IsNullOrWhiteSpace(message.Subject))
            return Result.Failure("A subject is required.");

        if (choice == null || !choice.IsAvailable)
        {
            logger.LogWarning("No mail server is set on the Settings screen or in configuration; nothing was sent");
            return Result.Failure(NotSetMessage);
        }

        return Result.Success();
    }

    /// <summary>
    /// Checks the Settings-screen values before a test send.
    /// </summary>
    /// <param name="server">The values on the screen.</param>
    /// <param name="toAddress">The address typed for the test.</param>
    /// <returns>A sentence naming the problem, or null when the test can run.</returns>
    private static string? CheckTestInput(SmtpSettings server, string toAddress)
    {
        if (server == null || string.IsNullOrWhiteSpace(server.Host))
            return "Fill in the mail server (Host) first.";

        if (string.IsNullOrWhiteSpace(server.FromAddress) && string.IsNullOrWhiteSpace(server.UserName))
            return "Fill in the From address (or the Username) first.";

        if (string.IsNullOrWhiteSpace(toAddress) || !MailAddress.TryCreate(toAddress.Trim(), out _))
            return "Type the address the test mail should go to.";

        return null;
    }

    /// <summary>
    /// Turns a failed test send into the mail server's answer in its own words.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The SMTP status, then every message down the exception chain —
    /// the innermost one is usually the server's reply or the socket error ("Connection refused",
    /// "No such host is known"). Admin-only; see <see cref="SendTestMailAsync"/>.</para>
    /// </remarks>
    /// <param name="failure">What the send threw.</param>
    /// <param name="server">The values used, for the host and port.</param>
    /// <returns>The answer to show.</returns>
    private static string DescribeFailure(Exception failure, SmtpSettings server)
    {
        if (failure is OperationCanceledException or TimeoutException)
            return $"No answer from {server.Host}:{server.Port} within {SendTimeoutSeconds} seconds. " +
                   "Check the host and port, and that the hosting provider does not block outgoing mail on that port.";

        var parts = new List<string>();
        if (failure is SmtpException smtpFailure)
            parts.Add($"SMTP {(int)smtpFailure.StatusCode} ({smtpFailure.StatusCode})");

        for (var current = failure; current != null; current = current.InnerException)
        {
            var text = current.Message?.Trim();
            if (!string.IsNullOrEmpty(text) && !parts.Contains(text))
                parts.Add(text);
        }

        return $"Refused by {server.Host}:{server.Port}: {string.Join(" — ", parts)}";
    }

    /// <summary>
    /// Builds the transport message, including the bulk-mail unsubscribe header.
    /// </summary>
    /// <param name="message">The message to render.</param>
    /// <param name="server">Supplies the sender address and name.</param>
    /// <returns>A configured <c>MailMessage</c> the caller must dispose.</returns>
    private static MailMessage BuildMailMessage(EmailMessage message, SmtpSettings server)
    {
        var fromAddress = string.IsNullOrWhiteSpace(server.FromAddress) ? server.UserName : server.FromAddress;
        var mailMessage = new MailMessage
        {
            From = new MailAddress(fromAddress.Trim(), string.IsNullOrWhiteSpace(server.FromName) ? "TechieBlog" : server.FromName),
            Subject = message.Subject,
            Body = message.IsHtml ? message.HtmlBody : message.TextBody,
            IsBodyHtml = message.IsHtml
        };

        mailMessage.To.Add(string.IsNullOrWhiteSpace(message.ToName)
            ? new MailAddress(message.ToAddress.Trim())
            : new MailAddress(message.ToAddress.Trim(), message.ToName));

        if (!string.IsNullOrWhiteSpace(message.UnsubscribeUrl))
            mailMessage.Headers.Add("List-Unsubscribe", $"<{message.UnsubscribeUrl}>");

        return mailMessage;
    }

    /// <summary>
    /// Builds a per-send SMTP client.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Anonymous relay is supported — credentials are attached only when
    /// a user name is set.</para>
    /// </remarks>
    /// <param name="server">The mail server values.</param>
    /// <returns>A configured <c>SmtpClient</c>.</returns>
    private static SmtpClient BuildClient(SmtpSettings server)
    {
        var client = new SmtpClient(server.Host.Trim(), server.Port)
        {
            EnableSsl = server.IsSslEnabled,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (string.IsNullOrWhiteSpace(server.UserName))
            return client;

        client.UseDefaultCredentials = false;
        client.Credentials = new NetworkCredential(server.UserName.Trim(), server.Password);
        return client;
    }

    /// <summary>
    /// Builds the Settings screen's test mail.
    /// </summary>
    /// <param name="toAddress">The recipient.</param>
    /// <param name="server">The values being tested, named in the body.</param>
    /// <returns>The message.</returns>
    private static EmailMessage BuildTestMessage(string toAddress, SmtpSettings server)
    {
        var host = WebUtility.HtmlEncode(server.Host);
        return new EmailMessage
        {
            ToAddress = toAddress.Trim(),
            Subject = "Test mail from your blog",
            HtmlBody = $"<p>This is a test mail sent from the Settings screen through {host}:{server.Port}.</p>" +
                       "<p>If you can read it, reader confirmations and password resets will reach their mailboxes.</p>",
            TextBody = $"This is a test mail sent from the Settings screen through {server.Host}:{server.Port}."
        };
    }

    /// <summary>
    /// Renders the password-reset email body.
    /// </summary>
    /// <param name="resetUrl">The absolute reset URL.</param>
    /// <returns>The HTML body.</returns>
    private static string BuildPasswordResetBody(string resetUrl)
    {
        return $@"<p>We received a request to reset your password.</p>
<p><a href=""{resetUrl}"">Reset your password</a></p>
<p>If the link does not work, copy this address into your browser:<br />{resetUrl}</p>
<p>This link expires in 24 hours. If you did not request a reset, you can ignore this email.</p>";
    }
}
