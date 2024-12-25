using BlogModels.Interfaces;
using BlogModels;
using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlogUI.Pages.AdminPages;

partial class ManagePost : ComponentBase
{
    [Parameter]
    public long PageId { get; set; }
    [Inject]
    NavigationManager AppNavManager { get; set; }
    [Inject]
    public IManageService<BlogPost> DataService { get; set; }
    public string PageHeader { get; set; }
    public BlogPost PageObj { get; set; }

    protected void SaveData()
    {

    }
}