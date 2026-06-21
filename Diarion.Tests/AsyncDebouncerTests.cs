using System;
using System.Threading.Tasks;
using Diarion.Helpers;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class AsyncDebouncerTests
{
    [Fact]
    public async Task Debounce_ExecutesActionAfterDelay()
    {
        // Arrange
        var debouncer = new AsyncDebouncer(TimeSpan.FromMilliseconds(50));
        bool executed = false;

        // Act
        debouncer.Debounce(() =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        // Assert
        executed.Should().BeFalse();
        await Task.Delay(100);
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task Debounce_MultipleCalls_OnlyExecutesLast()
    {
        // Arrange
        var debouncer = new AsyncDebouncer(TimeSpan.FromMilliseconds(100));
        int executionCount = 0;

        // Act
        debouncer.Debounce(() => { executionCount++; return Task.CompletedTask; });
        await Task.Delay(20);
        debouncer.Debounce(() => { executionCount++; return Task.CompletedTask; });
        await Task.Delay(20);
        debouncer.Debounce(() => { executionCount++; return Task.CompletedTask; });

        // Assert
        executionCount.Should().Be(0);
        await Task.Delay(150);
        executionCount.Should().Be(1);
    }

    [Fact]
    public async Task FlushAsync_WhenPending_ExecutesImmediatelyAndCancels()
    {
        // Arrange
        var debouncer = new AsyncDebouncer(TimeSpan.FromMilliseconds(200));
        int executionCount = 0;

        // Act
        debouncer.Debounce(() => { executionCount++; return Task.CompletedTask; });
        await Task.Delay(20); // Not enough time to execute normally

        await debouncer.FlushAsync(() => { executionCount++; return Task.CompletedTask; });

        // Assert
        executionCount.Should().Be(1);

        // Ensure the originally scheduled task was cancelled and doesn't fire later
        await Task.Delay(300);
        executionCount.Should().Be(1);
    }

    [Fact]
    public async Task FlushAsync_WhenNotPending_DoesNotExecute()
    {
        // Arrange
        var debouncer = new AsyncDebouncer(TimeSpan.FromMilliseconds(100));
        int executionCount = 0;

        // Act
        // No debounce called before flush
        await debouncer.FlushAsync(() => { executionCount++; return Task.CompletedTask; });

        // Assert
        executionCount.Should().Be(0);
    }
}
