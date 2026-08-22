namespace BlogApp.Services;

/// <summary>
/// How the desktop head delivers uploaded media to the site (REQ-FN-062).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Names the three honest answers to "which machine do these bytes end up
/// on", so the connection screen asks the question out loud instead of inferring it from a path.</para>
///
/// <para><b>Why this type exists at all.</b> The first attempt at REQ-FN-062 offered only a folder
/// box and assumed the server's uploads directory could be mounted. For this deployment it cannot:
/// the site runs on a Linux VPS that answers on 443 and 22 only, so no Windows path reaches
/// <c>/srv/data/techieblog/uploads</c>. Typing that Linux path into the folder box created a
/// same-named directory on the operator's own C: drive, the writability probe called it good, and
/// five uploads went to the laptop while the operator believed they were on the server. An explicit
/// transport makes that mistake unrepresentable rather than merely discouraged.</para>
///
/// <para><b>Dependencies:</b> None.</para>
///
/// <para><b>Usage:</b> Compared case-insensitively; an unrecognised value reads as
/// <see cref="None"/>, so a settings blob written before this existed keeps the old behaviour.</para>
/// </remarks>
public static class MediaTransports
{
    /// <summary>
    /// No delivery: uploads stay on this machine, in the desktop head's own app-data folder.
    /// </summary>
    /// <remarks>
    /// The default, and a legal choice — it is right for an operator who only edits text from the
    /// desktop, and it is what every installation had before REQ-FN-062. It is not silent: the
    /// connection screen says so, because "the picture is on your laptop" is exactly the fact that
    /// went unsaid the first time.
    /// </remarks>
    public const string None = "None";

    /// <summary>
    /// Uploads are written over SSH to a directory on the server (the deployment's route).
    /// </summary>
    /// <remarks>
    /// Port 22 is the only channel this desktop has to the VPS filesystem, and the same access the
    /// operator already uses to reach the site's database. The bytes land directly in
    /// <c>/srv/data/techieblog/uploads</c>, which the container serves at <c>/uploads</c>.
    /// </remarks>
    public const string Sftp = "Sftp";

    /// <summary>
    /// Uploads are written to a filesystem path that genuinely reaches the server.
    /// </summary>
    /// <remarks>
    /// A mapped network drive or a UNC share — never a local folder that happens to be named after
    /// the server's. <c>MediaLocationProbe</c> refuses a local fixed drive outright, which is the
    /// specific guard for the mistake described on this type.
    /// </remarks>
    public const string Folder = "Folder";

    /// <summary>Every transport, in the order the setup screen offers them.</summary>
    public static readonly string[] All = { None, Sftp, Folder };

    /// <summary>
    /// Human-readable label for a transport, for the setup screen's picker.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The labels say where the file ends up rather than naming a
    /// protocol, because that is the decision the operator is actually making.</para>
    /// <para><b>Flow:</b> match the constant → return its label.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="transport">One of the constants on this type.</param>
    /// <returns>The label to render.</returns>
    public static string LabelFor(string transport)
    {
        if (string.Equals(transport, Sftp, StringComparison.OrdinalIgnoreCase))
        {
            return "Send to the server over SSH (SFTP)";
        }

        if (string.Equals(transport, Folder, StringComparison.OrdinalIgnoreCase))
        {
            return "Write to a mapped drive or network share";
        }

        return "Keep uploads on this machine";
    }
}
