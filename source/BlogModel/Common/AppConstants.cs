namespace BlogModels;

/// <summary>
/// Magic strings and numbers shared across the host, the RCL and the engine.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Keeps browser-storage keys and the paging sizes in one place so the UI and
/// the engine cannot drift apart on a literal. Only a handful of these members are live today — see
/// the per-member remarks, which record the single call site or state plainly that the member is
/// unreferenced.</para>
///
/// <para><b>Code Flow:</b> Almost entirely declarative. The one exception is the pair
/// <see cref="AccessKey"/> / <see cref="RefreshKey"/>, which <c>CustomAuthStateProvider</c> reads
/// when persisting the session to browser local storage: they are computed from
/// <see cref="AppSecrets.SessionFingerprint"/> and therefore require the host to have loaded its
/// secrets first.</para>
///
/// <para><b>Dependencies:</b> <see cref="AppSecrets"/> only, and only for the two storage keys.
/// Nothing here reaches outside <c>BlogModels</c>, which sits at the bottom of the project graph.</para>
///
/// <para><b>Usage:</b> Reference the constants rather than re-typing the literal. Do NOT add
/// secrets here — REQ-NFR-027 removed the two that used to live in this file (a JWT signing key and
/// an AES key, both readable by anyone with repository access). Secrets belong in configuration and
/// are reached through <see cref="AppSecrets"/>.</para>
///
/// <para><b>Standards drift:</b> the <c>public static string</c> members below are mutable statics,
/// not <c>const</c>, so any caller can reassign them process-wide. They are declared this way for
/// historical reasons; treat them as read-only.</para>
/// </remarks>
public static class AppConstants
{
    /// <summary>
    /// Session/claim key under which the authenticated user was stashed by the pre-Blazor-Server
    /// incarnation of this app. Unreferenced in the current codebase.
    /// </summary>
    public const string LoggedUser = "CurrUser";

    /// <summary>
    /// Legacy administrator role literal. Unreferenced — the live role names are the
    /// <see cref="AppRoles"/> constants, which use different values; do not authorise against this.
    /// </summary>
    public const string Admin = "SysAdmin";

    /// <summary>
    /// Legacy reader role literal. Unreferenced — see the warning on <see cref="Admin"/>.
    /// </summary>
    public const string BlogUser = "BlogUser";

    /// <summary>
    /// Intended page size for the public post list. Unreferenced — the live paging sizes are passed
    /// per call site, so changing this value has no effect on any page.
    /// </summary>
    public const int BlogListPageSize = 4;

    /// <summary>
    /// Intended page size for admin grids. Unreferenced; see <see cref="BlogListPageSize"/>.
    /// </summary>
    public const int ListPageSize = 5;

    /// <summary>
    /// OAuth-style form field name for the access token. Unreferenced; the token is exchanged
    /// through <see cref="AccessKey"/> instead.
    /// </summary>
    public static string AppTokenKey = "access_token";

    /// <summary>
    /// Browser local-storage key holding the signed JWT for the current session.
    /// </summary>
    /// <remarks>
    /// <para>Written on sign-in and cleared on sign-out by
    /// <c>BlogUI.Common.CustomAuthStateProvider</c>; read on every circuit start to rehydrate the
    /// principal.</para>
    /// <para><b>Rotation (REQ-NFR-027):</b> the name is suffixed with
    /// <see cref="AppSecrets.SessionFingerprint"/>, so a new JWT signing key produces a new storage
    /// key. Every browser still holding a token issued under the previous key looks in a slot that
    /// no longer exists and is signed out — which is what makes rotating the signing key actually
    /// invalidate the sessions minted under the compromised one. The token those browsers hold can
    /// never be presented again, so its <c>UserLogin</c> row is unreachable as well.</para>
    /// <para><b>Why the suffix is needed at all:</b> because the JWT signature is never verified on
    /// read. <c>SvcUtils.GetUserIDFromToken</c> decodes the token with <c>ReadJwtToken</c>, which
    /// parses without validating, and session validity is decided by the <c>UserLogin</c> lookup
    /// instead. A new signing key therefore invalidates nothing by itself, and the rotation had to be
    /// made to bite at the storage-key layer. Treat this as a documented limitation rather than the
    /// intended design — see the limitation note on <see cref="AppSecrets"/>.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <see cref="AppSecrets.Initialise"/> has not run in this process.
    /// </exception>
    public static string AccessKey => $"AccessToken-{AppSecrets.SessionFingerprint}";

    /// <summary>
    /// Browser local-storage key holding the refresh token that pairs with <see cref="AccessKey"/>.
    /// </summary>
    /// <remarks>
    /// Written and cleared alongside <see cref="AccessKey"/>; the two must always be set and removed
    /// together or the session becomes unrenewable. It carries the same signing-key fingerprint
    /// suffix and is therefore invalidated by a rotation in exactly the same way.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <see cref="AppSecrets.Initialise"/> has not run in this process.
    /// </exception>
    public static string RefreshKey => $"RefreshToken-{AppSecrets.SessionFingerprint}";

    /// <summary>
    /// The <c>Authorization</c> scheme prefix. Unreferenced — this solution has no REST layer left
    /// to send bearer headers to.
    /// </summary>
    public static string BearerKey = "Bearer";

    /// <summary>
    /// JSON content-type header value. Unreferenced; see <see cref="BearerKey"/>.
    /// </summary>
    public static string JsonMediaTypeHeader = "application/json";

    /// <summary>
    /// The <c>User-Agent</c> header name. Unreferenced.
    /// </summary>
    public static string UserAgent = "User-Agent";

    /// <summary>
    /// Client-kind discriminator for the Blazor Server head, from the era when a MAUI head shared
    /// the same service layer. Unreferenced.
    /// </summary>
    public static string AppTypeBlazor = "BlazorServer";

    /// <summary>
    /// Carried over from an earlier property-management application; meaningless in a blog and
    /// unreferenced. Safe to delete.
    /// </summary>
    public static string ImageTypeReceipt = "ReceiptImage";

    /// <summary>
    /// Carried over from an earlier property-management application; unreferenced. See
    /// <see cref="ImageTypeReceipt"/>.
    /// </summary>
    public static string DocTypeWorkOrder = "WorkOrder";

    /// <summary>
    /// Carried over from an earlier property-management application; unreferenced. See
    /// <see cref="ImageTypeReceipt"/>.
    /// </summary>
    public static string DocTypeEstimate = "Estimate";

    /// <summary>
    /// Carried over from an earlier property-management application; unreferenced. See
    /// <see cref="ImageTypeReceipt"/>.
    /// </summary>
    public static string DocTypePropDocs = "PropertyDocs";

    /// <summary>
    /// Carried over from an earlier property-management application; unreferenced. See
    /// <see cref="ImageTypeReceipt"/>.
    /// </summary>
    public static string OfficeGeoConstant = "Office";

    /// <summary>
    /// Legacy role literal (the name is a typo for <c>AppUserRole</c>). Unreferenced — authorise
    /// against <see cref="AppRoles"/> instead.
    /// </summary>
    public static string AppUseRole = "AppUser";

    /// <summary>
    /// Legacy administrator role literal. Unreferenced — see <see cref="AppUseRole"/>.
    /// </summary>
    public static string AppAdminRole = "AppAdmin";
}
