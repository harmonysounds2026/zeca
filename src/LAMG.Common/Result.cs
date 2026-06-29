namespace LAMG.Common;

/// <summary>
/// Outcome of an operation that does not return a value.
/// </summary>
/// <remarks>
/// <see cref="Result"/> models <em>expected</em> failures (corrupted track,
/// retryable IO error, validation problems) and is the preferred return
/// type for service methods that may fail without aborting the job.
/// Unexpected failures should still surface as exceptions.
/// </remarks>
public readonly struct Result : IEquatable<Result>
{
    private Result(bool isSuccess, string? error, Exception? exception)
    {
        IsSuccess = isSuccess;
        Error = error;
        Exception = exception;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public string? Error { get; }

    public Exception? Exception { get; }

    public static Result Success() => new(true, null, null);

    public static Result Failure(string error, Exception? exception = null)
        => new(false, error ?? "Unknown error", exception);

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Failure<T>(string error, Exception? exception = null)
        => Result<T>.Failure(error, exception);

    public bool Equals(Result other)
        => IsSuccess == other.IsSuccess
           && string.Equals(Error, other.Error, StringComparison.Ordinal)
           && ReferenceEquals(Exception, other.Exception);

    public override bool Equals(object? obj) => obj is Result other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(IsSuccess, Error, Exception);

    public static bool operator ==(Result left, Result right) => left.Equals(right);

    public static bool operator !=(Result left, Result right) => !left.Equals(right);

    public override string ToString()
        => IsSuccess ? "Success" : $"Failure: {Error}";
}

/// <summary>
/// Outcome of an operation that returns a value of type <typeparamref name="T"/>.
/// </summary>
public readonly struct Result<T> : IEquatable<Result<T>>
{
    private Result(bool isSuccess, T? value, string? error, Exception? exception)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        Exception = exception;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public T? Value { get; }

    public string? Error { get; }

    public Exception? Exception { get; }

    public static Result<T> Success(T value) => new(true, value, null, null);

    public static Result<T> Failure(string error, Exception? exception = null)
        => new(false, default, error ?? "Unknown error", exception);

    /// <summary>
    /// Returns the value if successful; throws <see cref="InvalidOperationException"/> otherwise.
    /// </summary>
    public T GetValueOrThrow()
    {
        if (!IsSuccess)
        {
            throw new InvalidOperationException(
                $"Cannot read Value from a failed Result: {Error}",
                Exception);
        }

        return Value!;
    }

    public bool Equals(Result<T> other)
        => IsSuccess == other.IsSuccess
           && EqualityComparer<T?>.Default.Equals(Value, other.Value)
           && string.Equals(Error, other.Error, StringComparison.Ordinal)
           && ReferenceEquals(Exception, other.Exception);

    public override bool Equals(object? obj) => obj is Result<T> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(IsSuccess, Value, Error, Exception);

    public static bool operator ==(Result<T> left, Result<T> right) => left.Equals(right);

    public static bool operator !=(Result<T> left, Result<T> right) => !left.Equals(right);

    public override string ToString()
        => IsSuccess ? $"Success({Value})" : $"Failure: {Error}";
}
