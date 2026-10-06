using System;
using System.IO;
using System.Linq;
using Diarion.Models;
using Diarion.Services.Database;
using FluentAssertions;
using LiteDB;
using Xunit;

namespace Diarion.Tests;

public class DatabaseSeederPromptTests
{
    private static ILiteCollection<GuidedPrompt> Prompts(LiteDatabase db) =>
        db.GetCollection<GuidedPrompt>(DatabaseConstants.GuidedPromptsCollection);

    [Fact]
    public void Seed_CreatesEveryBuiltInPromptInBothLanguages()
    {
        using var db = new LiteDatabase(new MemoryStream());

        new DatabaseSeeder().Seed(db);

        var seeded = Prompts(db).FindAll().ToList();
        seeded.Should().HaveCount(40);
        seeded.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.TextEn));
        seeded.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.TextUk));
        seeded.Should().OnlyContain(p => !string.IsNullOrEmpty(p.ResourceKey));
    }

    [Fact]
    public void Seed_StoresTheTwoLanguagesSeparately()
    {
        using var db = new LiteDatabase(new MemoryStream());

        new DatabaseSeeder().Seed(db);

        // A missing Ukrainian satellite assembly would silently seed English into both fields, and no
        // later fix could reach a database already written that way.
        Prompts(db).FindAll().Should().OnlyContain(p => p.TextUk != p.TextEn);
    }

    [Fact]
    public void Seed_IsActiveForHistoricEntries()
    {
        using var db = new LiteDatabase(new MemoryStream());

        new DatabaseSeeder().Seed(db);

        Prompts(db).FindAll().Should().OnlyContain(p => p.CreatedAt == DateTime.MinValue);
    }

    [Fact]
    public void Seed_RunTwice_DoesNotDuplicate()
    {
        using var db = new LiteDatabase(new MemoryStream());

        new DatabaseSeeder().Seed(db);
        new DatabaseSeeder().Seed(db);

        Prompts(db).Count().Should().Be(40);
    }

    [Fact]
    public void Seed_LeavesAnExistingLibraryAlone()
    {
        using var db = new LiteDatabase(new MemoryStream());
        Prompts(db).Insert(new GuidedPrompt { TextEn = "mine", Category = PromptCategory.OpenReflection });

        new DatabaseSeeder().Seed(db);

        // Someone who has already curated their library must not have forty built-ins poured back in.
        Prompts(db).Count().Should().Be(1);
    }
}
