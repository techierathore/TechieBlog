using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components;
using System.Security.Claims;
using BlogModels;

namespace BlogUI.Pages.AdminPages;

partial class BlogsList : ComponentBase
{
    [Inject]
    public BlogEngine.Services.BlogSvc BlogService { get; set; }

    [Inject]
    public NavigationManager NavigationManager { get; set; }

    public List<BlogPost> ObjectList { get; set; }
    public List<BlogPost> FilteredList { get; set; }
    public BlogPost SelObject { get; set; }
    public string StatusMessage { get; set; }
    public bool IsError { get; set; }
    public bool ShowDeleteConfirm { get; set; }
    public BlogPost PostToDelete { get; set; }
    public string StatusFilter { get; set; } = "all";
    public bool IsProcessing { get; set; }
    public string SearchTerm { get; set; } = "";
    public string BulkAction { get; set; } = "";
    public bool SelectAll { get; set; }

    // Computed counts
    public int PublishedCount => ObjectList?.Count(p => p.Published) ?? 0;
    public int DraftCount => ObjectList?.Count(p => !p.Published && !p.IsScheduled) ?? 0;
    public int ScheduledCount => ObjectList?.Count(p => p.IsScheduled) ?? 0;
    public bool HasSelectedPosts => FilteredList?.Any(p => p.IsSelected) ?? false;

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; }

    ClaimsPrincipal LoggedInUser;
    long CurrentUserId;
    bool IsAdminOrEditor;

    protected override async Task OnInitializedAsync()
    {
        LoggedInUser = (await AuthStateTask).User;

        // Get user ID
        string userId = LoggedInUser.Claims.FirstOrDefault(
            c => c.Type == ClaimTypes.PrimarySid)?.Value;
        CurrentUserId = long.TryParse(userId, out long id) ? id : 0;

        // Check if user is Admin or Editor
        IsAdminOrEditor = LoggedInUser.IsInRole(AppRoles.Admin) ||
                          LoggedInUser.IsInRole(AppRoles.Editor);

        LoadPosts();
    }

    private void LoadPosts()
    {
        var posts = BlogService.GetAllPosts(CurrentUserId, IsAdminOrEditor);
        ObjectList = posts?.ToList() ?? new List<BlogPost>();
        ApplyFilter();
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
