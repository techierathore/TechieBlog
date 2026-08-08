namespace BlogModels.Models;

/// <summary>
/// Outbound e-mail (SMTP) configuration held in site settings.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Published contract for the SMTP e-mail sender. A real
/// <c>IEmailService</c> implementation reads this instead of <c>IConfiguration</c> so an
/// administrator can change mail delivery from the Settings screen without a redeploy.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Admin saves SMTP values on the Settings screen.</item>
///   <item><c>SiteSettingsService</c> persists them; <see cref="Password"/> is encrypted at rest.</item>
///   <item>The sender calls <c>ISiteSettingsService.GetSmtpSettingsAsync</c> per send and
///     receives the decrypted values.</item>
/// </list>
///
/// <para><b>Dependencies:</b> None.</para>
///
/// <para><b>Usage:</b> Treat <see cref="IsConfigured"/> as the guard before attempting a send;
/// when it is false the console/no-op sender should be used instead. This is not a persistence
/// shape — there is no <c>SmtpSettings</c> table. Each property is stored as one
/// <see cref="SiteSetting"/> row under its own key and reassembled by
/// <c>SiteSettingsMapper</c>.</para>
///
/// <para><b>Security:</b> <see cref="Password"/> is the only encrypted member (key
/// <c>SmtpPassword</c>); everything else is stored in the clear because an administrator expects to
/// read it back. Encryption uses the single <c>AppEncryptionKey</c> configuration value with no key
/// versioning, so <b>rotating that key renders the stored password permanently undecryptable</b>
/// and it must be re-entered. An instance of this type holds a live credential in memory: never log
/// one, never bind one into a component that outlives the request, and never return one from an
/// endpoint the browser can reach.</para>
/// </remarks>
public class SmtpSettings
{
    /// <summary>
    /// Relay host name, for example <c>smtp.sendgrid.net</c>. Empty means unconfigured, which is
    /// half of what <see cref="IsConfigured"/> tests.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// TCP port on <see cref="Host"/>. Defaults to 587, the submission port used with STARTTLS;
    /// 465 is implicit TLS and 25 is unauthenticated relay. Stored as text and parsed back, so a
    /// value that will not parse falls back to this default rather than failing the load.
    /// </summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// Whether the connection is secured with TLS. Defaults to true so the credential below is not
    /// sent over a plaintext connection by accident; turning it off transmits
    /// <see cref="UserName"/> and <see cref="Password"/> in the clear and should only ever be done
    /// against a trusted local relay.
    /// </summary>
    public bool IsSslEnabled { get; set; } = true;

    /// <summary>
    /// Account name presented to the relay. Empty is legitimate — an anonymous internal relay needs
    /// no credentials, which is why <see cref="IsConfigured"/> deliberately does not require it.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// The relay password. <b>Encrypted at rest</b> under the setting key <c>SmtpPassword</c>; this
    /// property always carries the decrypted plain value, because that is the form the SMTP client
    /// needs.
    /// </summary>
    /// <remarks>
    /// A live credential. It must never be logged, never echoed back to the Settings screen as a
    /// readable field, and never included in a diagnostic dump of the settings aggregate. It is
    /// also the value lost by an <c>AppEncryptionKey</c> rotation — see the security note on the
    /// type.
    /// </remarks>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Envelope sender address stamped on every outbound message. Required for a send to be
    /// attempted at all (the other half of <see cref="IsConfigured"/>), and normally must belong to
    /// a domain the relay is authorised for, or messages are rejected or spam-filed regardless of
    /// how correct the rest of this configuration is.
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>
    /// Friendly display name shown beside <see cref="FromAddress"/> in a recipient's mail client.
    /// Cosmetic — it carries no delivery meaning and is not verified by anything.
    /// </summary>
    public string FromName { get; set; } = string.Empty;

    /// <summary>
    /// True when enough values are present for a send to be attempted.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A host and a from-address are the minimum viable
    /// configuration; anonymous relays legitimately have no credentials.</para>
    /// </remarks>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}
