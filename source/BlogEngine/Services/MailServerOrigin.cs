namespace BlogEngine.Services;

/// <summary>
/// Where the mail server used for a send came from.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets a caller tell "mail is really sent" from "mail is only logged"
/// without knowing how the choice is made (REQ-FN-033).</para>
/// <para><b>Usage:</b> Read from <see cref="MailServerChoice.Origin"/>.</para>
/// </remarks>
public enum MailServerOrigin
{
    /// <summary>No mail server is set anywhere; mail is written to the log only.</summary>
    None = 0,

    /// <summary>The mail server the administrator saved on the Settings screen.</summary>
    SiteSettings = 1,

    /// <summary>The <c>EmailSettings</c> configuration section, used only when Site Settings has none.</summary>
    Configuration = 2
}
