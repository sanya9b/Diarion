using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Diarion.Models;
using LiteDB;

namespace Diarion.Services;

public class DiaryService : IDiaryService, IDisposable
{
    private const string DbFileName = "diarion_local.db";
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<DiaryEntry> _entriesCollection;
    private readonly ILiteCollection<TodoItem> _todosCollection;

    public DiaryService()
    {
        // Шлях до бази даних в локальній папці додатку
        string dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DbFileName);
        
        // Відкриваємо базу даних один раз на весь час життя додатку (Singleton)
        _db = new LiteDatabase(dbPath);
        _entriesCollection = _db.GetCollection<DiaryEntry>("entries");
        _todosCollection = _db.GetCollection<TodoItem>("todos");

        // Створюємо індекс для швидкого сортування за датою
        _entriesCollection.EnsureIndex(x => x.CreatedAt);
        // Створюємо індекс для швидкого пошуку тудушок за записом
        _todosCollection.EnsureIndex(x => x.DiaryEntryId);
    }

    public Task<List<DiaryEntry>> GetAllEntriesAsync()
    {
        return Task.Run(() =>
        {
            // Повертаємо всі записи, відсортовані за датою (найновіші перші)
            return _entriesCollection.Query().OrderByDescending(x => x.CreatedAt).ToList();
        });
    }

    public Task<DiaryEntry> GetEntryByIdAsync(Guid id)
    {
        return Task.Run(() =>
        {
            return _entriesCollection.FindById(id);
        });
    }

    public Task SaveEntryAsync(DiaryEntry entry)
    {
        return Task.Run(() =>
        {
            // LiteDB Upsert додає новий запис або оновлює існуючий за Id
            _entriesCollection.Upsert(entry);
        });
    }

    public Task DeleteEntryAsync(Guid id)
    {
        return Task.Run(() =>
        {
            _entriesCollection.Delete(id);
            // Delete associated todos when entry is deleted
            _todosCollection.DeleteMany(x => x.DiaryEntryId == id);
        });
    }

    public Task<List<TodoItem>> GetTodosForEntryAsync(Guid entryId)
    {
        return Task.Run(() =>
        {
            var items = _todosCollection.Query()
                .Where(x => x.DiaryEntryId == entryId)
                .ToList();
                
            return items
                .OrderBy(x => x.IsCompleted) // Спочатку невиконані
                .ThenByDescending(x => x.Priority) // Потім найпріоритетніші
                .ToList();
        });
    }

    public Task SaveTodoAsync(TodoItem todo)
    {
        return Task.Run(() =>
        {
            _todosCollection.Upsert(todo);
        });
    }

    public Task DeleteTodoAsync(Guid todoId)
    {
        return Task.Run(() =>
        {
            _todosCollection.Delete(todoId);
        });
    }

    public void Dispose()
    {
        _db?.Dispose();
    }
}
