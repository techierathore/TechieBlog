/// <summary>
/// Represents the result of an operation that can succeed or fail.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a clean pattern for returning success/failure from service methods.</para>
/// <para><b>Usage:</b> Used by service layer methods to return results with error messages.</para>
/// </remarks>
namespace BlogModels;

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
    /// The data returned on success.
    /// </summary>
    public T Data { get; private set; }

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    public string ErrorMessage { get; private set; }

    private Result() { }

    /// <summary>
    /// Creates a successful result with data.
    /// </summary>
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
    /// Creates a failed result with an error message.
    /// </summary>
    public static Result<T> Failure(string errorMessage)
    {
        return new Result<T>
        {
            IsSuccess = false,
            Data = default,
            ErrorMessage = errorMessage
        };
    }
}

/// <summary>
/// Represents the result of an operation without return data.
/// </summary>
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
    public string ErrorMessage { get; private set; }

    private Result() { }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static Result Success()
    {
        return new Result
        {
            IsSuccess = true,
            ErrorMessage = null
        };
    }

    /// <summary>
    /// Creates a failed result with an error message.
    /// </summary>
    public static Result Failure(string errorMessage)
    {
        return new Result
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }
}
