namespace Diarion.Services.Database;

public static class DatabaseConstants
{
    public const string EntriesCollection = "entries";
    public const string TodosCollection = "todos";
    public const string HabitDefinitionsCollection = "habit_definitions";
    public const string HarmfulHabitTrackersCollection = "harmful_habit_trackers";
    public const string ReadingTrackerBooksCollection = "reading_tracker_books";
    public const string HappyMomentsCollection = "happy_moments";
    public const string GoodDeedsCollection = "good_deeds";
    public const string ProfileCollection = "profile";
    public const string WishlistCollection = "wishlist_entries";
    public const string FinanceCollection = "finance_transactions";
    public const string NotesCollection = "Notes";
    public const string BudgetsCollection = "budgets";
    public const string AccountsCollection = "finance_accounts";
    public const string TransfersCollection = "finance_transfers";
    public const string RecurringTransactionsCollection = "finance_recurring";
    public const string GuidedPromptsCollection = "guided_prompts";
    public const string CycleLogsCollection = "cycle_logs";
    public const string RecurringTasksCollection = "todo_recurring";

    /// <summary>
    /// Embedded chunks of diary entries and notes. Derived data — always rebuildable from the
    /// sources, so it is excluded from export and dropped rather than migrated.
    /// </summary>
    public const string EmbeddingsCollection = "ai_embeddings";
}
