namespace LAMG.Application.Abstractions.System;

/// <summary>
/// Prevents the host machine from entering sleep or display-off while a
/// long-running job is active. Implemented on Windows via
/// <c>SetThreadExecutionState</c>. Cleared automatically when the
/// returned token is disposed.
/// </summary>
public interface ISystemAwakeKeeper
{
    /// <summary>
    /// Asks the OS to keep the system awake until the returned token
    /// is disposed. Calling this multiple times is safe; the awake
    /// state is reference-counted internally.
    /// </summary>
    IDisposable KeepAwake();
}
