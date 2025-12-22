using BlogEngine.Common;
using BlogModels;

namespace BlogEngine.Services;

/// <summary>
/// Service layer for category operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides business logic for CRUD operations on categories.</para>
/// <para><b>Dependencies:</b> ICategoryRepo for data access, SlugGenerator for URL slugs.</para>
/// </remarks>
public class CategorySvc
{
    private readonly ICategoryRepo CategoryRepo;

    public CategorySvc(ICategoryRepo categoryRepo)
    {
        CategoryRepo = categoryRepo;
    }

    /// <summary>
    /// Gets all categories ordered by name.
    /// </summary>
    /// <returns>List of all categories.</returns>
    public IEnumerable<Category> GetAllCategories()
    {
        return CategoryRepo.GetAll();
    }

    /// <summary>
    /// Gets all categories with their post counts.
    /// </summary>
    /// <returns>Categories with PostCount field populated.</returns>
    public IEnumerable<Category> GetAllWithCounts()
    {
        return CategoryRepo.GetAllWithCounts();
    }

    /// <summary>
    /// Gets a single category by ID.
    /// </summary>
    /// <param name="categoryId">Category ID.</param>
    /// <returns>Category if found, null otherwise.</returns>
    public Category GetCategory(long categoryId)
    {
        return CategoryRepo.GetSingle(categoryId);
    }

    /// <summary>
    /// Gets a category by its URL slug.
    /// </summary>
    /// <param name="slug">URL-friendly slug.</param>
    /// <returns>Category if found, null otherwise.</returns>
    public Category GetCategoryBySlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;
        return CategoryRepo.GetBySlug(slug);
    }

    /// <summary>
    /// Creates a new category with validation and slug generation.
    /// </summary>
    /// <param name="category">The category to create.</param>
    /// <returns>Result with created category on success, error message on failure.</returns>
    public Result<Category> CreateCategory(Category category)
    {
        if (category == null)
            return Result<Category>.Failure("Category cannot be null");

        if (string.IsNullOrWhiteSpace(category.CategoryName))
            return Result<Category>.Failure("Category name is required");

        // Generate slug if not provided
        if (string.IsNullOrWhiteSpace(category.Slug))
        {
            category.Slug = SlugGenerator.GenerateSlug(category.CategoryName);
        }

        // Check for duplicate slug
        if (CategoryRepo.SlugExists(category.Slug))
        {
            category.Slug = SlugGenerator.GenerateUniqueSlug(category.Slug, 1);
            int counter = 2;
            while (CategoryRepo.SlugExists(category.Slug) && counter < 100)
            {
                category.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(category.CategoryName), counter);
                counter++;
            }
        }

        try
        {
            var categoryId = CategoryRepo.InsertToGetId(category);
            category.CategoryId = categoryId;
            return Result<Category>.Success(category);
        }
        catch (Exception ex)
        {
            return Result<Category>.Failure($"Failed to create category: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates an existing category.
    /// </summary>
    /// <param name="category">The category to update.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result<Category> UpdateCategory(Category category)
    {
        if (category == null)
            return Result<Category>.Failure("Category cannot be null");

        if (category.CategoryId <= 0)
            return Result<Category>.Failure("Invalid category ID");

        if (string.IsNullOrWhiteSpace(category.CategoryName))
            return Result<Category>.Failure("Category name is required");

        // Check if category exists
        var existing = CategoryRepo.GetSingle(category.CategoryId);
        if (existing == null)
            return Result<Category>.Failure("Category not found");

        // Generate slug if not provided
        if (string.IsNullOrWhiteSpace(category.Slug))
        {
            category.Slug = SlugGenerator.GenerateSlug(category.CategoryName);
        }

        // Check for duplicate slug (exclude current category)
        if (CategoryRepo.SlugExists(category.Slug, category.CategoryId))
        {
            category.Slug = SlugGenerator.GenerateUniqueSlug(category.Slug, 1);
            int counter = 2;
            while (CategoryRepo.SlugExists(category.Slug, category.CategoryId) && counter < 100)
            {
                category.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(category.CategoryName), counter);
                counter++;
            }
        }

        try
        {
            CategoryRepo.Update(category);
            return Result<Category>.Success(category);
        }
        catch (Exception ex)
        {
            return Result<Category>.Failure($"Failed to update category: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves a category (insert or update based on CategoryId).
    /// </summary>
    /// <param name="category">The category to save.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result<Category> SaveCategory(Category category)
    {
        if (category == null)
            return Result<Category>.Failure("Category cannot be null");

        if (category.CategoryId <= 0)
        {
            return CreateCategory(category);
        }
        else
        {
            return UpdateCategory(category);
        }
    }

    /// <summary>
    /// Deletes a category.
    /// </summary>
    /// <param name="categoryId">ID of the category to delete.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result DeleteCategory(long categoryId)
    {
        if (categoryId <= 0)
            return Result.Failure("Invalid category ID");

        var existing = CategoryRepo.GetSingle(categoryId);
        if (existing == null)
            return Result.Failure("Category not found");

        try
        {
            CategoryRepo.Delete(categoryId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to delete category: {ex.Message}");
        }
    }
}
