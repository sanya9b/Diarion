using System;
using System.IO;
using System.Linq;
using Diarion.Diagnostics;
using Diarion.Models;
using LiteDB;

namespace Diarion.Services.Database;

public class DatabaseContext : IDatabaseContext, IDisposable
{
    private const string DbFileName = "diarion_local.db";

    /// <summary>
    /// The copy taken before a schema migration, named after the schema it holds. It exists for the
    /// seconds the migration runs and is deleted on success; a migration that throws puts it back.
    /// </summary>
    public static string SafetyCopyPath(string databasePath, int fromVersion) => $"{databasePath}.v{fromVersion}.bak";

    private readonly object _lock = new();
    private readonly IEncryptionKeyProvider? _keyProvider;
    private readonly IDatabaseSeeder? _seeder;
    private readonly bool _useInMemory;
    private readonly string? _explicitPath;
    private readonly Action<LiteDatabase> _migrate;
    private LiteDatabase? _db;
    private string _dbPath = string.Empty;

    public string DatabasePath => _dbPath;

    public Exception? MigrationFailure { get; private set; }

    public DatabaseContext(IDatabaseSeeder? seeder = null, IEncryptionKeyProvider? keyProvider = null, bool useInMemory = false)
        : this(seeder, keyProvider, useInMemory, databasePath: null, migrate: null)
    {
    }

    /// <summary>
    /// Opens the database at a given path instead of the platform's application-data folder. For the
    /// upgrade tests, which open a file written by an older build; <paramref name="migrate"/> lets
    /// them stand in a migration that fails.
    /// </summary>
    public DatabaseContext(string databasePath, IDatabaseSeeder? seeder = null, IEncryptionKeyProvider? keyProvider = null, Action<LiteDatabase>? migrate = null)
        : this(seeder, keyProvider, useInMemory: false, databasePath, migrate)
    {
    }

    private DatabaseContext(IDatabaseSeeder? seeder, IEncryptionKeyProvider? keyProvider, bool useInMemory, string? databasePath, Action<LiteDatabase>? migrate)
    {
        _keyProvider = keyProvider;
        _seeder = seeder;
        _useInMemory = useInMemory;
        _explicitPath = databasePath;
        _migrate = migrate ?? MigrationRunner.Run;
        Initialize(seeder, useInMemory);
    }

    private void Initialize(IDatabaseSeeder? seeder, bool useInMemory)
    {
        using var _ = StartupTrace.Measure("DatabaseContext.Initialize");

        if (useInMemory)
        {
            _db = new LiteDatabase(new MemoryStream());
        }
        else
        {
            _dbPath = _explicitPath ?? DefaultDatabasePath();
            // Encrypt the database at rest using a device-keychain-backed key. On the first run
            // after upgrading, a pre-existing plaintext file is migrated transparently and safely.
            var password = _keyProvider?.GetOrCreateKey();
            _db = EncryptedLiteDatabaseFactory.Open(_dbPath, password);
        }

        EnsureIndexes(_db);

        // Apply any pending schema migrations before seeding/using the data.
        if (!MigrateWithSafetyCopy(useInMemory))
        {
            return;
        }

        seeder?.Seed(_db!);
    }

    private static string DefaultDatabasePath()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
#if DEBUG
        // A sandbox run (Services/DebugSandbox.cs in the app) keeps the real diary closed: the database
        // goes to the throwaway folder instead. Debug builds only, like the mock seeder.
        var sandbox = Environment.GetEnvironmentVariable("DIARION_SANDBOX_DIR");
        if (!string.IsNullOrWhiteSpace(sandbox))
        {
            Directory.CreateDirectory(sandbox);
            folder = sandbox;
        }
#endif
        return Path.Combine(folder, DbFileName);
    }

    private static void EnsureIndexes(LiteDatabase db)
    {
        var entriesCollection = db.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);
        var todosCollection = db.GetCollection<TodoItem>(DatabaseConstants.TodosCollection);
        db.GetCollection<HabitDefinition>(DatabaseConstants.HabitDefinitionsCollection);
        var harmfulHabitTrackersCollection = db.GetCollection<HarmfulHabitTracker>(DatabaseConstants.HarmfulHabitTrackersCollection);
        var readingTrackerBooksCollection = db.GetCollection<ReadingTrackerBook>(DatabaseConstants.ReadingTrackerBooksCollection);
        var happyMomentsCollection = db.GetCollection<HappyMoment>(DatabaseConstants.HappyMomentsCollection);
        var goodDeedsCollection = db.GetCollection<GoodDeed>(DatabaseConstants.GoodDeedsCollection);
        db.GetCollection<UserProfile>(DatabaseConstants.ProfileCollection);
        var wishlistCollection = db.GetCollection<WishlistEntry>(DatabaseConstants.WishlistCollection);
        var financeCollection = db.GetCollection<FinanceTransaction>(DatabaseConstants.FinanceCollection);
        var transfersCollection = db.GetCollection<Transfer>(DatabaseConstants.TransfersCollection);
        // No index: a few dozen rows, read once per day-load, and every write would have to maintain it.
        db.GetCollection<GuidedPrompt>(DatabaseConstants.GuidedPromptsCollection);
        // No index either: a handful of rules read once per finance-page load, and a Date index would be
        // meaningless on a rule, which has a recurrence rather than a date.
        db.GetCollection<RecurringTransaction>(DatabaseConstants.RecurringTransactionsCollection);
        // Same again for repeating tasks: a handful of rules, read once per day-load, no date to index.
        db.GetCollection<RecurringTask>(DatabaseConstants.RecurringTasksCollection);

        entriesCollection.EnsureIndex(x => x.Date);
        wishlistCollection.EnsureIndex(x => x.Date);
        financeCollection.EnsureIndex(x => x.Date);
        transfersCollection.EnsureIndex(x => x.Date);
        todosCollection.EnsureIndex(x => x.TargetDate);
        harmfulHabitTrackersCollection.EnsureIndex(x => x.StartDate);
        readingTrackerBooksCollection.EnsureIndex(x => x.SlotNumber, true);
        happyMomentsCollection.EnsureIndex(x => x.SlotNumber, true);
        goodDeedsCollection.EnsureIndex(x => x.SlotNumber, true);
    }

    /// <summary>
    /// Runs the migrations with the file copied aside first. A migration that throws used to throw out
    /// of this constructor, which on a phone is an app that no longer opens, with the diary half
    /// migrated inside it. Now the copy goes back, the database reopens on the schema it had, and
    /// <see cref="MigrationFailure"/> carries the exception for the crash report. Returns false when
    /// that happened, so the caller leaves the old schema alone.
    /// </summary>
    private bool MigrateWithSafetyCopy(bool useInMemory)
    {
        MigrationFailure = null;
        var db = _db!;
        var from = db.UserVersion;

        if (useInMemory || from >= MigrationRunner.CurrentVersion)
        {
            _migrate(db);
            return true;
        }

        var safetyCopy = SafetyCopyPath(_dbPath, from);
        var copied = TryCopyAside(db, _dbPath, safetyCopy);

        try
        {
            _migrate(db);
            if (copied) SafeDelete(safetyCopy);
            return true;
        }
        catch (Exception ex) when (copied)
        {
            MigrationFailure = ex;
            db.Dispose();
            _db = null;

            File.Copy(safetyCopy, _dbPath, overwrite: true);
            // The log holds pages the failed migration wrote; replaying them over the restored file
            // would redo exactly what is being undone.
            SafeDelete(LogFilePath(_dbPath));
            SafeDelete(safetyCopy);

            _db = EncryptedLiteDatabaseFactory.Open(_dbPath, _keyProvider?.GetOrCreateKey());
            EnsureIndexes(_db);
            return false;
        }
    }

    private static bool TryCopyAside(LiteDatabase db, string databasePath, string safetyCopy)
    {
        try
        {
            // The data file alone is not the database until the log is folded into it.
            db.Checkpoint();
            File.Copy(databasePath, safetyCopy, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            // No copy, no rollback: the migration still runs, as it always has. Refusing to open the
            // diary because the disk is too full for a second copy would be the worse outcome.
            System.Diagnostics.Debug.WriteLine($"Pre-migration copy failed: {ex.Message}");
            return false;
        }
    }

    public ILiteCollection<T> GetCollection<T>(string name)
    {
        lock (_lock)
        {
            if (_db == null)
                throw new InvalidOperationException("Database is not initialized or has been closed.");

            return _db.GetCollection<T>(name);
        }
    }

    public void ExportTo(string destinationPath, string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("An exported copy must be encrypted.", nameof(password));
        }

        LiteDatabase source;
        lock (_lock)
        {
            source = _db ?? throw new InvalidOperationException("Database is not initialized or has been closed.");
        }

        // Outside the lock on purpose: the copy is seconds on a large diary, and a reader that arrives
        // meanwhile should read, not wait. LiteDB serialises the two on its own.
        using var destination = new LiteDatabase(new ConnectionString { Filename = destinationPath, Password = password });
        EncryptedLiteDatabaseFactory.CopyAllCollections(source, destination);
    }

    public void ReplaceDatabaseFile(string candidatePath)
    {
        if (_useInMemory)
        {
            throw new InvalidOperationException("An in-memory database has no file to replace.");
        }

        var rollbackPath = _dbPath + ".importbak";

        lock (_lock)
        {
            _db?.Dispose();
            _db = null;
            SafeDelete(rollbackPath);

            try
            {
                if (File.Exists(_dbPath))
                {
                    File.Replace(candidatePath, _dbPath, rollbackPath);
                }
                else
                {
                    File.Move(candidatePath, _dbPath);
                }
                // A log left by the previous file would be applied to the new one.
                SafeDelete(LogFilePath(_dbPath));

                Initialize(_seeder, _useInMemory);
            }
            catch
            {
                // Whatever went wrong, the user leaves with the database they came with.
                _db?.Dispose();
                _db = null;
                if (File.Exists(rollbackPath))
                {
                    File.Copy(rollbackPath, _dbPath, overwrite: true);
                    SafeDelete(LogFilePath(_dbPath));
                }
                Initialize(_seeder, _useInMemory);
                throw;
            }
            finally
            {
                SafeDelete(rollbackPath);
            }
        }
    }

    public void DropAllData()
    {
        lock (_lock)
        {
            if (_db == null) return;
            var collections = _db.GetCollectionNames().ToList();
            foreach (var colName in collections)
            {
                _db.DropCollection(colName);
            }
        }
    }

    public void Close()
    {
        lock (_lock)
        {
            _db?.Dispose();
            _db = null;
        }
    }

    public void Reopen()
    {
        lock (_lock)
        {
            _db?.Dispose();
            _db = null;
            Initialize(_seeder, _useInMemory);
        }
    }

    public void Dispose()
    {
        Close();
    }

    /// <summary>LiteDB's write-ahead log sits next to the data file as <c>name-log.db</c>.</summary>
    public static string LogFilePath(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(databasePath);
        var extension = Path.GetExtension(databasePath);
        return Path.Combine(directory, $"{name}-log{extension}");
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // A leftover file is clutter, not a failure.
        }
    }
}

