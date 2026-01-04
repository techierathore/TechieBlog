using BlogModels;
using BlogModels.Models;
using Microsoft.AspNetCore.Components;

namespace BlogUI.Pages;

/// <summary>
/// Code-behind for ResumePage.razor.
/// Handles loading the site owner's data for the public resume display.
/// </summary>
public partial class ResumePage
{
    /// <summary>
    /// Repository for accessing user data.
    /// </summary>
    [Inject]
    public IBlogUserRepo UserRepo { get; set; }

    /// <summary>
    /// The site owner's user data.
    /// </summary>
    private AppUser siteOwner;

    /// <summary>
    /// Indicates whether data is currently being loaded.
    /// </summary>
    private bool isLoading = true;

    /// <summary>
    /// Loads the site owner data on component initialization.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        await LoadSiteOwner();
    }

    /// <summary>
    /// Loads the site owner from the database.
    /// </summary>
    private async Task LoadSiteOwner()
    {
        isLoading = true;

        try
        {
            // Get the user where IsSiteOwner = true
            siteOwner = UserRepo.GetSiteOwner();
        }
        catch
        {
            siteOwner = null;
        }

        isLoading = false;
        StateHasChanged();
    }
}
