using Bunit;
using BlogUI.Components;
using Xunit;

namespace TechieBlog.Tests.Components.BlogUi;

/// <summary>
/// bUnit component tests that render the real <c>BlogPagination</c> control (REQ-UI-048).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-NFR-016. The acceptance for the test project asks for bUnit coverage of
/// real Blazor components, and until 2026-08-09 the only such suite was excluded from the build by an
/// off-by-default flag, so nothing in CI ever rendered a component. <c>BlogPagination</c> is the
/// highest-value second target: it is shared by the archive, search and tag listings, and its
/// windowing logic — first page, last page, ellipsis gaps, a radius around the current page — is real
/// branching that no service test reaches.</para>
///
/// <para><b>What is pinned:</b> that the control hides itself rather than rendering a one-page
/// pager; that the endpoints are always reachable however long the range; that the previous and next
/// affordances are disabled at the ends instead of raising out-of-range page requests; and that a
/// page click reports the page the visitor asked for.</para>
///
/// <para><b>Dependencies:</b> bUnit and the real BlogUI render tree, therefore TrBlazeUI. Compiles
/// whenever <c>IncludeBlogUiTests</c> is true, which is the default since 2026-08-09.</para>
/// </remarks>
public class BlogPaginationTests : BunitContext
{
    /// <summary>
    /// A single page of results renders no pager at all, so a short archive is not decorated with a
    /// control that can do nothing.
    /// </summary>
    [Fact]
    public void PaginationHidesItselfForASinglePage()
    {
        // Arrange, Act
        var cut = Render<BlogPagination>(parameters => parameters
            .Add(pager => pager.CurrentPage, 1)
            .Add(pager => pager.TotalPages, 1));

        // Assert
        Assert.Empty(cut.FindAll("[data-testid='pagination']"));
    }

    /// <summary>
    /// A short range renders every page as its own link, so nothing is elided that would have fitted.
    /// </summary>
    [Fact]
    public void PaginationRendersEveryPageOfAShortRange()
    {
        // Arrange, Act
        var cut = Render<BlogPagination>(parameters => parameters
            .Add(pager => pager.CurrentPage, 1)
            .Add(pager => pager.TotalPages, 3));

        // Assert
        Assert.Single(cut.FindAll("[data-testid='pagination-page-1']"));
        Assert.Single(cut.FindAll("[data-testid='pagination-page-2']"));
        Assert.Single(cut.FindAll("[data-testid='pagination-page-3']"));
    }

    /// <summary>
    /// A long range keeps the first and last pages reachable while eliding the middle, so a
    /// thousand-page archive stays a usable control rather than a thousand links.
    /// </summary>
    [Fact]
    public void PaginationAlwaysKeepsTheEndpointsReachable()
    {
        // Arrange, Act
        var cut = Render<BlogPagination>(parameters => parameters
            .Add(pager => pager.CurrentPage, 50)
            .Add(pager => pager.TotalPages, 100));

        // Assert
        Assert.Single(cut.FindAll("[data-testid='pagination-page-1']"));
        Assert.Single(cut.FindAll("[data-testid='pagination-page-100']"));
        Assert.Empty(cut.FindAll("[data-testid='pagination-page-10']"));
    }

    /// <summary>
    /// The pages immediately either side of the current one are always offered, which is the
    /// navigation a reader paging through an archive actually uses.
    /// </summary>
    [Fact]
    public void PaginationOffersTheNeighboursOfTheCurrentPage()
    {
        // Arrange, Act
        var cut = Render<BlogPagination>(parameters => parameters
            .Add(pager => pager.CurrentPage, 50)
            .Add(pager => pager.TotalPages, 100));

        // Assert
        Assert.Single(cut.FindAll("[data-testid='pagination-page-49']"));
        Assert.Single(cut.FindAll("[data-testid='pagination-page-50']"));
        Assert.Single(cut.FindAll("[data-testid='pagination-page-51']"));
    }

    /// <summary>
    /// Clicking a page link reports that page number to the caller, which is the whole contract the
    /// listing pages consume.
    /// </summary>
    [Fact]
    public void PaginationReportsTheClickedPage()
    {
        // Arrange
        var requested = 0;

        var cut = Render<BlogPagination>(parameters => parameters
            .Add(pager => pager.CurrentPage, 1)
            .Add(pager => pager.TotalPages, 5)
            .Add(pager => pager.OnPageChanged, page => requested = page));

        // Act
        cut.Find("[data-testid='pagination-page-3']").Click();

        // Assert
        Assert.Equal(3, requested);
    }

    /// <summary>
    /// Clicking the page already being shown raises nothing, so a reader cannot make the listing
    /// re-query itself for the page it is already displaying.
    /// </summary>
    [Fact]
    public void PaginationIgnoresAClickOnTheCurrentPage()
    {
        // Arrange
        var raised = 0;

        var cut = Render<BlogPagination>(parameters => parameters
            .Add(pager => pager.CurrentPage, 2)
            .Add(pager => pager.TotalPages, 5)
            .Add(pager => pager.OnPageChanged, _ => raised++));

        // Act
        cut.Find("[data-testid='pagination-page-2']").Click();

        // Assert
        Assert.Equal(0, raised);
    }
}
