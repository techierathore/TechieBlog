using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using BlogModels;

using BlogUI.Common;

namespace BlogUI.Pages.AdminPages;

/// <summary>
/// Code-behind for the series editor page.
/// </summary>
partial class ManageSeries : ComponentBase
{
    /// <summary>Identifier of the series being edited. Zero creates a new series.</summary>
    [Parameter]
    public long PageId { get; set; }

    [Inject]
    NavigationManager AppNavManager { get; set; } = default!;

    /// <summary>Series service used to load and persist series.</summary>
    [Inject]
    public BlogEngine.Services.SeriesSvc SeriesService { get; set; } = default!;

    /// <summary>Provides the signed-in user's claims.</summary>
    [Inject]
    public AuthenticationStateProvider AuthStateProvider { get; set; } = default!;

    /// <summary>Panel heading shown above the form.</summary>
    public string PageHeader { get; set; } = string.Empty;

    /// <summary>The series being edited.</summary>
    public BlogSeries? PageObj { get; set; }

    /// <summary>Posts already assigned to this series.</summary>
    public List<BlogPost> SeriesPosts { get; set; } = new();

    /// <summary>Status text shown in the page-level alert.</summary>
    public string? StatusMessage { get; set; }

    /// <summary>True when <see cref="StatusMessage"/> reports a failure.</summary>
    public bool IsError { get; set; }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (PageId > 0)
        {
            PageHeader = "Edit Series";
            PageObj = await SeriesService.GetSeriesAsync(PageId);
            if (PageObj == null)
            {
                StatusMessage = "Series not found.";
                IsError = true;
                PageObj = new BlogSeries { Status = SeriesStatus.InProgress };
            }
            else
            {
                // Load posts in this series
                SeriesPosts = (await SeriesService.GetPostsInSeriesAsync(PageId)).ToList();
            }
        }
        else
        {
            await ResetPage();
        }
    }

    /// <summary>Prepares the form for creating a new series owned by the current user.</summary>
    private async Task ResetPage()
    {
        PageHeader = "New Series";
        PageObj = new BlogSeries { Status = SeriesStatus.InProgress };
        SeriesPosts = new List<BlogPost>();
        StatusMessage = null;
        IsError = false;

        // Set current user as author
        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        var userIdClaim = authState.User.FindFirst(ClaimTypes.PrimarySid)?.Value;
        if (long.TryParse(userIdClaim, out long userId))
        {
            PageObj.AuthorId = userId;
        }
    }

    /// <summary>Moves one part up or down in the pending reading order; Save persists it.</summary>
    /// <param name="index">Current position of the part.</param>
    /// <param name="delta">-1 to move up, 1 to move down.</param>
    private void MovePart(int index, int delta)
    {
        var target = index + delta;
        if (target < 0 || target >= SeriesPosts.Count) return;
        (SeriesPosts[index], SeriesPosts[target]) = (SeriesPosts[target], SeriesPosts[index]);
    }

    /// <summary>Validates and persists the series, then returns to the series list.</summary>
    /// <returns>A task that completes when the series has been saved.</returns>
    public async Task SaveDataAsync()
    {
        if (PageObj == null) return;

        if (string.IsNullOrWhiteSpace(PageObj.Name))
        {
            StatusMessage = "Series name is required.";
            IsError = true;
            return;
        }

        var result = await SeriesService.SaveSeriesAsync(PageObj);

        // REQ-UI-024: persist the reading order the author arranged with the arrows.
        if (result.IsSuccess && PageId > 0 && SeriesPosts.Count > 1)
        {
            var reorder = await SeriesService.ReorderPartsAsync(PageId, SeriesPosts.Select(p => p.PostID).ToList());
            if (!reorder.IsSuccess)
            {
                StatusMessage = reorder.ErrorMessage;
                IsError = true;
                return;
            }
        }

        if (result.IsSuccess)
        {
            AppNavManager.NavigateTo("/admin/series");
        }
        else
        {
            StatusMessage = result.ErrorMessage;
            IsError = true;
        }
    }
}
