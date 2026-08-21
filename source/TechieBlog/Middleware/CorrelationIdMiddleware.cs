using Serilog.Context;

namespace TechieBlog.Middleware;

/// <summary>
/// Stamps every request with a correlation identifier and pushes it into the log context
/// (REQ-NFR-015, BRD-75).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> A Blazor Server page load fans out into database calls, background
/// publishing and outbound email. Without a shared identifier those log lines cannot be stitched
/// back together, which makes production diagnosis guesswork. This middleware gives every request
/// one identifier, echoes it to the client and attaches it to every log event written while the
/// request is in flight.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Reuse an inbound <c>X-Correlation-ID</c> header when a caller or reverse proxy already
///     supplied one, so a trace spans process boundaries.</item>
///   <item>Otherwise fall back to the ASP.NET Core <c>TraceIdentifier</c>, which is already
///     unique per request and cheap.</item>
///   <item>Store it on <c>HttpContext.Items</c>, echo it in the response header before the
///     response starts, and push it onto the Serilog <see cref="LogContext"/> for the duration.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Serilog's <see cref="LogContext"/> enricher, which
/// <c>Program.cs</c> enables with <c>Enrich.FromLogContext()</c>.</para>
///
/// <para><b>Usage:</b> <c>app.UseMiddleware&lt;CorrelationIdMiddleware&gt;()</c> — register it
/// before request logging so the logged summary carries the identifier too.</para>
/// </remarks>
public class CorrelationIdMiddleware
{
    /// <summary>
    /// Header used to receive and echo the correlation identifier.
    /// </summary>
    public const string HeaderName = "X-Correlation-ID";

    /// <summary>
    /// Key under which the identifier is stored on <c>HttpContext.Items</c>.
    /// </summary>
    public const string ContextItemKey = "CorrelationId";

    /// <summary>
    /// Property name the identifier is logged under.
    /// </summary>
    public const string LogPropertyName = "CorrelationId";

    private readonly RequestDelegate next;

    /// <summary>
    /// Initialises the middleware.
    /// </summary>
    /// <param name="next">The next delegate in the pipeline.</param>
    public CorrelationIdMiddleware(RequestDelegate next)
    {
        this.next = next;
    }

    /// <summary>
    /// Resolves the correlation identifier and runs the rest of the pipeline inside its scope.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An inbound header wins so a trace can cross services; the
    /// response header is written from an <c>OnStarting</c> callback because headers cannot be
    /// modified once the response has begun.</para>
    /// <para><b>Flow:</b> resolve id → store on the context → register response header → push log
    /// property → invoke next.</para>
    /// <para><b>Side Effects:</b> Adds a response header and an ambient log property.</para>
    /// </remarks>
    /// <param name="context">The current request context.</param>
    /// <returns>A task that completes when the rest of the pipeline has run.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);
        context.Items[ContextItemKey] = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(LogPropertyName, correlationId))
        {
            await next(context);
        }
    }

    /// <summary>
    /// Reads the inbound correlation identifier or falls back to the request trace identifier.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Inbound values are length-capped so a hostile client cannot
    /// bloat every log line, and blank headers are ignored.</para>
    /// <para><b>Flow:</b> read header → validate → fall back to <c>TraceIdentifier</c>.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="context">The current request context.</param>
    /// <returns>The correlation identifier to use for this request.</returns>
    public static string ResolveCorrelationId(HttpContext context)
    {
        const int maxLength = 64;

        var inbound = context.Request.Headers[HeaderName].ToString();
        if (!string.IsNullOrWhiteSpace(inbound))
            return inbound.Length > maxLength ? inbound.Substring(0, maxLength) : inbound;

        return string.IsNullOrEmpty(context.TraceIdentifier)
            ? Guid.NewGuid().ToString("N")
            : context.TraceIdentifier;
    }
}
