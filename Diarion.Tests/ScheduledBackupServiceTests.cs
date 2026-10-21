using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Services;
using Diarion.Services.Backup;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The scheduler around the portable backup. The envelope itself is covered by
/// <see cref="PortableBackupTests"/>; what these guard is when a backup happens, where the file
/// goes, what is left behind, and that a failure is remembered without being mistaken for a run.
/// </summary>
public class ScheduledBackupServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 14, 9, 30, 0, DateTimeKind.Utc);

    private readonly string _cache;
    private readonly FakeFolder _folder = new();
    private readonly FakeStore _store = new();
    private readonly Mock<IBackupService> _backup = new();
    private readonly FixedClock _clock = new(Now);

    public ScheduledBackupServiceTests()
    {
        _cache = Path.Combine(Path.GetTempPath(), "diarion_sched_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cache);

        // The real writer re-encrypts the database; here it just has to leave a file at the path.
        _backup.Setup(b => b.WritePortableBackupAsync(It.IsAny<string>(), It.IsAny<string>()))
               .Returns((string _, string path) =>
               {
                   File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                   return Task.FromResult(BackupOutcome.Success);
               });
    }

    public void Dispose()
    {
        try { Directory.Delete(_cache, recursive: true); } catch { /* best-effort */ }
    }

    private ScheduledBackupService Create() =>
        new(_backup.Object, _folder, _store, new FakeFileSystem(_cache), _clock);

    private ScheduledBackupService Enabled(int intervalDays = 7, DateTime? lastRunUtc = null)
    {
        var service = Create();
        service.Enable(new BackupFolderPick("tree://backups", "Diarion"), "correct horse", intervalDays);
        if (lastRunUtc is not null)
        {
            _store.Settings.LastRunUtc = lastRunUtc;
        }
        return service;
    }

    // ---- due or not ----

    [Fact]
    public async Task Switched_off_means_nothing_happens()
    {
        var service = Create();

        (await service.RunIfDueAsync()).Should().Be(ScheduledBackupOutcome.Disabled);
        (await service.RunNowAsync()).Should().Be(ScheduledBackupOutcome.Disabled);
        _folder.Files.Should().BeEmpty();
        _backup.Verify(b => b.WritePortableBackupAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Switched_on_and_never_run_is_due_at_once()
    {
        var service = Enabled();

        service.IsDue(Now).Should().BeTrue();
        (await service.RunIfDueAsync()).Should().Be(ScheduledBackupOutcome.Success);
    }

    [Theory]
    [InlineData(7, 2, false)]
    [InlineData(7, 7, true)]
    [InlineData(7, 30, true)]
    [InlineData(1, 0.5, false)]
    [InlineData(1, 1, true)]
    public void Due_when_the_interval_has_passed_since_the_last_success(int intervalDays, double daysAgo, bool due)
    {
        var service = Enabled(intervalDays, Now.AddDays(-daysAgo));

        service.IsDue(Now).Should().Be(due);
    }

    [Fact]
    public async Task Not_yet_due_leaves_the_folder_alone()
    {
        var service = Enabled(7, Now.AddDays(-2));

        (await service.RunIfDueAsync()).Should().Be(ScheduledBackupOutcome.NotDue);
        _folder.Files.Should().BeEmpty();
    }

    // ---- a run ----

    [Fact]
    public async Task A_run_writes_one_envelope_named_by_time_and_moves_the_clock()
    {
        var service = Enabled();

        var outcome = await service.RunNowAsync();

        outcome.Should().Be(ScheduledBackupOutcome.Success);
        _folder.Files.Keys.Should().ContainSingle()
            .Which.Should().Be(ScheduledBackupService.FileNameFor(Now.ToLocalTime()));
        _folder.Files.Values.Single().Should().Equal(1, 2, 3);
        _store.Settings.LastRunUtc.Should().Be(Now);
        _store.Settings.LastOutcome.Should().Be(ScheduledBackupOutcome.Success);
        _store.SaveCount.Should().BeGreaterThan(0);
        _backup.Verify(b => b.WritePortableBackupAsync("correct horse", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task The_temporary_file_does_not_outlive_the_run()
    {
        var service = Enabled();

        await service.RunNowAsync();

        Directory.EnumerateFiles(_cache).Should().BeEmpty("the envelope was copied into the folder and the local copy removed");
    }

    [Fact]
    public async Task Back_up_now_ignores_the_interval()
    {
        var service = Enabled(7, Now.AddDays(-1));

        (await service.RunNowAsync()).Should().Be(ScheduledBackupOutcome.Success);
    }

    // ---- what can go wrong ----

    [Fact]
    public async Task A_folder_whose_grant_is_gone_is_reported_and_the_clock_does_not_move()
    {
        var service = Enabled(7, Now.AddDays(-10));
        _folder.Writable = false;

        var outcome = await service.RunNowAsync();

        outcome.Should().Be(ScheduledBackupOutcome.FolderUnavailable);
        _store.Settings.LastOutcome.Should().Be(ScheduledBackupOutcome.FolderUnavailable);
        _store.Settings.LastRunUtc.Should().Be(Now.AddDays(-10), "a failure is not a run; the next launch must try again");
        service.IsDue(Now).Should().BeTrue();
    }

    [Fact]
    public async Task A_lost_passphrase_is_reported_rather_than_backed_up_unprotected()
    {
        var service = Enabled();
        _store.SetPassphrase(null);

        (await service.RunNowAsync()).Should().Be(ScheduledBackupOutcome.NoPassphrase);
        _folder.Files.Should().BeEmpty();
        _backup.Verify(b => b.WritePortableBackupAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task A_write_the_backup_service_refuses_is_a_failure_with_nothing_in_the_folder()
    {
        _backup.Setup(b => b.WritePortableBackupAsync(It.IsAny<string>(), It.IsAny<string>()))
               .ReturnsAsync(BackupOutcome.Failed);
        var service = Enabled();

        (await service.RunNowAsync()).Should().Be(ScheduledBackupOutcome.Failed);
        _folder.Files.Should().BeEmpty();
        _store.Settings.LastRunUtc.Should().BeNull();
        _store.Settings.LastOutcome.Should().Be(ScheduledBackupOutcome.Failed);
    }

    [Fact]
    public async Task A_folder_that_throws_on_write_is_a_failure_not_a_crash()
    {
        _folder.ThrowOnWrite = true;
        var service = Enabled();

        (await service.RunNowAsync()).Should().Be(ScheduledBackupOutcome.Failed);
        Directory.EnumerateFiles(_cache).Should().BeEmpty();
    }

    // ---- pruning ----

    [Fact]
    public async Task Only_the_newest_five_of_our_own_files_stay_and_other_files_are_never_touched()
    {
        var service = Enabled();
        foreach (var day in Enumerable.Range(1, 6))
        {
            _folder.Files[ScheduledBackupService.FileNameFor(new DateTime(2026, 9, day, 8, 0, 0))] = new byte[] { 9 };
        }
        _folder.Files["holiday.jpg"] = new byte[] { 7 };
        _folder.Files["DiarionBackup_notes.txt"] = new byte[] { 7 };

        await service.RunNowAsync();

        var ours = _folder.Files.Keys.Where(ScheduledBackupService.IsOurs).OrderBy(n => n, StringComparer.Ordinal).ToList();
        ours.Should().HaveCount(ScheduledBackupSettings.DefaultKeepCount);
        ours.Should().NotContain(ScheduledBackupService.FileNameFor(new DateTime(2026, 9, 1, 8, 0, 0)));
        ours.Should().NotContain(ScheduledBackupService.FileNameFor(new DateTime(2026, 9, 2, 8, 0, 0)));
        ours.Should().Contain(ScheduledBackupService.FileNameFor(Now.ToLocalTime()));
        _folder.Files.Should().ContainKey("holiday.jpg");
        _folder.Files.Should().ContainKey("DiarionBackup_notes.txt", "the prefix alone does not make a file ours");
    }

    [Fact]
    public void Our_own_files_are_recognised_by_prefix_and_extension_together()
    {
        ScheduledBackupService.IsOurs("DiarionBackup_20261014_093000.diarionbackup").Should().BeTrue();
        ScheduledBackupService.IsOurs("DiarionBackup_20261014_093000.DIARIONBACKUP").Should().BeTrue();
        ScheduledBackupService.IsOurs("DiarionBackup_20261014_093000.db").Should().BeFalse();
        ScheduledBackupService.IsOurs("Other_20261014.diarionbackup").Should().BeFalse();
    }

    // ---- settings ----

    [Fact]
    public void Enable_stores_the_passphrase_in_the_secure_half_and_the_rest_in_the_plain_half()
    {
        var service = Create();

        service.Enable(new BackupFolderPick("tree://x", "iCloud Drive/Diarion"), "pass", 14);

        _store.Passphrase.Should().Be("pass");
        _store.Settings.IsEnabled.Should().BeTrue();
        _store.Settings.FolderToken.Should().Be("tree://x");
        _store.Settings.FolderName.Should().Be("iCloud Drive/Diarion");
        _store.Settings.IntervalDays.Should().Be(14);
        service.Settings.Should().BeSameAs(_store.Settings);
    }

    [Fact]
    public void Disable_forgets_the_passphrase_but_keeps_the_folder()
    {
        var service = Enabled();

        service.Disable();

        _store.Passphrase.Should().BeNull("a secret with nothing to protect should not linger");
        _store.Settings.IsEnabled.Should().BeFalse();
        _store.Settings.FolderName.Should().Be("Diarion");
        service.IsDue(Now).Should().BeFalse();
    }

    [Fact]
    public void Enable_refuses_an_empty_passphrase()
    {
        var service = Create();

        var act = () => service.Enable(new BackupFolderPick("t", "n"), "  ", 7);

        act.Should().Throw<ArgumentException>();
        _store.Settings.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void A_nonsense_interval_falls_back_to_the_default()
    {
        var service = Enabled();

        service.SetInterval(0);

        _store.Settings.IntervalDays.Should().Be(ScheduledBackupSettings.DefaultIntervalDays);
    }

    [Fact]
    public void Settings_come_from_the_store_on_construction()
    {
        _store.Settings.IsEnabled = true;
        _store.Settings.FolderToken = "tree://saved";
        _store.Settings.IntervalDays = 3;
        _store.Settings.LastRunUtc = Now.AddDays(-1);

        var service = Create();

        service.Settings.IntervalDays.Should().Be(3);
        service.IsDue(Now).Should().BeFalse();
        service.IsDue(Now.AddDays(2)).Should().BeTrue();
    }

    [Fact]
    public void Unsupported_platform_shows_through()
    {
        var unsupported = new ScheduledBackupService(
            _backup.Object, new NullBackupFolderService(), _store, new FakeFileSystem(_cache), _clock);

        unsupported.IsSupported.Should().BeFalse();
        Create().IsSupported.Should().BeTrue();
    }

    // ---- fakes ----

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTime utc) => _now = new DateTimeOffset(utc, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FakeFileSystem : IFileSystemService
    {
        public FakeFileSystem(string cache) => CacheDirectory = cache;
        public string CacheDirectory { get; }
        public string AppDataDirectory => CacheDirectory;
    }

    private sealed class FakeStore : IScheduledBackupStore
    {
        public ScheduledBackupSettings Settings { get; } = new();
        public string? Passphrase { get; private set; }
        public int SaveCount { get; private set; }

        public ScheduledBackupSettings Load() => Settings;

        public void Save(ScheduledBackupSettings settings)
        {
            settings.Should().BeSameAs(Settings);
            SaveCount++;
        }

        public string? GetPassphrase() => Passphrase;

        public void SetPassphrase(string? passphrase) => Passphrase = passphrase;
    }

    private sealed class FakeFolder : IBackupFolderService
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
        public bool Writable { get; set; } = true;
        public bool ThrowOnWrite { get; set; }

        public bool IsSupported => true;

        public Task<BackupFolderPick?> PickFolderAsync() =>
            Task.FromResult<BackupFolderPick?>(new BackupFolderPick("tree://picked", "Picked"));

        public Task<bool> CanWriteAsync(string token) => Task.FromResult(Writable);

        public Task WriteFileAsync(string token, string fileName, string sourcePath)
        {
            if (ThrowOnWrite) throw new IOException("disk full");
            Files[fileName] = File.ReadAllBytes(sourcePath);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListFileNamesAsync(string token) =>
            Task.FromResult<IReadOnlyList<string>>(Files.Keys.ToList());

        public Task DeleteFileAsync(string token, string fileName)
        {
            Files.Remove(fileName);
            return Task.CompletedTask;
        }
    }
}
