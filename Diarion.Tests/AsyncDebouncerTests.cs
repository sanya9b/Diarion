using System;
using System.Threading;
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
        await Task.Delay(150);
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task Debounce_MultipleCalls_OnlyExecutesLast()
    {
        // Arrange
        var debouncer = new AsyncDebouncer(TimeSpan.FromMilliseconds(100));
        int executionCount = 0;

        // Act — fire back-to-back with no awaits between calls. Each call cancels the previous
        // schedule synchronously, so only the last survives. Avoiding inter-call delays keeps the
        // test deterministic under load (a stretched await could otherwise let an earlier fire run).
        debouncer.Debounce(() => { Interlocked.Increment(ref executionCount); return Task.CompletedTask; });
        debouncer.Debounce(() => { Interlocked.Increment(ref executionCount); return Task.CompletedTask; });
        debouncer.Debounce(() => { Interlocked.Increment(ref executionCount); return Task.CompletedTask; });

        // Nothing can have run yet: Debounce only schedules, and Task.Delay never completes early.
        Volatile.Read(ref executionCount).Should().Be(0);

        // Generous margin past the 100ms window so a slow runner can't fail the "did it fire" check.
        await Task.Delay(400);
        Volatile.Read(ref executionCount).Should().Be(1);
    }

    [Fact]
    public async Task FlushAsync_WhenPending_ExecutesScheduledActionImmediately()
    {
        // Arrange
        var debouncer = new AsyncDebouncer(TimeSpan.FromMilliseconds(500));
        int executionCount = 0;

        // Act
        debouncer.Debounce(() => { executionCount++; return Task.CompletedTask; });
        await debouncer.FlushAsync();

        // Assert - runs immediately, well before the 500ms delay
        executionCount.Should().Be(1);

        // And the (now cancelled) scheduled fire must not run it again later.
        await Task.Delay(600);
        executionCount.Should().Be(1);
    }

    [Fact]
    public async Task FlushAsync_RunsTheActionThatWasScheduled()
    {
        // Proves the debouncer flushes the ACTUAL pending action (capture-safe),
        // rather than a replacement action chosen at flush time.
        var debouncer = new AsyncDebouncer(TimeSpan.FromMilliseconds(500));
        string? result = null;

        debouncer.Debounce(() => { result = "scheduled"; return Task.CompletedTask; });
        await debouncer.FlushAsync();

        result.Should().Be("scheduled");
    }

    [Fact]
    public async Task FlushAsync_WhenNotPending_DoesNothing()
    {
        // Arrange
        var debouncer = new AsyncDebouncer(TimeSpan.FromMilliseconds(100));

        // Act / Assert - no pending action; must not throw and must not run anything
        Func<Task> act = async () => await debouncer.FlushAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FlushAsync_CalledTwice_ExecutesActionOnlyOnce()
    {
        // Arrange
        var debouncer = new AsyncDebouncer(TimeSpan.FromMilliseconds(500));
        int executionCount = 0;

        // Act
        debouncer.Debounce(() => { executionCount++; return Task.CompletedTask; });
        await debouncer.FlushAsync();
        await debouncer.FlushAsync();

        // Assert
        executionCount.Should().Be(1);
    }

    [Fact]
    public async Task ScheduledFireAndFlush_NeverRunConcurrently_AndEachActionRunsOnce()
    {
        // A slow scheduled action must not overlap with a later action forced via flush,
        // and neither action may run more than once (exactly-once + mutual exclusion).
        var debouncer = new AsyncDebouncer(TimeSpan.FromMilliseconds(40));
        int running = 0;
        int maxConcurrent = 0;
        int executionCount = 0;

        Func<Task> MakeSlowAction() => async () =>
        {
            int current = Interlocked.Increment(ref running);
            maxConcurrent = Math.Max(maxConcurrent, current);
            Interlocked.Increment(ref executionCount);
            await Task.Delay(120);
            Interlocked.Decrement(ref running);
        };

        // action1 fires (~40ms) and runs for ~120ms.
        debouncer.Debounce(MakeSlowAction());
        await Task.Delay(70); // action1 is now in flight

        // Schedule action2, then flush it: flush must wait for action1's gate, then run action2 once.
        debouncer.Debounce(MakeSlowAction());
        await debouncer.FlushAsync();

        // Assert
        maxConcurrent.Should().Be(1);
        executionCount.Should().Be(2);
    }
}
