using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using BlogModels;

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
            PageObj = SeriesService.GetSeries(PageId);
            if (PageObj == null)
            {
                StatusMessage = "Series not found.";
                IsError = true;
                PageObj = new BlogSeries { Status = "In Progress" };
            }
            else
            {
                // Load posts in this series
                SeriesPosts = SeriesService.GetPostsInSeries(PageId).ToList();
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
        PageObj = new BlogSeries { Status = "In Progress" };
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

    /// <summary>Validates and persists the series, then returns to the series list.</summary>
    public void SaveData()
    {
        if (PageObj == null) return;

        if (string.IsNullOrWhiteSpace(PageObj.Name))
        {
            StatusMessage = "Series name is required.";
            IsError = true;
            return;
        }

        var result = SeriesService.SaveSeries(PageObj);

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
