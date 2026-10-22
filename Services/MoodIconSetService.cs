using System;
using Diarion.Models;
using Microsoft.Maui.Storage;

namespace Diarion.Services;

/// <summary>
/// The mood row's picture set, kept in MAUI <c>Preferences</c> next to the theme and the language. In a
/// debug sandbox run it lives in memory only, so trying the switch there never rewrites the owner's
/// real preference — the same rule the forced sandbox theme follows.
/// </summary>
public sealed class MoodIconSetService : IMoodIconSetService
{
    private const string PreferenceKey = "MoodIconSet";

    private MoodIconSet? _sandboxed;

    public MoodIconSet Current
    {
        get
        {
            if (_sandboxed is { } sandboxed) return sandboxed;

            var stored = Preferences.Default.Get(PreferenceKey, (int)MoodIconSet.Emoji);
            return Enum.IsDefined(typeof(MoodIconSet), stored) ? (MoodIconSet)stored : MoodIconSet.Emoji;
        }
    }

    public void Set(MoodIconSet set)
    {
        if (DebugSandbox.DataDirectory is not null)
        {
            _sandboxed = set;
            return;
        }

        Preferences.Default.Set(PreferenceKey, (int)set);
    }
}
