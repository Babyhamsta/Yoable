using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Yoable.Services;

/// <summary>
/// Avalonia-based dispatcher service
/// </summary>
public class AvaloniaDispatcherService : IDispatcherService
{
    public async Task InvokeAsync(Action action)
    {
        await Dispatcher.UIThread.InvokeAsync(action);
    }

    public async Task<T> InvokeAsync<T>(Func<T> function)
    {
        return await Dispatcher.UIThread.InvokeAsync(function);
    }

    public bool CheckAccess()
    {
        return Dispatcher.UIThread.CheckAccess();
    }
}
