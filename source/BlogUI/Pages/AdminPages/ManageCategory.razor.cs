using Microsoft.AspNetCore.Components;
using BlogModels;

namespace BlogUI.Pages.AdminPages;

/// <summary>
/// Code-behind for the category editor page.
/// </summary>
partial class ManageCategory : ComponentBase
{
    /// <summary>Identifier of the category being edited. Zero creates a new category.</summary>
    [Parameter]
    public long PageId { get; set; }

    [Inject]
    NavigationManager AppNavManager { get; set; } = default!;

    /// <summary>Category service used to load and persist categories.</summary>
    [Inject]
    public BlogEngine.Services.CategorySvc CategoryService { get; set; } = default!;

    /// <summary>Panel heading shown above the form.</summary>
    public string PageHeader { get; set; } = string.Empty;

    /// <summary>The category being edited.</summary>
    public Category? PageObj { get; set; }

    /// <summary>Status text shown in the page-level alert.</summary>
    public string? StatusMessage { get; set; }

    /// <summary>True when <see cref="StatusMessage"/> reports a failure.</summary>
    public bool IsError { get; set; }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (PageId > 0)
        {
            PageHeader = "Edit Category";
            PageObj = await CategoryService.GetCategoryAsync(PageId);
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

    /// <summary>Prepares the form for creating a new category.</summary>
    private Task ResetPage()
    {
        PageHeader = "Add New Category";
        PageObj = new Category();
        StatusMessage = null;
        IsError = false;
        return Task.CompletedTask;
    }

    /// <summary>Validates and persists the category, then returns to the category list.</summary>
    /// <returns>A task that completes when the category has been saved.</returns>
    public async Task SaveDataAsync()
    {
        if (PageObj == null) return;

        if (string.IsNullOrWhiteSpace(PageObj.CategoryName))
        {
            StatusMessage = "Category name is required.";
            IsError = true;
            return;
        }

        var result = await CategoryService.SaveCategoryAsync(PageObj);

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
