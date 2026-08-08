namespace BlogModels;

/// <summary>
/// A block of author-managed sidebar content.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Would let an author place arbitrary markup — an about box, a promo, a link
/// list — in the layout's sidebar without a code change.</para>
///
/// <para><b>Code Flow:</b> Unreferenced. No repository, service, component or migration in this
/// solution mentions <c>Widget</c>; the sidebar is composed from fixed components instead.</para>
///
/// <para><b>Dependencies:</b> None that exist — there is no <c>Widget</c> table.</para>
///
/// <para><b>Usage:</b> A deletion candidate. If the feature is ever built, note that
/// <see cref="WidgetContent"/> is raw markup destined for a layout region and would need explicit
/// sanitisation; nothing here provides it.</para>
/// </remarks>
public class Widget
{
	/// <summary>
	/// Intended surrogate key of the widget. No table backs it, so the value is whatever the code
	/// that constructed the instance put there — never a database-assigned identity.
	/// </summary>
	public long WidgetID
	{ get; set; }

	/// <summary>
	/// Administrator-facing label identifying the block in a management list. Not rendered to a
	/// visitor; the sidebar would show <see cref="WidgetContent"/>, not this.
	/// </summary>
	public string WidgetName
	{ get; set; } = string.Empty;

	/// <summary>
	/// The block's body — <b>raw markup</b>, not Markdown and not plain text. It is the whole point
	/// of the type (arbitrary author-authored HTML in a layout region) and therefore its whole
	/// danger: it would be emitted unescaped, so a stored value containing a
	/// <c>script</c> element executes for every visitor of every page carrying the sidebar. If this
	/// feature is ever built, sanitise on write and on render; nothing on this type does either.
	/// </summary>
	public string WidgetContent
	{ get; set; } = string.Empty;

	/// <summary>
	/// When the block was last edited, for an admin "last changed" column. Nothing sets it, so it
	/// is always <see cref="DateTime.MinValue"/> on a real instance.
	/// </summary>
	public DateTime UpdatedTime
	{ get; set; }

	/// <summary>
	/// The <c>BlogUser</c> that would own the block. Intended as the authorisation anchor — an
	/// author may edit only their own widgets — but no check exists, because nothing reads the
	/// type.
	/// </summary>
	public long UserID
	{ get; set; }

}
