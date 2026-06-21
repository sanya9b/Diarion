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
    private readonly object _initializationLock = new();
    private LiteDatabase? _db;
    private readonly bool _useInMemory;
    private readonly IDatabaseSeeder? _seeder;
    private string _dbPath = string.Empty;

    public string DatabasePath => _dbPath;

    public DatabaseContext(IDatabaseSeeder? seeder = null, bool useInMemory = false)
    {
        _seeder = seeder;
        _useInMemory = useInMemory;
    }

    public ILiteCollection<T> GetCollection<T>(string name)
    {
        EnsureInitialized();
        return _db!.GetCollection<T>(name);
    }

    private void EnsureInitialized()
    {
        if (_db != null)
        {
            return;
        }

        lock (_initializationLock)
        {
            if (_db != null)
            {
                return;
            }

            using var _ = StartupTrace.Measure("DatabaseContext.EnsureInitialized");
            
            LiteDatabase database;
            if (_useInMemory)
            {
                database = new LiteDatabase(new MemoryStream());
            }
            else
            {
                _dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DbFileName);
                database = new LiteDatabase(_dbPath);
            }
            
            var entriesCollection = database.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);
            var todosCollection = database.GetCollection<TodoItem>(DatabaseConstants.TodosCollection);
            var habitsCollection = database.GetCollection<HabitDefinition>(DatabaseConstants.HabitDefinitionsCollection);
            var harmfulHabitTrackersCollection = database.GetCollection<HarmfulHabitTracker>(DatabaseConstants.HarmfulHabitTrackersCollection);
            var readingTrackerBooksCollection = database.GetCollection<ReadingTrackerBook>(DatabaseConstants.ReadingTrackerBooksCollection);
            var happyMomentsCollection = database.GetCollection<HappyMoment>(DatabaseConstants.HappyMomentsCollection);
            var goodDeedsCollection = database.GetCollection<GoodDeed>(DatabaseConstants.GoodDeedsCollection);
            var profileCollection = database.GetCollection<UserProfile>(DatabaseConstants.ProfileCollection);
            var wishlistCollection = database.GetCollection<WishlistEntry>(DatabaseConstants.WishlistCollection);
            var financeCollection = database.GetCollection<FinanceTransaction>(DatabaseConstants.FinanceCollection);

            entriesCollection.EnsureIndex(x => x.Date);
            wishlistCollection.EnsureIndex(x => x.Date);
            financeCollection.EnsureIndex(x => x.Date);
            todosCollection.EnsureIndex(x => x.TargetDate);
            harmfulHabitTrackersCollection.EnsureIndex(x => x.StartDate);
            readingTrackerBooksCollection.EnsureIndex(x => x.SlotNumber, true);
            happyMomentsCollection.EnsureIndex(x => x.SlotNumber, true);
            goodDeedsCollection.EnsureIndex(x => x.SlotNumber, true);

            if (_seeder != null)
            {
                _seeder.Seed(database);
            }

            _db = database;
        }
    }

    public void Close()
    {
        lock (_initializationLock)
        {
            _db?.Dispose();
            _db = null;
        }
    }

    public void Dispose()
    {
        Close();
    }
}