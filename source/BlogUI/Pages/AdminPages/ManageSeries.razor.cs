using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using BlogModels;

namespace BlogUI.Pages.AdminPages;

partial class ManageSeries : ComponentBase
{
    [Parameter]
    public long PageId { get; set; }

    [Inject]
    NavigationManager AppNavManager { get; set; }

    [Inject]
    public BlogEngine.Services.SeriesSvc SeriesService { get; set; }

    [Inject]
    public AuthenticationStateProvider AuthStateProvider { get; set; }

    public string PageHeader { get; set; }
    public BlogSeries PageObj { get; set; }
    public List<BlogPost> SeriesPosts { get; set; }
    public string StatusMessage { get; set; }
    public bool IsError { get; set; }

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

    public void SaveData()
    {
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
