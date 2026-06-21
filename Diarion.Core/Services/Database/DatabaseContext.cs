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

    public DatabaseContext(bool useInMemory = false)
    {
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
                string dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DbFileName);
                database = new LiteDatabase(dbPath);
            }
            
            var entriesCollection = database.GetCollection<DiaryEntry>("entries");
            var todosCollection = database.GetCollection<TodoItem>("todos");
            var habitsCollection = database.GetCollection<HabitDefinition>("habit_definitions");
            var harmfulHabitTrackersCollection = database.GetCollection<HarmfulHabitTracker>("harmful_habit_trackers");
            var readingTrackerBooksCollection = database.GetCollection<ReadingTrackerBook>("reading_tracker_books");
            var happyMomentsCollection = database.GetCollection<HappyMoment>("happy_moments");
            var goodDeedsCollection = database.GetCollection<GoodDeed>("good_deeds");
            var profileCollection = database.GetCollection<UserProfile>("profile");
            var wishlistCollection = database.GetCollection<WishlistEntry>("wishlist_entries");
            var financeCollection = database.GetCollection<FinanceTransaction>("finance_transactions");

            entriesCollection.EnsureIndex(x => x.Date);
            wishlistCollection.EnsureIndex(x => x.Date);
            financeCollection.EnsureIndex(x => x.Date);
            todosCollection.EnsureIndex(x => x.TargetDate);
            harmfulHabitTrackersCollection.EnsureIndex(x => x.StartDate);
            readingTrackerBooksCollection.EnsureIndex(x => x.SlotNumber, true);
            happyMomentsCollection.EnsureIndex(x => x.SlotNumber, true);
            goodDeedsCollection.EnsureIndex(x => x.SlotNumber, true);

            // Pre-seed defaults if empty
            if (habitsCollection.Count() == 0)
            {
                var defaults = new[] 
                { 
                    Diarion.Resources.Localization.AppResources.HabitPhysicalActivity, 
                    Diarion.Resources.Localization.AppResources.HabitWater, 
                    Diarion.Resources.Localization.AppResources.HabitVitamins, 
                    Diarion.Resources.Localization.AppResources.HabitReading, 
                    Diarion.Resources.Localization.AppResources.HabitSocial 
                };
                foreach (var d in defaults)
                {
                    habitsCollection.Insert(new HabitDefinition { Name = d ?? string.Empty, CreatedAt = DateTime.MinValue });
                }
            }

            // One-time cleanup for old default food habits from the database
            var foodHabits = new[] { "Breakfast", "Сніданок", "Lunch", "Обід", "Dinner", "Вечеря" };
            var habitsToDelete = habitsCollection.FindAll()
                .Where(h => foodHabits.Contains(h.Name))
                .ToList();
            
            foreach (var h in habitsToDelete)
            {
                habitsCollection.Delete(h.Id);
                // Also remove them from existing entries
                var entriesWithHabit = entriesCollection.FindAll().Where(e => e.Habits.Any(x => x.HabitId == h.Id)).ToList();
                foreach (var entry in entriesWithHabit)
                {
                    var item = entry.Habits.FirstOrDefault(x => x.HabitId == h.Id);
                    if (item != null)
                    {
                        entry.Habits.Remove(item);
                        entriesCollection.Update(entry);
                    }
                }
            }

#if DEBUG
            SeedMockDataIfEmpty(entriesCollection, todosCollection, habitsCollection);
#endif
            _db = database;
        }
    }

#if DEBUG
    private void SeedMockDataIfEmpty(ILiteCollection<DiaryEntry> entriesCollection, ILiteCollection<TodoItem> todosCollection, ILiteCollection<HabitDefinition> habitsCollection)
    {
        // Перевіряємо, чи база вже має створені нотатки
        if (entriesCollection.Count() > 0) return;

        var habits = habitsCollection.FindAll().ToList();
        var random = new Random();
        var today = DateTime.Today;

        // Генеруємо дні: від 10 травня до 3 днів у майбутнє
        var startDate = new DateTime(today.Year > 2000 ? today.Year : 2026, 5, 10);
        if (startDate > today) startDate = startDate.AddYears(-1);
        int daysDiff = (int)(today.AddDays(3) - startDate).TotalDays;

        for (int i = 0; i <= daysDiff; i++)
        {
            var date = startDate.AddDays(i);
            
            // Заповнення моделі дня
            var entry = new DiaryEntry
            {
                Id = Guid.NewGuid(),
                Date = date,
                CreatedAt = date.AddHours(20),
                SleepStart = new TimeSpan(22, random.Next(0, 59), 0),
                SleepEnd = new TimeSpan(7, random.Next(0, 59), 0),
                SleepQuality = random.Next(4, 11),
                HealthStatus = random.Next(5, 11),
                CycleDay = random.Next(1, 28).ToString(),
                IntimateLife = random.NextDouble() > 0.7 ? "Так" : "Ні",
                Triggers = "Стрес на роботі, затори на дорогах...",
                Gratitude = "Смачна кава зранку, гарна погода, подзвонив друг",
                SoulFood = "Читав книгу 'Clean Architecture', слухав джаз",
            };

            // Заповнення гібридних звичок
            foreach (var h in habits)
            {
                entry.Habits.Add(new HabitItem 
                { 
                    HabitId = h.Id, 
                    Name = h.Name, 
                    IsCompleted = random.NextDouble() > 0.4
                });
            }

            entriesCollection.Insert(entry);

            // Заповнення завдань на цей день (від 1 до 4)
            int tasksCount = random.Next(1, 5);
            for (int t = 0; t < tasksCount; t++)
            {
                todosCollection.Insert(new TodoItem
                {
                    Id = Guid.NewGuid(),
                    TargetDate = date,
                    TaskDescription = $"Тестове завдання {t + 1}",
                    IsCompleted = random.NextDouble() > 0.5,
                    Priority = (TodoPriority)random.Next(0, 3),
                    HasTime = random.NextDouble() > 0.5,
                    TargetTime = new TimeSpan(random.Next(8, 20), random.Next(0, 5) * 10, 0),
                    CreatedAt = date.AddHours(-1)
                });
            }
        }
    }
#endif

    public void Dispose()
    {
        _db?.Dispose();
    }
}