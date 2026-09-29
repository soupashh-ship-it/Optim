using System.Text;
using System.Text.Json;

namespace Optim.Core.Tweaks;

/// <summary>
/// A journaled snapshot of one registry value captured immediately before a
/// mutation. Replaying a journal in reverse restores the prior system state.
/// </summary>
public sealed record JournalEntry(
    string TweakId,
    string Hive,
    string KeyPath,
    string? ValueName,
    RegistryValueHint Kind,
    string? SerializedValue,
    bool ValueExisted);

/// <summary>
/// Append-only change journal stored in %LOCALAPPDATA%\Optim\journal.json.
/// Written BEFORE any mutation (crash-safe ordering). "Revert all" replays
/// entries newest-first and clears the journal afterwards.
/// </summary>
public sealed class ChangeJournal
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Optim", "journal.json");

    private readonly string _path;
    private static readonly object Gate = new();

    /// <summary>
    /// Safety cap on journal size. A decade of daily use at ~10 entries a day
    /// stays far below this; the oldest entries are pruned first. Reverting a
    /// pruned entry is no longer possible, but "revert all" always replays
    /// what remains and pruned entries are the oldest, least-relevant changes.
    /// </summary>
    public const int MaxEntries = 20_000;

    /// <summary>Named mutex so two Optim instances never interleave journal writes.</summary>
    private static Mutex CrossProcessGate(string path)
    {
        var name = "Global\\OptimJournal-" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant())));
        return new Mutex(initiallyOwned: false, name);
    }

    public ChangeJournal(string? path = null) => _path = path ?? DefaultPath;

    public void Append(JournalEntry entry)
    {
        using var mutex = CrossProcessGate(_path);
        var owned = false;
        try
        {
            try { owned = mutex.WaitOne(TimeSpan.FromSeconds(10)); } catch (AbandonedMutexException) { owned = true; }
            if (!owned)
            {
                throw new TimeoutException("Timed out waiting for the change journal.");
            }

            lock (Gate)
            {
                var entries = LoadUnsafe();
                entries.Add(entry);
                if (entries.Count > MaxEntries)
                {
                    // Drop the oldest entries first so the cap stays a bound on
                    // the file, not a surprise data loss event.
                    entries.RemoveRange(0, entries.Count - MaxEntries);
                }
                SaveUnsafe(entries);
            }
        }
        finally
        {
            if (owned)
            {
                try { mutex.ReleaseMutex(); } catch { }
            }
        }
    }

    /// <summary>Truncates the journal back to <paramref name="count"/> entries.</summary>
    public void TruncateTo(int count)
    {
        WithCrossProcessLock(() =>
        {
            var entries = LoadUnsafe();
            if (entries.Count > count)
            {
                entries.RemoveRange(count, entries.Count - count);
                SaveUnsafe(entries);
            }
        });
    }

    public IReadOnlyList<JournalEntry> Snapshot()
    {
        var result = new List<JournalEntry>();
        WithCrossProcessLock(() => result = LoadUnsafe());
        return result;
    }

    /// <summary>Atomically replaces the whole journal (used after partial replays).</summary>
    public void Rewrite(IReadOnlyList<JournalEntry> entries)
    {
        WithCrossProcessLock(() => SaveUnsafe(entries.ToList()));
    }

    public void Clear()
    {
        WithCrossProcessLock(() =>
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        });
    }

    private void WithCrossProcessLock(Action body)
    {
        using var mutex = CrossProcessGate(_path);
        var owned = false;
        try
        {
            try { owned = mutex.WaitOne(TimeSpan.FromSeconds(10)); } catch (AbandonedMutexException) { owned = true; }
            if (!owned)
            {
                throw new TimeoutException("Timed out waiting for the change journal.");
            }

            lock (Gate)
            {
                body();
            }
        }
        finally
        {
            if (owned)
            {
                try { mutex.ReleaseMutex(); } catch { }
            }
        }
    }

    private void SaveUnsafe(List<JournalEntry> entries)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Atomic write: a crash mid-write must never leave a truncated journal.
        var json = JsonSerializer.Serialize(entries, JsonOpts.Indented);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json, Encoding.UTF8);
        File.Move(tmp, _path, overwrite: true);
    }

    private List<JournalEntry> LoadUnsafe()
    {
        if (!File.Exists(_path))
        {
            return new List<JournalEntry>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<JournalEntry>>(
                File.ReadAllText(_path, Encoding.UTF8), JsonOpts.Default) ?? new List<JournalEntry>();
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Journal load failed: {ex.Message}");
            // Quarantine the corrupt file instead of letting the next Append
            // silently discard the entire history.
            try
            {
                var bad = _path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Move(_path, bad);
                Optim.Core.Logging.FileLogger.Warn($"Corrupt journal quarantined to {bad}");
            }
            catch
            {
            }

            return new List<JournalEntry>();
        }
    }
}

internal static class JsonOpts
{
    // String enums keep old numeric journals readable: deserialization still
    // accepts integers (AllowIntegerValues), so existing files keep loading.
    public static readonly JsonSerializerOptions Default = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}
