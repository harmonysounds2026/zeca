namespace LAMG.Common;

/// <summary>
/// Wraps a delegate as an <see cref="IDisposable"/>. The delegate runs
/// exactly once, on the first call to <see cref="Dispose"/>.
/// </summary>
public sealed class DisposableAction : IDisposable
{
    private Action? _action;

    public DisposableAction(Action action)
    {
        _action = Guard.NotNull(action);
    }

    public void Dispose()
    {
        Action? action = Interlocked.Exchange(ref _action, null);
        action?.Invoke();
    }

    /// <summary>
    /// Returns an <see cref="IDisposable"/> that does nothing on dispose.
    /// </summary>
    public static IDisposable Empty { get; } = new DisposableAction(static () => { });
}

/// <summary>
/// Wraps an async delegate as an <see cref="IAsyncDisposable"/>. The
/// delegate runs exactly once, on the first call to <see cref="DisposeAsync"/>.
/// </summary>
public sealed class AsyncDisposableAction : IAsyncDisposable
{
    private Func<ValueTask>? _action;

    public AsyncDisposableAction(Func<ValueTask> action)
    {
        _action = Guard.NotNull(action);
    }

    public ValueTask DisposeAsync()
    {
        Func<ValueTask>? action = Interlocked.Exchange(ref _action, null);
        return action is null ? ValueTask.CompletedTask : action.Invoke();
    }
}
