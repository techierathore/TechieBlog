using Microsoft.AspNetCore.Components;
using BlogModels;

namespace BlogUI.Pages.AdminPages;

partial class ManageCategory : ComponentBase
{
    [Parameter]
    public long PageId { get; set; }

    [Inject]
    NavigationManager AppNavManager { get; set; }

    [Inject]
    public BlogEngine.Services.CategorySvc CategoryService { get; set; }

    public string PageHeader { get; set; }
    public Category PageObj { get; set; }
    public string StatusMessage { get; set; }
    public bool IsError { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (PageId > 0)
        {
            PageHeader = "Edit Category";
            PageObj = CategoryService.GetCategory(PageId);
            if (PageObj == null)
            {
                StatusMessage = "Category not found.";
                IsError = true;
                PageObj = new Category();
            }
        }
        else
        {
            await ResetPage();
        }
    }

    private Task ResetPage()
    {
        PageHeader = "Add New Category";
        PageObj = new Category();
        StatusMessage = null;
        IsError = false;
        return Task.CompletedTask;
    }

    public void SaveData()
    {
        if (string.IsNullOrWhiteSpace(PageObj.CategoryName))
        {
            StatusMessage = "Category name is required.";
            IsError = true;
            return;
        }

        var result = CategoryService.SaveCategory(PageObj);

        if (result.IsSuccess)
        {
            AppNavManager.NavigateTo("/admin/categories");
        }
        else
        {
            StatusMessage = result.ErrorMessage;
            IsError = true;
        }
    }
}
