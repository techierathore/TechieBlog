using BlogModels;
using BlogModels.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace BlogUI.Pages.AdminPages;

/// <summary>
/// Code-behind for ManageProfile.razor.
/// Handles self-service profile management for authors and admins.
/// </summary>
public partial class ManageProfile : ComponentBase
{
    [Inject]
    public IBlogUserRepo UserRepo { get; set; } = default!;

    [Inject]
    public BlogEngine.Services.AuthSvc AuthService { get; set; } = default!;

    [Inject]
    public NavigationManager NavManager { get; set; } = default!;

    [Inject]
    public AuthenticationStateProvider AuthStateProvider { get; set; } = default!;

    /// <summary>
    /// The current logged-in user.
    /// </summary>
    protected AppUser? CurrentUser { get; set; }

    /// <summary>
    /// The current user's ID.
    /// </summary>
    protected long CurrentUserId { get; set; }

    /// <summary>
    /// Profile form model.
    /// </summary>
    protected ProfileFormModel ProfileModel { get; set; } = new();

    /// <summary>
    /// Status message to display.
    /// </summary>
    protected string StatusMessage { get; set; } = string.Empty;

    /// <summary>
    /// Whether the status message is an error.
    /// </summary>
    protected bool IsError { get; set; }

    /// <summary>
    /// Loading state.
    /// </summary>
    protected bool IsLoading { get; set; } = true;

    /// <summary>
    /// Saving state.
    /// </summary>
    protected bool IsSaving { get; set; }

    /// <summary>
    /// Username validation message.
    /// </summary>
    protected string UsernameValidationMessage { get; set; } = string.Empty;

    /// <summary>
    /// Whether the username is valid.
    /// </summary>
    protected bool IsUsernameValid { get; set; } = true;

    /// <summary>
    /// The original username for comparison.
    /// </summary>
    private string _originalUsername = string.Empty;

    /// <summary>
    /// Regex pattern for valid usernames (alphanumeric + hyphens only).
    /// </summary>
    private static readonly Regex UsernamePattern = new(@"^[a-zA-Z0-9-]+$", RegexOptions.Compiled);

    protected override async Task OnInitializedAsync()
    {
        await LoadProfile();
    }

    /// <summary>
    /// Loads the current user's profile.
    /// </summary>
    private async Task LoadProfile()
    {
        IsLoading = true;

        try
        {
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            var userIdClaim = authState.User.FindFirst(ClaimTypes.PrimarySid);

            if (userIdClaim != null && long.TryParse(userIdClaim.Value, out var userId))
            {
                CurrentUserId = userId;
                CurrentUser = UserRepo.GetSingle(userId);

                if (CurrentUser != null)
                {
                    _originalUsername = CurrentUser.Username ?? string.Empty;
                    ProfileModel = new ProfileFormModel
                    {
                        FirstName = CurrentUser.FirstName ?? string.Empty,
                        LastName = CurrentUser.LastName ?? string.Empty,
                        Username = CurrentUser.Username ?? string.Empty,
                        Title = CurrentUser.Title ?? string.Empty,
                        Tagline = CurrentUser.Tagline ?? string.Empty,
                        ProfileDescription = CurrentUser.ProfileDescription ?? string.Empty,
                        ProfileImagePath = CurrentUser.ProfileImagePath,
                        LinkedInUrl = CurrentUser.LinkedInUrl ?? string.Empty,
                        GitHubUrl = CurrentUser.GitHubUrl ?? string.Empty,
                        TwitterUrl = CurrentUser.TwiiterUrl ?? string.Empty,
                        InstagramUrl = CurrentUser.InstagramUrl ?? string.Empty,
                        ResumeEnabled = CurrentUser.ResumeEnabled,
                        CVFilePath = CurrentUser.CVFilePath,
                        PhoneNumber = CurrentUser.PhoneNumber ?? string.Empty,
                        Location = CurrentUser.Location ?? string.Empty
                    };
                }
            }
        }
        catch
        {
            CurrentUser = null;
        }

        IsLoading = false;
    }

    /// <summary>
    /// Handles username input changes for validation.
    /// </summary>
    protected void OnUsernameChanged(Microsoft.AspNetCore.Components.ChangeEventArgs e)
    {
        var username = e.Value?.ToString() ?? string.Empty;
        ProfileModel.Username = username;
        ValidateUsername(username);
    }

    /// <summary>
    /// Validates the username format and availability.
    /// </summary>
    private void ValidateUsername(string username)
    {
        UsernameValidationMessage = string.Empty;
        IsUsernameValid = true;

        if (string.IsNullOrWhiteSpace(username))
        {
            return; // Username is optional
        }

        // Check format
        if (!UsernamePattern.IsMatch(username))
        {
            UsernameValidationMessage = "Username can only contain letters, numbers, and hyphens";
            IsUsernameValid = false;
            return;
        }

        // Check length
        if (username.Length < 3)
        {
            UsernameValidationMessage = "Username must be at least 3 characters";
            IsUsernameValid = false;
            return;
        }

        if (username.Length > 50)
        {
            UsernameValidationMessage = "Username cannot exceed 50 characters";
            IsUsernameValid = false;
            return;
        }

        // Check availability only if changed from original
        if (!string.Equals(username, _originalUsername, StringComparison.OrdinalIgnoreCase))
        {
            var isAvailable = UserRepo.IsUsernameAvailable(username);
            if (!isAvailable)
            {
                UsernameValidationMessage = "This username is already taken";
                IsUsernameValid = false;
                return;
            }
            else
            {
                UsernameValidationMessage = "Username is available";
                IsUsernameValid = true;
            }
        }
    }

    /// <summary>
    /// Saves the profile changes.
    /// </summary>
    protected async Task SaveProfile()
    {
        if (IsSaving || CurrentUser == null) return;

        // Validate username if provided
        if (!string.IsNullOrWhiteSpace(ProfileModel.Username))
        {
            ValidateUsername(ProfileModel.Username);
            if (!IsUsernameValid)
            {
                StatusMessage = UsernameValidationMessage;
                IsError = true;
                return;
            }
        }

        IsSaving = true;
        StatusMessage = string.Empty;

        try
        {
            // Update the current user object with form values
            CurrentUser.FirstName = ProfileModel.FirstName?.Trim() ?? string.Empty;
            CurrentUser.LastName = ProfileModel.LastName?.Trim() ?? string.Empty;
            CurrentUser.ProfileImagePath = ProfileModel.ProfileImagePath ?? string.Empty;
            CurrentUser.ProfileDescription = ProfileModel.ProfileDescription?.Trim() ?? string.Empty;
            CurrentUser.TwiiterUrl = ProfileModel.TwitterUrl?.Trim() ?? string.Empty;
            CurrentUser.LinkedInUrl = ProfileModel.LinkedInUrl?.Trim() ?? string.Empty;
            CurrentUser.GitHubUrl = ProfileModel.GitHubUrl?.Trim() ?? string.Empty;

            // Update the basic fields via Update method
            UserRepo.Update(CurrentUser);

            // Update username if changed
            if (!string.IsNullOrWhiteSpace(ProfileModel.Username) &&
                !string.Equals(ProfileModel.Username, _originalUsername, StringComparison.OrdinalIgnoreCase))
            {
                UserRepo.UpdateUsername(CurrentUserId, ProfileModel.Username.Trim());
                _originalUsername = ProfileModel.Username.Trim();
            }

            // Update resume fields separately
            var resumeData = new AppUser
            {
                Title = ProfileModel.Title?.Trim(),
                Tagline = ProfileModel.Tagline?.Trim(),
                Location = ProfileModel.Location?.Trim(),
                PhoneNumber = ProfileModel.PhoneNumber?.Trim(),
                CVFilePath = ProfileModel.CVFilePath,
                ResumeEnabled = ProfileModel.ResumeEnabled,
                InstagramUrl = ProfileModel.InstagramUrl?.Trim()
            };
            UserRepo.UpdateResumeFields(CurrentUserId, resumeData);

            StatusMessage = "Profile saved successfully!";
            IsError = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"An error occurred while saving your profile: {ex.Message}";
            IsError = true;
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Form model for profile editing.
    /// </summary>
    protected class ProfileFormModel
    {
        [Required(ErrorMessage = "First name is required")]
        [StringLength(50, ErrorMessage = "First name cannot exceed 50 characters")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Last name is required")]
        [StringLength(50, ErrorMessage = "Last name cannot exceed 50 characters")]
        public string LastName { get; set; } = string.Empty;

        [StringLength(50, ErrorMessage = "Username cannot exceed 50 characters")]
        public string Username { get; set; } = string.Empty;

        [StringLength(100, ErrorMessage = "Title cannot exceed 100 characters")]
        public string Title { get; set; } = string.Empty;

        [StringLength(200, ErrorMessage = "Tagline cannot exceed 200 characters")]
        public string Tagline { get; set; } = string.Empty;

        [StringLength(2000, ErrorMessage = "Bio cannot exceed 2000 characters")]
        public string ProfileDescription { get; set; } = string.Empty;

        public string? ProfileImagePath { get; set; }

        [StringLength(200, ErrorMessage = "LinkedIn URL cannot exceed 200 characters")]
        public string LinkedInUrl { get; set; } = string.Empty;

        [StringLength(200, ErrorMessage = "GitHub URL cannot exceed 200 characters")]
        public string GitHubUrl { get; set; } = string.Empty;

        [StringLength(200, ErrorMessage = "Twitter URL cannot exceed 200 characters")]
        public string TwitterUrl { get; set; } = string.Empty;

        [StringLength(200, ErrorMessage = "Instagram URL cannot exceed 200 characters")]
        public string InstagramUrl { get; set; } = string.Empty;

        public bool ResumeEnabled { get; set; }

        public string? CVFilePath { get; set; }

        [StringLength(30, ErrorMessage = "Phone number cannot exceed 30 characters")]
        public string PhoneNumber { get; set; } = string.Empty;

        [StringLength(100, ErrorMessage = "Location cannot exceed 100 characters")]
        public string Location { get; set; } = string.Empty;
    }
}
