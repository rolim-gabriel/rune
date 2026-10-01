using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Rune.Services;

namespace Rune;

/// <summary>
/// Hand-written entry point. The XAML compiler normally generates one, but it
/// goes straight to <see cref="Application.Start"/>, and the single-instance
/// check has to run before that: once XAML is up there is a window to tear
/// down again. <c>DISABLE_XAML_GENERATED_MAIN</c> in the csproj turns the
/// generated one off; the body below is the same as the generated one plus
/// the redirect.
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (SingleInstance.RedirectToRunningInstance())
        {
            // The window that is already open took the file. Nothing to show.
            return;
        }

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}
