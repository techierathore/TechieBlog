using Bunit;
using TrBlazeUI.Primitives.Extensions;
using Xunit;

namespace TechieBlog.Tests.Components.BlogUi;

/// <summary>
/// bUnit tests that render a REAL TrBlazeUI <c>Select</c> with a pre-selected value and NO
/// <c>DisplayTextSelector</c>, and assert what its trigger says before the listbox has ever been
/// opened (REQ-UI-034 / REQ-UI-038 / REQ-UI-039).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> This is the regression guard for the workaround this solution just
/// deleted. Until TrBlazeUI 2.0.2, a <c>SelectItem</c> only registered its display text once the
/// listbox had been rendered, so a pre-selected value fell back to <c>Value.ToString()</c> and the
/// admin pages showed the raw user id "1" or the sentinel "0" instead of a name. The fix for that
/// was <c>BlogUI.Common.SelectFirstPaintLabel</c> plus a <c>DisplayTextSelector</c> on all eighteen
/// call sites; 2.0.2 registers items while the listbox is closed, so all of it was removed.</para>
///
/// <para><b>What is pinned:</b> that the library resolves a pre-selected value to its item's
/// <c>Text</c> on the FIRST paint with no help from the application. If a future library version
/// regresses to echoing the raw value, <see cref="SelectResolvesAPreselectedValueToItsItemText"/>
/// and <see cref="SelectResolvesASentinelValueToItsItemText"/> fail here — loudly, in the suite —
/// instead of quietly shipping "0" / "1" to users again. Do not weaken these assertions to
/// <c>Assert.Contains</c> on a substring: the raw value must be excluded, not merely
/// unmentioned.</para>
///
/// <para><b>Dependencies:</b> bUnit and the real TrBlazeUI render tree, through
/// <see cref="SelectPreselectedValueProbe"/>. Compiles whenever <c>IncludeBlogUiTests</c> is true,
/// which is the default.</para>
/// </remarks>
public class SelectPreselectedValueRenderTests : BunitContext
{
    /// <summary>
    /// Options whose label is nothing like the value that binds them, as every admin user picker
    /// has: a numeric id showing a person's name and email.
    /// </summary>
    private static readonly KeyValuePair<string, string>[] UserOptions =
    [
        new("1", "Ravi Rathore (Ravi@techieblog.com)"),
        new("2", "Priya Sharma (priya@techieblog.com)")
    ];

    /// <summary>
    /// Registers the primitives the library's Select injects, exactly as the host does.
    /// </summary>
    public SelectPreselectedValueRenderTests()
    {
        Services.AddTrBlazeUIPrimitives();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>
    /// A pre-selected value renders its item's <c>Text</c> on the first paint — no click, no opened
    /// listbox, no application-supplied selector. The raw value "1" must not appear.
    /// </summary>
    [Fact]
    public void SelectResolvesAPreselectedValueToItsItemText()
    {
        // Arrange, Act
        var cut = Render<SelectPreselectedValueProbe>(parameters => parameters
            .Add(probe => probe.Value, "1")
            .Add(probe => probe.Placeholder, "Select a user")
            .Add(probe => probe.Options, UserOptions));

        // Assert
        Assert.Equal(
            "Ravi Rathore (Ravi@techieblog.com)",
            cut.Find("[data-testid='probe-select']").TextContent.Trim());
    }

    /// <summary>
    /// A sentinel value ("0" = all users, "" = my own records) resolves through the very same
    /// mechanism, because the pages register it as a real <c>SelectItem</c> rather than relying on
    /// the placeholder. This is what keeps /admin/images reading "All Users" instead of "0".
    /// </summary>
    [Fact]
    public void SelectResolvesASentinelValueToItsItemText()
    {
        // Arrange
        KeyValuePair<string, string>[] withSentinel = [new("0", "All Users"), .. UserOptions];

        // Act
        var cut = Render<SelectPreselectedValueProbe>(parameters => parameters
            .Add(probe => probe.Value, "0")
            .Add(probe => probe.Placeholder, "Pick an owner")
            .Add(probe => probe.Options, withSentinel));

        // Assert
        Assert.Equal("All Users", cut.Find("[data-testid='probe-select']").TextContent.Trim());
    }

    /// <summary>
    /// An empty bound value leaves the placeholder in place, so a picker with nothing chosen still
    /// prompts rather than rendering an empty trigger.
    /// </summary>
    [Fact]
    public void SelectShowsThePlaceholderWhenNothingIsBound()
    {
        // Arrange, Act
        var cut = Render<SelectPreselectedValueProbe>(parameters => parameters
            .Add(probe => probe.Value, string.Empty)
            .Add(probe => probe.Placeholder, "Select a user")
            .Add(probe => probe.Options, UserOptions));

        // Assert
        Assert.Equal("Select a user", cut.Find("[data-testid='probe-select']").TextContent.Trim());
    }

    /// <summary>
    /// A value with no matching item still falls back to the raw value, NOT to the placeholder.
    /// That is why every sentinel a page can bind must be declared as its own <c>SelectItem</c>;
    /// this test documents the boundary the pages are written against.
    /// </summary>
    [Fact]
    public void SelectEchoesAValueThatHasNoMatchingItem()
    {
        // Arrange, Act
        var cut = Render<SelectPreselectedValueProbe>(parameters => parameters
            .Add(probe => probe.Value, "0")
            .Add(probe => probe.Placeholder, "Pick an owner")
            .Add(probe => probe.Options, UserOptions));

        // Assert
        Assert.Equal("0", cut.Find("[data-testid='probe-select']").TextContent.Trim());
    }
}
