namespace BlogModels;

/// <summary>
/// The outcome of an operation that either succeeds with a <typeparamref name="T"/> or fails with a
/// message.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets a service report an expected failure — a duplicate email, a stale
/// token, a rejected upload — as a return value instead of an exception, so the UI can render the
/// message without a try/catch around every call.</para>
///
/// <para><b>Code Flow:</b> The constructor is private; instances only ever come from
/// <see cref="Success"/> or <see cref="Failure"/>, and every property has a private setter, so an
/// instance is immutable once handed back. That is what makes it safe to pass a result up through
/// layers without defensive copying.</para>
///
/// <para><b>Dependencies:</b> None.</para>
///
/// <para><b>Usage:</b> Branch on <see cref="IsSuccess"/> before touching <see cref="Data"/> —
/// on the failure path <see cref="Data"/> is <c>default!</c>, which is <c>null</c> for reference
/// types despite the non-nullable declaration. Use this for expected outcomes only; genuine faults
/// (a dropped connection, a bug) should still throw.</para>
/// </remarks>
/// <typeparam name="T">The payload carried on the success path.</typeparam>
public class Result<T>
{
    /// <summary>
    /// Indicates whether the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; private set; }

    /// <summary>
    /// Indicates whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// The payload produced by a successful operation. Only meaningful when
    /// <see cref="IsSuccess"/> is <c>true</c> — on the failure path this is <c>default!</c>, i.e.
    /// <c>null</c> for reference types, and the non-nullable declaration will not warn you.
    /// </summary>
    public T Data { get; private set; } = default!;

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Private so the only way to obtain an instance is <see cref="Success"/> or
    /// <see cref="Failure"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Guarantees that <see cref="IsSuccess"/> and <see cref="Data"/>
    /// can never disagree — there is no way to construct a "successful" result that carries an error
    /// message, or a failure that also carries data.</para>
    /// <para><b>Flow:</b> invoked only by the two factory methods.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    private Result() { }

    /// <summary>
    /// Creates a successful result carrying the operation's payload.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The success half of the contract. <paramref name="data"/> is
    /// stored as given — including <c>null</c>, which a caller may legitimately want to mean
    /// "succeeded, nothing to return"; that is why "found nothing" is usually better expressed as
    /// <c>Success(null)</c> than as a <see cref="Failure"/>, since an empty search is not an error.</para>
    /// <para><b>Flow:</b> allocate → set <see cref="IsSuccess"/> → store the payload.</para>
    /// <para><b>Side Effects:</b> None; the returned instance is immutable.</para>
    /// </remarks>
    /// <param name="data">The payload to hand back to the caller.</param>
    /// <returns>A result whose <see cref="IsSuccess"/> is <c>true</c>.</returns>
    public static Result<T> Success(T data)
    {
        return new Result<T>
        {
            IsSuccess = true,
            Data = data,
            ErrorMessage = null
        };
    }

    /// <summary>
    /// Creates a failed result carrying a message the UI can display.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> For <i>expected</i> failures only — a duplicate email address, a
    /// token that has already been used, an upload the rules reject. The message is written to be
    /// read by an end user, so it must never contain a stack trace, a SQL fragment or anything else
    /// that would help an attacker. An unexpected fault (a dropped connection, a null dereference)
    /// is not modelled here; let it throw.</para>
    /// <para><b>Flow:</b> allocate → leave <see cref="IsSuccess"/> <c>false</c> → store the message
    /// and leave <see cref="Data"/> at <c>default!</c>.</para>
    /// <para><b>Side Effects:</b> None; the returned instance is immutable. Nothing is logged — the
    /// service that detected the failure is responsible for logging it, because only it knows the
    /// detail that must not reach the caller.</para>
    /// </remarks>
    /// <param name="errorMessage">User-facing explanation of why the operation did not succeed.</param>
    /// <returns>A result whose <see cref="IsFailure"/> is <c>true</c> and whose <see cref="Data"/> is <c>default!</c>.</returns>
    public static Result<T> Failure(string? errorMessage)
    {
        return new Result<T>
        {
            IsSuccess = false,
            Data = default!,
            ErrorMessage = errorMessage
        };
    }
}

/// <summary>
/// The outcome of an operation that either succeeds with nothing to return, or fails with a message.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The command-shaped half of the project-wide error convention:
/// <see cref="Result{T}"/> is for operations that produce a value, this one for operations that
/// only succeed or fail — delete a comment, send a mail, revoke a session. Using it instead of
/// <c>bool</c> is what lets the failure carry a reason.</para>
///
/// <para><b>Code Flow:</b> Identical in shape to <see cref="Result{T}"/> — a private constructor,
/// private setters, and <see cref="Success"/> / <see cref="Failure"/> as the only way in — so an
/// instance is immutable once handed back and cannot be put into a contradictory state.</para>
///
/// <para><b>Dependencies:</b> None.</para>
///
/// <para><b>Usage:</b> Branch on <see cref="IsSuccess"/>; read <see cref="ErrorMessage"/> only on
/// the failure path, where it is the message to render. The same contract as
/// <see cref="Result{T}"/> applies: <b>this type is for expected failures, exceptions are for the
/// unexpected.</b> Returning <c>Failure("Object reference not set…")</c> for a bug is a misuse — it
/// converts a fault the logs should record into a sentence shown to a reader. Conversely, throwing
/// for a duplicate email forces every call site into a try/catch and is the reason this type
/// exists.</para>
/// </remarks>
public class Result
{
    /// <summary>
    /// Indicates whether the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; private set; }

    /// <summary>
    /// Indicates whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Private so the only way to obtain an instance is <see cref="Success"/> or
    /// <see cref="Failure"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Keeps <see cref="IsSuccess"/> and <see cref="ErrorMessage"/>
    /// consistent — a success can never carry an error message.</para>
    /// <para><b>Flow:</b> invoked only by the two factory methods.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    private Result() { }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Reports that the command completed. There is nothing to inspect
    /// beyond <see cref="IsSuccess"/>; a caller that needs a value should be returning
    /// <see cref="Result{T}"/> instead.</para>
    /// <para><b>Flow:</b> allocate → set <see cref="IsSuccess"/> → leave the message null.</para>
    /// <para><b>Side Effects:</b> None; the returned instance is immutable.</para>
    /// </remarks>
    /// <returns>A result whose <see cref="IsSuccess"/> is <c>true</c>.</returns>
    public static Result Success()
    {
        return new Result
        {
            IsSuccess = true,
            ErrorMessage = null
        };
    }

    /// <summary>
    /// Creates a failed result carrying a message the UI can display.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> For expected failures only — see the class remarks. The message
    /// is end-user copy and must not leak internal detail.</para>
    /// <para><b>Flow:</b> allocate → leave <see cref="IsSuccess"/> <c>false</c> → store the message.</para>
    /// <para><b>Side Effects:</b> None; the returned instance is immutable, and logging remains the
    /// responsibility of the service that detected the failure.</para>
    /// </remarks>
    /// <param name="errorMessage">User-facing explanation of why the operation did not succeed.</param>
    /// <returns>A result whose <see cref="IsFailure"/> is <c>true</c>.</returns>
    public static Result Failure(string? errorMessage)
    {
        return new Result
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }
}
