using System;
using System.Diagnostics;

namespace Diarion.Diagnostics;

public static class StartupTrace
{
#if DEBUG
    private static readonly Stopwatch Stopwatch = Stopwatch.StartNew();

    public static IDisposable Measure(string name)
    {
        return new Scope(name);
    }

    public static void Mark(string name)
    {
        Write($"MARK {name}");
    }

    private static void Write(string message)
    {
        var line = $"[StartupTrace +{Stopwatch.ElapsedMilliseconds,5}ms] {message}";
        Debug.WriteLine(line);
        Console.WriteLine(line);
    }

    private sealed class Scope : IDisposable
    {
        private readonly string _name;
        private readonly long _startedAt;

        public Scope(string name)
        {
            _name = name;
            _startedAt = Stopwatch.ElapsedMilliseconds;
            Write($"BEGIN {_name}");
        }

        public void Dispose()
        {
            Write($"END {_name} ({Stopwatch.ElapsedMilliseconds - _startedAt}ms)");
        }
    }
#else
    public static IDisposable Measure(string name)
    {
        return NoopDisposable.Instance;
    }

    public static void Mark(string name)
    {
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
#endif
}