namespace BlogEngine.Services;

/// <summary>
/// What the mail server answered to a test mail sent from the Settings screen.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Carries the mail server's own answer, good or bad, back to the administrator
/// (REQ-UI-072).</para>
/// <para><b>Usage:</b> Admin-only. <see cref="ServerAnswer"/> holds the server's real reply text on a
/// failure, which can name the host and the rejected account; it is never shown on a public page.</para>
/// </remarks>
public sealed class MailTestOutcome
{
    /// <summary>Gets a value indicating whether the mail server accepted the message.</summary>
    public bool IsAccepted { get; init; }

    /// <summary>Gets the mail server's answer in its own words, prefixed with the SMTP status when known.</summary>
    public string ServerAnswer { get; init; } = string.Empty;
}
