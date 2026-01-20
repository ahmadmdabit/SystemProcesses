using System;

namespace SystemProcesses.Desktop.Helpers;

/// <summary>
/// Represents the result of an operation that can succeed or fail.
/// Provides a type-safe alternative to exceptions for expected failures.
/// </summary>
/// <typeparam name="T">The type of the success value.</typeparam>
public abstract record Result<T>
{
    /// <summary>
    /// Represents a successful operation result.
    /// </summary>
    /// <param name="Value">The successful result value.</param>
    public sealed record Success(T Value) : Result<T>;

    /// <summary>
    /// Represents a failed operation result.
    /// </summary>
    /// <param name="Error">The exception that caused the failure.</param>
    /// <param name="Context">Contextual information about where the failure occurred.</param>
    public sealed record Failure(Exception Error, string Context) : Result<T>;

    /// <summary>
    /// Executes a function based on the result state.
    /// </summary>
    /// <typeparam name="TResult">The return type of the mapping functions.</typeparam>
    /// <param name="onSuccess">Function to execute if result is Success.</param>
    /// <param name="onFailure">Function to execute if result is Failure.</param>
    /// <returns>The result of the executed function.</returns>
    public TResult Match<TResult>(
        Func<T, TResult> onSuccess,
        Func<Exception, string, TResult> onFailure) =>
        this switch
        {
            Success s => onSuccess(s.Value),
            Failure f => onFailure(f.Error, f.Context),
            _ => throw new InvalidOperationException("Unknown result type")
        };

    /// <summary>
    /// Executes an action based on the result state.
    /// </summary>
    /// <param name="onSuccess">Action to execute if result is Success.</param>
    /// <param name="onFailure">Action to execute if result is Failure.</param>
    public void Match(
        Action<T> onSuccess,
        Action<Exception, string> onFailure)
    {
        switch (this)
        {
            case Success s:
                onSuccess(s.Value);
                break;
            case Failure f:
                onFailure(f.Error, f.Context);
                break;
        }
    }

    /// <summary>
    /// Gets the value if successful, otherwise returns a default value.
    /// </summary>
    /// <param name="defaultValue">The default value to return on failure.</param>
    /// <returns>The success value or the default value.</returns>
    public T GetValueOrDefault(T defaultValue) =>
        this switch
        {
            Success s => s.Value,
            _ => defaultValue
        };

    /// <summary>
    /// Gets the value if successful, otherwise throws the exception.
    /// </summary>
    /// <returns>The success value.</returns>
    /// <exception cref="InvalidOperationException">Thrown if result is Failure.</exception>
    public T GetValueOrThrow() =>
        this switch
        {
            Success s => s.Value,
            Failure f => throw new InvalidOperationException(
                $"Operation failed in context: {f.Context}", f.Error),
            _ => throw new InvalidOperationException("Unknown result type")
        };

    /// <summary>
    /// Determines if the result is successful.
    /// </summary>
    public bool IsSuccess => this is Success;

    /// <summary>
    /// Determines if the result is a failure.
    /// </summary>
    public bool IsFailure => this is Failure;
}

/// <summary>
/// Non-generic result type for operations that don't return a value.
/// </summary>
public abstract record Result
{
    /// <summary>
    /// Represents a successful operation.
    /// </summary>
    public sealed record Success : Result;

    /// <summary>
    /// Represents a failed operation.
    /// </summary>
    /// <param name="Error">The exception that caused the failure.</param>
    /// <param name="Context">Contextual information about where the failure occurred.</param>
    public sealed record Failure(Exception Error, string Context) : Result;

    /// <summary>
    /// Executes a function based on the result state.
    /// </summary>
    /// <typeparam name="TResult">The return type of the mapping functions.</typeparam>
    /// <param name="onSuccess">Function to execute if result is Success.</param>
    /// <param name="onFailure">Function to execute if result is Failure.</param>
    /// <returns>The result of the executed function.</returns>
    public TResult Match<TResult>(
        Func<TResult> onSuccess,
        Func<Exception, string, TResult> onFailure) =>
        this switch
        {
            Success => onSuccess(),
            Failure f => onFailure(f.Error, f.Context),
            _ => throw new InvalidOperationException("Unknown result type")
        };

    /// <summary>
    /// Executes an action based on the result state.
    /// </summary>
    /// <param name="onSuccess">Action to execute if result is Success.</param>
    /// <param name="onFailure">Action to execute if result is Failure.</param>
    public void Match(
        Action onSuccess,
        Action<Exception, string> onFailure)
    {
        switch (this)
        {
            case Success:
                onSuccess();
                break;
            case Failure f:
                onFailure(f.Error, f.Context);
                break;
        }
    }

    /// <summary>
    /// Determines if the result is successful.
    /// </summary>
    public bool IsSuccess => this is Success;

    /// <summary>
    /// Determines if the result is a failure.
    /// </summary>
    public bool IsFailure => this is Failure;
}
