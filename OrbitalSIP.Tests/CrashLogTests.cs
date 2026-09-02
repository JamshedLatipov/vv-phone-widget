using System;
using System.IO;
using System.Linq;
using OrbitalSIP.Services.Logging;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// The crash log's own retention.
///
/// It used to be a bare File.AppendAllText onto an undated crash.log, which put it outside
/// every policy the other two logs follow: LogRetention cannot date it, so nothing ever
/// swept it, and nothing bounded it. A support copy pulled off an operator's machine held
/// four months of unobserved-task exceptions in one 2.9 MB file — larger than the three days
/// of app and SIP logs it arrived with, and none of it recent enough to be worth reading.
/// </summary>
public class CrashLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "OrbitalSIP-crashlog-" + Guid.NewGuid().ToString("N"));

    private string Base => Path.Combine(_dir, "crash.log");
    private static readonly DateTime Today = new(2026, 8, 22);

    public CrashLogTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string[] Files() =>
        Directory.GetFiles(_dir).Select(Path.GetFileName).OrderBy(n => n).ToArray()!;

    [Fact]
    public void WritesIntoTheFileForToday()
    {
        CrashLog.Write(Base, Today, "UnhandledException", "boom", LogRetention.DefaultKeepDays);

        Assert.Equal(new[] { "crash-2026-08-22.log" }, Files());
        Assert.Contains("boom", File.ReadAllText(Path.Combine(_dir, "crash-2026-08-22.log")));
    }

    [Fact]
    public void SweepsCrashFilesThatHaveFallenOutOfTheWindow()
    {
        File.WriteAllText(Path.Combine(_dir, "crash-2026-05-02.log"), "four months of this");

        CrashLog.Write(Base, Today, "UnhandledException", "boom", LogRetention.DefaultKeepDays);

        Assert.DoesNotContain("crash-2026-05-02.log", Files());
    }

    /// <summary>
    /// The undated file every existing installation already has. Deleting it outright on a
    /// guess is not this class's call; dating it is what folds it into the window so the next
    /// sweep can take it.
    /// </summary>
    [Fact]
    public void AdoptsTheLegacyUndatedFile()
    {
        var legacy = Path.Combine(_dir, "crash.log");
        File.WriteAllText(legacy, "written before the log was dated");
        File.SetLastWriteTime(legacy, Today.AddDays(-1));

        CrashLog.Write(Base, Today, "UnhandledException", "boom", LogRetention.DefaultKeepDays);

        Assert.DoesNotContain("crash.log", Files());
        Assert.Contains("crash-2026-08-21.log", Files());
    }
}
