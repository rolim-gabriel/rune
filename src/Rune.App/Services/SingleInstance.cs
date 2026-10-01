using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Windows.AppLifecycle;
using Rune.Engine;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace Rune.Services;

/// <summary>
/// Keeps Rune to one process, so a PDF double-clicked in Explorer opens as a
/// tab in the window that is already there instead of in a second window.
///
/// Explorer starts a fresh <c>Rune.exe</c> for every file it opens; there is
/// no way to tell it otherwise. So each new process asks the Windows App SDK
/// whether another Rune has registered itself as the main instance. If not,
/// this one registers and carries on to show a window. If so, this one hands
/// its activation (the file path) to that instance through
/// <see cref="AppInstance.RedirectActivationToAsync"/> and exits before it has
/// drawn anything — the whole detour costs a few hundred milliseconds and no
/// window ever flashes.
///
/// This runs from <c>Program.Main</c>, before XAML starts, because the only
/// reason to redirect is to avoid building a second window at all.
///
/// <c>--new-window</c> opts a launch out: it neither registers nor redirects,
/// so it gets its own window and the original stays the one Explorer talks to.
/// </summary>
internal static class SingleInstance
{
    // Any stable string works; it is scoped to the current user session.
    private const string Key = "Rune.MainInstance";

    /// <summary>
    /// Activations handed over by other processes, waiting for the UI thread.
    /// The handoff lands on a thread-pool thread, possibly before the window
    /// exists, so they queue here and <see cref="App"/> drains them on the
    /// dispatcher once it has a window.
    /// </summary>
    public static ConcurrentQueue<LaunchRequest> Pending { get; } = new();

    /// <summary>Raised (on a thread-pool thread) after something was queued.</summary>
    public static event Action? ActivationQueued;

    /// <summary>What this process itself was launched with.</summary>
    public static LaunchRequest Initial { get; private set; } = LaunchRequest.Empty;

    /// <summary>
    /// Decides whether this process should show a window. Returns true when
    /// it handed its arguments to a running Rune and should exit.
    /// </summary>
    public static bool RedirectToRunningInstance()
    {
        AppActivationArguments? activation = null;
        try
        {
            activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            Initial = Parse(activation);
        }
        catch (Exception ex)
        {
            // Not fatal: the command line is read again in App.OnLaunched.
            ErrorLog.Default.Write("SingleInstance.GetActivatedEventArgs", ex);
        }

        if (!Initial.HasPaths)
        {
            // Packaged file activation also puts the path on the command line,
            // and the unpackaged launch always does; this is the floor that
            // v0.8.0 and earlier stood on.
            Initial = LaunchRequest.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray());
        }

        if (Initial.NewWindow)
        {
            return false;
        }

        try
        {
            AppInstance main = AppInstance.FindOrRegisterForKey(Key);
            if (main.IsCurrent)
            {
                main.Activated += OnActivated;
                return false;
            }

            if (activation is null)
            {
                // Nothing to hand over. Showing our own window beats opening nothing.
                return false;
            }

            // A process the user just launched holds the right to set the
            // foreground window; lend it to the instance that will actually
            // show the document, or its window stays behind Explorer.
            AllowSetForegroundWindow((uint)main.ProcessId);
            RedirectAndWait(main, activation);
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Default.Write("SingleInstance.Redirect", ex);
            return false;
        }
    }

    private static void OnActivated(object? sender, AppActivationArguments e)
    {
        LaunchRequest request;
        try
        {
            request = Parse(e);
        }
        catch (Exception ex)
        {
            ErrorLog.Default.Write("SingleInstance.OnActivated", ex);
            request = LaunchRequest.Empty;
        }

        // Even an empty request is worth queuing: the user launched Rune, so
        // the window should at least come to the front.
        Pending.Enqueue(request);
        ActivationQueued?.Invoke();
    }

    /// <summary>
    /// Turns either kind of activation Rune receives into a request. A Store
    /// install registered as the .pdf handler gets a File activation; the
    /// portable exe (and any command line) gets a Launch activation carrying
    /// the raw command line.
    /// </summary>
    private static LaunchRequest Parse(AppActivationArguments activation)
    {
        switch (activation.Kind)
        {
            case ExtendedActivationKind.File when activation.Data is IFileActivatedEventArgs file:
                var paths = file.Files
                    .OfType<IStorageItem>()
                    .Select(item => item.Path)
                    .Where(path => !string.IsNullOrEmpty(path))
                    .ToList();
                return new LaunchRequest(paths, null, null, false);

            case ExtendedActivationKind.Launch when activation.Data is ILaunchActivatedEventArgs launch:
                return LaunchRequest.Parse(SplitCommandLine(launch.Arguments));

            default:
                return LaunchRequest.Empty;
        }
    }

    /// <summary>
    /// Splits a raw command line the way the C runtime does (quotes, escapes),
    /// dropping the executable if it leads. The unpackaged launch's Arguments
    /// is the whole line, exe included; a redirected one may or may not be.
    /// </summary>
    private static string[] SplitCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return [];
        }

        IntPtr argv = CommandLineToArgvW(commandLine, out int argc);
        if (argv == IntPtr.Zero)
        {
            return [];
        }

        try
        {
            var args = new List<string>(argc);
            for (int i = 0; i < argc; i++)
            {
                string? arg = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size));
                if (arg is not null)
                {
                    args.Add(arg);
                }
            }

            if (args.Count > 0 && IsThisExecutable(args[0]))
            {
                args.RemoveAt(0);
            }
            return args.ToArray();
        }
        finally
        {
            LocalFree(argv);
        }
    }

    private static bool IsThisExecutable(string token)
    {
        string? exe = Environment.ProcessPath;
        if (exe is not null)
        {
            try
            {
                if (string.Equals(Path.GetFullPath(token), exe, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                // Not a path at all; fall through to the name check.
            }
        }
        string name = Path.GetFileName(token);
        return name.Equals("Rune.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Rune.dll", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Hands the activation over and blocks until the other instance has it.
    /// Main is an STA thread, so a plain <c>.Wait()</c> on the WinRT operation
    /// would deadlock; the async call runs on the pool and the STA pumps COM
    /// messages until it signals. This is the pattern from Microsoft's
    /// AppLifecycle docs.
    /// </summary>
    private static void RedirectAndWait(AppInstance target, AppActivationArguments activation)
    {
        IntPtr done = CreateEvent(IntPtr.Zero, bManualReset: true, bInitialState: false, null);
        if (done == IntPtr.Zero)
        {
            // No event to wait on: fall back to a blocking wait on a pool thread.
            Task.Run(() => target.RedirectActivationToAsync(activation).AsTask()).Wait();
            return;
        }

        try
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await target.RedirectActivationToAsync(activation);
                }
                finally
                {
                    SetEvent(done);
                }
            });

            const uint CWMO_DEFAULT = 0;
            const uint INFINITE = 0xFFFFFFFF;
            _ = CoWaitForMultipleObjects(CWMO_DEFAULT, INFINITE, 1, [done], out _);
        }
        finally
        {
            CloseHandle(done);
        }
    }

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll")]
    private static extern bool SetEvent(IntPtr hEvent);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(uint dwFlags, uint dwMilliseconds, uint nHandles, IntPtr[] pHandles, out uint dwIndex);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);
}
