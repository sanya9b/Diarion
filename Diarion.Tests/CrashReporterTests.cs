using System;
using System.IO;
using Diarion.Diagnostics;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The reporter only ever runs while the process is dying, so the qualities that matter are that it
/// captures the cause and that it cannot itself throw. Both are asserted here rather than discovered
/// on a phone.
/// </summary>
public class CrashReporterTests : IDisposable
{
    private readonly string _dir;

    public CrashReporterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "diarion_crash_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private sealed class FakeFileSystem : IFileSystemService
    {
        public FakeFileSystem(string dir) => AppDataDirectory = dir;
        public string CacheDirectory => AppDataDirectory;
        public string AppDataDirectory { get; }
    }

    private CrashReporter NewReporter(string? dir = null)
        => new(new FakeFileSystem(dir ?? _dir), "1.2.3");

    private static Exception Thrown(Action action)
    {
        try { action(); }
        catch (Exception ex) { return ex; }
        throw new InvalidOperationException("expected the action to throw");
    }

    [Fact]
    public void A_recorded_crash_survives_to_be_read_back()
    {
        var reporter = NewReporter();
        reporter.HasReport.Should().BeFalse();

        reporter.Record("startup", Thrown(() => throw new InvalidOperationException("boom")));

        reporter.HasReport.Should().BeTrue();
        reporter.ReportPath.Should().NotBeNull().And.Match(p => File.Exists(p!));

        var report = reporter.ReadLast();
        report.Should().NotBeNull();
        report.Should().Contain("InvalidOperationException").And.Contain("boom").And.Contain("startup");
        report.Should().Contain("1.2.3", "the version is what ties a report to a build");
    }

    [Fact]
    public void The_inner_exception_is_kept_because_that_is_where_the_cause_lives()
    {
        // A linker or AOT failure surfaces as a TypeInitializationException with the real problem —
        // the missing member — underneath it. Reporting only the outer one would say nothing useful.
        var reporter = NewReporter();
        var inner = Thrown(() => throw new MissingMethodException("Model..ctor was trimmed"));
        var outer = new TypeInitializationException("Diarion.Models.DiaryEntry", inner);

        reporter.Record("test", outer);

        reporter.ReadLast().Should()
            .Contain("TypeInitializationException")
            .And.Contain("MissingMethodException")
            .And.Contain("was trimmed");
    }

    [Fact]
    public void Clearing_removes_the_report()
    {
        var reporter = NewReporter();
        reporter.Record("test", new Exception("x"));

        reporter.Clear();

        reporter.HasReport.Should().BeFalse();
        reporter.ReadLast().Should().BeNull();
        reporter.ReportPath.Should().BeNull();
    }

    [Fact]
    public void The_newest_crash_replaces_the_previous_one()
    {
        var reporter = NewReporter();
        reporter.Record("first", new Exception("older failure"));
        reporter.Record("second", new Exception("newer failure"));

        var report = reporter.ReadLast();
        report.Should().Contain("newer failure");
        report.Should().NotContain("older failure");
    }

    [Fact]
    public void Recording_never_throws_even_when_it_cannot_write()
    {
        // A reporter that throws turns a diagnosable crash into an undiagnosable one.
        var unwritable = Path.Combine(_dir, "file-where-a-directory-should-be");
        File.WriteAllText(unwritable, "not a directory");
        var reporter = NewReporter(unwritable);

        var record = () => reporter.Record("test", new Exception("x"));

        record.Should().NotThrow();
        reporter.ReadLast().Should().BeNull();
    }

    [Fact]
    public void A_handler_firing_without_an_exception_still_produces_a_report()
    {
        // AppDomain.UnhandledException carries an object, not necessarily an Exception.
        var reporter = NewReporter();

        reporter.Record("AppDomain.UnhandledException", null);

        reporter.ReadLast().Should().NotBeNull().And.Contain("No exception object");
    }

    [Fact]
    public void A_runaway_stack_is_truncated_rather_than_written_whole()
    {
        var reporter = NewReporter();
        var huge = new Exception(new string('x', CrashReport.MaxLength * 2));

        reporter.Record("test", huge);

        reporter.ReadLast()!.Length.Should().BeLessThan(CrashReport.MaxLength + 100);
    }
}
