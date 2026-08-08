using BlogEngine.Common;
using BlogModels;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace BlogUI.Pages.AdminPages;

/// <summary>
/// Code-behind for the post editor page.
/// </summary>
partial class ManagePost : ComponentBase
{
    /// <summary>Identifier of the post being edited. Zero creates a new post.</summary>
    [Parameter]
    public long PageId { get; set; }

    [Inject]
    NavigationManager AppNavManager { get; set; } = default!;

    /// <summary>Blog post service used to load and persist posts.</summary>
    [Inject]
    public BlogEngine.Services.BlogSvc BlogService { get; set; } = default!;

    /// <summary>Category service supplying the category picker.</summary>
    [Inject]
    public BlogEngine.Services.CategorySvc CategoryService { get; set; } = default!;

    /// <summary>Tag service supplying and persisting post tags.</summary>
    [Inject]
    public BlogEngine.Services.TagSvc TagService { get; set; } = default!;

    /// <summary>Series service supplying the series picker.</summary>
    [Inject]
    public BlogEngine.Services.SeriesSvc SeriesService { get; set; } = default!;

    /// <summary>Provides the signed-in user's claims.</summary>
    [Inject]
    public AuthenticationStateProvider AuthStateProvider { get; set; } = default!;

    /// <summary>Panel heading shown above the editor.</summary>
    public string PageHeader { get; set; } = "New Post";

    /// <summary>The post being edited.</summary>
    public BlogPost? PageObj { get; set; }

    /// <summary>Markdown body bound to the editor component.</summary>
    public string AnswerDetail { get; set; } = string.Empty;

    /// <summary>Slug preview shown beneath the slug input.</summary>
    public string SlugPreview { get; set; } = string.Empty;

    /// <summary>Status text shown in the page-level alert.</summary>
    public string? StatusMessage { get; set; }

    /// <summary>True when <see cref="StatusMessage"/> reports a failure.</summary>
    public bool IsError { get; set; }

    /// <summary>True while a save/publish/schedule operation is in flight.</summary>
    public bool IsSaving { get; set; }

    /// <summary>Identifies which action is currently saving, for per-button spinners.</summary>
    public string SaveAction { get; set; } = string.Empty;

    /// <summary>Categories offered by the category picker.</summary>
    public List<Category> Categories { get; set; } = new();

    /// <summary>Selected category identifier, as a string for the Select binding.</summary>
    public string SelectedCategoryId { get; set; } = "0";

    /// <summary>Identifier of the signed-in user, used by the featured-image picker.</summary>
    public long CurrentUserId { get; set; }

    // Tag management

    /// <summary>All known tags, used for quick-add suggestions.</summary>
    public List<BlogTag> AvailableTags { get; set; } = new();

    /// <summary>Tags currently attached to the post.</summary>
    public List<BlogTag> SelectedTags { get; set; } = new();

    /// <summary>Text of the new-tag input.</summary>
    public string NewTagInput { get; set; } = string.Empty;

    // Scheduling

    /// <summary>Scheduled publication date (local time).</summary>
    public DateTime? ScheduledDate { get; set; }

    /// <summary>Scheduled publication time of day (local time).</summary>
    public TimeSpan? ScheduledTime { get; set; }

    // Series

    /// <summary>Series offered by the series picker.</summary>
    public List<BlogSeries> AvailableSeries { get; set; } = new();

    /// <summary>Selected series identifier, as a string for the Select binding.</summary>
    public string SelectedSeriesId { get; set; } = "0";

    /// <summary>Part number suggested for a newly added series post.</summary>
    public int SuggestedPartNumber { get; set; } = 1;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        // Load categories for dropdown
        Categories = CategoryService.GetAllCategories().ToList();

        // Resolve the signed-in user for the featured-image picker
        var initialAuthState = await AuthStateProvider.GetAuthenticationStateAsync();
        var initialUserIdClaim = initialAuthState.User.FindFirst(ClaimTypes.PrimarySid)?.Value;
        if (long.TryParse(initialUserIdClaim, out long initialUserId))
        {
            CurrentUserId = initialUserId;
        }

        // Load available tags for selection
        AvailableTags = TagService.GetAllTags().ToList();

        // Load available series for selection
        AvailableSeries = SeriesService.GetAllWithCounts().ToList();

        if (PageId > 0)
        {
            // Edit mode - load existing post
            PageObj = BlogService.GetSinglePost(PageId);
            if (PageObj != null)
            {
                PageHeader = "Edit Post";
                AnswerDetail = PageObj.PostContent;
                SlugPreview = PageObj.Slug;
                SelectedCategoryId = PageObj.CategoryId.ToString();

                // Load existing tags for this post
                SelectedTags = TagService.GetTagsForPost(PageId).ToList();

                // Load scheduling data if scheduled
                if (PageObj.ScheduledPublishOn.HasValue)
                {
                    var localScheduled = PageObj.ScheduledPublishOn.Value.ToLocalTime();
                    ScheduledDate = localScheduled.Date;
                    ScheduledTime = localScheduled.TimeOfDay;
                }

                // Load series data if part of a series
                if (PageObj.SeriesId.HasValue)
                {
                    SelectedSeriesId = PageObj.SeriesId.Value.ToString();
                }
            }
            else
            {
                PageHeader = "Post Not Found";
                StatusMessage = "The requested post could not be found.";
                IsError = true;
            }
        }
        else
        {
            // Create mode - new post
            PageObj = new BlogPost
            {
                Published = false
            };
            PageHeader = "New Post";
            SelectedTags = new List<BlogTag>();
            SelectedSeriesId = "0";
        }
    }

    /// <summary>
    /// Saves the post from the EditForm submit, preserving its current publish state.
    /// </summary>
    protected async Task SaveData()
    {
        if (IsSaving) return;
        IsSaving = true;
        StatusMessage = null;
        IsError = false;

        try
        {
            // Sync content from text area
            if (PageObj == null) return;
            PageObj.PostContent = AnswerDetail;

            // Set category from dropdown
            if (int.TryParse(SelectedCategoryId, out int categoryId))
            {
                PageObj.CategoryId = categoryId;
            }

            // Get current user ID
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            var userIdClaim = authState.User.FindFirst(ClaimTypes.PrimarySid)?.Value;
            if (long.TryParse(userIdClaim, out long userId))
            {
                PageObj.UserID = userId;
            }

            // Generate slug if not provided
            if (string.IsNullOrWhiteSpace(PageObj.Slug))
            {
                PageObj.Slug = SlugGenerator.GenerateSlug(PageObj.Title);
            }

            // Save the post
            var result = BlogService.SavePost(PageObj);

            if (result.IsSuccess)
            {
                // Get post ID (for new posts, it's now set on PageObj)
                var postId = result.Data?.PostID ?? PageObj.PostID;

                // Save tags for the post
                var tagIds = SelectedTags.Select(t => t.TagId).ToList();
                TagService.SetTagsForPost(postId, tagIds);

                StatusMessage = PageId > 0 ? "Post updated successfully!" : "Post created successfully!";
                IsError = false;
                // Navigate to blog list after short delay
                await Task.Delay(500);
                AppNavManager.NavigateTo("/BlogsList");
            }
            else
            {
                StatusMessage = result.ErrorMessage;
                IsError = true;
            }
        }
        catch
        {
            StatusMessage = "An error occurred while saving the post.";
            IsError = true;
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Applies the edited title and refreshes the generated slug preview.
    /// </summary>
    protected void OnTitleChanged(string value)
    {
        if (PageObj != null)
        {
            PageObj.Title = value;
            // Auto-generate slug preview when title changes
            if (PageId == 0 || string.IsNullOrWhiteSpace(PageObj.Slug))
            {
                SlugPreview = SlugGenerator.GenerateSlug(value);
                PageObj.Slug = SlugPreview;
            }
        }
    }

    /// <summary>
    /// Applies markdown edits to the post body.
    /// </summary>
    protected Task OnMarkdownValueChanged(string value)
    {
        AnswerDetail = value;
        if (PageObj != null)
        {
            PageObj.PostContent = value;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds an existing tag to the selected tags list.
    /// </summary>
    protected void AddExistingTag(BlogTag tag)
    {
        if (tag != null && !SelectedTags.Any(t => t.TagId == tag.TagId))
        {
            SelectedTags.Add(tag);
        }
    }

    /// <summary>
    /// Removes a tag from the selected tags list.
    /// </summary>
    protected void RemoveTag(BlogTag tag)
    {
        SelectedTags.RemoveAll(t => t.TagId == tag.TagId);
    }

    /// <summary>
    /// Adds a new tag by name (creates if doesn't exist).
    /// </summary>
    protected void AddNewTag()
    {
        if (string.IsNullOrWhiteSpace(NewTagInput))
            return;

        var tagName = NewTagInput.Trim();

        // Check if already selected (case-insensitive)
        if (SelectedTags.Any(t => t.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase)))
        {
            NewTagInput = string.Empty;
            return;
        }

        // Get or create the tag
        var tag = TagService.GetOrCreateTag(tagName);
        if (tag != null)
        {
            SelectedTags.Add(tag);

            // Refresh available tags list to include new tag
            if (!AvailableTags.Any(t => t.TagId == tag.TagId))
            {
                AvailableTags.Add(tag);
            }
        }

        NewTagInput = string.Empty;
    }

    /// <summary>
    /// Gets tags not already selected for the dropdown.
    /// </summary>
    protected IEnumerable<BlogTag> GetUnselectedTags()
    {
        var selectedIds = SelectedTags.Select(t => t.TagId).ToHashSet();
        return AvailableTags.Where(t => !selectedIds.Contains(t.TagId));
    }

    /// <summary>
    /// Handles saving the post as a draft (not published).
    /// </summary>
    protected async Task HandleSaveDraft()
    {
        if (IsSaving) return;
        SaveAction = "draft";
        await SaveWithPublishState(false);
    }

    /// <summary>
    /// Handles publishing the post.
    /// </summary>
    protected async Task HandlePublish()
    {
        if (IsSaving) return;
        SaveAction = "publish";
        await SaveWithPublishState(true);
    }

    /// <summary>
    /// Handles saving changes to an already published post.
    /// </summary>
    protected async Task HandleSaveChanges()
    {
        if (IsSaving) return;
        SaveAction = "save";
        if (PageObj == null) return;
        await SaveWithPublishState(PageObj.Published);
    }

    /// <summary>
    /// Handles unpublishing a post.
    /// </summary>
    protected async Task HandleUnpublish()
    {
        if (IsSaving) return;
        SaveAction = "unpublish";
        IsSaving = true;
        StatusMessage = null;
        IsError = false;

        try
        {
            if (PageObj == null) return;
            var result = BlogService.UnpublishPost(PageObj.PostID);
            if (result.IsSuccess)
            {
                PageObj.Published = false;
                StatusMessage = "Post unpublished successfully!";
                IsError = false;
            }
            else
            {
                StatusMessage = result.ErrorMessage;
                IsError = true;
            }
        }
        catch
        {
            StatusMessage = "An error occurred while unpublishing the post.";
            IsError = true;
        }
        finally
        {
            IsSaving = false;
            SaveAction = string.Empty;
        }
    }

    /// <summary>
    /// Helper method to save the post with a specific publish state.
    /// </summary>
    private async Task SaveWithPublishState(bool publish)
    {
        IsSaving = true;
        StatusMessage = null;
        IsError = false;

        try
        {
            // Sync content from text area
            if (PageObj == null) return;
            PageObj.PostContent = AnswerDetail;

            // Set category from dropdown
            if (int.TryParse(SelectedCategoryId, out int categoryId))
            {
                PageObj.CategoryId = categoryId;
            }

            // Set series from dropdown
            if (long.TryParse(SelectedSeriesId, out long seriesId) && seriesId > 0)
            {
                PageObj.SeriesId = seriesId;
                if (!PageObj.SeriesPartNumber.HasValue || PageObj.SeriesPartNumber <= 0)
                {
                    PageObj.SeriesPartNumber = SeriesService.GetNextPartNumber(seriesId);
                }
            }
            else
            {
                PageObj.SeriesId = null;
                PageObj.SeriesPartNumber = null;
            }

            // Get current user ID
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            var userIdClaim = authState.User.FindFirst(ClaimTypes.PrimarySid)?.Value;
            if (long.TryParse(userIdClaim, out long userId))
            {
                PageObj.UserID = userId;
            }

            // Generate slug if not provided
            if (string.IsNullOrWhiteSpace(PageObj.Slug))
            {
                PageObj.Slug = SlugGenerator.GenerateSlug(PageObj.Title);
            }

            // Save using appropriate method based on publish state
            Result<BlogPost> result;
            if (publish)
            {
                result = BlogService.PublishPost(PageObj);
            }
            else
            {
                result = BlogService.SaveDraft(PageObj);
            }

            if (result.IsSuccess)
            {
                // Get post ID (for new posts, it's now set on PageObj)
                var postId = result.Data?.PostID ?? PageObj.PostID;

                // Update local object with saved data
                if (result.Data != null)
                {
                    PageObj = result.Data;
                }

                // Save tags for the post
                var tagIds = SelectedTags.Select(t => t.TagId).ToList();
                TagService.SetTagsForPost(postId, tagIds);

                if (publish)
                {
                    StatusMessage = PageId > 0 ? "Post published successfully!" : "Post created and published!";
                }
                else
                {
                    StatusMessage = PageId > 0 ? "Draft saved successfully!" : "Draft created successfully!";
                }
                IsError = false;

                // If publishing, redirect to list after short delay
                if (publish)
                {
                    await Task.Delay(500);
                    AppNavManager.NavigateTo("/BlogsList");
                }
            }
            else
            {
                StatusMessage = result.ErrorMessage;
                IsError = true;
            }
        }
        catch
        {
            StatusMessage = "An error occurred while saving the post.";
            IsError = true;
        }
        finally
        {
            IsSaving = false;
            SaveAction = string.Empty;
        }
    }

    /// <summary>
    /// Gets the combined scheduled date and time.
    /// </summary>
    protected DateTime? GetScheduledDateTime()
    {
        if (!ScheduledDate.HasValue)
            return null;

        var date = ScheduledDate.Value.Date;
        if (ScheduledTime.HasValue)
        {
            date = date.Add(ScheduledTime.Value);
        }
        else
        {
            // Default to 9:00 AM if no time specified
            date = date.AddHours(9);
        }
        return date;
    }

    /// <summary>
    /// Handles scheduling the post for future publication.
    /// </summary>
    protected async Task HandleSchedule()
    {
        if (IsSaving) return;

        var scheduledDateTime = GetScheduledDateTime();
        if (!scheduledDateTime.HasValue)
        {
            StatusMessage = "Please select a date for scheduling.";
            IsError = true;
            return;
        }

        if (scheduledDateTime <= DateTime.Now)
        {
            StatusMessage = "Scheduled time must be in the future.";
            IsError = true;
            return;
        }

        SaveAction = "schedule";
        IsSaving = true;
        StatusMessage = null;
        IsError = false;

        try
        {
            // Sync content from text area
            if (PageObj == null) return;
            PageObj.PostContent = AnswerDetail;

            // Set category from dropdown
            if (int.TryParse(SelectedCategoryId, out int categoryId))
            {
                PageObj.CategoryId = categoryId;
            }

            // Set series from dropdown
            if (long.TryParse(SelectedSeriesId, out long seriesId) && seriesId > 0)
            {
                PageObj.SeriesId = seriesId;
                if (!PageObj.SeriesPartNumber.HasValue || PageObj.SeriesPartNumber <= 0)
                {
                    PageObj.SeriesPartNumber = SeriesService.GetNextPartNumber(seriesId);
                }
            }
            else
            {
                PageObj.SeriesId = null;
                PageObj.SeriesPartNumber = null;
            }

            // Get current user ID
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            var userIdClaim = authState.User.FindFirst(ClaimTypes.PrimarySid)?.Value;
            if (long.TryParse(userIdClaim, out long userId))
            {
                PageObj.UserID = userId;
            }

            // Generate slug if not provided
            if (string.IsNullOrWhiteSpace(PageObj.Slug))
            {
                PageObj.Slug = SlugGenerator.GenerateSlug(PageObj.Title);
            }

            // Schedule the post
            var scheduledUtc = scheduledDateTime.Value.ToUniversalTime();
            var result = BlogService.SchedulePost(PageObj, scheduledUtc);

            if (result.IsSuccess)
            {
                // Update local object with saved data
                if (result.Data != null)
                {
                    PageObj = result.Data;
                }

                // Save tags for the post
                var postId = result.Data?.PostID ?? PageObj.PostID;
                var tagIds = SelectedTags.Select(t => t.TagId).ToList();
                TagService.SetTagsForPost(postId, tagIds);

                StatusMessage = $"Post scheduled for {scheduledDateTime:dddd, MMMM dd, yyyy 'at' h:mm tt}";
                IsError = false;
            }
            else
            {
                StatusMessage = result.ErrorMessage;
                IsError = true;
            }
        }
        catch
        {
            StatusMessage = "An error occurred while scheduling the post.";
            IsError = true;
        }
        finally
        {
            IsSaving = false;
            SaveAction = string.Empty;
        }
    }

    /// <summary>
    /// Handles canceling a scheduled post (reverts to draft).
    /// </summary>
    protected async Task HandleCancelSchedule()
    {
        if (IsSaving) return;
        if (PageObj == null || PageObj.PostID <= 0) return;

        SaveAction = "cancelschedule";
        IsSaving = true;
        StatusMessage = null;
        IsError = false;

        try
        {
            var result = BlogService.CancelSchedule(PageObj.PostID);

            if (result.IsSuccess)
            {
                PageObj.ScheduledPublishOn = null;
                ScheduledDate = null;
                ScheduledTime = null;
                StatusMessage = "Schedule canceled. Post is now a draft.";
                IsError = false;
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
            IsSaving = false;
            SaveAction = string.Empty;
        }
    }
}
