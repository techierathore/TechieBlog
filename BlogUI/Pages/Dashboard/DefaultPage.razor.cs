using Blazorise;
using Blazorise.Charts;
using Microsoft.AspNetCore.Components;

namespace BlogUI.Pages.Dashboard
{
    public partial class DefaultPage
    {
        [CascadingParameter] protected Theme Theme { get; set; }

    }
}
