﻿using Blazorise;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace BlogUI.Pages.AdminPages;

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
    private AppUser vValidatedUser;
    ClaimsPrincipal PageClaimsPrincipal;

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; }


    [Parameter]
    public string PageCode { get; set; }

    protected async override Task OnInitializedAsync()
    {
        LoginDetails = new SvcData();
        vValidatedUser = new AppUser();

        PageClaimsPrincipal = (await AuthStateTask).User;
        if (PageClaimsPrincipal.Identity.IsAuthenticated)
        { NavigationManager.NavigateTo("/Index"); }

        //TODO: Need to impliment confirmation PopUp in
        //place of this in future where the subscriber 
        //feature is wrritten. 
        //if (!string.IsNullOrEmpty(PageCode))
        //{
        //    try
        //    {
        //        await AuthSvc.VerifyEmailAsync(new SvcData
        //        {
        //            VerificationCode = PageCode
        //        });

        //        VerifySuccess.Show();
        //    }
        //    catch (Exception ex)
        //    {
        //        LoginMesssage = ex.Message;
        //    }
        //}
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
            //TODO: Impliment Role based access in this 
            //if (vValidatedUser.IsVerified)
            //{
            await ((CustomAuthStateProvider)AuthStateProvider).MarkUserAsAuthenticated(vValidatedUser);
            NavigationManager.NavigateTo("/Index");
            //}
            //else
            //{
            //    VerifyDialog.UserEmail = LoginDetails.LoginEmail;
            //    VerifyDialog.ShowPopUp();
            //}
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

    //public void ClosePopUp()
    //{
    //    VerifySuccess.Hide();
    //}
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
