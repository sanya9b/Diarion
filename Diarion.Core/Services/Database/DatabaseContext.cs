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
    private readonly object _lock = new();
    private readonly IEncryptionKeyProvider? _keyProvider;
    private readonly IDatabaseSeeder? _seeder;
    private readonly bool _useInMemory;
    private LiteDatabase? _db;
    private string _dbPath = string.Empty;

    public string DatabasePath => _dbPath;

    public DatabaseContext(IDatabaseSeeder? seeder = null, IEncryptionKeyProvider? keyProvider = null, bool useInMemory = false)
    {
        _keyProvider = keyProvider;
        _seeder = seeder;
        _useInMemory = useInMemory;
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
            _dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DbFileName);
            // Encrypt the database at rest using a device-keychain-backed key. On the first run
            // after upgrading, a pre-existing plaintext file is migrated transparently and safely.
            var password = _keyProvider?.GetOrCreateKey();
            _db = EncryptedLiteDatabaseFactory.Open(_dbPath, password);
        }
        
        var entriesCollection = _db.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);
        var todosCollection = _db.GetCollection<TodoItem>(DatabaseConstants.TodosCollection);
        var habitsCollection = _db.GetCollection<HabitDefinition>(DatabaseConstants.HabitDefinitionsCollection);
        var harmfulHabitTrackersCollection = _db.GetCollection<HarmfulHabitTracker>(DatabaseConstants.HarmfulHabitTrackersCollection);
        var readingTrackerBooksCollection = _db.GetCollection<ReadingTrackerBook>(DatabaseConstants.ReadingTrackerBooksCollection);
        var happyMomentsCollection = _db.GetCollection<HappyMoment>(DatabaseConstants.HappyMomentsCollection);
        var goodDeedsCollection = _db.GetCollection<GoodDeed>(DatabaseConstants.GoodDeedsCollection);
        var profileCollection = _db.GetCollection<UserProfile>(DatabaseConstants.ProfileCollection);
        var wishlistCollection = _db.GetCollection<WishlistEntry>(DatabaseConstants.WishlistCollection);
        var financeCollection = _db.GetCollection<FinanceTransaction>(DatabaseConstants.FinanceCollection);
        var transfersCollection = _db.GetCollection<Transfer>(DatabaseConstants.TransfersCollection);
        // No index: a few dozen rows, read once per day-load, and every write would have to maintain it.
        _db.GetCollection<GuidedPrompt>(DatabaseConstants.GuidedPromptsCollection);
        // No index either: a handful of rules read once per finance-page load, and a Date index would be
        // meaningless on a rule, which has a recurrence rather than a date.
        _db.GetCollection<RecurringTransaction>(DatabaseConstants.RecurringTransactionsCollection);
        // Same again for repeating tasks: a handful of rules, read once per day-load, no date to index.
        _db.GetCollection<RecurringTask>(DatabaseConstants.RecurringTasksCollection);

        entriesCollection.EnsureIndex(x => x.Date);
        wishlistCollection.EnsureIndex(x => x.Date);
        financeCollection.EnsureIndex(x => x.Date);
        transfersCollection.EnsureIndex(x => x.Date);
        todosCollection.EnsureIndex(x => x.TargetDate);
        harmfulHabitTrackersCollection.EnsureIndex(x => x.StartDate);
        readingTrackerBooksCollection.EnsureIndex(x => x.SlotNumber, true);
        happyMomentsCollection.EnsureIndex(x => x.SlotNumber, true);
        goodDeedsCollection.EnsureIndex(x => x.SlotNumber, true);

        // Apply any pending schema migrations before seeding/using the data.
        MigrationRunner.Run(_db);

        seeder?.Seed(_db);
    }

    public ILiteCollection<T> GetCollection<T>(string name)
    {
        if (_db == null)
            throw new InvalidOperationException("Database is not initialized or has been closed.");
            
        return _db.GetCollection<T>(name);
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
}