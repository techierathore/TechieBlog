using BlogModels.Models;

namespace BlogEngine.Services;

/// <summary>
/// The mail server that will carry the next message, and where it was read from.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> One value answers both questions every mail path asks at send time:
/// "is there a mail server?" and "which one?" (REQ-FN-033).</para>
/// <para><b>Code Flow:</b> Built by <see cref="IMailServerSource.GetCurrentAsync"/> once per send.</para>
/// <para><b>Dependencies:</b> <see cref="SmtpSettings"/>.</para>
/// <para><b>Usage:</b> Never cache it across sends: the administrator may change the Settings screen
/// between two messages, and the next send must use the new values.</para>
/// </remarks>
public sealed class MailServerChoice
{
    /// <summary>
    /// Gets the server values to send with. Empty when <see cref="Origin"/> is <see cref="MailServerOrigin.None"/>.
    /// </summary>
    public SmtpSettings Server { get; init; } = new SmtpSettings();

    /// <summary>
    /// Gets where <see cref="Server"/> came from.
    /// </summary>
    public MailServerOrigin Origin { get; init; } = MailServerOrigin.None;

    /// <summary>
    /// Gets a value indicating whether mail can really be sent.
    /// </summary>
    public bool IsAvailable => Origin != MailServerOrigin.None;

    /// <summary>
    /// The choice used when no mail server is set anywhere.
    /// </summary>
    /// <returns>A choice whose <see cref="IsAvailable"/> is false.</returns>
    public static MailServerChoice NotSet()
    {
        return new MailServerChoice();
    }
}
