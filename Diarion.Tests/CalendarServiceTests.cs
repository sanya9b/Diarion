using System;
using System.Linq;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class CalendarServiceTests
{
    private readonly CalendarService _service;

    public CalendarServiceTests()
    {
        _service = new CalendarService();
    }

    [Fact]
    public void GenerateCalendarDays_Returns42Days()
    {
        // Arrange
        var date = new DateTime(2023, 10, 15);

        // Act
        var result = _service.GenerateCalendarDays(date);

        // Assert
        result.Should().HaveCount(42);
    }

    [Fact]
    public void GenerateCalendarDays_SetsSelectedDateCorrectly()
    {
        // Arrange
        var date = new DateTime(2023, 10, 15);

        // Act
        var result = _service.GenerateCalendarDays(date);

        // Assert
        var selectedDay = result.SingleOrDefault(d => d.IsSelected);
        selectedDay.Should().NotBeNull();
        selectedDay!.Date.Date.Should().Be(date.Date);
        selectedDay.Day.Should().Be(15);
    }
}