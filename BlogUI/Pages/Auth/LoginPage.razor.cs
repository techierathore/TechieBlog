using Blazorise;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace BlogUI.Pages.Auth;

public partial class LoginPage : ComponentBase
{
    public SvcData LoginDetails { get; set; }
    public string LoginMesssage { get; set; }
    [Inject]
    public AuthenticationStateProvider AuthStateProvider { get; set; }
    [Inject]
    public NavigationManager NavigationManager { get; set; }
    [Inject]
    public IAuthService AuthSvc { get; set; }
    private BlogUser vValidatedUser;
    ClaimsPrincipal PageClaimsPrincipal;

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; }

    VerifyPopUp VerifyDialog;

    [Parameter]
    public string PageCode { get; set; }
    Modal VerifySuccess;
    protected async override Task OnInitializedAsync()
    {
        LoginDetails = new SvcData();
        vValidatedUser = new BlogUser();

        PageClaimsPrincipal = (await AuthStateTask).User;
        if (PageClaimsPrincipal.Identity.IsAuthenticated)
        { NavigationManager.NavigateTo("/Index"); }

        if (!string.IsNullOrEmpty(PageCode))
        {
            try
            {
                await AuthSvc.VerifyEmailAsync(new SvcData
                {
                    VerificationCode = PageCode
                });

                VerifySuccess.Show();
            }
            catch (Exception ex)
            {
                LoginMesssage = ex.Message;
            }
        }
    }

    public async Task ValidateUser()
    {
        try
        {
            vValidatedUser = await AuthSvc.LoginAsync(new SvcData
            {
                LoginEmail = LoginDetails.LoginEmail,
                LoginPass = LoginDetails.LoginPass
            });
            if (vValidatedUser == null)
            {
                LoginMesssage = "Invalid User Email or Password";
                return;
            }
            if (vValidatedUser.IsVerified)
            {
                await ((CustomAuthStateProvider)AuthStateProvider).MarkUserAsAuthenticated(vValidatedUser);
                NavigationManager.NavigateTo("/Index");
            }
            else
            {
                VerifyDialog.UserEmail = LoginDetails.LoginEmail;
                VerifyDialog.ShowPopUp();
            }
        }
        catch (Exception ex)
        {
            LoginMesssage = ex.Message;
        }
    }

    public void OnDialogClose()
    {
        StateHasChanged();
    }

    public void ClosePopUp()
    {
        VerifySuccess.Hide();
    }
    public Task OnModalClosing(ModalClosingEventArgs e)
    {
        if (e.CloseReason != CloseReason.UserClosing)
        {
            // just set Cancel to true to prevent modal from closing
            e.Cancel = true;
        }
        return Task.CompletedTask;
    }
}
