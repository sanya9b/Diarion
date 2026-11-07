using System.Linq;
using Diarion.Models;
using LiteDB;

namespace Diarion.Services.Database.Migrations;

/// <summary>
/// Tops up a library seeded before the <see cref="PromptCategory.Restoration"/> category existed
/// (spec 18, phase 5) with its ten built-in prompts. The seeder only fills an empty collection, so
/// without this every existing user would have a category the selector can pick and nothing in it:
/// a sad, tired day would get no question at all.
///
/// Leaves an empty collection alone — that is a fresh install, and the seeder, which runs right after
/// the migrations, writes all fifty in one order. Idempotent by <see cref="GuidedPrompt.ResourceKey"/>:
/// only keys not yet present are inserted, so a second run adds nothing and a built-in the user has
/// removed is never brought back. New rows take <see cref="GuidedPrompt.Order"/> after the current
/// maximum among built-ins, i.e. the category lands at the end of the library screen, where it also
/// sits in the seed manifest. User-written prompts carry no order and are not counted.
/// </summary>
public sealed class M009_SeedRestorationPrompts : IMigration
{
    public int ToVersion => 9;

    public void Up(LiteDatabase db)
    {
        var prompts = db.GetCollection<GuidedPrompt>(DatabaseConstants.GuidedPromptsCollection);
        if (prompts.Count() == 0) return;

        var existing = prompts.FindAll().ToList();
        var present = existing
            .Select(p => p.ResourceKey)
            .Where(key => !string.IsNullOrEmpty(key))
            .ToHashSet();
        var order = existing
            .Where(p => p.Order != int.MaxValue)
            .Select(p => p.Order)
            .DefaultIfEmpty(-1)
            .Max() + 1;

        var missing = PromptCatalog.SeedKeys[PromptCategory.Restoration]
            .Where(key => !present.Contains(key))
            .Select(key => DatabaseSeeder.BuiltInPrompt(key, PromptCategory.Restoration, order++))
            .ToList();

        if (missing.Count > 0)
        {
            prompts.InsertBulk(missing);
        }
    }
}
