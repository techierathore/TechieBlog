namespace BlogModels;

/// <summary>
/// The legal values of <see cref="BlogComment.ModerationStatus"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Gives the moderation state machine a single, typo-proof vocabulary
/// shared by the service layer, the repositories and the SQL in migration script 014.</para>
///
/// <para><b>Code Flow:</b> A comment is born <see cref="PendingVerification"/>. Consuming the
/// double opt-in token promotes it to <see cref="PendingApproval"/>. An administrator then
/// moves it to <see cref="Approved"/>, <see cref="Rejected"/> or <see cref="Spam"/>.</para>
///
/// <para><b>Dependencies:</b> None - plain string constants, deliberately not an enum so the
/// value round-trips through the <c>VARCHAR(30)</c> database column without a converter.</para>
///
/// <para><b>Usage:</b> Only <see cref="Approved"/> is publicly visible. Only
/// <see cref="PendingApproval"/> appears in the moderation queue.</para>
/// </remarks>
public static class CommentModerationStatus
{
    /// <summary>The address has not been confirmed. Never visible, never queued.</summary>
    public const string PendingVerification = "PendingVerification";

    /// <summary>The address is confirmed and the comment awaits administrator approval.</summary>
    public const string PendingApproval = "PendingApproval";

    /// <summary>Approved by an administrator and visible on the public site.</summary>
    public const string Approved = "Approved";

    /// <summary>Rejected by an administrator. Never visible.</summary>
    public const string Rejected = "Rejected";

    /// <summary>Classified as spam, either by the spam guard or by an administrator.</summary>
    public const string Spam = "Spam";
}
