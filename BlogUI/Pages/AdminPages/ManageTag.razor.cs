﻿using BlogModels.Interfaces;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using BlogModels;

namespace BlogUI.Pages.AdminPages;

partial class ManageTag: ComponentBase
{
    [Parameter]
    public long PageId { get; set; }
    [Inject]
    NavigationManager AppNavManager { get; set; }
    [Inject]
    public IManageService<BlogTag> DataService { get; set; }
    public string PageHeader { get; set; }
    public BlogTag PageObj { get; set; }
    public IEnumerable<BlogTag> CategoryList { get; set; }

    long SubCatId;
    protected const string GetObjectServiceUrl = "CategorySvc/GetSingleCategory/";
    protected const string CreateServiceUrl = "CategorySvc/CreateCategory";
    protected const string UpdateServiceUrl = "CategorySvc/UpdateCategory";
    protected const string ListPageUrl = "/ExpCatList";
    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; }
    ClaimsPrincipal LoggedInUser;
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            if (PageId != 0)
            {
                PageHeader = "Edit Expense Category";
                PageObj = await DataService.GetSingleAsync(GetObjectServiceUrl, PageId);
            }
            else await ResetPage();
            StateHasChanged();
        }
    }


    private async Task ResetPage()
    {
        PageHeader = "Add New Tag";
        PageObj = new BlogTag();        
        StateHasChanged();
    }

    public async void SaveData()
    {
       // PageObj.ParentId = SubCatId;
        if (PageId != 0)
        { _ = await DataService.UpdateAsync(UpdateServiceUrl, PageObj); }
        else
        {
            LoggedInUser = (await AuthStateTask).User;
            string vEmpId = LoggedInUser.Claims.FirstOrDefault(
                c => c.Type == ClaimTypes.PrimarySid)?.Value;
            long lEmployeeId = Convert.ToInt64(vEmpId);
           // PageObj.AppUserId = lEmployeeId;
            // PageObj.ParentId = 0;
            _ = await DataService.SaveAsync(CreateServiceUrl, PageObj);
        }

        AppNavManager.NavigateTo(ListPageUrl);
    }

}
