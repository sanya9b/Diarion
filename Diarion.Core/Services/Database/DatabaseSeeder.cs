using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Diarion.Models;
using Diarion.Resources.Localization;
using LiteDB;

namespace Diarion.Services.Database;

public partial class DatabaseSeeder : IDatabaseSeeder
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

        SeedGuidedPrompts(database.GetCollection<GuidedPrompt>(DatabaseConstants.GuidedPromptsCollection));

#if DEBUG
        // A sandbox run (Services/DebugSandbox.cs in the app) is for looking at the empty screens;
        // the mock diary would fill them.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DIARION_SANDBOX_DIR")))
            SeedMockDataIfEmpty(database);
#endif
    }

    /// <summary>
    /// Copies the built-in prompts out of the resource files into the collection, once. Both languages
    /// are read by explicit culture rather than the ambient one: seeding runs during database init,
    /// which can precede culture setup, and whatever landed here would be the text forever.
    ///
    /// Deleting a prompt only sets DeletedAt, so the row survives and this guard cannot re-seed behind
    /// a user who cleared the library — unlike the habits above, where a full delete brings them back.
    /// </summary>
    private static void SeedGuidedPrompts(ILiteCollection<GuidedPrompt> promptsCollection)
    {
        if (promptsCollection.Count() > 0) return;

        var order = 0;
        var seeded = new List<GuidedPrompt>();

        foreach (var (category, keys) in PromptCatalog.SeedKeys)
        {
            foreach (var key in keys)
            {
                seeded.Add(BuiltInPrompt(key, category, order++));
            }
        }

        promptsCollection.InsertBulk(seeded);
    }

    private static readonly CultureInfo Ukrainian = new("uk");

    /// <summary>
    /// One built-in prompt row, text copied out of both resource sets by explicit culture — the ambient
    /// culture is not set up yet when seeding runs. Shared with the migrations that top up a library
    /// seeded before a category existed, so a row looks the same whichever path wrote it.
    /// </summary>
    public static GuidedPrompt BuiltInPrompt(string key, PromptCategory category, int order) => new()
    {
        ResourceKey = key,
        Category = category,
        TextEn = AppResources.ResourceManager.GetString(key, CultureInfo.InvariantCulture) ?? string.Empty,
        TextUk = AppResources.ResourceManager.GetString(key, Ukrainian) ?? string.Empty,
        Order = order,
        CreatedAt = DateTime.MinValue
    };

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
}
