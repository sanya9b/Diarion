using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Diarion.Models;
using Diarion.Resources.Localization;
using LiteDB;

namespace Diarion.Services.Database;

public class DatabaseSeeder : IDatabaseSeeder
{
    public void Seed(LiteDatabase database)
    {
        var entriesCollection = database.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);
        var todosCollection = database.GetCollection<TodoItem>(DatabaseConstants.TodosCollection);
        var habitsCollection = database.GetCollection<HabitDefinition>(DatabaseConstants.HabitDefinitionsCollection);

        // Pre-seed the built-in defaults if empty. Each carries a stable ResourceKey so its name
        // stays bilingual (resolved from resources) instead of frozen to the seed-time language.
        if (habitsCollection.Count() == 0)
        {
            foreach (var key in HabitLocalization.DefaultHabitResourceKeys)
            {
                var name = AppResources.ResourceManager.GetString(key, AppResources.Culture) ?? key;
                habitsCollection.Insert(new HabitDefinition
                {
                    Name = name,
                    ResourceKey = key,
                    CreatedAt = DateTime.MinValue
                });
            }
        }

        // Backfill ResourceKey on installs seeded before it existed, so their default habits
        // (stored with a single-language literal name) also become bilingual.
        BackfillDefaultHabitResourceKeys(habitsCollection);

#if DEBUG
        SeedMockDataIfEmpty(entriesCollection, todosCollection, habitsCollection);
#endif
    }

    private static void BackfillDefaultHabitResourceKeys(ILiteCollection<HabitDefinition> habitsCollection)
    {
        // Map every known localized spelling of a default habit (across supported cultures) back
        // to its resource key, so we can recognise legacy rows no matter which language seeded them.
        var cultures = new[] { CultureInfo.InvariantCulture, new CultureInfo("en"), new CultureInfo("uk") };
        var nameToKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in HabitLocalization.DefaultHabitResourceKeys)
        {
            foreach (var culture in cultures)
            {
                var value = AppResources.ResourceManager.GetString(key, culture);
                if (!string.IsNullOrWhiteSpace(value))
                    nameToKey[value.Trim()] = key;
            }
        }

        foreach (var habit in habitsCollection.FindAll())
        {
            if (!string.IsNullOrEmpty(habit.ResourceKey))
                continue;

            if (!string.IsNullOrWhiteSpace(habit.Name) && nameToKey.TryGetValue(habit.Name.Trim(), out var key))
            {
                habit.ResourceKey = key;
                habitsCollection.Update(habit);
            }
        }
    }

#if DEBUG
    private void SeedMockDataIfEmpty(ILiteCollection<DiaryEntry> entriesCollection, ILiteCollection<TodoItem> todosCollection, ILiteCollection<HabitDefinition> habitsCollection)
    {
        if (entriesCollection.Count() > 0) return;

        var habits = habitsCollection.FindAll().ToList();
        var random = new Random();
        var today = DateTime.Today;

        var startDate = new DateTime(today.Year > 2000 ? today.Year : 2026, 5, 10);
        if (startDate > today) startDate = startDate.AddYears(-1);
        int daysDiff = (int)(today.AddDays(3) - startDate).TotalDays;

        var entriesToInsert = new System.Collections.Generic.List<DiaryEntry>();
        var todosToInsert = new System.Collections.Generic.List<TodoItem>();

        for (int i = 0; i <= daysDiff; i++)
        {
            var date = startDate.AddDays(i);
            
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
                IntimateLife = random.NextDouble() > 0.7 ? "Yes" : "No",
                // Left blank so the localized reflection placeholders are visible in mock data.
                Triggers = string.Empty,
                Gratitude = string.Empty,
                SoulFood = string.Empty,
            };

            foreach (var h in habits)
            {
                entry.HabitsList.Add(new HabitItem 
                { 
                    HabitId = h.Id, 
                    Name = h.Name, 
                    IsCompleted = random.NextDouble() > 0.4
                });
            }

            entriesToInsert.Add(entry);

            int tasksCount = random.Next(1, 5);
            for (int t = 0; t < tasksCount; t++)
            {
                todosToInsert.Add(new TodoItem
                {
                    Id = Guid.NewGuid(),
                    TargetDate = date,
                    TaskDescription = $"Test task {t + 1}",
                    IsCompleted = random.NextDouble() > 0.5,
                    Priority = (TodoPriority)random.Next(0, 3),
                    HasTime = random.NextDouble() > 0.5,
                    TargetTime = new TimeSpan(random.Next(8, 20), random.Next(0, 5) * 10, 0),
                    CreatedAt = date.AddHours(-1)
                });
            }
        }

        entriesCollection.InsertBulk(entriesToInsert);
        todosCollection.InsertBulk(todosToInsert);
    }
#endif
}