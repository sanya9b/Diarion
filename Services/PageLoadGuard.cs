using System;
using System.Threading.Tasks;
using Diarion.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;

namespace Diarion.Services;

/// <summary>
/// Runs a page's appearance load so that a failure is recorded rather than fatal.
/// <para>
/// <c>OnAppearing</c> is <c>async void</c> on every page, and an exception out of an <c>async void</c>
/// method has no caller to catch it: on iOS the runtime ends the process. That is how a screen whose
/// data would not load took the whole app down with it, instead of showing an empty state. The
/// report lands in the same slot the crash handler writes, so Settings can show and share it.
/// </para>
/// </summary>
public static class PageLoadGuard
{
    public static async Task RunAsync(object page, Func<Task> load)
    {
        try
        {
            await load();
        }
        catch (Exception ex)
        {
            Report(page, ex);
        }
    }

    private static void Report(object page, Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"{page.GetType().Name} failed to load: {ex}");
        try
        {
            IPlatformApplication.Current?.Services.GetService<ICrashReporter>()
                ?.Record($"{page.GetType().Name}.OnAppearing (survived)", ex);
        }
        catch
        {
            // A report that cannot be written is not worth a second failure.
        }
    }
}
