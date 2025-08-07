using BlogModels.Interfaces;
using BlogModels;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components;
using System.Security.Claims;

namespace BlogUI.Pages.AdminPages;

partial class TagsList : ComponentBase
{
    [Inject]
    public IManageService<BlogTag> DataService { get; set; }
    public List<BlogTag> ObjectList { get; set; }
    public BlogTag SelObject { get; set; }
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
