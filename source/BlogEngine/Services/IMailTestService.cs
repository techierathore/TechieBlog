using BlogModels.Models;

namespace BlogEngine.Services;

/// <summary>
/// Sends one test mail with mail server values the administrator is looking at.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The "Send test mail" button on the Settings screen, on the website and in
/// BlogApp (REQ-UI-072).</para>
/// <para><b>Usage:</b> Admin-only. The values are the ones on the screen, saved or not, so the
/// administrator can test before saving.</para>
/// </remarks>
public interface IMailTestService
{
    /// <summary>
    /// Sends a short test mail and reports the mail server's answer.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Uses exactly the values given — no Site Settings, no
    /// configuration fallback.</para>
    /// <para><b>Flow:</b> validate → connect → send → report.</para>
    /// <para><b>Side Effects:</b> Sends one real email to <paramref name="toAddress"/>.</para>
    /// </remarks>
    /// <param name="server">The mail server values to test.</param>
    /// <param name="toAddress">Where the test mail goes.</param>
    /// <returns>The server's answer; never throws.</returns>
    Task<MailTestOutcome> SendTestMailAsync(SmtpSettings server, string toAddress);
}
