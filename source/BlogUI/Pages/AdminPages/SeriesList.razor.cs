using Microsoft.AspNetCore.Components;
using BlogModels;

namespace BlogUI.Pages.AdminPages;

partial class SeriesList : ComponentBase
{
    [Inject]
    public BlogEngine.Services.SeriesSvc SeriesService { get; set; }

    public List<BlogSeries> ObjectList { get; set; }
    public string StatusMessage { get; set; }
    public bool IsError { get; set; }
    public bool ShowDeleteConfirm { get; set; }
    public BlogSeries SeriesToDelete { get; set; }

    protected override async Task OnInitializedAsync()
    {
        LoadSeries();
    }

    private void LoadSeries()
    {
        var series = SeriesService.GetAllWithCounts();
        ObjectList = series?.ToList() ?? new List<BlogSeries>();
    }

    private void ShowDeleteDialog(BlogSeries series)
    {
        SeriesToDelete = series;
        ShowDeleteConfirm = true;
    }

    private void CancelDelete()
    {
        SeriesToDelete = null;
        ShowDeleteConfirm = false;
    }

    private void ConfirmDelete()
    {
        if (SeriesToDelete == null) return;

        var result = SeriesService.DeleteSeries(SeriesToDelete.SeriesId);

        if (result.IsSuccess)
        {
            StatusMessage = $"Series \"{SeriesToDelete.Name}\" deleted successfully.";
            IsError = false;
            LoadSeries();
        }
        else
        {
            StatusMessage = result.ErrorMessage;
            IsError = true;
        }

        SeriesToDelete = null;
        ShowDeleteConfirm = false;
    }
}
