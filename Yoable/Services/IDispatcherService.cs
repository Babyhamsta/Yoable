using System;
using System.Threading.Tasks;

namespace Yoable.Services;

/// <summary>
/// Cross-platform dispatcher service for UI thread marshaling
/// </summary>
public interface IDispatcherService
{
    /// <summary>
    /// Invokes an action on the UI thread
    /// </summary>
    Task InvokeAsync(Action action);

    /// <summary>
    /// Invokes a function on the UI thread and returns the result
    /// </summary>
    Task<T> InvokeAsync<T>(Func<T> function);

    /// <summary>
    /// Checks if the current thread is the UI thread
    /// </summary>
    bool CheckAccess();
}
