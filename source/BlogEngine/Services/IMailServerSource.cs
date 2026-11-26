namespace BlogEngine.Services;

/// <summary>
/// Answers, at the moment of a send, which mail server to use.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The single rule behind every mail decision in the engine (REQ-FN-033):
/// the mail server saved on the Settings screen first, the <c>EmailSettings</c> configuration
/// section only as the fallback, and otherwise none.</para>
/// <para><b>Code Flow:</b> <see cref="RoutingEmailService"/>, <see cref="RoutingVerificationEmailSender"/>,
/// <c>EmailVerificationSvc</c> and the admin dashboard all ask this interface, so "really mail or only
/// log" can never be decided two different ways.</para>
/// <para><b>Dependencies:</b> None at the contract level.</para>
/// <para><b>Usage:</b> Call it per send; never hold the answer.</para>
/// </remarks>
public interface IMailServerSource
{
    /// <summary>
    /// Reads the mail server to use now.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Site Settings wins whenever it has a host and a sender address
    /// (a blank sender address falls back to the user name). Configuration is read only when Site
    /// Settings has no host.</para>
    /// <para><b>Flow:</b> site settings → configuration → none.</para>
    /// <para><b>Side Effects:</b> None; never throws — a settings store that cannot be read is treated
    /// as "no site value" and logged.</para>
    /// </remarks>
    /// <returns>The current choice.</returns>
    Task<MailServerChoice> GetCurrentAsync();
}
