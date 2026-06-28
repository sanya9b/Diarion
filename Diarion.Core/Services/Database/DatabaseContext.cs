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
    private LiteDatabase? _db;
    private string _dbPath = string.Empty;

    public string DatabasePath => _dbPath;

    public DatabaseContext(IDatabaseSeeder? seeder = null, bool useInMemory = false)
    {
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
            _db = new LiteDatabase(_dbPath);
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

        entriesCollection.EnsureIndex(x => x.Date);
        wishlistCollection.EnsureIndex(x => x.Date);
        financeCollection.EnsureIndex(x => x.Date);
        todosCollection.EnsureIndex(x => x.TargetDate);
        harmfulHabitTrackersCollection.EnsureIndex(x => x.StartDate);
        readingTrackerBooksCollection.EnsureIndex(x => x.SlotNumber, true);
        happyMomentsCollection.EnsureIndex(x => x.SlotNumber, true);
        goodDeedsCollection.EnsureIndex(x => x.SlotNumber, true);

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

    public void Dispose()
    {
        Close();
    }
}