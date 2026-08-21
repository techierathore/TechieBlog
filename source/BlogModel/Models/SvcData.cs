namespace BlogModels;

/// <summary>
/// The request/response envelope for every authentication call.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> One shape carries sign-in, email verification, password reset and token
/// exchange. Which properties are meaningful depends entirely on the operation — the rest are left
/// at their empty-string defaults, and callers must not assume a populated field means anything on
/// its own.</para>
///
/// <para><b>Code Flow:</b> The UI fills the inbound fields, <c>TechieBlog.Services.AuthService</c>
/// encrypts <see cref="LoginEmail"/> and <see cref="LoginPass"/> with
/// <see cref="AppEncrypt.EncryptText"/> before the call, and <c>BlogEngine.Services.AuthSvc</c>
/// decrypts them on arrival. On the way back, <c>AuthSvc</c> returns a fresh instance carrying
/// <see cref="ComplexData"/> and <see cref="JwToken"/>, which the caller unpacks.</para>
///
/// <para><b>Dependencies:</b> <see cref="AppEncrypt"/> for the transport encryption; no table backs
/// this type.</para>
///
/// <para><b>Usage:</b> Instances hold credentials in memory. Clear <see cref="LoginPass"/> as soon
/// as the call returns (the login page does), never log an instance, and never bind one straight
/// into a component that survives the request.</para>
/// </remarks>
public class SvcData
{
	/// <summary>
	/// Response only: the authenticated <see cref="Models.AppUser"/>, JSON-serialised and then
	/// encrypted with <see cref="AppEncrypt.EncryptText"/>. The caller decrypts and deserialises it
	/// to rebuild the principal — it is opaque to everything in between.
	/// </summary>
	public string ComplexData { get; set; } = string.Empty;

	/// <summary>
	/// Tenant discriminator inherited from the multi-tenant service this envelope came from.
	/// TechieBlog is single-tenant, so nothing reads or writes it.
	/// </summary>
	public string OrgCode { get; set; } = string.Empty;

	/// <summary>
	/// Request only: the email address being signed in, verified or reset — <b>encrypted in
	/// transit</b>, not plaintext. <c>AuthSvc</c> decrypts and trims it before lookup.
	/// </summary>
	public string LoginEmail { get; set; } = string.Empty;

	/// <summary>
	/// Request only: the plaintext password, <b>encrypted in transit</b> by the caller. This is a
	/// reversible envelope, not the stored form — what lands in the database is the one-way
	/// <see cref="PasswordHasher"/> hash. On the reset path this field carries the <i>new</i>
	/// password.
	/// </summary>
	public string LoginPass { get; set; } = string.Empty;

	/// <summary>
	/// The signed JWT: set by <c>AuthSvc</c> on a successful sign-in, and set by the caller on a
	/// token-exchange or refresh request. A bearer credential — never log it.
	/// </summary>
	public string JwToken { get; set; } = string.Empty;

	/// <summary>
	/// The one-time code emailed for address confirmation. Only meaningful on the verify-email and
	/// resend-verification paths.
	/// </summary>
	public string VerificationCode { get; set; } = string.Empty;

	/// <summary>
	/// The single-use password-reset token from the emailed link, matched against a
	/// <see cref="Models.PasswordResetToken"/> row. Only meaningful on the reset path, where it pairs with
	/// the new <see cref="LoginPass"/>.
	/// </summary>
	public string ResetToken { get; set; } = string.Empty;

	/// <summary>
	/// The user's given name, for personalising an outbound verification or reset email. Never used
	/// to identify the account.
	/// </summary>
	public string FirstName { get; set; } = string.Empty;

	/// <summary>
	/// Request only: the client's remote address, recorded on the sign-in audit row (REQ-FN-051).
	/// </summary>
	/// <remarks>
	/// Best-effort. The host adapter fills it from the current <c>HttpContext</c> when there is one;
	/// a sign-in submitted over a Blazor Server circuit has no HTTP request behind it, so the value
	/// stays empty and the audit row records an empty address — which the audit trail documents as a
	/// legitimate "could not be determined", not as a missing row.
	/// </remarks>
	public string ClientIP { get; set; } = string.Empty;

	/// <summary>
	/// Request only: the client's user-agent string, recorded on the sign-in audit row
	/// (REQ-FN-051). Best-effort, with the same caveat as <see cref="ClientIP"/>.
	/// </summary>
	public string ClientUserAgent { get; set; } = string.Empty;
}

/// <summary>
/// A request to exchange an expiring access token for a fresh one.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Names the token pair the refresh endpoint needs, rather than overloading
/// <see cref="SvcData"/> for it.</para>
///
/// <para><b>Code Flow:</b> Built by <c>AuthService.RefreshTokenAsync</c> from the two values held
/// in browser local storage under <see cref="AppConstants.AccessKey"/> and
/// <see cref="AppConstants.RefreshKey"/>; only <see cref="RefreshToken"/> is actually forwarded to
/// <c>AuthSvc</c>.</para>
///
/// <para><b>Dependencies:</b> None.</para>
///
/// <para><b>Usage:</b> Both properties are bearer credentials. Note that <c>AuthSvc</c> currently
/// issues the same JWT string as both access and refresh token, so the two fields normally hold
/// identical values — do not write code that relies on them differing.</para>
/// </remarks>
public class RefreshRequest
{
	/// <summary>
	/// The access token being replaced, typically already expired. Carried for correlation; the
	/// current refresh path does not validate it.
	/// </summary>
	public string AccessToken { get; set; } = string.Empty;

	/// <summary>
	/// The long-lived token authorising the reissue. This is the value that is actually checked
	/// against the stored <see cref="UserLogin"/> row.
	/// </summary>
	public string RefreshToken { get; set; } = string.Empty;
}
