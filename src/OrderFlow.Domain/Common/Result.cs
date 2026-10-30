using System.Diagnostics.CodeAnalysis;

namespace OrderFlow.Domain.Common;

/// <summary>The outcome of an operation that can fail in an expected way.</summary>
public class Result
{
    private protected Result(Error? error) => Error = error;

    public Error? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => Error is not null;

    public static Result Success() => new(null);

    public static Result Failure(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));

    public static Result<T> Success<T>(T value) => new(value);

    public static Result<T> Failure<T>(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>The outcome of an operation that produces a value or fails in an expected way.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T value)
        : base(null) => _value = value;

    internal Result(Error error)
        : base(error)
    {
    }

    /// <exception cref="InvalidOperationException">The result is a failure.</exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({Error.Code}).");

    public static implicit operator Result<T>(T value) => new(value);

    public static implicit operator Result<T>(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));
}
