using System;
using System.Text;

namespace Diarion.Diagnostics;

/// <summary>
/// Formats a crash into the text that gets stored and, if the user chooses, shared.
/// <para>
/// Pure and separate from the writing so the shape of a report can be asserted in tests — the code
/// that produces it only ever runs while the process is dying, which is the worst possible place to
/// discover a mistake.
/// </para>
/// <para>
/// Deliberately narrow: exception type, message, stack, and where it was caught. No diary content, no
/// database values, no profile. An exception message can still quote data the runtime was handling, so
/// the report is never sent anywhere on its own — the app has no network at all, and sharing it is an
/// explicit act by the user.
/// </para>
/// </summary>
public static class CrashReport
{
    public const int MaxLength = 16 * 1024;

    public static string Format(string source, Exception? exception, DateTime timestampUtc, string appVersion)
    {
        var text = new StringBuilder();
        text.Append("Diarion crash report").Append('\n');
        text.Append("When:    ").Append(timestampUtc.ToString("yyyy-MM-dd HH:mm:ss")).Append(" UTC\n");
        text.Append("Version: ").Append(string.IsNullOrWhiteSpace(appVersion) ? "unknown" : appVersion).Append('\n');
        text.Append("Source:  ").Append(string.IsNullOrWhiteSpace(source) ? "unknown" : source).Append('\n');
        text.Append('\n');

        if (exception == null)
        {
            // A handler can fire with a non-Exception payload. Saying so beats writing nothing and
            // leaving the reader to wonder whether the report itself failed.
            text.Append("No exception object was supplied.\n");
        }
        else
        {
            AppendException(text, exception, depth: 0);
        }

        var report = text.ToString();
        return report.Length <= MaxLength
            ? report
            : report[..MaxLength] + "\n… truncated\n";
    }

    private static void AppendException(StringBuilder text, Exception exception, int depth)
    {
        // Inner exceptions carry the actual cause when the linker or the AOT compiler is involved:
        // a TypeInitializationException on the surface, the missing member underneath.
        var indent = new string(' ', depth * 2);

        text.Append(indent).Append(exception.GetType().FullName).Append('\n');
        text.Append(indent).Append("  ").Append(exception.Message).Append('\n');

        if (!string.IsNullOrEmpty(exception.StackTrace))
        {
            text.Append(exception.StackTrace).Append('\n');
        }

        if (exception.InnerException != null && depth < 5)
        {
            text.Append('\n').Append(indent).Append("--- caused by ---\n");
            AppendException(text, exception.InnerException, depth + 1);
        }
    }
}
