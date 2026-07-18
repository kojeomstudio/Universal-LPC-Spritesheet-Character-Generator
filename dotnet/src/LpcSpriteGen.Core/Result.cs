// Minimal Result type — replaces neverthrow. Methods mirror the subset we use from it.
namespace LpcSpriteGen.Core;

public readonly struct Result<T, TError>
{
    public bool IsOk { get; }
    public T Value { get; }
    public TError Error { get; }

    private Result(bool isOk, T value, TError error)
    {
        IsOk = isOk; Value = value; Error = error;
    }

    public static Result<T, TError> Ok(T value) => new(true, value, default!);
    public static Result<T, TError> Err(TError error) => new(false, default!, error);

    public T UnwrapOr(T fallback) => IsOk ? Value! : fallback;

    /// <summary>Get the value or throw if Err. Use only when IsOk is guaranteed.</summary>
    public T UnsafeUnwrap() => IsOk
        ? Value!
        : throw new InvalidOperationException($"Result.Err unwrapped: {Error}");

    public Result<U, TError> Map<U>(Func<T, U> f) => IsOk
        ? Result<U, TError>.Ok(f(Value!))
        : Result<U, TError>.Err(Error);

    public void Match(Action<T> ok, Action<TError> err)
    {
        if (IsOk) ok(Value!); else err(Error);
    }
}

public readonly struct Unit { public static readonly Unit Value = new(); }
