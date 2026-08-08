namespace BlogModels;

/// <summary>
/// Selects which subscribers a newsletter send targets — everyone, or a filtered segment.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets the composer dispatch to the whole active list or to a narrower
/// segment without the service growing one overload per filter.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>The admin screen builds an audience (default = <see cref="Everyone"/>).</item>
///   <item><c>INewsletterService.SendAsync</c> passes it to the repository.</item>
///   <item>The repository turns it into a parameterised <c>WHERE</c> clause — never string
///         concatenation.</item>
/// </list>
///
/// <para><b>Dependencies:</b> None — dependency-leaf contract.</para>
///
/// <para><b>Usage:</b> <c>NewsletterAudience.Everyone</c> for a full send;
/// <c>new NewsletterAudience { EmailFilter = "contoso.com" }</c> for a segment. The three
/// properties combine with AND, so a filter and a cap narrow the same set rather than each
/// producing their own.</para>
///
/// <para><b>Consent:</b> the default audience only reaches addresses that completed double opt-in.
/// <see cref="IncludeInactive"/> is the one switch that overrides that, and setting it turns a
/// marketing send into an unsolicited one — see the remarks on that property before using it.</para>
/// </remarks>
public class NewsletterAudience
{
    /// <summary>
    /// Case-insensitive substring matched anywhere in the subscriber's address, via PostgreSQL
    /// <c>ILIKE '%value%'</c>. Empty disables email filtering entirely. Typically a domain
    /// (<c>contoso.com</c>) — but because it is an unanchored substring, a short value matches far
    /// more than intended: <c>"co"</c> catches every <c>.com</c> address on the list.
    /// </summary>
    /// <remarks>
    /// Admin-supplied and passed as a bound parameter, never concatenated into SQL. Any
    /// <c>%</c> or <c>_</c> the administrator types is still interpreted as an <c>ILIKE</c>
    /// wildcard, so those characters widen the match rather than being matched literally.
    /// </remarks>
    public string EmailFilter { get; set; } = string.Empty;

    /// <summary>
    /// When true, the confirmation requirement is dropped and every subscriber row matching the
    /// other criteria is mailed. Defaults to false, which is what keeps an ordinary send to
    /// consenting recipients only.
    /// </summary>
    /// <remarks>
    /// Consider this switch dangerous. Because <c>IsConfirmed</c> is the only consent bit the
    /// schema has — <c>Subscriber.IsActive</c> is an alias of it, see the remarks on
    /// <see cref="Subscriber"/> — the set it unlocks is "never opted in" and "explicitly
    /// unsubscribed" mixed together, with no way to tell them apart. Enabling it therefore mails
    /// people who asked to be removed. It exists for operational replays against a known-good list,
    /// not for growing reach.
    /// </remarks>
    public bool IncludeInactive { get; set; }

    /// <summary>
    /// Hard cap on recipients, applied as a <c>LIMIT</c> after the ordering (most recent
    /// subscribers first). Zero or negative means no cap — the value is passed through
    /// <c>NULLIF(@MaxRecipients, 0)</c>, so zero becomes a null limit rather than "mail nobody".
    /// A negative value reaches the database as a negative <c>LIMIT</c>, which PostgreSQL rejects.
    /// </summary>
    public int MaxRecipients { get; set; }

    /// <summary>
    /// An unfiltered audience: every confirmed subscriber, uncapped. A fresh instance each time, so
    /// a caller that mutates the result does not affect anyone else's send.
    /// </summary>
    public static NewsletterAudience Everyone => new NewsletterAudience();
}
