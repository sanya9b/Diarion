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
    private ILiteCollection<HarmfulHabitTracker>? _harmfulHabitTrackersCollection;
    private ILiteCollection<ReadingTrackerBook>? _readingTrackerBooksCollection;
    private ILiteCollection<HappyMoment>? _happyMomentsCollection;
    private ILiteCollection<GoodDeed>? _goodDeedsCollection;
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
            var harmfulHabitTrackersCollection = database.GetCollection<HarmfulHabitTracker>("harmful_habit_trackers");
            var readingTrackerBooksCollection = database.GetCollection<ReadingTrackerBook>("reading_tracker_books");
            var happyMomentsCollection = database.GetCollection<HappyMoment>("happy_moments");
            var goodDeedsCollection = database.GetCollection<GoodDeed>("good_deeds");
            var profileCollection = database.GetCollection<UserProfile>("profile");

            entriesCollection.EnsureIndex(x => x.Date);
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
                    Diarion.Resources.Localization.AppResources.HabitBreakfast, 
                    Diarion.Resources.Localization.AppResources.HabitLunch, 
                    Diarion.Resources.Localization.AppResources.HabitDinner, 
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

            _db = database;
            _entriesCollection = entriesCollection;
            _todosCollection = todosCollection;
            _habitsCollection = habitsCollection;
            _harmfulHabitTrackersCollection = harmfulHabitTrackersCollection;
            _readingTrackerBooksCollection = readingTrackerBooksCollection;
            _happyMomentsCollection = happyMomentsCollection;
            _goodDeedsCollection = goodDeedsCollection;
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

    private ILiteCollection<HarmfulHabitTracker> HarmfulHabitTrackersCollection
    {
        get
        {
            EnsureInitialized();
            return _harmfulHabitTrackersCollection!;
        }
    }

    private ILiteCollection<ReadingTrackerBook> ReadingTrackerBooksCollection
    {
        get
        {
            EnsureInitialized();
            return _readingTrackerBooksCollection!;
        }
    }

    private ILiteCollection<HappyMoment> HappyMomentsCollection
    {
        get
        {
            EnsureInitialized();
            return _happyMomentsCollection!;
        }
    }

    private ILiteCollection<GoodDeed> GoodDeedsCollection
    {
        get
        {
            EnsureInitialized();
            return _goodDeedsCollection!;
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

    public Task<List<HarmfulHabitTracker>> GetHarmfulHabitTrackersAsync()
    {
        return Task.Run(() => HarmfulHabitTrackersCollection.Query().OrderByDescending(x => x.CreatedAt).ToList());
    }

    public Task SaveHarmfulHabitTrackerAsync(HarmfulHabitTracker tracker)
    {
        return Task.Run(() =>
        {
            var normalizedName = (tracker.HarmfulHabitName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                throw new ArgumentException("Tracker name is required.", nameof(tracker));
            }

            var normalizedStartDate = tracker.StartDate.Date > DateTime.Today ? DateTime.Today : tracker.StartDate.Date;
            var hasDuplicate = HarmfulHabitTrackersCollection.FindAll()
                .Any(x => x.Id != tracker.Id && string.Equals(x.HarmfulHabitName.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase));

            if (hasDuplicate)
            {
                throw new InvalidOperationException("Tracker with the same name already exists.");
            }

            tracker.HarmfulHabitName = normalizedName;
            tracker.StartDate = normalizedStartDate;
            tracker.CreatedAt = tracker.CreatedAt == default ? DateTime.UtcNow : tracker.CreatedAt;
            tracker.MarkedDays = (tracker.MarkedDays ?? new List<DateTime>())
                .Select(x => x.Date)
                .Where(x => x >= tracker.StartDate && x <= DateTime.Today)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            HarmfulHabitTrackersCollection.Upsert(tracker);
        });
    }

    public Task SetHarmfulHabitDayMarkedAsync(Guid trackerId, DateTime date, bool isMarked)
    {
        return Task.Run(() =>
        {
            var tracker = HarmfulHabitTrackersCollection.FindById(trackerId)
                ?? throw new InvalidOperationException("Tracker was not found.");

            var targetDate = date.Date;
            if (targetDate < tracker.StartDate.Date || targetDate > DateTime.Today)
            {
                return;
            }

            tracker.MarkedDays ??= new List<DateTime>();
            tracker.MarkedDays = tracker.MarkedDays
                .Select(x => x.Date)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            if (isMarked)
            {
                if (!tracker.MarkedDays.Contains(targetDate))
                {
                    tracker.MarkedDays.Add(targetDate);
                }
            }
            else
            {
                tracker.MarkedDays.RemoveAll(x => x == targetDate);
            }

            tracker.MarkedDays = tracker.MarkedDays.OrderBy(x => x).ToList();
            HarmfulHabitTrackersCollection.Update(tracker);
        });
    }

    public Task<List<ReadingTrackerBook>> GetReadingTrackerBooksAsync()
    {
        return Task.Run(() => ReadingTrackerBooksCollection.Query().OrderBy(x => x.SlotNumber).ToList());
    }

    public Task SaveReadingTrackerBookAsync(ReadingTrackerBook book)
    {
        return Task.Run(() =>
        {
            var normalizedTitle = (book.BookTitle ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedTitle))
            {
                throw new ArgumentException("Book title is required.", nameof(book));
            }

            if (book.SlotNumber <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(book), "Slot number must be greater than zero.");
            }

            var completedOn = book.CompletedOn.Date > DateTime.Today ? DateTime.Today : book.CompletedOn.Date;
            var hasDuplicateSlot = ReadingTrackerBooksCollection.FindAll()
                .Any(x => x.Id != book.Id && x.SlotNumber == book.SlotNumber);

            if (hasDuplicateSlot)
            {
                throw new InvalidOperationException("Book slot is already filled.");
            }

            book.BookTitle = normalizedTitle;
            book.CompletedOn = completedOn;
            book.CreatedAt = book.CreatedAt == default ? DateTime.UtcNow : book.CreatedAt;

            ReadingTrackerBooksCollection.Upsert(book);
        });
    }

    public Task DeleteReadingTrackerBookAsync(int slotNumber)
    {
        return Task.Run(() =>
        {
            var existingSlot = ReadingTrackerBooksCollection.FindOne(x => x.SlotNumber == slotNumber);
            if (existingSlot != null)
            {
                ReadingTrackerBooksCollection.Delete(existingSlot.Id);
            }
        });
    }

    public Task<List<HappyMoment>> GetHappyMomentsAsync()
    {
        return Task.Run(() => HappyMomentsCollection.Query().OrderBy(x => x.SlotNumber).ToList());
    }

      public Task DeleteHappyMomentAsync(int slotNumber)
      {
          return Task.Run(() =>
          {
              HappyMomentsCollection.DeleteMany(x => x.SlotNumber == slotNumber);
          });
      }

      public Task SaveHappyMomentAsync(HappyMoment moment)
      {
        return Task.Run(() =>
        {
            var normalizedTitle = (moment.Title ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedTitle))
            {
                throw new ArgumentException("Moment title is required.", nameof(moment));
            }

            if (moment.SlotNumber <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(moment), "Slot number must be positive.");
            }

            var momentDate = moment.Date.Date > DateTime.Today ? DateTime.Today : moment.Date.Date;
            var existingSlot = HappyMomentsCollection.FindOne(x => x.SlotNumber == moment.SlotNumber);

            if (existingSlot != null)
            {
                moment.Id = existingSlot.Id;
                moment.CreatedAt = existingSlot.CreatedAt;
            }
            else if (moment.CreatedAt == default)
            {
                moment.CreatedAt = DateTime.UtcNow;
            }

            moment.Title = normalizedTitle;
            moment.Date = momentDate;

            HappyMomentsCollection.Upsert(moment);
        });
    }

    public Task<List<GoodDeed>> GetGoodDeedsAsync()
    {
        return Task.Run(() => GoodDeedsCollection.Query().OrderBy(x => x.SlotNumber).ToList());
    }

    public Task SaveGoodDeedAsync(GoodDeed deed)
    {
        return Task.Run(() =>
        {
            var normalizedTitle = (deed.Title ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedTitle))
            {
                throw new ArgumentException("Deed title is required.", nameof(deed));
            }

            if (deed.SlotNumber <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(deed), "Slot number must be positive.");
            }

            var deedDate = deed.Date.Date > DateTime.Today ? DateTime.Today : deed.Date.Date;
            var existingSlot = GoodDeedsCollection.FindOne(x => x.SlotNumber == deed.SlotNumber);

            if (existingSlot != null)
            {
                deed.Id = existingSlot.Id;
                deed.CreatedAt = existingSlot.CreatedAt;
            }
            else if (deed.CreatedAt == default)
            {
                deed.CreatedAt = DateTime.UtcNow;
            }

            deed.Title = normalizedTitle;
            deed.Date = deedDate;

            GoodDeedsCollection.Upsert(deed);
        });
    }

    public Task DeleteGoodDeedAsync(int slotNumber)
    {
        return Task.Run(() =>
        {
            var existingSlot = GoodDeedsCollection.FindOne(x => x.SlotNumber == slotNumber);
            if (existingSlot != null)
            {
                GoodDeedsCollection.Delete(existingSlot.Id);
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
                profile.NormalizeCycleSettings();
                ProfileCollection.Insert(profile);
            }
            else if (profile.NormalizeCycleSettings())
            {
                ProfileCollection.Update(profile);
            }

            return profile;
        });
    }

    public Task SaveUserProfileAsync(UserProfile profile)
    {
        return Task.Run(() =>
        {
            profile.NormalizeCycleSettings();
            ProfileCollection.Upsert(profile);
        });
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
