namespace BlogModels.Interfaces;

/// <summary>
/// Presents a synchronous repository member's outcome as an already-completed task.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Backs the default implementations that <see cref="IGenericRepository{TEntity}"/>
/// pioneered and that the entity-specific repository interfaces now carry too (REQ-NFR-026). Adding a
/// plain abstract <c>…Async</c> member to an interface such as <c>IBlogCommentRepo</c> would break every
/// implementer at once — the production repository and the hand-written test doubles under
/// <c>tests/unit</c> alike — and nothing would compile again until the last of them had been converted.
/// A default implementation that runs the synchronous twin keeps every unconverted implementer
/// compiling and behaving exactly as it did.</para>
///
/// <para><b>Code Flow:</b> interface default implementation → <see cref="Run{TResult}"/> or
/// <see cref="Run(Action, CancellationToken)"/> → synchronous member → completed task.</para>
///
/// <para><b>Dependencies:</b> None.</para>
///
/// <para><b>Usage:</b> Only for the temporary bridge. It preserves task semantics faithfully — a
/// cancelled token yields a cancelled task and a thrown exception yields a faulted task — so a caller
/// that only observes failures through <c>await</c> sees them whichever implementation it reaches.
/// <b>It is not asynchrony</b>: the operation runs inline on the calling thread and still parks it for
/// the whole round trip. A repository that inherits a bridged member is unconverted, however green the
/// build is. The final stage of REQ-NFR-026 deletes this type together with the synchronous surface.</para>
///
/// <para><b>Visibility:</b> <c>internal</c> deliberately — default interface method bodies are emitted
/// into the declaring assembly, so <c>BlogModels</c> is the only assembly that needs to see it, and the
/// bridge stays out of the public API it is meant to disappear from.</para>
/// </remarks>
internal static class RepoSyncBridge
{
    /// <summary>
    /// Runs a value-returning synchronous member and wraps its outcome in a completed task.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A token that is already cancelled short-circuits before the
    /// operation runs, so a cancelled request costs no database round trip.</para>
    /// <para><b>Flow:</b> check the token → invoke → wrap value, cancellation or exception.</para>
    /// <para><b>Side Effects:</b> Those of <paramref name="syncOperation"/>, executed inline.</para>
    /// </remarks>
    /// <typeparam name="TResult">Return type of the synchronous member.</typeparam>
    /// <param name="syncOperation">The synchronous member being bridged.</param>
    /// <param name="cancellationToken">Token observed before the member runs.</param>
    /// <returns>A completed, cancelled or faulted task carrying the outcome.</returns>
    internal static Task<TResult> Run<TResult>(Func<TResult> syncOperation, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<TResult>(cancellationToken);

        try
        {
            return Task.FromResult(syncOperation());
        }
        catch (Exception ex)
        {
            return Task.FromException<TResult>(ex);
        }
    }

    /// <summary>
    /// Runs a void synchronous member and wraps its outcome in a completed task.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Void-returning counterpart of <see cref="Run{TResult}"/>; see that
    /// overload for why cancellation and exceptions are wrapped rather than thrown inline.</para>
    /// <para><b>Flow:</b> check the token → invoke → wrap.</para>
    /// <para><b>Side Effects:</b> Those of <paramref name="syncOperation"/>, executed inline.</para>
    /// </remarks>
    /// <param name="syncOperation">The synchronous member being bridged.</param>
    /// <param name="cancellationToken">Token observed before the member runs.</param>
    /// <returns>A completed, cancelled or faulted task.</returns>
    internal static Task Run(Action syncOperation, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        try
        {
            syncOperation();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }
}
