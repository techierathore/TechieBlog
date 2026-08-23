using BlogEngine.Common;
using BlogEngine.Services;
using BlogModels;
using BlogUI.Components;
using BlogUI.Pages.AdminPages;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Security.Claims;
using TrBlazeUI.Primitives.Extensions;

namespace TechieBlog.Tests.Components.BlogUi;

/// <summary>
/// bUnit tests for the post editor's route-parameter reload (REQ-UI-016).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Locks the two halves of the 2026-08-11 defect and its fix. The defect:
/// <c>ManagePost</c> loaded the post in <c>OnInitializedAsync</c>, which the router runs once per
/// visit, so navigating client-side from <c>/ManagePost/{a}</c> to <c>/ManagePost/{b}</c> left post
/// A's title, slug, body and sidebar fields on screen under post B's URL — and a save from there
/// would have written A's content over B. The fix moves the load to <c>OnParametersSetAsync</c> and
/// hands <c>PostMarkdownEditor</c> a <c>ResetKey</c> that releases its keystroke latch.</para>
/// <para><b>Why the keystroke tests live here too:</b> the latch being released is exactly the
/// clobber TR-057 was fixed to stop, so the reload tests are only safe in the company of tests that
/// prove typing still survives. Removing either half hides a regression in the other.</para>
/// <para><b>Dependencies:</b> BlogUI, therefore this suite compiles only under
/// <c>-p:IncludeBlogUiTests=true</c>; the csproj removes this folder otherwise.</para>
/// </remarks>
public class PostEditorRouteReloadTests : BunitContext
{
    /// <summary>Body text seeded for the first post opened in a test.</summary>
    private const string PostABody = "## Indexing basics\n\nB-tree indexes serve predicates.";

    /// <summary>Body text seeded for the second post opened in a test.</summary>
    private const string PostBBody = "# The Markdown Kitchen Sink\n\nEvery construct this site renders.";

    /// <summary>
    /// Reads the text the markdown editor's element is currently carrying.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this is not just <c>TextContent</c>:</b> a textarea can carry its text two ways,
    /// and REQ-UI-016 has used both. The uncontrolled workaround built for TR-057 seeded the element
    /// through CHILD CONTENT, which reads as <c>TextContent</c>; TrBlazeUI's own <c>Textarea</c> —
    /// restored 2026-08-11 once 2.0.2 fixed TR-057 — renders a <c>value</c> ATTRIBUTE instead. Both
    /// are the same fact: the text the editor element would show. Asserting through this helper
    /// keeps these tests pinned to the BEHAVIOUR (which document's text is in the editor) rather
    /// than to the markup shape, so they still fail for a real regression under either
    /// implementation.</para>
    /// </remarks>
    /// <typeparam name="TComponent">Component under test — the editor itself, or the page hosting it.</typeparam>
    /// <param name="cut">The rendered component holding the editor.</param>
    /// <returns>The text the editor element carries.</returns>
    private static string EditorText<TComponent>(IRenderedComponent<TComponent> cut)
        where TComponent : IComponent
    {
        var element = cut.Find("[data-testid='markdown-input']");
        return element.GetAttribute("value") ?? element.TextContent;
    }

    /// <summary>
    /// Changing <c>ResetKey</c> replaces the editor's text even after the user has typed, which is
    /// what stops post A's body from surviving into post B.
    /// </summary>
    [Fact]
    public void EditorAdoptsNewDocumentWhenResetKeyChanges()
    {
        // Arrange
        Services.AddSingleton(new MarkdownRenderer());
        var cut = Render<PostMarkdownEditor>(parameters => parameters
            .Add(editor => editor.Value, PostABody)
            .Add(editor => editor.ResetKey, "post-5-1"));
        cut.Find("[data-testid='markdown-input']").Input("edited by the user");

        // Act
        cut.Render(parameters => parameters
            .Add(editor => editor.Value, PostBBody)
            .Add(editor => editor.ResetKey, "post-7-2"));

        // Assert
        Assert.Equal(PostBBody, EditorText(cut));
    }

    /// <summary>
    /// A value echoed back under an UNCHANGED <c>ResetKey</c> is still ignored, so the TR-057
    /// keystroke-loss fix is intact.
    /// </summary>
    [Fact]
    public void EditorIgnoresEchoedValueWhenResetKeyUnchanged()
    {
        // Arrange
        Services.AddSingleton(new MarkdownRenderer());
        var cut = Render<PostMarkdownEditor>(parameters => parameters
            .Add(editor => editor.Value, PostABody)
            .Add(editor => editor.ResetKey, "post-5-1"));
        cut.Find("[data-testid='markdown-input']").Input("## Live heading");

        // Act — a stale echo of an earlier keystroke arrives from the parent.
        cut.Render(parameters => parameters
            .Add(editor => editor.Value, "## Live headin")
            .Add(editor => editor.ResetKey, "post-5-1"));

        // Assert — the DOM was never rewritten, so the element still carries the pre-edit text.
        Assert.Equal(PostABody, EditorText(cut));
    }

    /// <summary>
    /// Typing fifteen characters one at a time, with the parent echoing each one back, yields all
    /// fifteen in order — the TR-057 regression the reset must not re-open.
    /// </summary>
    [Fact]
    public void EditorKeepsEveryKeystrokeInOrder()
    {
        // Arrange
        Services.AddSingleton(new MarkdownRenderer());
        const string Typed = "## Live heading";
        var lastNotified = string.Empty;
        var cut = Render<PostMarkdownEditor>(parameters => parameters
            .Add(editor => editor.Value, string.Empty)
            .Add(editor => editor.ResetKey, "post-5-1")
            .Add(editor => editor.ValueChanged, value => lastNotified = value));

        // Act — each keystroke sends the whole field value, and the parent echoes it straight back.
        for (int typedLength = 1; typedLength <= Typed.Length; typedLength++)
        {
            cut.Find("[data-testid='markdown-input']").Input(Typed[..typedLength]);
            cut.Render(parameters => parameters
                .Add(editor => editor.Value, lastNotified)
                .Add(editor => editor.ResetKey, "post-5-1"));
        }

        // Assert
        Assert.Equal(Typed, lastNotified);
        Assert.Equal(Typed.Length, lastNotified.Length);
    }

    /// <summary>
    /// A route-parameter change reloads the post: title, slug and body all become post B's.
    /// </summary>
    [Fact]
    public void ManagePostReloadsBodyAndSlugWhenRouteParameterChanges()
    {
        // Arrange
        var cut = RenderEditorPage(5);

        // Act
        cut.Render(parameters => parameters.Add(page => page.PageId, 7));

        // Assert
        Assert.Equal("The Markdown Kitchen Sink", cut.Instance.PageObj!.Title);
        Assert.Equal("the-markdown-kitchen-sink", cut.Instance.PageObj.Slug);
        Assert.Equal("the-markdown-kitchen-sink", cut.Instance.SlugPreview);
        Assert.Equal(PostBBody, cut.Instance.AnswerDetail);
        Assert.Equal(PostBBody, EditorText(cut));
    }

    /// <summary>
    /// A route-parameter change reloads every metadata sidebar field, so no value from post A
    /// survives to be written over post B.
    /// </summary>
    [Fact]
    public void ManagePostReloadsEveryMetadataFieldWhenRouteParameterChanges()
    {
        // Arrange
        var cut = RenderEditorPage(5);

        // Act
        cut.Render(parameters => parameters.Add(page => page.PageId, 7));

        // Assert
        Assert.Equal("5", cut.Instance.SelectedCategoryId);
        Assert.Equal("0", cut.Instance.SelectedSeriesId);
        Assert.Equal("Every Markdown construct this site renders.", cut.Instance.PageObj!.Abstract);
        Assert.Equal("/_content/BlogUI/images/FullLogo.png", cut.Instance.PageObj.FeaturedImage);
        Assert.Equal(new[] { "markdown" }, cut.Instance.SelectedTags.Select(tag => tag.TagName));
        Assert.Null(cut.Instance.PageObj.SeriesPartNumber);
    }

    /// <summary>
    /// Leaving an existing post for the new-post route clears the form instead of keeping the old
    /// post, which is what turned a "new post" into a silent update.
    /// </summary>
    [Fact]
    public void ManagePostClearsFormWhenRouteDropsThePostId()
    {
        // Arrange
        var cut = RenderEditorPage(5);

        // Act
        cut.Render(parameters => parameters.Add(page => page.PageId, 0L));

        // Assert
        Assert.Equal("New Post", cut.Instance.PageHeader);
        Assert.Equal(0, cut.Instance.PageObj!.PostID);
        Assert.Equal(string.Empty, cut.Instance.AnswerDetail);
        Assert.Equal(string.Empty, cut.Instance.SlugPreview);
        Assert.Equal("0", cut.Instance.SelectedCategoryId);
        Assert.Empty(cut.Instance.SelectedTags);
    }

    /// <summary>
    /// The editor's reset key moves with the loaded post, which is the signal that releases the
    /// markdown editor's keystroke latch.
    /// </summary>
    [Fact]
    public void ManagePostMovesTheEditorResetKeyOnEveryLoad()
    {
        // Arrange
        var cut = RenderEditorPage(5);
        var keyForPostA = cut.Instance.EditorResetKey;

        // Act
        cut.Render(parameters => parameters.Add(page => page.PageId, 7));

        // Assert
        Assert.NotEqual(keyForPostA, cut.Instance.EditorResetKey);
    }

    /// <summary>
    /// Re-rendering with the SAME route parameter does not re-read the post, so an in-page edit is
    /// never thrown away by a stray parameter set.
    /// </summary>
    [Fact]
    public void ManagePostDoesNotReloadWhenRouteParameterIsUnchanged()
    {
        // Arrange
        var postRepo = BuildPostRepo();
        var cut = RenderEditorPage(5, postRepo);
        cut.Instance.PageObj!.Title = "Edited in the browser";

        // Act
        cut.Render(parameters => parameters.Add(page => page.PageId, 5));

        // Assert
        Assert.Equal("Edited in the browser", cut.Instance.PageObj.Title);
        postRepo.Received(1).GetSingleAsync(5, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Renders <c>ManagePost</c> for one post identifier with every dependency faked.
    /// </summary>
    /// <param name="pageId">Route parameter to open the editor on.</param>
    /// <param name="postRepo">Optional shared post repository, for call-count assertions.</param>
    /// <returns>The rendered page, ready for a further parameter change.</returns>
    private IRenderedComponent<ManagePost> RenderEditorPage(long pageId, IBlogPostRepo? postRepo = null)
    {
        RegisterEditorServices(postRepo ?? BuildPostRepo());
        ComponentFactories.AddStub<ImagePicker>();
        return Render<ManagePost>(parameters => parameters.Add(page => page.PageId, pageId));
    }

    /// <summary>
    /// Registers the services <c>ManagePost</c> resolves, over faked repositories.
    /// </summary>
    /// <param name="postRepo">Post repository returning the two fixture posts.</param>
    private void RegisterEditorServices(IBlogPostRepo postRepo)
    {
        // TrBlazeUI's Select, DatePicker and TimePicker each import a JS module on first render;
        // none of them is what these tests assert, so the calls are answered rather than planned.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddTrBlazeUIPrimitives();
        Services.AddSingleton(new MarkdownRenderer());
        Services.AddSingleton(new BlogSvc(postRepo, NullLogger<BlogSvc>.Instance));
        Services.AddSingleton(new CategorySvc(BuildCategoryRepo(), NullLogger<CategorySvc>.Instance));
        Services.AddSingleton(new TagSvc(BuildTagRepo(), NullLogger<TagSvc>.Instance));
        Services.AddSingleton(new SeriesSvc(BuildSeriesRepo(), postRepo, NullLogger<SeriesSvc>.Instance));

        // UAT-023 mechanism B: ManagePost now injects ISiteCacheNotifier. The website's own
        // no-op registration is the correct stand-in here — these tests assert route-reload
        // behaviour, not the cache-refresh call, and a null registration would make every render
        // in this file throw at DI resolution.
        Services.AddSingleton<ISiteCacheNotifier>(new NullSiteCacheNotifier());

        // ManagePost renders <SiteBrandTitle> (added 2026-08-23 so the page has a browser-tab title
        // at all), which resolves ISiteSettingsService to read the configured site name. Same
        // reasoning as the notifier above: these tests assert route-reload behaviour, not branding,
        // so the service is stubbed rather than exercised — but it must be registered or every
        // render in this file throws at DI resolution.
        var siteSettings = Substitute.For<BlogModels.Interfaces.ISiteSettingsService>();
        siteSettings.GetSettingsAsync()
            .Returns(Task.FromResult(new BlogModels.Models.SiteSettings { SiteTitle = "TechieBlog" }));
        Services.AddSingleton(siteSettings);

        var authorization = AddAuthorization();
        authorization.SetAuthorized("Ravi@techieblog.com");
        authorization.SetClaims(new Claim(ClaimTypes.PrimarySid, "1"));
    }

    /// <summary>
    /// Builds a post repository holding the two fixture posts, which differ in every field the
    /// editor binds.
    /// </summary>
    /// <returns>A substituted <see cref="IBlogPostRepo"/>.</returns>
    private static IBlogPostRepo BuildPostRepo()
    {
        var postRepo = Substitute.For<IBlogPostRepo>();
        postRepo.GetSingleAsync(5, Arg.Any<CancellationToken>()).Returns(new BlogPost
        {
            PostID = 5,
            Title = "Indexing Basics for .NET Developers",
            Slug = "postgres-indexing-for-dotnet-developers",
            PostContent = PostABody,
            Abstract = "B-tree, partial and expression indexes explained.",
            CategoryId = 2,
            SeriesId = 2,
            SeriesPartNumber = 1,
            FeaturedImage = "/_content/BlogUI/images/Podcastbg.jpg",
            Published = true
        });
        postRepo.GetSingleAsync(7, Arg.Any<CancellationToken>()).Returns(new BlogPost
        {
            PostID = 7,
            Title = "The Markdown Kitchen Sink",
            Slug = "the-markdown-kitchen-sink",
            PostContent = PostBBody,
            Abstract = "Every Markdown construct this site renders.",
            CategoryId = 5,
            SeriesId = null,
            SeriesPartNumber = null,
            FeaturedImage = "/_content/BlogUI/images/FullLogo.png",
            Published = true
        });
        return postRepo;
    }

    /// <summary>Builds a category repository offering the seeded categories.</summary>
    /// <returns>A substituted <see cref="ICategoryRepo"/>.</returns>
    private static ICategoryRepo BuildCategoryRepo()
    {
        var categoryRepo = Substitute.For<ICategoryRepo>();
        categoryRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Category>
        {
            new() { CategoryId = 2, CategoryName = "Programming" },
            new() { CategoryId = 5, CategoryName = "Technology" }
        });
        return categoryRepo;
    }

    /// <summary>Builds a tag repository giving each fixture post a distinct tag set.</summary>
    /// <returns>A substituted <see cref="IBlogTagRepo"/>.</returns>
    private static IBlogTagRepo BuildTagRepo()
    {
        var tagRepo = Substitute.For<IBlogTagRepo>();
        tagRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<BlogTag>
        {
            new() { TagId = 8, TagName = "postgresql" },
            new() { TagId = 10, TagName = "markdown" }
        });
        tagRepo.GetTagsForPostAsync(5, Arg.Any<CancellationToken>()).Returns(new List<BlogTag>
        {
            new() { TagId = 8, TagName = "postgresql" }
        });
        tagRepo.GetTagsForPostAsync(7, Arg.Any<CancellationToken>()).Returns(new List<BlogTag>
        {
            new() { TagId = 10, TagName = "markdown" }
        });
        return tagRepo;
    }

    /// <summary>Builds a series repository offering the seeded series.</summary>
    /// <returns>A substituted <see cref="IBlogSeriesRepo"/>.</returns>
    private static IBlogSeriesRepo BuildSeriesRepo()
    {
        var seriesRepo = Substitute.For<IBlogSeriesRepo>();
        seriesRepo.GetAllWithCountsAsync(Arg.Any<CancellationToken>()).Returns(new List<BlogSeries>
        {
            new() { SeriesId = 2, Name = "PostgreSQL for .NET Developers", PostCount = 2 }
        });
        return seriesRepo;
    }
}
