using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Helpers;
using Diarion.Services;
using Diarion.Services.Database;
using FluentAssertions;
using LiteDB;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// Covers the defect these backups exist to fix: before this, a backup was a plain copy of a database
/// encrypted with a key held only in the exporting device's keystore, so restoring it on a new phone
/// was impossible — and failed silently. Every test that mentions "new device" is that scenario.
/// </summary>
public class PortableBackupTests : IDisposable
{
    private readonly string _dir;

    public PortableBackupTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "diarion_backup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    // ---- fakes -------------------------------------------------------------

    private sealed class FakeKeyProvider : IEncryptionKeyProvider
    {
        private readonly string _key;
        public FakeKeyProvider(string key) => _key = key;
        public string GetOrCreateKey() => _key;
    }

    private sealed class FakeFileSystem : IFileSystemService
    {
        public FakeFileSystem(string cacheDirectory) => CacheDirectory = cacheDirectory;
        public string CacheDirectory { get; }
        public string AppDataDirectory => CacheDirectory;
    }

    private sealed class FakeShareService : IShareService
    {
        public string? SharedPath { get; private set; }
        public Task ShareFileAsync(string title, string filePath)
        {
            SharedPath = filePath;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFilePicker : IFilePickerService
    {
        private readonly string? _path;
        public FakeFilePicker(string? path) => _path = path;
        public Task<Stream?> PickBackupFileAsync(string title)
            => Task.FromResult<Stream?>(_path == null ? null : File.OpenRead(_path));
    }

    /// <summary>
    /// Opens and closes a real LiteDB file, which is what the service coordinates around. A fake that
    /// only tracked call counts would not catch a file still being locked when it is copied.
    /// </summary>
    private sealed class FakeDatabaseContext : IDatabaseContext
    {
        private readonly string _password;
        private LiteDatabase? _db;

        public FakeDatabaseContext(string path, string password)
        {
            DatabasePath = path;
            _password = password;
            Reopen();
        }

        public string DatabasePath { get; }

        public ILiteCollection<T> GetCollection<T>(string name)
            => (_db ?? throw new InvalidOperationException("closed")).GetCollection<T>(name);

        public void Close()
        {
            _db?.Dispose();
            _db = null;
        }

        public void Reopen()
        {
            _db ??= new LiteDatabase(new ConnectionString { Filename = DatabasePath, Password = _password });
        }

        public void DropAllData() => throw new NotSupportedException();
    }

    // ---- helpers -----------------------------------------------------------

    private string NewDatabase(string password, string marker, int userVersion = 0)
    {
        var path = Path.Combine(_dir, $"db_{Guid.NewGuid():N}.db");
        using var db = new LiteDatabase(new ConnectionString { Filename = path, Password = password });
        db.GetCollection<BsonDocument>(DatabaseConstants.EntriesCollection)
          .Insert(new BsonDocument { ["_id"] = ObjectId.NewObjectId(), ["marker"] = marker });
        db.UserVersion = userVersion;
        db.Checkpoint();
        return path;
    }

    private static string ReadMarker(string path, string password)
    {
        using var db = new LiteDatabase(new ConnectionString { Filename = path, Password = password });
        return db.GetCollection<BsonDocument>(DatabaseConstants.EntriesCollection)
                 .FindAll().Single()["marker"].AsString;
    }

    private (BackupService Service, FakeShareService Share, FakeDatabaseContext Context) BuildService(
        string dbPath, string deviceKey, string? pickedFile = null)
    {
        var context = new FakeDatabaseContext(dbPath, deviceKey);
        var share = new FakeShareService();
        var service = new BackupService(
            context,
            new FakeKeyProvider(deviceKey),
            new FakeFileSystem(_dir),
            share,
            new FakeFilePicker(pickedFile));
        return (service, share, context);
    }

    private static Func<Task<string?>> Passphrase(string? value) => () => Task.FromResult(value);

    // ---- the headline scenario --------------------------------------------

    [Fact]
    public async Task Backup_exported_on_one_device_restores_on_a_different_device()
    {
        const string oldDeviceKey = "b2xkLWRldmljZS1rZXktMzJieXRlcy1sb25nLXh4";
        const string newDeviceKey = "bmV3LWRldmljZS1rZXktMzJieXRlcy1sb25nLXh4";
        const string passphrase = "correct horse battery staple";

        var sourcePath = NewDatabase(oldDeviceKey, "written-on-the-old-phone");
        var (exporter, share, exportContext) = BuildService(sourcePath, oldDeviceKey);

        var exported = await exporter.ExportBackupAsync(Passphrase(passphrase));
        exported.Should().Be(BackupOutcome.Success);
        exportContext.Close();

        share.SharedPath.Should().NotBeNull();
        var backupFile = Path.Combine(_dir, "carried_over" + PortableBackupFile.FileExtension);
        File.Copy(share.SharedPath!, backupFile);

        // A brand-new phone: different keystore key, and a database that knows nothing of the old one.
        var newPhoneDb = NewDatabase(newDeviceKey, "fresh-install");
        var (importer, _, importContext) = BuildService(newPhoneDb, newDeviceKey, backupFile);

        var imported = await importer.ImportBackupAsync(Passphrase(passphrase));
        imported.Should().Be(BackupOutcome.Success);

        importContext.Close();
        ReadMarker(newPhoneDb, newDeviceKey).Should().Be("written-on-the-old-phone");
    }

    [Fact]
    public async Task Restored_database_keeps_its_schema_version()
    {
        const string oldDeviceKey = "dmVyc2lvbi1vbGQta2V5LTMyYnl0ZXMtbG9uZy0x";
        const string newDeviceKey = "dmVyc2lvbi1uZXcta2V5LTMyYnl0ZXMtbG9uZy0y";
        const string passphrase = "schema stays put";

        // Losing UserVersion would make the runner re-apply every migration to already-migrated data.
        var sourcePath = NewDatabase(oldDeviceKey, "versioned", userVersion: MigrationRunner.CurrentVersion);
        var (exporter, share, exportContext) = BuildService(sourcePath, oldDeviceKey);
        (await exporter.ExportBackupAsync(Passphrase(passphrase))).Should().Be(BackupOutcome.Success);
        exportContext.Close();

        var backupFile = Path.Combine(_dir, "versioned" + PortableBackupFile.FileExtension);
        File.Copy(share.SharedPath!, backupFile);

        var newPhoneDb = NewDatabase(newDeviceKey, "fresh", userVersion: 0);
        var (importer, _, importContext) = BuildService(newPhoneDb, newDeviceKey, backupFile);
        (await importer.ImportBackupAsync(Passphrase(passphrase))).Should().Be(BackupOutcome.Success);
        importContext.Close();

        using var restored = new LiteDatabase(new ConnectionString { Filename = newPhoneDb, Password = newDeviceKey });
        restored.UserVersion.Should().Be(MigrationRunner.CurrentVersion);
    }

    // ---- refusals ----------------------------------------------------------

    [Fact]
    public async Task Wrong_passphrase_is_reported_and_leaves_the_live_data_alone()
    {
        const string oldDeviceKey = "d3JvbmctcGFzcy1vbGQta2V5LTMyYnl0ZXMtbG4=";
        const string newDeviceKey = "d3JvbmctcGFzcy1uZXcta2V5LTMyYnl0ZXMtbG4=";

        var sourcePath = NewDatabase(oldDeviceKey, "secret");
        var (exporter, share, exportContext) = BuildService(sourcePath, oldDeviceKey);
        await exporter.ExportBackupAsync(Passphrase("the right one"));
        exportContext.Close();

        var backupFile = Path.Combine(_dir, "wrongpass" + PortableBackupFile.FileExtension);
        File.Copy(share.SharedPath!, backupFile);

        var livePath = NewDatabase(newDeviceKey, "untouched");
        var (importer, _, importContext) = BuildService(livePath, newDeviceKey, backupFile);

        var outcome = await importer.ImportBackupAsync(Passphrase("not the right one"));

        outcome.Should().Be(BackupOutcome.WrongPassphrase);
        importContext.Close();
        ReadMarker(livePath, newDeviceKey).Should().Be("untouched");
    }

    [Fact]
    public async Task Cancelling_the_passphrase_prompt_is_not_an_error()
    {
        const string deviceKey = "Y2FuY2VsLWtleS0zMmJ5dGVzLWxvbmctcGFkZGVk";
        var dbPath = NewDatabase(deviceKey, "anything");
        var (service, share, _) = BuildService(dbPath, deviceKey);

        var outcome = await service.ExportBackupAsync(Passphrase(null));

        outcome.Should().Be(BackupOutcome.Cancelled);
        share.SharedPath.Should().BeNull();
    }

    [Fact]
    public async Task A_foreign_file_is_rejected_without_touching_the_live_data()
    {
        const string deviceKey = "Zm9yZWlnbi1rZXktMzJieXRlcy1sb25nLXBhZGRl";
        var foreign = Path.Combine(_dir, "holiday-photo.jpg");
        File.WriteAllBytes(foreign, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 });

        var livePath = NewDatabase(deviceKey, "untouched");
        var (service, _, context) = BuildService(livePath, deviceKey, foreign);

        var outcome = await service.ImportBackupAsync(Passphrase("irrelevant"));

        outcome.Should().NotBe(BackupOutcome.Success);
        context.Close();
        ReadMarker(livePath, deviceKey).Should().Be("untouched");
    }

    [Fact]
    public async Task A_legacy_backup_from_this_device_still_restores()
    {
        // Existing users have plain .db copies encrypted with their device key. Those must keep working.
        const string deviceKey = "bGVnYWN5LWtleS0zMmJ5dGVzLWxvbmctcGFkZGVk";

        var legacyBackup = NewDatabase(deviceKey, "made-before-the-change");
        var livePath = NewDatabase(deviceKey, "current");
        var (service, _, context) = BuildService(livePath, deviceKey, legacyBackup);

        var outcome = await service.ImportBackupAsync(Passphrase(null));

        outcome.Should().Be(BackupOutcome.Success);
        context.Close();
        ReadMarker(livePath, deviceKey).Should().Be("made-before-the-change");
    }

    [Fact]
    public async Task A_legacy_backup_from_another_device_says_so_instead_of_failing_silently()
    {
        const string otherDeviceKey = "b3RoZXItZGV2aWNlLWtleS0zMmJ5dGVzLWxvbmc=";
        const string thisDeviceKey = "dGhpcy1kZXZpY2Uta2V5LTMyYnl0ZXMtbG9uZy0=";

        var legacyBackup = NewDatabase(otherDeviceKey, "stranded");
        var livePath = NewDatabase(thisDeviceKey, "current");
        var (service, _, context) = BuildService(livePath, thisDeviceKey, legacyBackup);

        var outcome = await service.ImportBackupAsync(Passphrase(null));

        outcome.Should().Be(BackupOutcome.LegacyBackupFromAnotherDevice);
        context.Close();
        ReadMarker(livePath, thisDeviceKey).Should().Be("current");
    }

    // ---- container and key derivation --------------------------------------

    [Fact]
    public void Container_round_trips_its_header_and_payload()
    {
        var salt = BackupKeyDeriver.NewSalt();
        var payload = new byte[] { 9, 8, 7, 6, 5 };

        using var file = new MemoryStream();
        using (var payloadStream = new MemoryStream(payload))
        {
            PortableBackupFile.Write(file, salt, 1234, payloadStream);
        }

        file.Position = 0;
        var header = PortableBackupFile.TryReadHeader(file);

        header.Should().NotBeNull();
        header!.Iterations.Should().Be(1234);
        header.Salt.Should().Equal(salt);

        using var extracted = new MemoryStream();
        PortableBackupFile.ExtractPayload(file, header, extracted);
        extracted.ToArray().Should().Equal(payload);
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3 })]                                     // too short for a header
    [InlineData(new byte[] { 68, 73, 65, 82, 73, 79, 78, 57, 0, 0, 0, 0, 0, 0, 0, 0 })] // "DIARION9"
    public void A_file_that_is_not_a_portable_backup_reads_as_no_header(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        PortableBackupFile.TryReadHeader(stream).Should().BeNull();
    }

    [Fact]
    public void Key_derivation_is_deterministic_and_separates_passphrases()
    {
        var salt = BackupKeyDeriver.NewSalt();

        var a = BackupKeyDeriver.DeriveKey("same", salt, 1000);
        var b = BackupKeyDeriver.DeriveKey("same", salt, 1000);
        var different = BackupKeyDeriver.DeriveKey("other", salt, 1000);
        var otherSalt = BackupKeyDeriver.DeriveKey("same", BackupKeyDeriver.NewSalt(), 1000);

        a.Should().Be(b);
        a.Should().NotBe(different);
        a.Should().NotBe(otherSalt, "the salt is what stops two identical passphrases sharing a key");
    }

    [Fact]
    public void Iterations_travel_with_the_file_so_raising_the_work_factor_keeps_old_backups_readable()
    {
        var salt = BackupKeyDeriver.NewSalt();
        using var file = new MemoryStream();
        using (var payload = new MemoryStream(new byte[] { 1 }))
        {
            PortableBackupFile.Write(file, salt, 50_000, payload);
        }

        file.Position = 0;
        var header = PortableBackupFile.TryReadHeader(file)!;

        header.Iterations.Should().Be(50_000).And.NotBe(BackupKeyDeriver.CurrentIterations);
        BackupKeyDeriver.DeriveKey("p", salt, header.Iterations)
            .Should().NotBe(BackupKeyDeriver.DeriveKey("p", salt, BackupKeyDeriver.CurrentIterations));
    }
}
