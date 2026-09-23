using Optim.Core.Tweaks;
using Xunit;

namespace Optim.Core.Tests;

/// <summary>
/// Journal tests run against a throwaway file so they never touch the real
/// user journal in %LOCALAPPDATA%\Optim.
/// </summary>
public class ChangeJournalTests : IDisposable
{
    private readonly string _journalPath;

    public ChangeJournalTests()
    {
        _journalPath = Path.Combine(Path.GetTempPath(), "optim-tests-" + Guid.NewGuid().ToString("N"), "journal.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_journalPath)!);
    }

    [Fact]
    public void Append_then_snapshot_round_trips_entries()
    {
        var journal = new ChangeJournal(_journalPath);
        var entry = new JournalEntry("t1", "HKCU", @"Software\OptimTest", "Value", RegistryValueHint.DWord, "42", ValueExisted: true);
        journal.Append(entry);

        var snapshot = journal.Snapshot();
        var last = snapshot.Last();
        Assert.Equal("t1", last.TweakId);
        Assert.Equal("42", last.SerializedValue);
        Assert.True(last.ValueExisted);
    }

    [Fact]
    public void Clear_removes_all_entries()
    {
        var journal = new ChangeJournal(_journalPath);
        journal.Append(new JournalEntry("t", "HKCU", @"Software\OptimTest", null, RegistryValueHint.String, "x", true));
        journal.Clear();
        Assert.Empty(journal.Snapshot());
    }

    [Fact]
    public void Corrupt_journal_file_is_tolerated()
    {
        File.WriteAllText(_journalPath, "{ this is not json");
        // The corrupt-file path logs an error; keep unit runs out of the real app log.
        var wasEnabled = Optim.Core.Logging.FileLogger.Enabled;
        Optim.Core.Logging.FileLogger.Enabled = false;
        try
        {
            var journal = new ChangeJournal(_journalPath);
            Assert.Empty(journal.Snapshot());
        }
        finally
        {
            Optim.Core.Logging.FileLogger.Enabled = wasEnabled;
        }
    }

    [Fact]
    public void Multiple_appends_accumulate_in_order()
    {
        var journal = new ChangeJournal(_journalPath);
        journal.Append(new JournalEntry("a", "HKCU", "K1", null, RegistryValueHint.DWord, "1", true));
        journal.Append(new JournalEntry("b", "HKCU", "K2", null, RegistryValueHint.DWord, "2", false));
        journal.Append(new JournalEntry("c", "HKCU", "K3", null, RegistryValueHint.DWord, "3", true));

        var snapshot = journal.Snapshot();
        Assert.Equal(3, snapshot.Count);
        Assert.Equal(new[] { "a", "b", "c" }, snapshot.Select(e => e.TweakId));
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_journalPath)!, recursive: true); } catch { }
    }
}
