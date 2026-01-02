using BlogModels;
using BlogModels.Models;
using Microsoft.AspNetCore.Components;

namespace BlogUI.Pages.BlogPages;

/// <summary>
/// Code-behind for the AuthorProfilePage component.
/// Displays an author's profile, published articles, and optional resume sections.
/// </summary>
public partial class AuthorProfilePage : ComponentBase
{
    /// <summary>
    /// Username from the URL route parameter.
    /// </summary>
    [Parameter]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Repository for user data access.
    /// </summary>
    [Inject]
    public IBlogUserRepo UserRepo { get; set; } = default!;

    /// <summary>
    /// Service for blog post operations.
    /// </summary>
    [Inject]
    public BlogEngine.Services.BlogSvc BlogService { get; set; } = default!;

    /// <summary>
    /// Repository for blog post data access.
    /// </summary>
    [Inject]
    public IBlogPostRepo PostRepo { get; set; } = default!;

    /// <summary>
    /// Navigation manager for redirects.
    /// </summary>
    [Inject]
    public NavigationManager Navigation { get; set; } = default!;

    /// <summary>
    /// The author being displayed.
    /// </summary>
    private AppUser? author;

    /// <summary>
    /// List of published articles by this author.
    /// </summary>
    private List<BlogPost> articles = new();

    /// <summary>
    /// Loading state indicator.
    /// </summary>
    private bool isLoading = true;

    /// <summary>
    /// Loads author data on component initialization.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        await LoadAuthorAsync();
    }

    /// <summary>
    /// Reloads author when the Username parameter changes.
    /// </summary>
    protected override async Task OnParametersSetAsync()
    {
        await LoadAuthorAsync();
    }

    /// <summary>
    /// Fetches the author by username and loads their published articles.
    /// </summary>
    private async Task LoadAuthorAsync()
    {
        if (string.IsNullOrEmpty(Username))
        {
            author = null;
            articles = new List<BlogPost>();
            isLoading = false;
            return;
        }

        isLoading = true;

        try
        {
            // Run synchronous repo calls on background thread
            await Task.Run(() =>
            {
                // Get author by username
                author = UserRepo.GetByUsername(Username);

                if (author != null)
                {
                    // Get published articles by this author
                    articles = PostRepo.GetAllById(author.UserId)
                        .Where(p => p.Published && !p.IsDeleted)
                        .OrderByDescending(p => p.CreatedOn)
                        .ToList();
                }
                else
                {
                    articles = new List<BlogPost>();
                }
            });
        }
        catch (Exception)
        {
            author = null;
            articles = new List<BlogPost>();
        }
        finally
        {
            isLoading = false;
        }
    }

    /// <summary>
    /// Gets the first tag as category, or "General".
    /// </summary>
    private string GetCategory(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
            return "General";

        var firstTag = tags.Split(',').FirstOrDefault()?.Trim();
        return string.IsNullOrEmpty(firstTag) ? "General" : firstTag;
    }

    /// <summary>
    /// Gets a short excerpt from content.
    /// </summary>
    private string GetExcerpt(string? content, int maxLength = 150)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        // Strip markdown/HTML basic
        var text = content
            .Replace("#", "")
            .Replace("*", "")
            .Replace("_", "")
            .Replace("`", "")
            .Trim();

        if (text.Length <= maxLength)
            return text;

        return text.Substring(0, maxLength).TrimEnd() + "...";
    }

    /// <summary>
    /// Gets reading time from content.
    /// </summary>
    private string GetReadingTime(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "1 min read";

        return BlogEngine.Common.ReadingTimeCalculator.Calculate(content);
    }
}
