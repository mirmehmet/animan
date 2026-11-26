namespace AniMan.Core.Common;

public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }

    /// <summary>Technical description for the log; the UI shows a localized text for <see cref="Kind"/>.</summary>
    public string? Error { get; }

    /// <summary>Why the operation failed; meaningless on success.</summary>
    public ErrorKind Kind { get; }

    private Result(T value)
    {
        IsSuccess = true;
        Value = value;
    }

    private Result(string error, ErrorKind kind)
    {
        IsSuccess = false;
        Error = error;
        Kind = kind;
    }

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(string error, ErrorKind kind = ErrorKind.Unexpected) => new(error, kind);
}

public sealed class Result
{
    public bool IsSuccess { get; }

    /// <summary>Technical description for the log; the UI shows a localized text for <see cref="Kind"/>.</summary>
    public string? Error { get; }

    /// <summary>Why the operation failed; meaningless on success.</summary>
    public ErrorKind Kind { get; }

    private Result(bool success, string? error, ErrorKind kind)
    {
        IsSuccess = success;
        Error = error;
        Kind = kind;
    }

    public static Result Success() => new(true, null, ErrorKind.Unexpected);
    public static Result Failure(string error, ErrorKind kind = ErrorKind.Unexpected) => new(false, error, kind);
}
