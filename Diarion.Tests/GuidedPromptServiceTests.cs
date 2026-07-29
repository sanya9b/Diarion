using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Database;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class GuidedPromptServiceTests : IDisposable
{
    private readonly DatabaseContext _dbContext;
    private readonly GuidedPromptService _service;

    public GuidedPromptServiceTests()
    {
        // No seeder: these cover the CRUD surface, not the built-in library.
        _dbContext = new DatabaseContext(useInMemory: true);
        _service = new GuidedPromptService(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    private static GuidedPrompt UserPrompt(string text) =>
        new() { TextUk = text, TextEn = text, Category = PromptCategory.OpenReflection };

    [Fact]
    public async Task AddAsync_PlacesTheNewPromptLast()
    {
        var first = UserPrompt("first");
        var second = UserPrompt("second");

        await _service.AddAsync(first);
        await _service.AddAsync(second);

        var library = await _service.GetLibraryAsync();
        library.Ordered.Select(p => p.Id).Should().Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task UpdateAsync_PersistsTheEditedText()
    {
        var prompt = UserPrompt("before");
        await _service.AddAsync(prompt);

        prompt.TextEn = "after";
        await _service.UpdateAsync(prompt);

        (await _service.GetByIdAsync(prompt.Id))!.TextEn.Should().Be("after");
    }

    [Fact]
    public async Task DeleteAsync_IsSoft_SoTheRowStillResolves()
    {
        var prompt = UserPrompt("to be removed");
        await _service.AddAsync(prompt);

        await _service.DeleteAsync(prompt.Id, new DateTime(2026, 7, 15));

        var library = await _service.GetLibraryAsync();
        library.Ordered.Should().NotContain(p => p.Id == prompt.Id);
        library.Find(prompt.Id.ToString()).Should().NotBeNull(
            "an entry that answered it must still be able to show the question");
    }

    [Fact]
    public async Task UpdateOrderAsync_RenumbersFromZero()
    {
        var first = UserPrompt("first");
        var second = UserPrompt("second");
        await _service.AddAsync(first);
        await _service.AddAsync(second);

        await _service.UpdateOrderAsync(new List<Guid> { second.Id, first.Id });

        (await _service.GetLibraryAsync()).Ordered.First().Id.Should().Be(second.Id);
        (await _service.GetByIdAsync(first.Id))!.Order.Should().Be(1);
    }

    [Fact]
    public async Task GetLibraryAsync_OnAFreshDatabase_IsEmpty()
    {
        (await _service.GetLibraryAsync()).All.Should().BeEmpty();
    }
}
