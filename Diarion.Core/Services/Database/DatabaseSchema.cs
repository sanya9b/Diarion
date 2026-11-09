using System;
using System.Collections.Generic;
using Diarion.Models;

namespace Diarion.Services.Database;

/// <summary>
/// Every collection the app persists, with the type it is read back as. One list, so the upgrade test
/// can open a database written by an older build and prove that each collection still deserialises
/// through the current mappers: a renamed enum member or a changed property type fails at the first
/// read on the user's phone, not at build time, and nothing else in the pipeline exercises that path.
/// Add a line here with every new collection.
/// </summary>
public static class DatabaseSchema
{
    public sealed record Collection(string Name, Type DocumentType);

    public static IReadOnlyList<Collection> Collections { get; } = new[]
    {
        new Collection(DatabaseConstants.EntriesCollection, typeof(DiaryEntry)),
        new Collection(DatabaseConstants.TodosCollection, typeof(TodoItem)),
        new Collection(DatabaseConstants.HabitDefinitionsCollection, typeof(HabitDefinition)),
        new Collection(DatabaseConstants.HarmfulHabitTrackersCollection, typeof(HarmfulHabitTracker)),
        new Collection(DatabaseConstants.ReadingTrackerBooksCollection, typeof(ReadingTrackerBook)),
        new Collection(DatabaseConstants.HappyMomentsCollection, typeof(HappyMoment)),
        new Collection(DatabaseConstants.GoodDeedsCollection, typeof(GoodDeed)),
        new Collection(DatabaseConstants.ProfileCollection, typeof(UserProfile)),
        new Collection(DatabaseConstants.WishlistCollection, typeof(WishlistEntry)),
        new Collection(DatabaseConstants.FinanceCollection, typeof(FinanceTransaction)),
        new Collection(DatabaseConstants.NotesCollection, typeof(Note)),
        new Collection(DatabaseConstants.BudgetsCollection, typeof(Budget)),
        new Collection(DatabaseConstants.AccountsCollection, typeof(Account)),
        new Collection(DatabaseConstants.TransfersCollection, typeof(Transfer)),
        new Collection(DatabaseConstants.RecurringTransactionsCollection, typeof(RecurringTransaction)),
        new Collection(DatabaseConstants.GuidedPromptsCollection, typeof(GuidedPrompt)),
        new Collection(DatabaseConstants.CycleLogsCollection, typeof(CycleLog)),
        new Collection(DatabaseConstants.RecurringTasksCollection, typeof(RecurringTask)),
    };
}
