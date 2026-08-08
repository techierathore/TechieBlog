using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components;
using System.Security.Claims;
using BlogModels;

namespace BlogUI.Pages.AdminPages;

/// <summary>
/// Administration list of blog posts (REQ-UI-017 / BRD-14).
/// </summary>
/// <remarks>
/// The page is gated by <c>AppPolicies.AuthorOrAbove</c> so that an Author — who is
/// allowed to open the post editor — can also reach the list. Row scoping is enforced
/// server-side by <c>BlogSvc.GetAllPosts(userId, isAdminOrEditor)</c>: an Author only
/// ever receives their own posts, while Editor and Admin receive every post.
/// </remarks>
partial class BlogsList : ComponentBase
{
    /// <summary>Blog post service supplying the list and the publish/delete operations.</summary>
    [Inject]
    public BlogEngine.Services.BlogSvc BlogService { get; set; } = default!;

    /// <summary>Navigation manager used for preview redirects.</summary>
    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    /// <summary>All posts visible to the signed-in user.</summary>
    public List<BlogPost> ObjectList { get; set; } = new();

    /// <summary>Posts remaining after the status and search filters are applied.</summary>
    public List<BlogPost> FilteredList { get; set; } = new();

    /// <summary>Currently selected post.</summary>
    public BlogPost? SelObject { get; set; }

    /// <summary>Feedback message rendered in the page alert.</summary>
    public string? StatusMessage { get; set; }

    /// <summary>True when <see cref="StatusMessage"/> describes a failure.</summary>
    public bool IsError { get; set; }

    /// <summary>True while the delete confirmation dialog is open.</summary>
    public bool ShowDeleteConfirm { get; set; }

    /// <summary>Post awaiting delete confirmation.</summary>
    public BlogPost? PostToDelete { get; set; }

    /// <summary>Active status tab: all, published, draft or scheduled.</summary>
    public string StatusFilter { get; set; } = "all";

    /// <summary>True while a publish/unpublish/schedule operation is running.</summary>
    public bool IsProcessing { get; set; }

    /// <summary>Free-text search term applied to title, slug and author.</summary>
    public string SearchTerm { get; set; } = "";

    /// <summary>Selected bulk action key.</summary>
    public string BulkAction { get; set; } = "";

    /// <summary>True when the select-all checkbox is checked.</summary>
    public bool SelectAll { get; set; }

    /// <summary>Number of published posts.</summary>
    public int PublishedCount => ObjectList?.Count(p => p.Published) ?? 0;

    /// <summary>Number of unpublished, unscheduled posts.</summary>
    public int DraftCount => ObjectList?.Count(p => !p.Published && !p.IsScheduled) ?? 0;

    /// <summary>Number of scheduled posts.</summary>
    public int ScheduledCount => ObjectList?.Count(p => p.IsScheduled) ?? 0;

    /// <summary>True when at least one row is ticked.</summary>
    public bool HasSelectedPosts => FilteredList?.Any(p => p.IsSelected) ?? false;

    /// <summary>True when a status tab other than "all" or a search term is active.</summary>
    public bool HasActiveFilters => StatusFilter != "all" || !string.IsNullOrEmpty(SearchTerm);

    /// <summary>Sub-title describing the scope of the list for the current user.</summary>
    public string PageDescription => isAdminOrEditor
        ? "Every post on the blog."
        : "The posts you have written.";

    /// <summary>Heading rendered by the empty state.</summary>
    public string EmptyTitle => HasActiveFilters ? "No posts match your filters" : "No posts yet";

    /// <summary>Body text rendered by the empty state.</summary>
    public string EmptyDescription => HasActiveFilters
        ? "Try a different status tab or clear the search term."
        : "Create your first post to get started.";

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    private ClaimsPrincipal? loggedInUser;
    private long currentUserId;
    private bool isAdminOrEditor;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        loggedInUser = (await AuthStateTask).User;

        // Get user ID
        string? userId = loggedInUser.Claims.FirstOrDefault(
            c => c.Type == ClaimTypes.PrimarySid)?.Value;
        currentUserId = long.TryParse(userId, out long id) ? id : 0;

        // Check if user is Admin or Editor
        isAdminOrEditor = loggedInUser.IsInRole(AppRoles.Admin) ||
                          loggedInUser.IsInRole(AppRoles.Editor);

        LoadPosts();
    }

    /// <summary>
    /// Loads the posts visible to the signed-in user. REQ-UI-017: the service applies the
    /// author scoping — Admin/Editor get every post, everyone else only their own.
    /// </summary>
    private void LoadPosts()
    {
        var posts = BlogService.GetAllPosts(currentUserId, isAdminOrEditor);
        ObjectList = posts?.ToList() ?? new List<BlogPost>();
        ApplyFilter();
    }

    /// <summary>Handles the select-all checkbox toggle.</summary>
    /// <param name="isChecked">New checked state.</param>
    private void OnSelectAllChanged(bool isChecked)
    {
        SelectAll = isChecked;
        ToggleSelectAll();
    }

    private void SetFilter(string filter)
    {
        StatusFilter = filter;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (ObjectList == null)
        {
            FilteredList = new List<BlogPost>();
            return;
        }

        // Apply status filter
        var filtered = StatusFilter switch
        {
            "published" => ObjectList.Where(p => p.Published),
            "scheduled" => ObjectList.Where(p => p.IsScheduled),
            "draft" => ObjectList.Where(p => !p.Published && !p.IsScheduled),
            _ => ObjectList.AsEnumerable()
        };

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var searchLower = SearchTerm.ToLower();
            filtered = filtered.Where(p =>
                (p.Title?.ToLower().Contains(searchLower) ?? false) ||
                (p.Slug?.ToLower().Contains(searchLower) ?? false) ||
                (p.BlogWriter?.ToLower().Contains(searchLower) ?? false));
        }

        FilteredList = filtered.ToList();
    }

    private void ClearFilters()
    {
        StatusFilter = "all";
        SearchTerm = "";
        ApplyFilter();
    }

    private void ToggleSelectAll()
    {
        if (FilteredList == null) return;

        foreach (var post in FilteredList)
        {
            post.IsSelected = SelectAll;
        }
    }

    private void NavigateToPreview(BlogPost post)
    {
        if (post.Published)
        {
            NavigationManager.NavigateTo($"/post/{post.Slug}");
        }
        else
        {
            NavigationManager.NavigateTo($"/admin/preview/{post.PostID}");
        }
    }

    private void ApplyBulkAction()
    {
        if (string.IsNullOrEmpty(BulkAction) || !HasSelectedPosts) return;

        var selectedPosts = FilteredList.Where(p => p.IsSelected).ToList();

        foreach (var post in selectedPosts)
        {
            switch (BulkAction)
            {
                case "publish":
                    QuickPublish(post);
                    break;
                case "unpublish":
                    QuickUnpublish(post);
                    break;
                case "delete":
                    BlogService.DeletePost(post.PostID);
                    break;
            }
        }

        BulkAction = "";
        SelectAll = false;
        LoadPosts();
    }

    private void QuickPublish(BlogPost post)
    {
        if (IsProcessing || post == null) return;
        IsProcessing = true;
        StatusMessage = null;

        try
        {
            var result = BlogService.QuickPublish(post.PostID);
            if (result.IsSuccess)
            {
                StatusMessage = $"Post \"{post.Title}\" published successfully!";
                IsError = false;
                LoadPosts();
            }
            else
            {
                StatusMessage = result.ErrorMessage;
                IsError = true;
            }
        }
        catch
        {
            StatusMessage = "An error occurred while publishing.";
            IsError = true;
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private void QuickUnpublish(BlogPost post)
    {
        if (IsProcessing || post == null) return;
        IsProcessing = true;
        StatusMessage = null;

        try
        {
            var result = BlogService.UnpublishPost(post.PostID);
            if (result.IsSuccess)
            {
                StatusMessage = $"Post \"{post.Title}\" unpublished successfully!";
                IsError = false;
                LoadPosts();
            }
            else
            {
                StatusMessage = result.ErrorMessage;
                IsError = true;
            }
        }
        catch
        {
            StatusMessage = "An error occurred while unpublishing.";
            IsError = true;
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private void CancelSchedule(BlogPost post)
    {
        if (IsProcessing || post == null) return;
        IsProcessing = true;
        StatusMessage = null;

        try
        {
            var result = BlogService.CancelSchedule(post.PostID);
            if (result.IsSuccess)
            {
                StatusMessage = $"Schedule canceled for \"{post.Title}\". Post is now a draft.";
                IsError = false;
                LoadPosts();
            }
            else
            {
                StatusMessage = result.ErrorMessage;
                IsError = true;
            }
        }
        catch
        {
            StatusMessage = "An error occurred while canceling the schedule.";
            IsError = true;
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private void ShowDeleteDialog(BlogPost post)
    {
        PostToDelete = post;
        ShowDeleteConfirm = true;
    }

    private void CancelDelete()
    {
        PostToDelete = null;
        ShowDeleteConfirm = false;
    }

    private void ConfirmDelete()
    {
        if (PostToDelete == null) return;

        var result = BlogService.DeletePost(PostToDelete.PostID);

        if (result.IsSuccess)
        {
            StatusMessage = $"Post \"{PostToDelete.Title}\" deleted successfully.";
            IsError = false;
            LoadPosts();
        }
        else
        {
            StatusMessage = result.ErrorMessage;
            IsError = true;
        }

        PostToDelete = null;
        ShowDeleteConfirm = false;
    }
}
