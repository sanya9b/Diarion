using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Database;
using Diarion.Services.Recovery;
using FluentAssertions;
using LiteDB;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// Spec 21: nothing leaves the phone before consent; a copy is the live database under the device
/// key; a restore swaps it in only when the key opens it; the cadence is daily at launch and hourly
/// on backgrounding when something changed. The store is a folder on disk standing in for iCloud.
/// </summary>
public class CloudRecoveryServiceTests : IDisposable
{
    private const string Key = "device-key";
    private readonly string _dir;
    private readonly FakeTimeProvider _clock = new(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc));

    public CloudRecoveryServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "diarion_cloud_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    // ---- fakes -------------------------------------------------------------

    private sealed class FixedKey : IEncryptionKeyProvider
    {
        private readonly string _key;
        public FixedKey(string key) => _key = key;
        public string GetOrCreateKey() => _key;
    }

    private sealed class FakeFileSystem : IFileSystemService
    {
        public FakeFileSystem(string dir) => CacheDirectory = dir;
        public string CacheDirectory { get; }
        public string AppDataDirectory => CacheDirectory;
    }

    private sealed class MemorySettingsStore : ICloudRecoverySettingsStore
    {
        public CloudRecoverySettings Stored { get; set; } = new();
        public int Saves { get; private set; }
        public CloudRecoverySettings Load() => Stored;
        public void Save(CloudRecoverySettings settings)
        {
            Stored = settings;
            Saves++;
        }
    }

    /// <summary>A folder standing in for the iCloud container, with a switch for "signed out".</summary>
    private sealed class FolderStore : IRecoveryStore
    {
        private readonly string _file;
        public FolderStore(string dir) => _file = Path.Combine(dir, "cloud", "diarion-recovery.db");
        public bool IsSupported { get; set; } = true;
        public bool Available { get; set; } = true;
        public int Writes { get; private set; }
        public bool Exists => File.Exists(_file);

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(Available);

        public Task<RecoveryCopyInfo?> FindAsync(TimeSpan searchTimeout, CancellationToken ct = default) =>
            Task.FromResult(Exists ? new RecoveryCopyInfo(File.GetLastWriteTimeUtc(_file), new FileInfo(_file).Length, true) : null);

        public Task<string?> DownloadAsync(TimeSpan timeout, IProgress<double>? progress, CancellationToken ct = default)
        {
            progress?.Report(1);
            return Task.FromResult(Exists ? _file : null);
        }

        public Task WriteAsync(string sourcePath, CancellationToken ct = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.Copy(sourcePath, _file, overwrite: true);
            Writes++;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(CancellationToken ct = default)
        {
            if (Exists) File.Delete(_file);
            return Task.CompletedTask;
        }

        /// <summary>Plants a copy as another phone would have left it.</summary>
        public void Plant(string password, params string[] contents)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            using var db = new LiteDatabase(new ConnectionString { Filename = _file, Password = password });
            var entries = db.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);
            foreach (var content in contents)
                entries.Insert(new DiaryEntry { Id = Guid.NewGuid(), Date = DateTime.Today, Content = content });
            db.UserVersion = MigrationRunner.CurrentVersion;
            db.Checkpoint();
        }
    }

    // ---- helpers -----------------------------------------------------------

    private DatabaseContext NewDatabase(params string[] contents)
    {
        var context = new DatabaseContext(Path.Combine(_dir, $"db_{Guid.NewGuid():N}.db"), keyProvider: new FixedKey(Key));
        foreach (var content in contents)
        {
            context.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection)
                .Insert(new DiaryEntry { Id = Guid.NewGuid(), Date = DateTime.Today, Content = content });
        }
        return context;
    }

    private static string[] Contents(IDatabaseContext context) =>
        context.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection).FindAll().Select(e => e.Content).OrderBy(c => c).ToArray();

    private (CloudRecoveryService Service, FolderStore Store, MemorySettingsStore Settings) Build(
        DatabaseContext database, CloudRecoverySettings? settings = null, string key = Key)
    {
        var store = new FolderStore(_dir);
        var settingsStore = new MemorySettingsStore { Stored = settings ?? new CloudRecoverySettings() };
        var service = new CloudRecoveryService(store, settingsStore, database, new FixedKey(key), new FakeFileSystem(_dir), _clock);
        return (service, store, settingsStore);
    }

    private static CloudRecoverySettings Enabled(DateTime? lastCopy = null) =>
        new() { Decision = CloudRecoveryDecision.Enabled, LastCopyUtc = lastCopy };

    // ---- consent -----------------------------------------------------------

    [Fact]
    public void NeedsDecision_UntilAnswered_OnASupportedPlatform()
    {
        using var db = NewDatabase();
        var (service, store, _) = Build(db);

        service.NeedsDecision.Should().BeTrue();

        service.Decline();
        service.NeedsDecision.Should().BeFalse();
        service.Settings.Decision.Should().Be(CloudRecoveryDecision.Disabled);

        store.IsSupported = false;
        new CloudRecoveryService(store, new MemorySettingsStore(), db, new FixedKey(Key), new FakeFileSystem(_dir), _clock)
            .NeedsDecision.Should().BeFalse("nothing can be asked where there is no container");
    }

    [Fact]
    public async Task NothingIsWritten_BeforeConsent()
    {
        using var db = NewDatabase("запис");
        var (service, store, _) = Build(db);

        (await service.CopyNowAsync()).Should().Be(CloudRecoveryOutcome.Disabled);
        (await service.RunIfDueAsync()).Should().Be(CloudRecoveryOutcome.Disabled);
        service.MarkChanged();
        (await service.FlushOnBackgroundAsync()).Should().Be(CloudRecoveryOutcome.Disabled);

        store.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Enable_RemembersTheAnswer_AndMakesTheFirstCopyAtOnce()
    {
        using var db = NewDatabase("перший", "другий");
        var (service, store, settings) = Build(db);

        var outcome = await service.EnableAsync();

        outcome.Should().Be(CloudRecoveryOutcome.Success);
        settings.Stored.Decision.Should().Be(CloudRecoveryDecision.Enabled);
        settings.Stored.LastCopyUtc.Should().Be(_clock.GetUtcNow().UtcDateTime);
        store.Writes.Should().Be(1);

        // The copy is the live database under the device key — and the live one never closed.
        Contents(db).Should().Equal("другий", "перший");
        using var copy = new LiteDatabase(new ConnectionString { Filename = Path.Combine(_dir, "cloud", "diarion-recovery.db"), Password = Key });
        copy.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection).FindAll().Select(e => e.Content)
            .Should().BeEquivalentTo("перший", "другий");
        copy.UserVersion.Should().Be(MigrationRunner.CurrentVersion);
    }

    [Fact]
    public async Task Copy_WhenICloudIsSignedOut_ReportsUnavailable_AndKeepsTheLastDate()
    {
        var last = _clock.GetUtcNow().UtcDateTime.AddDays(-3);
        using var db = NewDatabase("запис");
        var (service, store, settings) = Build(db, Enabled(last));
        store.Available = false;

        (await service.CopyNowAsync()).Should().Be(CloudRecoveryOutcome.Unavailable);

        settings.Stored.LastOutcome.Should().Be(CloudRecoveryOutcome.Unavailable);
        settings.Stored.LastCopyUtc.Should().Be(last, "a failure must not look like a success to the next launch");
        store.Writes.Should().Be(0);
    }

    // ---- cadence -----------------------------------------------------------

    [Fact]
    public async Task RunIfDue_CopiesOncePerDay()
    {
        using var db = NewDatabase("запис");
        var (service, store, _) = Build(db, Enabled());

        (await service.RunIfDueAsync()).Should().Be(CloudRecoveryOutcome.Success, "never copied before");
        _clock.Advance(TimeSpan.FromHours(5));
        (await service.RunIfDueAsync()).Should().Be(CloudRecoveryOutcome.NotDue);
        _clock.Advance(TimeSpan.FromHours(20));
        (await service.RunIfDueAsync()).Should().Be(CloudRecoveryOutcome.Success);

        store.Writes.Should().Be(2);
    }

    [Fact]
    public async Task FlushOnBackground_CopiesOnlyWhenSomethingChanged_AndNotMoreThanHourly()
    {
        using var db = NewDatabase("запис");
        var (service, store, _) = Build(db, Enabled());

        (await service.FlushOnBackgroundAsync()).Should().Be(CloudRecoveryOutcome.NotDue, "nothing changed");

        service.MarkChanged();
        (await service.FlushOnBackgroundAsync()).Should().Be(CloudRecoveryOutcome.Success);

        service.MarkChanged();
        _clock.Advance(TimeSpan.FromMinutes(20));
        (await service.FlushOnBackgroundAsync()).Should().Be(CloudRecoveryOutcome.NotDue, "the hour has not passed");

        _clock.Advance(TimeSpan.FromMinutes(41));
        (await service.FlushOnBackgroundAsync()).Should().Be(CloudRecoveryOutcome.Success);
        (await service.FlushOnBackgroundAsync()).Should().Be(CloudRecoveryOutcome.NotDue, "the copy cleared the change");

        store.Writes.Should().Be(2);
    }

    // ---- disable / delete --------------------------------------------------

    [Fact]
    public async Task Disable_WithDelete_RemovesTheCopy_AndForgetsTheDate()
    {
        using var db = NewDatabase("запис");
        var (service, store, settings) = Build(db);
        await service.EnableAsync();
        store.Exists.Should().BeTrue();

        await service.DisableAsync(deleteCopy: true);

        store.Exists.Should().BeFalse();
        settings.Stored.Decision.Should().Be(CloudRecoveryDecision.Disabled);
        settings.Stored.LastCopyUtc.Should().BeNull();
        (await service.CopyNowAsync()).Should().Be(CloudRecoveryOutcome.Disabled);
    }

    [Fact]
    public async Task Disable_WithoutDelete_LeavesTheCopyInTheCloud()
    {
        using var db = NewDatabase("запис");
        var (service, store, _) = Build(db);
        await service.EnableAsync();

        await service.DisableAsync(deleteCopy: false);

        store.Exists.Should().BeTrue();
    }

    // ---- restore -----------------------------------------------------------

    [Fact]
    public async Task Restore_SwapsTheCopyIn_AndTurnsTheFeatureOn()
    {
        using var db = NewDatabase("свіже");
        var (service, store, settings) = Build(db);
        store.Plant(Key, "з хмари 1", "з хмари 2");

        var progress = new List<double>();
        var outcome = await service.RestoreAsync(new SynchronousProgress(progress));

        outcome.Should().Be(RecoveryRestoreOutcome.Restored);
        Contents(db).Should().Equal("з хмари 1", "з хмари 2");
        settings.Stored.Decision.Should().Be(CloudRecoveryDecision.Enabled, "the copy the user restored from is the one they want kept");
        progress.Should().Contain(1);
    }

    [Fact]
    public async Task Restore_CopyUnderAnotherKey_IsRefused_AndTheDatabaseIsUntouched()
    {
        using var db = NewDatabase("своє");
        var (service, store, settings) = Build(db);
        store.Plant("some-other-phone", "чуже");

        (await service.RestoreAsync()).Should().Be(RecoveryRestoreOutcome.KeyMismatch);

        Contents(db).Should().Equal("своє");
        settings.Stored.Decision.Should().Be(CloudRecoveryDecision.Undecided);
    }

    [Fact]
    public async Task Restore_NewerSchema_IsRefused()
    {
        using var db = NewDatabase("своє");
        var (service, store, _) = Build(db);
        store.Plant(Key, "з майбутнього");
        using (var future = new LiteDatabase(new ConnectionString { Filename = Path.Combine(_dir, "cloud", "diarion-recovery.db"), Password = Key }))
        {
            future.UserVersion = MigrationRunner.CurrentVersion + 1;
            future.Checkpoint();
        }

        (await service.RestoreAsync()).Should().Be(RecoveryRestoreOutcome.NewerSchema);
        Contents(db).Should().Equal("своє");
    }

    [Fact]
    public async Task Restore_WithoutACopy_SaysSo()
    {
        using var db = NewDatabase("своє");
        var (service, _, _) = Build(db);

        (await service.RestoreAsync()).Should().Be(RecoveryRestoreOutcome.NotFound);
        (await service.FindCopyAsync(TimeSpan.Zero)).Should().BeNull();
    }

    [Fact]
    public async Task Restore_OnAPlatformWithoutAContainer_IsNotFound()
    {
        using var db = NewDatabase("своє");
        var service = new CloudRecoveryService(new NullRecoveryStore(), new MemorySettingsStore(), db, new FixedKey(Key), new FakeFileSystem(_dir), _clock);

        service.IsSupported.Should().BeFalse();
        (await service.IsAvailableAsync()).Should().BeFalse();
        (await service.RestoreAsync()).Should().Be(RecoveryRestoreOutcome.NotFound);
        (await service.EnableAsync()).Should().Be(CloudRecoveryOutcome.Unavailable);
    }

    /// <summary>Progress&lt;T&gt; posts to a synchronisation context; the tests want the values now.</summary>
    private sealed class SynchronousProgress : IProgress<double>
    {
        private readonly List<double> _values;
        public SynchronousProgress(List<double> values) => _values = values;
        public void Report(double value) => _values.Add(value);
    }
}
