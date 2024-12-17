using BlogModels.Interfaces;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components;
using System.Security.Claims;
using BlogModels;

namespace BlogUI.Pages.AdminPages;

partial class BlogsList : ComponentBase
{
    [Inject]
    public IManageService<BlogPost> DataService { get; set; }
    public List<BlogPost> ObjectList { get; set; }
    public BlogPost SelObject { get; set; }
    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; }
    ClaimsPrincipal LoggedInUser;
    protected override async Task OnInitializedAsync()
    {
        LoggedInUser = (await AuthStateTask).User;
        string vEmpId = LoggedInUser.Claims.FirstOrDefault(
            c => c.Type == ClaimTypes.PrimarySid)?.Value;
        long lEmployeeId = Convert.ToInt64(vEmpId);
       // ObjectList = await DataService.GetAllSubsAsync(ClientConstants.UserAccSvcUrl, lEmployeeId);
    }
}