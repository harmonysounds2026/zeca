namespace LAMG.Common;

/// <summary>
/// Small, dependency-free async helpers used across the application.
/// Add new helpers here only when they remove real duplication.
/// </summary>
public static class AsyncExtensions
{
    /// <summary>
    /// Awaits the task, ignoring <see cref="OperationCanceledException"/> for the
    /// supplied <paramref name="cancellationToken"/>. Useful in cleanup paths
    /// where cancellation is the expected outcome.
    /// </summary>
    public static async Task IgnoreCancellationAsync(
        this Task task,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected.
        }
    }

    /// <summary>
    /// Awaits the task, swallowing any <see cref="OperationCanceledException"/>.
    /// </summary>
    public static async Task IgnoreCancellationAsync(this Task task)
    {
        ArgumentNullException.ThrowIfNull(task);

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Intentionally ignored.
        }
    }

    /// <summary>
    /// Returns a <see cref="Task"/> that completes when either the source
    /// task completes or the cancellation token fires. The original task
    /// continues running in the background.
    /// </summary>
    public static async Task<T> WaitWithCancellationAsync<T>(
        this Task<T> task,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (!cancellationToken.CanBeCanceled)
        {
            return await task.ConfigureAwait(false);
        }

        TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (cancellationToken.Register(static state =>
            ((TaskCompletionSource<bool>)state!).TrySetResult(true), tcs).ConfigureAwait(false))
        {
            Task completed = await Task.WhenAny(task, tcs.Task).ConfigureAwait(false);
            if (completed != task)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        return await task.ConfigureAwait(false);
    }
}
