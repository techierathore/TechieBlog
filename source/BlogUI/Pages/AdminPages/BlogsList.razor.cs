using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components;
using System.Security.Claims;
using BlogModels;

namespace BlogUI.Pages.AdminPages;

partial class BlogsList : ComponentBase
{
    [Inject]
    public BlogEngine.Services.BlogSvc BlogService { get; set; }

    public List<BlogPost> ObjectList { get; set; }
    public List<BlogPost> FilteredList { get; set; }
    public BlogPost SelObject { get; set; }
    public string StatusMessage { get; set; }
    public bool IsError { get; set; }
    public bool ShowDeleteConfirm { get; set; }
    public BlogPost PostToDelete { get; set; }
    public string StatusFilter { get; set; } = "all";
    public bool IsProcessing { get; set; }

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
            c => c.Type == ClaimTypes.NameIdentifier)?.Value;
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

    private void ApplyFilter()
    {
        if (ObjectList == null)
        {
            FilteredList = new List<BlogPost>();
            return;
        }

        FilteredList = StatusFilter switch
        {
            "published" => ObjectList.Where(p => p.Published).ToList(),
            "scheduled" => ObjectList.Where(p => p.IsScheduled).ToList(),
            "draft" => ObjectList.Where(p => !p.Published && !p.IsScheduled).ToList(),
            _ => ObjectList.ToList()
        };
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
