using Microsoft.AspNetCore.Components;
using BlogModels;

namespace BlogUI.Pages.AdminPages;

partial class CategoriesList : ComponentBase
{
    [Inject]
    public BlogEngine.Services.CategorySvc CategoryService { get; set; }

    public List<Category> ObjectList { get; set; }
    public string StatusMessage { get; set; }
    public bool IsError { get; set; }
    public bool ShowDeleteConfirm { get; set; }
    public Category CategoryToDelete { get; set; }

    protected override async Task OnInitializedAsync()
    {
        LoadCategories();
    }

    private void LoadCategories()
    {
        var categories = CategoryService.GetAllWithCounts();
        ObjectList = categories?.ToList() ?? new List<Category>();
    }

    private void ShowDeleteDialog(Category category)
    {
        CategoryToDelete = category;
        ShowDeleteConfirm = true;
    }

    private void CancelDelete()
    {
        CategoryToDelete = null;
        ShowDeleteConfirm = false;
    }

    private void ConfirmDelete()
    {
        if (CategoryToDelete == null) return;

        var result = CategoryService.DeleteCategory(CategoryToDelete.CategoryId);

        if (result.IsSuccess)
        {
            StatusMessage = $"Category \"{CategoryToDelete.CategoryName}\" deleted successfully.";
            IsError = false;
            LoadCategories();
        }
        else
        {
            StatusMessage = result.ErrorMessage;
            IsError = true;
        }

        CategoryToDelete = null;
        ShowDeleteConfirm = false;
    }
}
