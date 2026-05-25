using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Diagnostics;
using Diarion.Models;
using LiteDB;

namespace Diarion.Services;

public class DiaryService : IDiaryService, IDisposable
{
    private const string DbFileName = "diarion_local.db";
    private readonly object _initializationLock = new();
    private LiteDatabase? _db;
    private ILiteCollection<DiaryEntry>? _entriesCollection;
    private ILiteCollection<TodoItem>? _todosCollection;
    private readonly bool _useInMemory;

    public DiaryService(bool useInMemory = false)
    {
        _useInMemory = useInMemory;
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

            using var _ = StartupTrace.Measure("DiaryService.EnsureInitialized");
            
            LiteDatabase database;
            if (_useInMemory)
            {
                database = new LiteDatabase(new MemoryStream());
            }
            else
            {
                string dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DbFileName);
                database = new LiteDatabase(dbPath);
            }
            
            var entriesCollection = database.GetCollection<DiaryEntry>("entries");
            var todosCollection = database.GetCollection<TodoItem>("todos");

            entriesCollection.EnsureIndex(x => x.CreatedAt);
            todosCollection.EnsureIndex(x => x.TargetDate);

            _db = database;
            _entriesCollection = entriesCollection;
            _todosCollection = todosCollection;
        }
    }

    private ILiteCollection<DiaryEntry> EntriesCollection
    {
        get
        {
            EnsureInitialized();
            return _entriesCollection!;
        }
    }

    private ILiteCollection<TodoItem> TodosCollection
    {
        get
        {
            EnsureInitialized();
            return _todosCollection!;
        }
    }

    public Task<List<DiaryEntry>> GetAllEntriesAsync()
    {
        return Task.Run(() => EntriesCollection.Query().OrderByDescending(x => x.CreatedAt).ToList());
    }

    public Task<List<DiaryEntry>> GetEntriesForDateAsync(DateTime date)
    {
        return Task.Run(() =>
        {
            var startedAt = Stopwatch.GetTimestamp();
            var dateOnly = date.Date;
            var entries = EntriesCollection.Query()
                .Where(x => x.CreatedAt >= dateOnly && x.CreatedAt < dateOnly.AddDays(1))
                .OrderByDescending(x => x.CreatedAt)
                .ToList();

            StartupTrace.Mark($"DiaryService.GetEntriesForDateAsync count={entries.Count} duration={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F1}ms");
            return entries;
        });
    }

    public Task<DiaryEntry> GetEntryByIdAsync(Guid id)
    {
        return Task.Run(() => EntriesCollection.FindById(id));
    }

    public Task SaveEntryAsync(DiaryEntry entry)
    {
        return Task.Run(() => EntriesCollection.Upsert(entry));
    }

    public Task DeleteEntryAsync(Guid id)
    {
        return Task.Run(() =>
        {
            EntriesCollection.Delete(id);
            TodosCollection.DeleteMany(x => x.DiaryEntryId == id);
        });
    }

    public Task<TodoItem?> GetTodoByIdAsync(Guid id)
    {
        return Task.Run<TodoItem?>(() =>
        {
            return TodosCollection.FindById(id);
        });
    }

    public Task<List<TodoItem>> GetTodosForDateAsync(DateTime date)
    {
        return Task.Run(() =>
        {
            var startedAt = Stopwatch.GetTimestamp();
            var dateOnly = date.Date;
            var items = TodosCollection.Query()
                .Where(x => x.TargetDate == dateOnly)
                .ToList();
            
            var todos = items.OrderBy(x => x.IsCompleted).ThenByDescending(x => x.Priority).ToList();
            StartupTrace.Mark($"DiaryService.GetTodosForDateAsync count={todos.Count} duration={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F1}ms");
            return todos;
        });
    }

    public Task<List<TodoItem>> GetAllTodosAsync()
    {
        return Task.Run(() => TodosCollection.FindAll().ToList());
    }

    public Task SaveTodoAsync(TodoItem todo)
    {
        return Task.Run(() => TodosCollection.Upsert(todo));
    }

    public Task DeleteTodoAsync(Guid todoId)
    {
        return Task.Run(() => TodosCollection.Delete(todoId));
    }

    public void Dispose()
    {
        _db?.Dispose();
    }
}
