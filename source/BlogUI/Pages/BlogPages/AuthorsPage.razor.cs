using BlogModels;
using BlogModels.Models;
using Microsoft.AspNetCore.Components;

namespace BlogUI.Pages.BlogPages;

/// <summary>
/// Code-behind for the AuthorsPage component.
/// Lists all authors who have published at least one article.
/// </summary>
public partial class AuthorsPage : ComponentBase
{
    /// <summary>
    /// Repository for user data access.
    /// </summary>
    [Inject]
    public IBlogUserRepo UserRepo { get; set; } = default!;

    /// <summary>
    /// Repository for blog post data access.
    /// </summary>
    [Inject]
    public IBlogPostRepo PostRepo { get; set; } = default!;

    /// <summary>
    /// List of authors with published posts.
    /// </summary>
    private List<AppUser> authors = new();

    /// <summary>
    /// Dictionary mapping author UserId to their article count.
    /// </summary>
    private Dictionary<long, int> articleCounts = new();

    /// <summary>
    /// Loading state indicator.
    /// </summary>
    private bool isLoading = true;

    /// <summary>
    /// Loads all authors on component initialization.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        await LoadAuthorsAsync();
    }

    /// <summary>
    /// Fetches all authors who have published posts and calculates their article counts.
    /// </summary>
    private async Task LoadAuthorsAsync()
    {
        isLoading = true;

        try
        {
            // Run synchronous repo calls on background thread
            await Task.Run(() =>
            {
                // Get all authors who have published posts
                var allAuthors = UserRepo.GetAllAuthors().ToList();

                // Calculate article counts for each author
                foreach (var author in allAuthors)
                {
                    // GetAllById returns posts by UserID, filter to published only
                    var publishedPosts = PostRepo.GetAllById(author.UserId)
                        .Where(p => p.Published && !p.IsDeleted)
                        .Count();

                    if (publishedPosts > 0)
                    {
                        articleCounts[author.UserId] = publishedPosts;
                    }
                }

                // Only include authors with published articles
                authors = allAuthors
                    .Where(a => articleCounts.ContainsKey(a.UserId))
                    .OrderByDescending(a => articleCounts.GetValueOrDefault(a.UserId, 0))
                    .ToList();
            });
        }
        catch (Exception)
        {
            authors = new List<AppUser>();
            articleCounts = new Dictionary<long, int>();
        }
        finally
        {
            isLoading = false;
        }
    }

    /// <summary>
    /// Gets the article count for an author.
    /// </summary>
    /// <param name="userId">The author's user ID.</param>
    /// <returns>Number of published articles.</returns>
    private int GetArticleCount(long userId)
    {
        return articleCounts.GetValueOrDefault(userId, 0);
    }
}
