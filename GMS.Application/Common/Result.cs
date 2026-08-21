namespace GMS.Application.Common;

/// <summary>
/// Generic result wrapper for service operations.
/// </summary>
public class Result
{
    public bool IsSuccess { get; protected set; }
    public string? Message { get; protected set; }
    public string? Error { get; protected set; }

    protected Result(bool isSuccess, string? message, string? error)
    {
        IsSuccess = isSuccess;
        Message = message;
        Error = error;
    }

    public static Result Success(string? message = null)
    {
        return new Result(true, message, null);
    }

    public static Result Failure(string error, string? message = null)
    {
        return new Result(false, message, error);
    }
}

/// <summary>
/// Generic result wrapper with data payload.
/// </summary>
public class Result<T> : Result
{
    public T? Data { get; set; }

    private Result(bool isSuccess, T? data, string? message, string? error)
        : base(isSuccess, message, error)
    {
        Data = data;
    }

    public static Result<T> Success(T data, string? message = null)
    {
        return new Result<T>(true, data, message, null);
    }

    public static new Result<T> Failure(string error, string? message = null)
    {
        return new Result<T>(false, default, message, error);
    }
}
