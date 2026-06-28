using System;
using System.Linq;
using Diarion.Models;
using LiteDB;

namespace Diarion.Services.Database;

public class DatabaseSeeder : IDatabaseSeeder
{
    public void Seed(LiteDatabase database)
    {
        var entriesCollection = database.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);
        var todosCollection = database.GetCollection<TodoItem>(DatabaseConstants.TodosCollection);
        var habitsCollection = database.GetCollection<HabitDefinition>(DatabaseConstants.HabitDefinitionsCollection);

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

#if DEBUG
        SeedMockDataIfEmpty(entriesCollection, todosCollection, habitsCollection);
#endif
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
                Triggers = "Work stress, traffic jams...",
                Gratitude = "Tasty morning coffee, good weather, friend called",
                SoulFood = "Read 'Clean Architecture' book, listened to jazz",
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

            entriesCollection.Insert(entry);

            int tasksCount = random.Next(1, 5);
            for (int t = 0; t < tasksCount; t++)
            {
                todosCollection.Insert(new TodoItem
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
    }
#endif
}