using System.Collections.Generic;
using System.Linq;
using Diarion.Models;

namespace Diarion.Services;

/// <summary>
/// The seed manifest for the built-in prompts: which resource keys exist and which category each
/// belongs to. Nothing reads this at runtime — <see cref="Database.DatabaseSeeder"/> copies the text
/// out of the resource files into the prompt collection once, and every later read goes to the
/// database so that user-written prompts and built-ins are one uniform list.
///
/// The cost of that uniformity is that seeded wording is frozen per database: reaching existing users
/// with a corrected phrasing or a third language needs a migration keyed on <see cref="GuidedPrompt.ResourceKey"/>.
/// </summary>
public static class PromptCatalog
{
    public static readonly IReadOnlyDictionary<PromptCategory, string[]> SeedKeys =
        new Dictionary<PromptCategory, string[]>
        {
            [PromptCategory.CbtReframe] = Numbered("PromptCbt", 10),
            [PromptCategory.Savouring] = Numbered("PromptSavour", 10),
            [PromptCategory.OpenReflection] = Numbered("PromptOpen", 10),
            [PromptCategory.EveningGratitude] = Numbered("PromptGratitude", 10),
        };

    public static IEnumerable<string> AllKeys => SeedKeys.Values.SelectMany(k => k);

    private static string[] Numbered(string prefix, int count) =>
        Enumerable.Range(1, count).Select(i => $"{prefix}{i:00}").ToArray();
}
