using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Vitals.App;

internal static class Program
{
    // Passed by "Restart as administrator": wait for the old window to close instead of handing over to it.
    internal const string RestartingArgument = "--restarting";

    [STAThread]
    private static void Main(string[] args)
    {
        using var instance = SingleInstance.TryAcquire(waitForPrevious: Array.IndexOf(args, RestartingArgument) >= 0);
        if (instance is null)
        {
            SingleInstance.ShowRunningInstance();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

// One copy at a time: two copies double the load, and a second elevated copy stops the first one's ETW session.
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Manggdy.Vitals";
    public static readonly int ShowMessage = RegisterWindowMessage("Manggdy.Vitals.ShowMainWindow");

    private readonly Mutex _mutex;

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    public static SingleInstance? TryAcquire(bool waitForPrevious)
    {
        Mutex mutex;
        try
        {
            mutex = new Mutex(false, MutexName);
        }
        catch (UnauthorizedAccessException)
        {
            return null; // held by an elevated copy this process may not open
        }

        try
        {
            if (mutex.WaitOne(waitForPrevious ? TimeSpan.FromSeconds(10) : TimeSpan.Zero))
            {
                return new SingleInstance(mutex);
            }
        }
        catch (AbandonedMutexException)
        {
            return new SingleInstance(mutex); // the previous copy crashed; the mutex is ours now
        }

        mutex.Dispose();
        return null;
    }

    public static void ShowRunningInstance() => PostMessage(new IntPtr(0xFFFF) /* HWND_BROADCAST */, ShowMessage, IntPtr.Zero, IntPtr.Zero);

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
