using System;
using System.Threading.Tasks;
using Diarion.Diagnostics;

namespace Diarion.Services;

/// <summary>
/// Points the runtime's last-resort exception hooks at the crash reporter.
/// <para>
/// This exists for a specific job. The iOS release build carries <c>MtouchLink=None</c> and
/// <c>UseInterpreter=true</c>, added to stop a crash nobody wrote down, and those two settings cost
/// real IPA size and startup time. Turning them off means finding out what actually breaks, and each
/// guess costs a macOS build plus a sideload. One report beats three round trips.
/// </para>
/// <para>
/// Only managed exceptions reach here. A native crash — a real signal, an Objective-C exception that
/// never crosses back — leaves nothing behind, so an empty report file is not proof that the app
/// exited cleanly.
/// </para>
/// </summary>
public static class CrashHandlerInstaller
{
    private static bool _installed;

    public static void Install(ICrashReporter reporter)
    {
        if (_installed)
        {
            return;
        }
        _installed = true;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            reporter.Record("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            reporter.Record("TaskScheduler.UnobservedTaskException", e.Exception);
            // Left unobserved on purpose: marking it handled would change behaviour, and this is a
            // recorder, not a recovery mechanism.
        };

#if IOS || MACCATALYST
        // The one that matters most on iOS. A managed exception escaping into native code is
        // terminated by the runtime without going through AppDomain, so without this hook the exact
        // failures the linker causes would be recorded nowhere.
        ObjCRuntime.Runtime.MarshalManagedException += (_, e) =>
            reporter.Record($"MarshalManagedException ({e.ExceptionMode})", e.Exception);
#endif
    }
}
