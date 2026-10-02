using System.Security.Principal;
using System.Windows.Threading;

namespace CodexTempo;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private RegisteredWaitHandle? _wait;
    public bool IsFirst { get; }
    public SingleInstance()
    {
        var suffix = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        _mutex = new Mutex(true, @"Local\CodexTempo-" + suffix, out var first);
        IsFirst = first;
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\CodexTempo-Activate-" + suffix);
        if (!first) _activate.Set();
    }
    public void Listen(Dispatcher dispatcher, Action activate) =>
        _wait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) =>
        { if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(activate); }, null, Timeout.Infinite, false);
    public void Dispose()
    {
        _wait?.Unregister(null);
        _activate.Dispose();
        if (IsFirst) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
