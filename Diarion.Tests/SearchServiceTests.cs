using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Search;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// Word search over the diary and notes — what is left of search after the on-device encoder went.
/// </summary>
public class SearchServiceTests
{
    private readonly Mock<IDiaryService> _diary = new();
    private readonly Mock<INoteService> _notes = new();
    private readonly SearchService _service;

    public SearchServiceTests()
    {
        _diary.Setup(d => d.GetAllEntriesAsync()).ReturnsAsync(new List<DiaryEntry>());
        _notes.Setup(n => n.SearchNotesAsync(It.IsAny<string>())).ReturnsAsync(new List<Note>());
        _service = new SearchService(_diary.Object, _notes.Object);
    }

    private void Diary(params DiaryEntry[] entries) =>
        _diary.Setup(d => d.GetAllEntriesAsync()).ReturnsAsync(entries.ToList());

    [Fact]
    public async Task Diary_MatchesEveryWord_InAnyTypedField()
    {
        Diary(
            new DiaryEntry { Date = new DateTime(2026, 9, 2), Gratitude = "Вдячна за спокійний вечір" },
            new DiaryEntry { Date = new DateTime(2026, 9, 3), Content = "Спокійний ранок, але без вечора" });

        var hits = await _service.SearchAsync("спокійний вечір", SearchScope.Diary);

        hits.Should().ContainSingle().Which.Snippet.Should().Be("Вдячна за спокійний вечір");
        hits[0].SourceKind.Should().Be(DocumentKind.Diary);
    }

    [Fact]
    public async Task Diary_SkipsDaysWithNothingWritten()
    {
        // Opening a day persists a blank row; it has no words to match and must not come back.
        Diary(new DiaryEntry { Date = new DateTime(2026, 9, 2) });

        var hits = await _service.SearchAsync("будь-що", SearchScope.Diary);

        hits.Should().BeEmpty();
    }

    [Fact]
    public async Task Notes_ComeFromTheNotesOwnSearch()
    {
        var id = LiteDB.ObjectId.NewObjectId();
        _notes.Setup(n => n.SearchNotesAsync("ідея"))
              .ReturnsAsync(new List<Note> { new() { Id = id, Title = "Ідеї", Content = "ідея для подарунка" } });

        var hits = await _service.SearchAsync("ідея", SearchScope.Notes);

        hits.Should().ContainSingle().Which.SourceId.Should().Be(id.ToString());
        hits[0].SourceKind.Should().Be(DocumentKind.Note);
        _diary.Verify(d => d.GetAllEntriesAsync(), Times.Never, "the diary is out of scope");
    }

    [Fact]
    public async Task All_IsNewestFirst_AndCappedAtTheLimit()
    {
        Diary(Enumerable.Range(1, 5)
            .Select(day => new DiaryEntry { Date = new DateTime(2026, 9, day), Content = $"кава {day}" })
            .ToArray());

        var hits = await _service.SearchAsync("кава", limit: 3);

        hits.Select(h => h.SourceDate.Day).Should().Equal(5, 4, 3);
    }

    [Fact]
    public async Task BlankQuery_FindsNothing_AndReadsNothing()
    {
        var hits = await _service.SearchAsync("   ");

        hits.Should().BeEmpty();
        _diary.Verify(d => d.GetAllEntriesAsync(), Times.Never);
    }

    [Fact]
    public void EntryText_TakesTypedFieldsInReadingOrder_AndJoinsTheMeals()
    {
        var entry = new DiaryEntry
        {
            Content = "  день  ",
            Gratitude = "за каву",
            BreakfastFood = "вівсянка",
            DinnerFood = "борщ",
        };

        EntryText.Segments(entry).Should().Equal("день", "за каву", "вівсянка, борщ");
    }
}
