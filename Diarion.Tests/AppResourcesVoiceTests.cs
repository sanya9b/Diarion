using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Diarion.Resources.Localization;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// Spec 16, decision D1: the app addresses the person as «ви»; only the diary's own questions — the
/// fifty guided prompts — say «ти», because those are words a person says to themselves. The rule
/// used to hold by accident (84 strings one way, 11 strays the other on four screens), so it is pinned
/// here in both directions: no stray «ти» in interface strings, and no «ви» creeping into the prompts.
/// <para>
/// Reads the compiled Ukrainian resource set with <c>tryParents: false</c>, like
/// <see cref="AppResourcesCompletenessTests"/>, so an untranslated key cannot hide behind English.
/// </para>
/// </summary>
public class AppResourcesVoiceTests
{
    private static readonly Regex PromptKey = new(@"^Prompt(Cbt|Gratitude|Open|Savour|Rest)\d{2}$", RegexOptions.Compiled);

    // Pronouns are unambiguous. Ukrainian imperatives are not detectable by suffix without false
    // positives on nouns («назви», «зміни»), so the second group is a curated list of whole words
    // actually used as commands in this app. Extend it when a new verb appears; do not add suffixes.
    private static readonly Regex Informal = new(
        @"(?<![\p{L}'’ʼ])(ти|тобі|тебе|тобою|твій|твоя|твоє|твої|твого|твоєї|твоїх|твою|твоїм|твоєму|" +
        @"створи|обери|вибери|запиши|додай|спробуй|познач|відкрий|натисни|введи|напиши|почни|подивись|переглянь|" +
        @"увімкни|вимкни|зроби|вкажи|збережи|перевір|відмічай|веди|знайди|заповни|постав|налаштуй)(?![\p{L}'’ʼ])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Plural pronouns, plus the plural imperative endings, which are reliable in Ukrainian.
    private static readonly Regex Formal = new(
        @"(?<![\p{L}'’ʼ])(ви|вам|вас|вами|ваш|ваша|ваше|ваші|вашого|вашої|ваших|вашу|вашим|вашій)(?![\p{L}'’ʼ])" +
        @"|\p{L}{2,}(йте|іть|ьте|йтесь|йтеся|іться)(?![\p{L}'’ʼ])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // «навіть» ends in -іть and is not a verb; it is the one everyday word the suffix rule trips on.
    private static readonly Regex NotAVerb = new(@"(?<![\p{L}'’ʼ])навіть(?![\p{L}'’ʼ])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static List<(string Key, string Value)> Ukrainian()
    {
        var set = AppResources.ResourceManager.GetResourceSet(new CultureInfo("uk"), createIfNotExists: true, tryParents: false);
        set.Should().NotBeNull("the Ukrainian resources must be compiled into the assembly");

        return set!.Cast<DictionaryEntry>()
            .Select(e => ((string)e.Key, e.Value as string ?? string.Empty))
            .OrderBy(e => e.Item1)
            .ToList();
    }

    [Fact]
    public void Interface_strings_address_the_person_as_vy()
    {
        var strays = Ukrainian()
            .Where(e => !PromptKey.IsMatch(e.Key) && Informal.IsMatch(e.Value))
            .Select(e => $"{e.Key}: {e.Value}")
            .ToList();

        strays.Should().BeEmpty(
            "interface strings say «ви» (spec 16, D1); only the guided prompts say «ти»");
    }

    [Fact]
    public void Prompts_speak_to_the_reader_as_ty_and_never_as_vy()
    {
        var prompts = Ukrainian().Where(e => PromptKey.IsMatch(e.Key)).ToList();

        // Fifty built-in questions, ten per family; a new family must be added to PromptKey above.
        prompts.Should().HaveCount(50);

        prompts.Where(p => Formal.IsMatch(NotAVerb.Replace(p.Value, " ")))
            .Select(p => $"{p.Key}: {p.Value}")
            .Should().BeEmpty("the diary's questions are self-address and never say «ви»");

        // Not every question names the reader («Що сьогодні вдалося?» is fine), but most do; if this
        // drops, someone has started rewriting the prompts in the impersonal and D1 should be revisited.
        prompts.Count(p => Informal.IsMatch(p.Value)).Should().BeGreaterThan(15);
    }
}
