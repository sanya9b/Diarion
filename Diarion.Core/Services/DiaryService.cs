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
    private ILiteCollection<HabitDefinition>? _habitsCollection;
    private ILiteCollection<UserProfile>? _profileCollection;
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
            var habitsCollection = database.GetCollection<HabitDefinition>("habit_definitions");
            var profileCollection = database.GetCollection<UserProfile>("profile");

            entriesCollection.EnsureIndex(x => x.Date);
            todosCollection.EnsureIndex(x => x.TargetDate);

            // Pre-seed defaults if empty
            if (habitsCollection.Count() == 0)
            {
                var defaults = new[] { "Фізична активність", "Сніданок", "Обід", "Вечеря", "Вода", "Вітаміни/Ліки", "Читання / Нові знання", "Соціальні зв'язки" };
                foreach (var d in defaults)
                {
                    habitsCollection.Insert(new HabitDefinition { Name = d, CreatedAt = DateTime.MinValue });
                }
            }

            _db = database;
            _entriesCollection = entriesCollection;
            _todosCollection = todosCollection;
            _habitsCollection = habitsCollection;
            _profileCollection = profileCollection;

#if DEBUG
            SeedMockDataIfEmpty();
#endif
        }
    }

#if DEBUG
    private void SeedMockDataIfEmpty()
    {
        // Перевіряємо, чи база вже має створені нотатки
        if (_entriesCollection!.Count() > 0) return;

        var habits = _habitsCollection!.FindAll().ToList();
        var random = new Random();
        var today = DateTime.Today;

        // Генеруємо 14 днів: від 10 днів у минуле до 3 у майбутнє
        for (int i = -10; i <= 3; i++)
        {
            var date = today.AddDays(i);
            
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

            _entriesCollection.Insert(entry);

            // Заповнення завдань на цей день (від 1 до 4)
            int tasksCount = random.Next(1, 5);
            for (int t = 0; t < tasksCount; t++)
            {
                _todosCollection!.Insert(new TodoItem
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

    private ILiteCollection<HabitDefinition> HabitsCollection
    {
        get
        {
            EnsureInitialized();
            return _habitsCollection!;
        }
    }

    private ILiteCollection<UserProfile> ProfileCollection
    {
        get
        {
            EnsureInitialized();
            return _profileCollection!;
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

    public Task<List<HabitDefinition>> GetActiveHabitsForDateAsync(DateTime date)
    {
        return Task.Run(() =>
        {
            var dateOnly = date.Date;
            return HabitsCollection.Query()
                .Where(x => x.CreatedAt <= dateOnly && (x.DeletedAt == null || x.DeletedAt > dateOnly))
                .ToList();
        });
    }

    public Task AddHabitDefinitionAsync(HabitDefinition habit)
    {
        return Task.Run(() => HabitsCollection.Insert(habit));
    }

    public Task DeleteHabitDefinitionAsync(Guid id, DateTime deleteDate)
    {
        return Task.Run(() =>
        {
            var def = HabitsCollection.FindById(id);
            if (def != null)
            {
                def.DeletedAt = deleteDate.Date;
                HabitsCollection.Update(def);
            }
        });
    }

    public Task<UserProfile> GetUserProfileAsync()
    {
        return Task.Run(() =>
        {
            var profile = ProfileCollection.FindAll().FirstOrDefault();
            if (profile == null)
            {
                profile = new UserProfile();
                ProfileCollection.Insert(profile);
            }
            return profile;
        });
    }

    public Task SaveUserProfileAsync(UserProfile profile)
    {
        return Task.Run(() => ProfileCollection.Update(profile));
    }

    public Task<List<DiaryEntry>> GetAllEntriesAsync()
    {
        return Task.Run(() => EntriesCollection.Query().OrderByDescending(x => x.Date).ToList());
    }

    public Task<DiaryEntry> GetEntryForDateAsync(DateTime date)
    {
        return Task.Run(() =>
        {
            var startedAt = Stopwatch.GetTimestamp();
            var dateOnly = date.Date;
            var entry = EntriesCollection.Query()
                .Where(x => x.Date == dateOnly)
                .FirstOrDefault();

            if (entry == null)
            {
                entry = new DiaryEntry 
                { 
                    Date = dateOnly,
                    CreatedAt = DateTime.Now
                };
            }

            // Синхронізація звичок для цього дня
            var activeDefs = HabitsCollection.Query()
                .Where(x => x.CreatedAt <= dateOnly && (x.DeletedAt == null || x.DeletedAt > dateOnly))
                .ToList();

            var validIds = new HashSet<Guid>(activeDefs.Select(d => d.Id));
            
            // Залишаємо тільки ті звички, які були актуальні на цей день
            var filteredHabits = entry.Habits.Where(h => validIds.Contains(h.HabitId)).ToList();
            
            // Додаємо нові звички, яких ще немає в цьому записі
            var existingIds = new HashSet<Guid>(filteredHabits.Select(h => h.HabitId));
            foreach (var def in activeDefs)
            {
                if (!existingIds.Contains(def.Id))
                {
                    filteredHabits.Add(new HabitItem { HabitId = def.Id, Name = def.Name });
                }
                else
                {
                    // Оновлюємо ім'я, якщо воно змінилося
                    var h = filteredHabits.First(x => x.HabitId == def.Id);
                    if (h.Name != def.Name) h.Name = def.Name;
                }
            }

            // Відновлюємо колекцію у правильному порядку
            entry.Habits = new System.Collections.ObjectModel.ObservableCollection<HabitItem>(filteredHabits.OrderBy(h => 
            {
                var def = activeDefs.FirstOrDefault(d => d.Id == h.HabitId);
                return def?.CreatedAt ?? DateTime.MaxValue;
            }));

            StartupTrace.Mark($"DiaryService.GetEntryForDateAsync duration={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F1}ms");
            return entry;
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

            // Перевіряємо, чи є завдання з минулого, які повторюються щодня
            var pastRepeatingTodos = TodosCollection.Query()
                .Where(x => x.IsDailyRepeat && x.TargetDate < dateOnly)
                .ToList()
                .GroupBy(x => x.TaskDescription)
                .Select(g => g.OrderByDescending(x => x.TargetDate).First())
                .ToList();

            foreach (var task in pastRepeatingTodos)
            {
                // Якщо на сьогодні ще немає завдання з такою ж назвою і позначкою повторення
                if (!items.Any(x => x.TaskDescription == task.TaskDescription && x.IsDailyRepeat))
                {
                    var clone = new TodoItem
                    {
                        Id = Guid.NewGuid(),
                        TargetDate = dateOnly,
                        TargetTime = task.TargetTime,
                        HasTime = task.HasTime,
                        TaskDescription = task.TaskDescription,
                        IsCompleted = false, // Нове завдання на новий день ще не виконано
                        Priority = task.Priority,
                        CreatedAt = DateTime.Now,
                        IsDailyRepeat = true,
                        HasReminder = task.HasReminder
                    };
                    TodosCollection.Insert(clone);
                    items.Add(clone);
                }
            }

            // Сортування: 
            // 1. Спочатку не виконані, потім виконані (IsCompleted ASC).
            // 2. Пріоритет від Високого (High) до Низького (Low) (Priority DESC).
            // 3. За часом виконання (спочатку з часом, потім без; і по самому часу).
            var todos = items
                .OrderBy(x => x.IsCompleted)
                .ThenByDescending(x => x.Priority)
                .ThenBy(x => x.HasTime ? 0 : 1)
                .ThenBy(x => x.TargetTime)
                .ToList();

            StartupTrace.Mark($"DiaryService.GetTodosForDateAsync count={todos.Count} duration={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F1}ms");
            return todos;
        });
    }

    public Task<List<TodoItem>> GetTodosForMonthAsync(int year, int month)
    {
        return Task.Run(() =>
        {
            var startDate = new DateTime(year, month, 1);
            var endDate = startDate.AddMonths(1);
            
            return TodosCollection.Query()
                .Where(x => x.TargetDate >= startDate && x.TargetDate < endDate)
                .ToList();
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
