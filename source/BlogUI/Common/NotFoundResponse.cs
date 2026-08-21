using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace BlogUI.Common;

/// <summary>
/// Lets a routable component answer with HTTP 404 while still rendering its own screen
/// (REQ-UI-058).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> A URL that matches NO route already 404s correctly — routing ends the
/// request and <c>UseStatusCodePagesWithReExecute("/404")</c> supplies the body (REQ-UI-012). A URL
/// that matches a route and then fails to find its data does NOT: the endpoint was matched, the
/// component rendered successfully, and the response is HTTP 200 carrying an in-page "not found"
/// state. That is a soft 404 — search engines index the miss as a live page and monitoring cannot
/// see it. <c>/post/&lt;unknown-slug&gt;</c> was measured answering 200 on 2026-08-10.</para>
///
/// <para><b>Code Flow:</b> the component decides its data is missing → calls <see cref="Apply"/>
/// with the ambient <see cref="HttpContext"/> → the status code on the response being composed is
/// changed to 404 → the component goes on to render its not-found markup as usual.</para>
///
/// <para><b>Why this works, and when it does nothing.</b> The status can only be set while an HTTP
/// response is being composed, which on a Blazor page is the static/prerender pass — the same
/// window <c>PostViewTracker</c> relies on, and the reason <c>IHttpContextAccessor</c> rather than
/// the <c>HttpContext</c> cascading parameter is used (an <c>InteractiveServer</c> component never
/// receives that parameter). On the interactive pass there is no request, so this is a no-op and the
/// visitor keeps the page they already have.</para>
///
/// <para><b>Turning the status-code pages OFF for this request is NOT optional — measured
/// 2026-08-10.</b> The first version of this helper set the status and stopped there, on the
/// assumption that <c>UseStatusCodePagesWithReExecute("/404")</c> would stand aside once the
/// response had a content type. It does not: the response is still buffered when the pipeline
/// unwinds, so the middleware saw a body-less 404 and re-executed the request against <c>/404</c>.
/// Re-executing a request whose component tree has ALREADY rendered reuses the same scoped
/// services, and the second render threw
/// <c>InvalidOperationException: 'RemoteNavigationManager' already initialized</c> — an HTTP 500 in
/// place of the article. <see cref="IStatusCodePagesFeature"/> is the framework's own opt-out for
/// precisely this case: "this handler is producing the body itself, do not re-execute". Without it
/// the fix is strictly worse than the soft 404 it replaces.</para>
///
/// <para><b>Dependencies:</b> <c>Microsoft.AspNetCore.Http.Abstractions</c> and
/// <c>Microsoft.AspNetCore.Diagnostics.Abstractions</c>.</para>
///
/// <para><b>Usage:</b> <c>NotFoundResponse.Apply(HttpContextAccessor?.HttpContext);</c> from the
/// branch that decides the resource does not exist.</para>
/// </remarks>
public static class NotFoundResponse
{
    /// <summary>
    /// Marks the response being composed as HTTP 404.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only an untouched, unsent 200 response is changed. A response
    /// that has already begun streaming cannot have its status rewritten, and one that another
    /// layer has already given a non-200 status — a re-executed error page, a rate-limit rejection —
    /// owns that status and must keep it. Once the status is changed, the status-code page
    /// middleware is switched off for this request so it does not re-execute a render that has
    /// already happened.</para>
    /// <para><b>Flow:</b> null check → started check → status check → set 404 → disable
    /// re-execution.</para>
    /// <para><b>Side Effects:</b> Sets <c>HttpContext.Response.StatusCode</c> and disables
    /// <see cref="IStatusCodePagesFeature"/> for this request.</para>
    /// </remarks>
    /// <param name="httpContext">The request being served, or <c>null</c> on an interactive render.</param>
    /// <returns><c>true</c> when the status was changed; <c>false</c> when there was nothing to change.</returns>
    public static bool Apply(HttpContext? httpContext)
    {
        if (httpContext == null || httpContext.Response.HasStarted)
        {
            return false;
        }

        // 404 is accepted as well as 200 because NavigationManager.NotFound() may already have set
        // it; this call still has to switch the re-execution off. Any OTHER non-200 status belongs
        // to a layer that chose it deliberately and is left alone.
        if (httpContext.Response.StatusCode is not (StatusCodes.Status200OK or StatusCodes.Status404NotFound))
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status404NotFound;

        var statusCodePages = httpContext.Features.Get<IStatusCodePagesFeature>();
        if (statusCodePages != null)
        {
            statusCodePages.Enabled = false;
        }

        return true;
    }
}
