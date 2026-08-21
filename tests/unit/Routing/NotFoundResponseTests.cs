using BlogUI.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace TechieBlog.Tests.Routing;

/// <summary>
/// REQ-UI-058 — the helper that turns a matched-but-unresolvable route into a real HTTP 404.
/// </summary>
/// <remarks>
/// <c>/post/&lt;unknown-slug&gt;</c> matched its route and rendered PostView's own not-found panel,
/// so the response was HTTP 200 — a soft 404 that search engines index as a live page. These tests
/// pin the three conditions the fix must respect: it only touches an untouched 200, it never fights
/// a status another layer has already chosen, and it is a silent no-op on the interactive render
/// pass where there is no request at all.
/// </remarks>
public class NotFoundResponseTests
{
    /// <summary>
    /// An untouched 200 response being composed is changed to 404.
    /// </summary>
    [Fact]
    public void ApplySetsNotFoundOnAFreshResponse()
    {
        var httpContext = new DefaultHttpContext();

        var applied = NotFoundResponse.Apply(httpContext);

        Assert.True(applied);
        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
    }

    /// <summary>
    /// With no ambient request — the interactive Blazor render pass — the call does nothing and
    /// reports that it did nothing, rather than throwing.
    /// </summary>
    [Fact]
    public void ApplyIgnoresAMissingHttpContext()
    {
        Assert.False(NotFoundResponse.Apply(null));
    }

    /// <summary>
    /// A status another layer has already chosen is left alone, so a re-executed error page or a
    /// rate-limit rejection keeps the code it set.
    /// </summary>
    [Fact]
    public void ApplyLeavesAnAlreadyChosenStatusAlone()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        var applied = NotFoundResponse.Apply(httpContext);

        Assert.False(applied);
        Assert.Equal(StatusCodes.Status429TooManyRequests, httpContext.Response.StatusCode);
    }

    /// <summary>
    /// The status-code page middleware is switched off, so it cannot re-execute a request whose
    /// component tree has already rendered — which produced an HTTP 500 before this was added.
    /// </summary>
    [Fact]
    public void ApplyDisablesStatusCodePageReExecution()
    {
        var httpContext = new DefaultHttpContext();
        var feature = new StatusCodePagesFeature();
        httpContext.Features.Set<IStatusCodePagesFeature>(feature);

        NotFoundResponse.Apply(httpContext);

        Assert.False(feature.Enabled);
    }

    /// <summary>
    /// A pipeline without the status-code page middleware has no feature to switch off, and the
    /// status is still set rather than the call throwing.
    /// </summary>
    [Fact]
    public void ApplyToleratesAMissingStatusCodePagesFeature()
    {
        var httpContext = new DefaultHttpContext();

        Assert.True(NotFoundResponse.Apply(httpContext));
        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
    }

    /// <summary>
    /// Calling twice is harmless, and a response NavigationManager.NotFound() already marked 404
    /// is still accepted so that the re-execution can be switched off.
    /// </summary>
    [Fact]
    public void ApplyIsIdempotent()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
        var feature = new StatusCodePagesFeature();
        httpContext.Features.Set<IStatusCodePagesFeature>(feature);

        Assert.True(NotFoundResponse.Apply(httpContext));
        Assert.True(NotFoundResponse.Apply(httpContext));
        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
        Assert.False(feature.Enabled);
    }
}
