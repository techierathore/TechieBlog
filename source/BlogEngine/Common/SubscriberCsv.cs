using System.Globalization;
using System.Text;
using BlogModels;

namespace BlogEngine.Common;

/// <summary>
/// Builds the admin subscriber-list CSV export (REQ-FN-031).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The export must hold exactly the subscribers the admin asked for, with the
/// fields the database really holds, and must open safely in a spreadsheet. Keeping the formatting
/// in one pure function makes both properties testable without a browser.</para>
///
/// <para><b>Code Flow:</b> the subscribers page passes the rows it is currently showing (the status
/// tab and the search term already applied) to <see cref="Build"/> and hands the returned text to the
/// browser download.</para>
///
/// <para><b>Dependencies:</b> None beyond <see cref="Subscriber"/>; no I/O.</para>
///
/// <para><b>Usage:</b> Every field is quoted with embedded quotes doubled. A field that begins with
/// <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> is prefixed with an apostrophe so a hostile display name
/// cannot run as a spreadsheet formula (CSV injection).</para>
/// </remarks>
public static class SubscriberCsv
{
    /// <summary>
    /// The header row, in column order.
    /// </summary>
    public const string Header = "Email,Name,SubscribedOn,Status";

    /// <summary>
    /// Formats subscribers as CSV text.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> One line per subscriber, in the order given. The status column is
    /// the subscriber's real state (<c>Active</c> or <c>Inactive</c>), not a constant.</para>
    /// <para><b>Flow:</b> write the header → quote and guard each text field → append the date and
    /// status.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="subscribers">The rows to export.</param>
    /// <returns>The CSV text, with a trailing newline after every row.</returns>
    public static string Build(IEnumerable<Subscriber> subscribers)
    {
        ArgumentNullException.ThrowIfNull(subscribers);

        var builder = new StringBuilder();
        builder.Append(Header).Append('\n');
        foreach (var subscriber in subscribers)
        {
            builder.Append(Quote(subscriber.Email)).Append(',')
                .Append(Quote(subscriber.Name)).Append(',')
                .Append(subscriber.SubscribedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(subscriber.IsActive ? "Active" : "Inactive")
                .Append('\n');
        }

        return builder.ToString();
    }

    private static string Quote(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && "=+-@".Contains(text[0]))
            text = "'" + text;

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
