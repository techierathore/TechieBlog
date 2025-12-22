using BlogModels.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace BlogUI.Pages.AdminPages;

/// <summary>
/// Code-behind for ProfilePage.razor.
/// Handles profile updates and password changes.
/// </summary>
public partial class ProfilePage
{
    private AppUser? currentUser;
    private ProfileModel profileModel = new();
    private PasswordChangeModel passwordModel = new();
    private long currentUserId;

    private bool isLoading = true;
    private bool isSavingProfile = false;
    private bool isChangingPassword = false;

    private string profileMessage = string.Empty;
    private bool profileSuccess = false;
    private string passwordMessage = string.Empty;
    private bool passwordSuccess = false;

    protected override async Task OnInitializedAsync()
    {
        await LoadProfile();
    }

    private async Task LoadProfile()
    {
        isLoading = true;

        try
        {
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            var userIdClaim = authState.User.FindFirst(ClaimTypes.PrimarySid);

            if (userIdClaim != null && long.TryParse(userIdClaim.Value, out var userId))
            {
                currentUserId = userId;
                currentUser = AuthService.GetUserProfile(userId);

                if (currentUser != null)
                {
                    profileModel = new ProfileModel
                    {
                        FirstName = currentUser.FirstName ?? string.Empty,
                        LastName = currentUser.LastName ?? string.Empty,
                        ProfileDescription = currentUser.ProfileDescription ?? string.Empty,
                        TwitterUrl = currentUser.TwiiterUrl ?? string.Empty,
                        LinkedInUrl = currentUser.LinkedInUrl ?? string.Empty,
                        GitHubUrl = currentUser.GitHubUrl ?? string.Empty
                    };
                }
            }
        }
        catch
        {
            currentUser = null;
        }

        isLoading = false;
    }

    private async Task SaveProfile()
    {
        if (isSavingProfile) return;

        isSavingProfile = true;
        profileMessage = string.Empty;

        try
        {
            var result = AuthService.UpdateProfile(
                currentUserId,
                profileModel.FirstName,
                profileModel.LastName,
                profileModel.ProfileDescription,
                profileModel.TwitterUrl,
                profileModel.LinkedInUrl,
                profileModel.GitHubUrl
            );

            if (result.IsSuccess)
            {
                profileSuccess = true;
                profileMessage = "Profile updated successfully!";
            }
            else
            {
                profileSuccess = false;
                profileMessage = result.ErrorMessage;
            }
        }
        catch
        {
            profileSuccess = false;
            profileMessage = "An error occurred while saving your profile.";
        }
        finally
        {
            isSavingProfile = false;
        }
    }

    private async Task ChangePassword()
    {
        if (isChangingPassword) return;

        passwordMessage = string.Empty;

        // Validate passwords match
        if (passwordModel.NewPassword != passwordModel.ConfirmPassword)
        {
            passwordSuccess = false;
            passwordMessage = "New passwords do not match.";
            return;
        }

        isChangingPassword = true;

        try
        {
            var result = AuthService.ChangePassword(
                currentUserId,
                passwordModel.CurrentPassword,
                passwordModel.NewPassword
            );

            if (result.IsSuccess)
            {
                passwordSuccess = true;
                passwordMessage = "Password changed successfully!";
                passwordModel = new PasswordChangeModel(); // Clear form
            }
            else
            {
                passwordSuccess = false;
                passwordMessage = result.ErrorMessage;
            }
        }
        catch
        {
            passwordSuccess = false;
            passwordMessage = "An error occurred while changing your password.";
        }
        finally
        {
            isChangingPassword = false;
        }
    }

    /// <summary>
    /// Model for profile form fields.
    /// </summary>
    private class ProfileModel
    {
        [Required(ErrorMessage = "First name is required")]
        [StringLength(50, ErrorMessage = "First name cannot exceed 50 characters")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Last name is required")]
        [StringLength(50, ErrorMessage = "Last name cannot exceed 50 characters")]
        public string LastName { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "Bio cannot exceed 500 characters")]
        public string ProfileDescription { get; set; } = string.Empty;

        public string TwitterUrl { get; set; } = string.Empty;

        public string LinkedInUrl { get; set; } = string.Empty;

        public string GitHubUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// Model for password change form fields.
    /// </summary>
    private class PasswordChangeModel
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
