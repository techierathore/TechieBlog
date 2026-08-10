using Microsoft.AspNetCore.Http;

namespace TechieBlog.Tests.Analytics;

/// <summary>
/// Per-instance <see cref="IHttpContextAccessor"/> stub.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The framework's own <c>HttpContextAccessor</c> keeps its value in a
/// <b>static</b> <c>AsyncLocal</c>, so two instances in one test method share one context and the
/// second silently replaces the first. That makes it useless for a test that needs two different
/// visitors at once, which is exactly what the unique-view rule has to be proved against. This stub
/// holds its context in an ordinary field instead. [REQ-FN-034]</para>
///
/// <para><b>Code Flow:</b> a test constructs one stub per visitor and hands each to its own
/// tracker.</para>
///
/// <para><b>Dependencies:</b> <c>Microsoft.AspNetCore.Http.Abstractions</c>.</para>
///
/// <para><b>Usage:</b> <c>new StubHttpContextAccessor(context)</c>, or the parameterless form for
/// the "no request in flight" case.</para>
/// </remarks>
public class StubHttpContextAccessor : IHttpContextAccessor
{
    /// <summary>
    /// Initializes the stub with the context it should report, if any.
    /// </summary>
    /// <param name="httpContext">The context to report, or null for a render outside a request.</param>
    public StubHttpContextAccessor(HttpContext? httpContext = null)
    {
        HttpContext = httpContext;
    }

    /// <inheritdoc />
    public HttpContext? HttpContext { get; set; }
}
