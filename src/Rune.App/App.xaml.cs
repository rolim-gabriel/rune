using Microsoft.UI.Xaml;
using Rune.Services;

namespace Rune;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();

        // Safety net. An `async void` handler (every WinUI event handler is one)
        // has no Task to park an exception in, so the runtime rethrows it on the
        // dispatcher and the process dies unless Handled is set here. Log it and
        // keep running — a reader losing an hour of annotations to a transient
        // failure is far worse than a stale error message.
        UnhandledException += (_, e) =>
        {
            ErrorLog.Default.Write("UnhandledException", e.Exception);
            e.Handled = true;
            ReportToUser(e.Exception.Message);
        };

        // The other half: `_ = SomeAsync()` parks a failure in a Task nobody
        // awaits, which is silently swallowed. This surfaces those too.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ErrorLog.Default.Write("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        // Another Rune.exe (Explorer opening a PDF) handed us its file.
        SingleInstance.ActivationQueued += DrainRedirectedActivations;
    }

    /// <summary>
    /// 1 while a drain is scheduled or running. Each hand-over awaits its
    /// document load, so without this two drains could interleave on the
    /// dispatcher and a --page meant for one file could land on the other's
    /// tab. One drain at a time keeps arrival order.
    /// </summary>
    private static int _drainScheduled;

    /// <summary>
    /// Moves queued hand-overs from other processes onto the UI thread and
    /// opens them as tabs, one after another. Called from a pool thread when
    /// one arrives, and once from OnLaunched for anything that arrived before
    /// the window did. Dequeuing happens only on the dispatcher, so nothing is
    /// opened twice.
    /// </summary>
    private static void DrainRedirectedActivations()
    {
        if (MainWindow is not MainWindow window)
        {
            return; // OnLaunched drains the queue once there is a window
        }

        if (Interlocked.Exchange(ref _drainScheduled, 1) != 0)
        {
            return; // the running drain will pick this one up, or re-arm below
        }

        window.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                while (SingleInstance.Pending.TryDequeue(out var request))
                {
                    try
                    {
                        await window.HandleActivationAsync(request);
                    }
                    catch (Exception ex)
                    {
                        ErrorLog.Default.Write("RedirectedActivation", ex);
                        ReportToUser(ex.Message);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _drainScheduled, 0);
                // A hand-over that arrived after the loop saw an empty queue
                // but before the flag dropped was turned away above.
                if (!SingleInstance.Pending.IsEmpty)
                {
                    DrainRedirectedActivations();
                }
            }
        });
    }

    /// <summary>
    /// Surfaces a background failure in the main window's InfoBar. Never a
    /// dialog: the very bug this net was added for was "a second ContentDialog
    /// was shown", so opening one from the handler could re-trigger it.
    /// </summary>
    private static void ReportToUser(string message)
    {
        try
        {
            (MainWindow as MainWindow)?.ReportBackgroundError(message);
        }
        catch
        {
            // Window may be closing or not built yet — the log already has it.
        }
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();
        MainWindow = window;

        // Window/taskbar icon (the exe icon comes from ApplicationIcon in the csproj).
        string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "rune.ico");
        if (File.Exists(icon))
        {
            window.AppWindow.SetIcon(icon);
        }

        window.Activate();

        // "Rune.exe <file.pdf> [--page N] [--zoom Z] [--new-window]" — the file
        // path is how Explorer launches the default handler; --page/--zoom are
        // for scripted testing. SingleInstance parsed it before XAML started,
        // from the activation args (Store file association) or the command
        // line (portable exe).
        var initial = SingleInstance.Initial;
        if (initial.HasPaths)
        {
            await window.OpenFromLaunchAsync(initial);
        }

        // Files that other processes handed over while this window was still
        // being built.
        DrainRedirectedActivations();
    }
}
