using System;
using System.IO;

namespace Diarion.Services;

/// <summary>
/// A throwaway home for the app's data, for looking at screens the owner's real diary can never show:
/// onboarding, empty states, the first fortnight. <c>DIARION_SANDBOX_DIR=D:\some\folder</c> points the
/// database and cache there, so the real database is not opened at all, and the debug mock diary is
/// not seeded. <c>DIARION_SANDBOX_ONBOARDING=1</c> on top shows onboarding again without touching the
/// real preference. Debug builds only; in Release both read as "off". Same family as
/// <see cref="DebugStoreOverrides"/>, and described in <c>.claude/skills/run-diarion/SKILL.md</c>.
/// </summary>
internal static class DebugSandbox
{
    public const string DirectoryVariable = "DIARION_SANDBOX_DIR";

    /// <summary>The sandbox folder, created on first read; null when the app runs on its real data.</summary>
    public static string? DataDirectory
    {
        get
        {
#if DEBUG
            var dir = Environment.GetEnvironmentVariable(DirectoryVariable);
            if (string.IsNullOrWhiteSpace(dir))
                return null;
            Directory.CreateDirectory(dir);
            return dir;
#else
            return null;
#endif
        }
    }

    public static string? CacheDirectory
    {
        get
        {
            var data = DataDirectory;
            if (data is null)
                return null;
            var cache = Path.Combine(data, "cache");
            Directory.CreateDirectory(cache);
            return cache;
        }
    }

    /// <summary>
    /// <c>DIARION_SANDBOX_THEME=Light|Dark|Pink</c> forces the theme for a sandbox run, so both themes can
    /// be looked at without the toggle writing the owner's real preference. Null when not sandboxed.
    /// </summary>
    public static string? ForcedTheme
    {
        get
        {
#if DEBUG
            if (DataDirectory is null)
                return null;
            var theme = Environment.GetEnvironmentVariable("DIARION_SANDBOX_THEME");
            return string.IsNullOrWhiteSpace(theme) ? null : theme;
#else
            return null;
#endif
        }
    }

    public static bool ShowsOnboarding =>
#if DEBUG
        DataDirectory is not null && Environment.GetEnvironmentVariable("DIARION_SANDBOX_ONBOARDING") == "1";
#else
        false;
#endif
}
