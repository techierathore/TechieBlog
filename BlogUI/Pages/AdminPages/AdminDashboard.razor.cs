using Blazorise;
using Blazorise.Charts;
using Microsoft.AspNetCore.Components;

namespace BlogUI.Pages.Dashboard
{
    public partial class AdminDashboard : ComponentBase
    {
        [CascadingParameter] protected Theme Theme { get; set; }

    }
}
