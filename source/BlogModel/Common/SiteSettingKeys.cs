namespace BlogModels;

/// <summary>
/// The canonical key names used by every row of the <c>SiteSetting</c> table.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Site configuration is stored key/value, so the keys are the schema.
/// Holding them as constants means the service, the seed migration and any future admin screen
/// all agree on spelling, and a rename is a compile error rather than a silent default.</para>
///
/// <para><b>Code Flow:</b> <c>SiteSettingsService</c> reads every row into a dictionary and looks
/// values up by these constants when projecting onto <c>SiteSettings</c>; the reverse projection
/// writes the same keys back.</para>
///
/// <para><b>Dependencies:</b> None.</para>
///
/// <para><b>Usage:</b> Never type a settings key as a literal — always reference a constant here.
/// Keys are PascalCase and dot-scoped by group, matching the <see cref="Groups"/> names.</para>
///
/// <para><b>Encrypted values:</b> two of these settings — <see cref="SmtpPassword"/> and
/// <see cref="StorageCloudAccessKey"/> — are stored as ciphertext produced by
/// <see cref="AppEncrypt.EncryptText"/>, which is keyed by the configured
/// <see cref="AppSecrets.EncryptionKey"/>. They are therefore the rows that a rotation of that key
/// destroys: see the rotation warning on <see cref="AppSecrets"/>. Any new setting that holds a
/// credential belongs in the same category and must be encrypted the same way.</para>
/// </remarks>
public static class SiteSettingKeys
{
    /// <summary>Site title shown in headers, the browser tab and feed metadata.</summary>
    public const string SiteTitle = "General.SiteTitle";

    /// <summary>Short strapline displayed under the site title.</summary>
    public const string SiteTagline = "General.SiteTagline";

    /// <summary>Path or URL of the configured site logo, or empty when none is set (UAT-022).</summary>
    public const string SiteLogo = "General.SiteLogo";

    /// <summary>Address that receives administrative notifications.</summary>
    public const string AdminEmail = "General.AdminEmail";

    /// <summary>Number of posts rendered per page on public listings.</summary>
    public const string PostsPerPage = "Blog.PostsPerPage";

    /// <summary>Word count above which an article is split into pages; zero disables splitting.</summary>
    public const string PaginationWordCount = "Blog.PaginationWordCount";

    /// <summary>Whether the comment form is offered on posts.</summary>
    public const string AreCommentsAllowed = "Blog.AreCommentsAllowed";

    /// <summary>Whether comments are held for approval before display.</summary>
    public const string AreCommentsModerated = "Blog.AreCommentsModerated";

    /// <summary>Whether self-service registration is open.</summary>
    public const string IsRegistrationAllowed = "Blog.IsRegistrationAllowed";

    /// <summary>Identifier of the admin-selected site-wide theme.</summary>
    public const string SiteTheme = "Theme.SiteTheme";

    /// <summary>Whether the site defaults to dark mode for new visitors.</summary>
    public const string IsDarkModeDefault = "Theme.IsDarkModeDefault";

    /// <summary>Default meta description for pages that supply none.</summary>
    public const string MetaDescription = "Seo.MetaDescription";

    /// <summary>Default meta keywords emitted site-wide.</summary>
    public const string MetaKeywords = "Seo.MetaKeywords";

    /// <summary>Public Twitter/X profile URL.</summary>
    public const string TwitterUrl = "Social.TwitterUrl";

    /// <summary>Public LinkedIn profile URL.</summary>
    public const string LinkedInUrl = "Social.LinkedInUrl";

    /// <summary>Public GitHub profile URL.</summary>
    public const string GitHubUrl = "Social.GitHubUrl";

    /// <summary>SMTP server host name.</summary>
    public const string SmtpHost = "Smtp.Host";

    /// <summary>SMTP server port.</summary>
    public const string SmtpPort = "Smtp.Port";

    /// <summary>Whether the SMTP connection negotiates TLS.</summary>
    public const string SmtpIsSslEnabled = "Smtp.IsSslEnabled";

    /// <summary>SMTP account name.</summary>
    public const string SmtpUserName = "Smtp.UserName";

    /// <summary>
    /// SMTP account password. Stored encrypted by <see cref="AppEncrypt.EncryptText"/> and
    /// unrecoverable if <see cref="AppSecrets.EncryptionKey"/> is rotated.
    /// </summary>
    public const string SmtpPassword = "Smtp.Password";

    /// <summary>Envelope sender address for outbound mail.</summary>
    public const string SmtpFromAddress = "Smtp.FromAddress";

    /// <summary>Friendly display name for outbound mail.</summary>
    public const string SmtpFromName = "Smtp.FromName";

    /// <summary>Selected storage provider name.</summary>
    public const string StorageProviderName = "Storage.ProviderName";

    /// <summary>Filesystem root for the local storage provider.</summary>
    public const string StorageLocalRootPath = "Storage.LocalRootPath";

    /// <summary>UNC or mounted root for the network storage provider.</summary>
    public const string StorageNetworkRootPath = "Storage.NetworkRootPath";

    /// <summary>Endpoint base URL for the cloud storage provider.</summary>
    public const string StorageCloudServiceUrl = "Storage.CloudServiceUrl";

    /// <summary>Bucket or container name for the cloud storage provider.</summary>
    public const string StorageCloudContainerName = "Storage.CloudContainerName";

    /// <summary>
    /// Credential presented to the cloud endpoint. Stored encrypted by
    /// <see cref="AppEncrypt.EncryptText"/> and unrecoverable if
    /// <see cref="AppSecrets.EncryptionKey"/> is rotated.
    /// </summary>
    public const string StorageCloudAccessKey = "Storage.CloudAccessKey";

    /// <summary>Public URL prefix mapping to the storage root.</summary>
    public const string StoragePublicBaseUrl = "Storage.PublicBaseUrl";

    /// <summary>
    /// Group names used to bucket settings for the admin screen.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> The persisted <c>SettingGroup</c> column carries one of these values
    /// so the settings screen can render sections without hard-coding a key list.</para>
    /// <para><b>Usage:</b> Pass a group constant whenever writing an individual key.</para>
    /// </remarks>
    public static class Groups
    {
        /// <summary>Title, tagline and administrative contact.</summary>
        public const string General = "General";

        /// <summary>Pagination, comments and registration behaviour.</summary>
        public const string Blog = "Blog";

        /// <summary>Site-wide theme selection.</summary>
        public const string Theme = "Theme";

        /// <summary>Search-engine metadata defaults.</summary>
        public const string Seo = "Seo";

        /// <summary>Public social profile links.</summary>
        public const string Social = "Social";

        /// <summary>Outbound e-mail configuration.</summary>
        public const string Smtp = "Smtp";

        /// <summary>Uploaded-media storage configuration.</summary>
        public const string Storage = "Storage";
    }
}
