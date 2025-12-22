/// <summary>
/// Code-behind for AdminLayout component.
/// Manages layout state for the administrative dashboard navigation.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides state management for collapsible navigation menus
/// and user authentication state (logout, display current user).</para>
/// <para><b>Dependencies:</b> Microsoft.FluentUI.AspNetCore.Components, CustomAuthStateProvider</para>
/// </remarks>
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace BlogUI.Layouts
{
    /// <summary>
    /// Partial class containing state and logic for AdminLayout.razor.
    /// </summary>
    public partial class AdminLayout
    {
        /// <summary>
        /// Authentication state provider for managing user login state.
        /// </summary>
        [Inject]
        public AuthenticationStateProvider AuthStateProvider { get; set; }

        /// <summary>
        /// Navigation manager for redirecting after logout.
        /// </summary>
        [Inject]
        public NavigationManager NavigationManager { get; set; }

        /// <summary>
        /// Cascading authentication state task.
        /// </summary>
        [CascadingParameter]
        private Task<AuthenticationState> AuthStateTask { get; set; }

        /// <summary>
        /// Current user's display name from authentication state.
        /// </summary>
        public string CurrentUserName { get; set; } = "Guest";

        /// <summary>
        /// Current user's role from authentication state.
        /// </summary>
        public string CurrentUserRole { get; set; } = "Reader";

        /// <summary>
        /// Controls visibility of the Blog Management navigation group.
        /// </summary>
        public bool PagesMenuVisible { get; set; }

        /// <summary>
        /// Controls visibility of the Authentication navigation group.
        /// </summary>
        public bool AuthMenuVisible { get; set; }

        /// <summary>
        /// Controls visibility of the UI Elements navigation group.
        /// </summary>
        public bool UIElementsMenuVisible { get; set; }

        /// <summary>
        /// Controls visibility of the Forms navigation group.
        /// </summary>
        public bool FormsMenuVisible { get; set; }

        /// <summary>
        /// Controls visibility of the top navigation bar.
        /// </summary>
        public bool TopBarVisible { get; set; }

        /// <summary>
        /// Initializes the layout and retrieves current user information.
        /// </summary>
        protected override async Task OnInitializedAsync()
        {
            if (AuthStateTask != null)
            {
                var authState = await AuthStateTask;
                if (authState.User.Identity?.IsAuthenticated == true)
                {
                    CurrentUserName = authState.User.FindFirst(ClaimTypes.Name)?.Value ?? "User";
                    CurrentUserRole = authState.User.FindFirst(ClaimTypes.Role)?.Value ?? "Reader";
                }
            }
        }

        /// <summary>
        /// Logs out the current user and redirects to home page.
        /// </summary>
        /// <remarks>
        /// <para><b>Flow:</b></para>
        /// <list type="number">
        ///   <item>Calls CustomAuthStateProvider.MarkUserAsLoggedOut()</item>
        ///   <item>Clears tokens from LocalStorage</item>
        ///   <item>Notifies authentication state change</item>
        ///   <item>Redirects to home page</item>
        /// </list>
        /// </remarks>
        public async Task LogoutAsync()
        {
            await ((CustomAuthStateProvider)AuthStateProvider).MarkUserAsLoggedOut();
            NavigationManager.NavigateTo("/", forceLoad: true);
        }
    }
}
