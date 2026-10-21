using System;
using System.Globalization;
using System.Threading.Tasks;
using Diarion.Services.Backup;
using Microsoft.Maui.Storage;

namespace Diarion.Services;

/// <summary>
/// Settings in <see cref="Preferences"/>, the passphrase in <see cref="SecureStorage"/>. Device-local
/// by design (see <see cref="ScheduledBackupSettings"/>): none of this travels in a backup, so a
/// restored phone starts with the feature off and no secret it did not choose.
/// </summary>
public sealed class PreferencesScheduledBackupStore : IScheduledBackupStore
{
    private const string Prefix = "scheduled_backup_";
    private const string PassphraseKey = "diarion_scheduled_backup_passphrase";

    public ScheduledBackupSettings Load()
    {
        var settings = new ScheduledBackupSettings
        {
            IsEnabled = Preferences.Default.Get(Prefix + "enabled", false),
            IntervalDays = Preferences.Default.Get(Prefix + "interval_days", ScheduledBackupSettings.DefaultIntervalDays),
            KeepCount = Preferences.Default.Get(Prefix + "keep_count", ScheduledBackupSettings.DefaultKeepCount),
            FolderToken = NullIfEmpty(Preferences.Default.Get(Prefix + "folder_token", string.Empty)),
            FolderName = NullIfEmpty(Preferences.Default.Get(Prefix + "folder_name", string.Empty)),
            LastOutcome = Enum.TryParse<ScheduledBackupOutcome>(
                Preferences.Default.Get(Prefix + "last_outcome", string.Empty), out var outcome)
                ? outcome
                : ScheduledBackupOutcome.None
        };

        var lastRun = Preferences.Default.Get(Prefix + "last_run_utc", string.Empty);
        if (DateTime.TryParseExact(lastRun, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            settings.LastRunUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        return settings;
    }

    public void Save(ScheduledBackupSettings settings)
    {
        Preferences.Default.Set(Prefix + "enabled", settings.IsEnabled);
        Preferences.Default.Set(Prefix + "interval_days", settings.IntervalDays);
        Preferences.Default.Set(Prefix + "keep_count", settings.KeepCount);
        Preferences.Default.Set(Prefix + "folder_token", settings.FolderToken ?? string.Empty);
        Preferences.Default.Set(Prefix + "folder_name", settings.FolderName ?? string.Empty);
        Preferences.Default.Set(Prefix + "last_outcome", settings.LastOutcome.ToString());
        Preferences.Default.Set(
            Prefix + "last_run_utc",
            settings.LastRunUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);
    }

    public string? GetPassphrase()
    {
        try
        {
            // Same sync-over-async shape as the entitlement store and the app lock: SecureStorage
            // only speaks async, and the scheduler runs well off the UI thread.
            return NullIfEmpty(Task.Run(() => SecureStorage.Default.GetAsync(PassphraseKey)).GetAwaiter().GetResult());
        }
        catch
        {
            return null;
        }
    }

    public void SetPassphrase(string? passphrase)
    {
        try
        {
            if (string.IsNullOrEmpty(passphrase))
            {
                SecureStorage.Default.Remove(PassphraseKey);
                return;
            }

            Task.Run(() => SecureStorage.Default.SetAsync(PassphraseKey, passphrase)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Scheduled backup passphrase not stored: {ex.Message}");
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
