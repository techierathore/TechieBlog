using BlogModels;
using BlogUI;
using BlogUI.Pages.AdminPages;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using System.Reflection;

namespace TechieBlog.Tests.Routing;

/// <summary>
/// UAT-024 regression guard: a published post's preview opens OUTSIDE the current admin window,
/// while an unpublished post's preview keeps navigating the current window to the in-app route.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>BlogsList.razor.cs</c>'s <c>NavigateToPreviewAsync</c> used to send a
/// PUBLISHED post straight through <c>NavigationManager.NavigateTo($"/post/{slug}")</c> — the
/// current window navigated to the public page. On the website that cost only a Back-button click;
/// inside BlogApp's chrome-less <c>BlazorWebView</c> there was no way back at all except restarting
/// the app, which is the defect the owner reported. The fix routes a published post through
/// <see cref="IExternalLinkOpener"/> instead and leaves an unpublished post's route (which has no
/// public URL at all) exactly as it was. These tests would have caught the regression either
/// direction: a published post silently staying in-window, or an unpublished post being handed to
/// the external opener with no public URL to open.</para>
/// <para><b>Why direct instantiation instead of bUnit:</b> the decision under test needs only two
/// collaborators — <see cref="NavigationManager"/> and <see cref="IExternalLinkOpener"/> — both
/// plain <c>[Inject]</c> properties on a <see cref="Microsoft.AspNetCore.Components.ComponentBase"/>
/// that <c>NavigateToPreviewAsync</c> never renders against (no <c>StateHasChanged</c>, no child
/// content). Constructing the page directly and invoking the method reflectively (it is
/// intentionally <c>private</c> — an implementation detail, not part of the page's public surface)
/// exercises the REAL compiled logic without the cost or flakiness risk of rendering the whole
/// TrBlazeUI-heavy posts grid, which several other suites in this repository (see
/// <c>DesktopStatusBarPlacementTests</c>-adjacent notes) deliberately avoid for a page this test
/// does not need to render.</para>
/// <para><b>Dependencies:</b> xUnit v3, NSubstitute for <see cref="IExternalLinkOpener"/>. A
/// hand-written <see cref="NavigationManager"/> subclass (bUnit's fake requires a full render
/// context this test does not otherwise need) records what it was asked to navigate to.</para>
/// </remarks>
public class PostPreviewNavigationTests
{
    /// <summary>
    /// A published post's preview opens externally via <see cref="IExternalLinkOpener"/> with its
    /// public URL, and the current admin window is never navigated.
    /// </summary>
    [Fact]
    public async Task PublishedPostOpensExternallyAndLeavesAdminWindowInPlace()
    {
        // Arrange
        var linkOpener = Substitute.For<IExternalLinkOpener>();
        var navigation = new RecordingNavigationManager();
        var page = new BlogsList { LinkOpener = linkOpener, NavigationManager = navigation };
        var post = new BlogPost { PostID = 12, Slug = "postgres-indexing", Published = true };

        // Act
        await InvokeNavigateToPreviewAsync(page, post);

        // Assert
        await linkOpener.Received(1).OpenAsync("/post/postgres-indexing");
        Assert.Null(navigation.NavigatedTo);
    }

    /// <summary>
    /// An unpublished post has no public URL, so its preview still navigates the CURRENT window to
    /// the in-app admin preview route, unchanged, and the external opener is never invoked.
    /// </summary>
    [Fact]
    public async Task UnpublishedPostNavigatesCurrentWindowToAdminPreview()
    {
        // Arrange
        var linkOpener = Substitute.For<IExternalLinkOpener>();
        var navigation = new RecordingNavigationManager();
        var page = new BlogsList { LinkOpener = linkOpener, NavigationManager = navigation };
        var post = new BlogPost { PostID = 34, Slug = "uat-repro-post-abstract-staleness", Published = false };

        // Act
        await InvokeNavigateToPreviewAsync(page, post);

        // Assert — NavigateToCore receives the URI exactly as NavigateTo was called with it; the
        // base NavigationManager does not resolve it to absolute (that is a concrete
        // implementation's job, e.g. the browser's, which this recording double is not).
        Assert.Equal("/admin/preview/34", navigation.NavigatedTo);
        await linkOpener.DidNotReceive().OpenAsync(Arg.Any<string>());
    }

    /// <summary>
    /// Invokes <c>BlogsList.NavigateToPreviewAsync</c>, which is deliberately <c>private</c>.
    /// </summary>
    /// <param name="page">A <see cref="BlogsList"/> instance with its injected properties set.</param>
    /// <param name="post">The post to preview.</param>
    private static async Task InvokeNavigateToPreviewAsync(BlogsList page, BlogPost post)
    {
        var method = typeof(BlogsList).GetMethod(
            "NavigateToPreviewAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "BlogsList.NavigateToPreviewAsync was not found — check for a rename (UAT-024).");

        var task = (Task)method.Invoke(page, new object[] { post })!;
        await task;
    }

    /// <summary>
    /// A minimal <see cref="NavigationManager"/> that records the last URI it was asked to navigate
    /// to, without performing any real navigation.
    /// </summary>
    private sealed class RecordingNavigationManager : NavigationManager
    {
        /// <summary>The URI passed to the most recent <see cref="NavigateTo(string)"/> call, or <c>null</c>.</summary>
        public string? NavigatedTo { get; private set; }

        /// <summary>Initialises the manager with a fixed base and current URI.</summary>
        public RecordingNavigationManager()
        {
            Initialize("http://localhost/", "http://localhost/BlogsList");
        }

        /// <inheritdoc />
        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            NavigatedTo = uri;
        }
    }
}

/// <summary>
/// UAT-024 regression guard for the second named location: <c>PreviewPost.razor</c>'s
/// "View Live Post" button.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The button used to render as <c>&lt;Button Href="/post/{slug}"&gt;</c> — a
/// plain in-window link with no target, so clicking it navigated the CURRENT window exactly like
/// <c>BlogsList</c>'s old behaviour, and carried the identical BlogApp defect. It is only ever
/// rendered when the previewed post is published (the surrounding <c>@if (!blogPost.Published)</c>
/// branch renders "Publish Now" instead), so unlike <c>BlogsList</c> there is no unpublished case
/// to guard here — this proves the one case that exists: the button hands the post's public URL to
/// <see cref="IExternalLinkOpener"/> rather than navigating in-window.</para>
/// <para><b>Why reflection:</b> both <c>blogPost</c> (a private field) and the <c>@inject</c>-
/// generated <c>LinkOpener</c> (a <c>protected</c> property, per the Razor compiler's own
/// convention) are inaccessible from outside the component in ordinary C#; reflection reaches the
/// real compiled members without weakening their declared accessibility for production code.</para>
/// <para><b>Dependencies:</b> xUnit v3, NSubstitute for <see cref="IExternalLinkOpener"/>.</para>
/// </remarks>
public class PreviewPostViewLivePostTests
{
    /// <summary>
    /// "View Live Post" opens the post's public URL through <see cref="IExternalLinkOpener"/>
    /// rather than navigating the current admin window.
    /// </summary>
    [Fact]
    public async Task ViewLivePostOpensPublicUrlExternally()
    {
        // Arrange
        var linkOpener = Substitute.For<IExternalLinkOpener>();
        var page = new PreviewPost();
        SetLinkOpener(page, linkOpener);
        SetBlogPost(page, new BlogPost { PostID = 12, Slug = "postgres-indexing", Published = true });

        // Act
        await InvokeViewLivePostAsync(page);

        // Assert
        await linkOpener.Received(1).OpenAsync("/post/postgres-indexing");
    }

    /// <summary>
    /// No post loaded (still loading, or the not-found branch) is a no-op — there is no slug to
    /// build a URL from, and the caller must not be handed a broken open request.
    /// </summary>
    [Fact]
    public async Task ViewLivePostDoesNothingWhenNoPostIsLoaded()
    {
        // Arrange
        var linkOpener = Substitute.For<IExternalLinkOpener>();
        var page = new PreviewPost();
        SetLinkOpener(page, linkOpener);

        // Act
        await InvokeViewLivePostAsync(page);

        // Assert
        await linkOpener.DidNotReceive().OpenAsync(Arg.Any<string>());
    }

    /// <summary>Sets the <c>@inject</c>-generated, <c>protected</c> <c>LinkOpener</c> property.</summary>
    private static void SetLinkOpener(PreviewPost page, IExternalLinkOpener linkOpener)
    {
        var property = typeof(PreviewPost).GetProperty("LinkOpener", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("PreviewPost.LinkOpener was not found — check for a rename (UAT-024).");
        property.SetValue(page, linkOpener);
    }

    /// <summary>Sets the private <c>blogPost</c> field the page's markup and handlers read.</summary>
    private static void SetBlogPost(PreviewPost page, BlogPost post)
    {
        var field = typeof(PreviewPost).GetField("blogPost", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("PreviewPost.blogPost was not found — check for a rename.");
        field.SetValue(page, post);
    }

    /// <summary>Invokes the deliberately <c>private</c> <c>ViewLivePostAsync</c> handler.</summary>
    private static async Task InvokeViewLivePostAsync(PreviewPost page)
    {
        var method = typeof(PreviewPost).GetMethod("ViewLivePostAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("PreviewPost.ViewLivePostAsync was not found — check for a rename (UAT-024).");

        var task = (Task)method.Invoke(page, null)!;
        await task;
    }
}
