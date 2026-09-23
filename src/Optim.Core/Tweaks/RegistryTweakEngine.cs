using Microsoft.Win32;

namespace Optim.Core.Tweaks;

/// <summary>
/// Applies tweak definitions against the registry with journaling.
/// Every mutation is preceded by a journal snapshot of the previous value.
/// </summary>
public sealed class RegistryTweakEngine(ChangeJournal journal)
{
    public ChangeJournal Journal { get; } = journal;

    public TweakState Detect(TweakDefinition tweak)
    {
        try
        {
            foreach (var op in tweak.Apply)
            {
                if (!MatchesCurrent(op))
                {
                    return TweakState.NotApplied;
                }
            }

            return TweakState.Applied;
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Detect failed for {tweak.Id}: {ex.Message}");
            return TweakState.Unknown;
        }
    }

    /// <summary>Applies the tweak and journals previous values for every touched key.</summary>
    public void Apply(TweakDefinition tweak)
    {
        foreach (var op in tweak.Apply)
        {
            WriteJournaled(tweak.Id, op);
        }
    }

    /// <summary>Reverts a single tweak using its declared Revert operations.</summary>
    public void Revert(TweakDefinition tweak)
    {
        foreach (var op in tweak.Revert)
        {
            WriteJournaled(tweak.Id, op);
        }
    }

    /// <summary>
    /// Replays the whole journal newest-first, restoring every captured value.
    /// Entries whose original value did not exist get deleted. Journal is cleared on success.
    /// </summary>
    public void RevertAllFromJournal()
    {
        var entries = Journal.Snapshot();
        var failures = 0;
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var e = entries[i];
            try
            {
                var op = new RegistryOperation(
                    e.Hive, e.KeyPath, e.ValueName, e.Kind,
                    Deserialize(e.Kind, e.SerializedValue),
                    DeleteValue: !e.ValueExisted);
                WriteCore(op);
            }
            catch (Exception ex)
            {
                failures++;
                Optim.Core.Logging.FileLogger.Error($"Revert {e.Hive}\\{e.KeyPath} [{e.ValueName}]: {ex.Message}");
            }
        }

        if (failures == 0)
        {
            Journal.Clear();
        }
        else
        {
            throw new InvalidOperationException($"{failures} journaled change(s) could not be reverted — see logs. Journal kept for retry.");
        }
    }

    /// <summary>
    /// Replays journal entries for the given tweak ids newest-first, restoring
    /// the exact pre-Optim values (not the catalog defaults). Replayed entries
    /// are removed from the journal; unrelated entries are kept.
    /// Returns the number of replayed entries.
    /// </summary>
    public int RevertTweaksFromJournal(IReadOnlySet<string> tweakIds)
    {
        var entries = Journal.Snapshot();
        var replayed = new List<JournalEntry>();
        var failures = 0;
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var e = entries[i];
            if (!tweakIds.Contains(e.TweakId))
            {
                continue;
            }

            try
            {
                var op = new RegistryOperation(
                    e.Hive, e.KeyPath, e.ValueName, e.Kind,
                    Deserialize(e.Kind, e.SerializedValue),
                    DeleteValue: !e.ValueExisted);
                WriteCore(op);
                replayed.Add(e);
            }
            catch (Exception ex)
            {
                failures++;
                Optim.Core.Logging.FileLogger.Error($"Revert {e.Hive}\\{e.KeyPath} [{e.ValueName}]: {ex.Message}");
            }
        }

        if (replayed.Count > 0)
        {
            Journal.Rewrite(entries.Except(replayed).ToList());
        }

        if (failures > 0)
        {
            throw new InvalidOperationException($"{failures} journaled change(s) could not be reverted — see logs. Journal kept for retry.");
        }

        return replayed.Count;
    }
    /// <summary>
    /// Deletes one value with journal-before-write, so "Revert all" restores
    /// it. Used by the Policies page for non-catalog policy values. Missing
    /// keys/values are a successful no-op and are not journaled.
    /// </summary>
    public bool DeleteValueJournaled(string tweakId, string hive, string keyPath, string? valueName)
    {
        var root = OpenRoot(hive);
        RegistryValueHint kind = RegistryValueHint.String;
        var existed = false;
        using (var key = root.OpenSubKey(keyPath))
        {
            if (key is not null && SafeGetValue(key, valueName) is not null)
            {
                existed = true;
                kind = GuessKind(valueName, key);
            }
        }

        if (!existed)
        {
            return true;
        }

        var op = new RegistryOperation(hive, keyPath, valueName, kind, null, DeleteValue: true);
        var count = Journal.Snapshot().Count;
        Journal.Append(Capture(tweakId, op));
        try
        {
            WriteCore(op);
        }
        catch
        {
            Journal.TruncateTo(count);
            throw;
        }

        Optim.Core.Logging.FileLogger.Info($"Deleted {hive}\\{keyPath} [{valueName ?? "(Default)"}] (journaled as {tweakId}).");
        return true;
    }

    /// <summary>
    /// Deletes a key tree only when it holds no values and no subkeys, and
    /// only when it sits strictly below one of <paramref name="protectedRoots"/>
    /// (roots themselves are never removed). Empty keys journal nothing.
    /// </summary>
    public bool DeleteKeyTreeIfEmpty(string hive, string keyPath, IEnumerable<string> protectedRoots)
    {
        var guarded = protectedRoots.Any(r =>
            keyPath.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase));
        if (!guarded)
        {
            return false;
        }

        var root = OpenRoot(hive);
        using (var key = root.OpenSubKey(keyPath))
        {
            if (key is null)
            {
                return true;
            }

            if (SafeValueNames(key).Length > 0 || SafeSubKeyNames(key).Length > 0)
            {
                return false;
            }
        }

        root.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        Optim.Core.Logging.FileLogger.Info($"Removed empty policy key {hive}\\{keyPath}.");
        return true;
    }

    private void WriteJournaled(string tweakId, RegistryOperation op)
    {
        // DeleteKey removes a whole subtree: snapshot every value beneath it
        // first, so journal replay can restore pre-existing content.
        if (op.DeleteKey)
        {
            var before = Journal.Snapshot().Count;
            try
            {
                foreach (var captured in CaptureSubtree(tweakId, op))
                {
                    Journal.Append(captured);
                }

                WriteCore(op);
            }
            catch
            {
                Journal.TruncateTo(before);
                throw;
            }

            return;
        }

        var count = Journal.Snapshot().Count;
        Journal.Append(Capture(tweakId, op));
        try
        {
            WriteCore(op);
        }
        catch
        {
            // Journal-before-write is crash-safe, but a failed write must not
            // pollute the journal with values that never changed.
            Journal.TruncateTo(count);
            throw;
        }
    }

    private static IEnumerable<JournalEntry> CaptureSubtree(string tweakId, RegistryOperation op)
    {
        var root = OpenRoot(op.Hive);
        using var key = root.OpenSubKey(op.KeyPath);
        if (key is null)
        {
            yield break;
        }

        foreach (var valueName in SafeValueNames(key))
        {
            var raw = SafeGetValue(key, valueName);
            yield return raw is null
                ? new JournalEntry(tweakId, op.Hive, op.KeyPath, valueName, GuessKind(valueName, key), null, ValueExisted: false)
                : new JournalEntry(tweakId, op.Hive, op.KeyPath, valueName, GuessKind(valueName, key), Serialize(raw), ValueExisted: true);
        }

        foreach (var sub in SafeSubKeyNames(key))
        {
            foreach (var inner in CaptureSubtree(tweakId, op with { KeyPath = op.KeyPath + "\\" + sub }))
            {
                yield return inner;
            }
        }
    }

    private static string[] SafeValueNames(RegistryKey key)
    {
        try { return key.GetValueNames(); }
        catch { return Array.Empty<string>(); }
    }

    private static string[] SafeSubKeyNames(RegistryKey key)
    {
        try { return key.GetSubKeyNames(); }
        catch { return Array.Empty<string>(); }
    }

    private static object? SafeGetValue(RegistryKey key, string? valueName)
    {
        try { return key.GetValue(valueName ?? "", null, RegistryValueOptions.DoNotExpandEnvironmentNames); }
        catch { return null; }
    }

    private static RegistryValueHint GuessKind(string? valueName, RegistryKey key)
    {
        try
        {
            return key.GetValueKind(valueName ?? "") switch
            {
                RegistryValueKind.DWord => RegistryValueHint.DWord,
                RegistryValueKind.QWord => RegistryValueHint.QWord,
                RegistryValueKind.MultiString => RegistryValueHint.MultiString,
                RegistryValueKind.ExpandString => RegistryValueHint.ExpandString,
                RegistryValueKind.Binary => RegistryValueHint.Binary,
                _ => RegistryValueHint.String
            };
        }
        catch
        {
            return RegistryValueHint.String;
        }
    }

    private static JournalEntry Capture(string tweakId, RegistryOperation op)
    {
        // NOTE: root is a shared process-wide handle (Registry.LocalMachine /
        // Registry.CurrentUser) and must NOT be disposed. Only subkeys are owned here.
        var root = OpenRoot(op.Hive);
        using var key = root.OpenSubKey(op.KeyPath);
        if (key is null)
        {
            return new JournalEntry(tweakId, op.Hive, op.KeyPath, op.ValueName, op.Kind, null, ValueExisted: false);
        }

        var raw = SafeGetValue(key, op.ValueName);
        if (raw is null)
        {
            return new JournalEntry(tweakId, op.Hive, op.KeyPath, op.ValueName, op.Kind, null, ValueExisted: false);
        }

        return new JournalEntry(tweakId, op.Hive, op.KeyPath, op.ValueName, op.Kind, Serialize(raw), ValueExisted: true);
    }

    private static void WriteCore(RegistryOperation op)
    {
        if (!op.DeleteValue && !op.DeleteKey && op.Value is null)
        {
            throw new ArgumentException($"Tweak operation on {op.Hive}\\{op.KeyPath} has a null value without DeleteValue/DeleteKey.");
        }

        var root = OpenRoot(op.Hive);

        if (op.DeleteKey)
        {
            root.DeleteSubKeyTree(op.KeyPath, throwOnMissingSubKey: false);
            return;
        }

        using var key = root.CreateSubKey(op.KeyPath, writable: true);
        if (key is null)
        {
            throw new InvalidOperationException($"Could not open {op.Hive}\\{op.KeyPath} for writing.");
        }

        if (op.DeleteValue)
        {
            key.DeleteValue(op.ValueName ?? string.Empty, throwOnMissingValue: false);
            return;
        }

        switch (op.Kind)
        {
            case RegistryValueHint.DWord:
                key.SetValue(op.ValueName ?? "", Convert.ToInt32(op.Value), RegistryValueKind.DWord);
                break;
            case RegistryValueHint.QWord:
                key.SetValue(op.ValueName ?? "", Convert.ToInt64(op.Value), RegistryValueKind.QWord);
                break;
            case RegistryValueHint.Binary:
                key.SetValue(op.ValueName ?? "", (byte[])op.Value!, RegistryValueKind.Binary);
                break;
            case RegistryValueHint.ExpandString:
                key.SetValue(op.ValueName ?? "", op.Value ?? "", RegistryValueKind.ExpandString);
                break;
            case RegistryValueHint.MultiString:
                key.SetValue(op.ValueName ?? "", (string[])op.Value!, RegistryValueKind.MultiString);
                break;
            default:
                key.SetValue(op.ValueName ?? "", op.Value ?? "", RegistryValueKind.String);
                break;
        }
    }

    private bool MatchesCurrent(RegistryOperation op)
    {
        var root = OpenRoot(op.Hive);
        using var key = root.OpenSubKey(op.KeyPath);
        if (key is null)
        {
            return false;
        }

        var current = SafeGetValue(key, op.ValueName);
        if (current is null)
        {
            return false;
        }

        // Empty-string REG_SZ default values (e.g. the classic-context-menu
        // marker) read back as "" while the operation may carry null: treat
        // them as equal when both are empty so Detect can report Applied.
        if (current is string currentText && string.IsNullOrEmpty(currentText))
        {
            var wantText = op.Value?.ToString();
            if (string.IsNullOrEmpty(wantText))
            {
                return true;
            }
        }

        return op.Kind switch
        {
            RegistryValueHint.DWord => Convert.ToInt32(current) == Convert.ToInt32(op.Value),
            RegistryValueHint.QWord => Convert.ToInt64(current) == Convert.ToInt64(op.Value),
            RegistryValueHint.Binary => current is byte[] a && op.Value is byte[] b && a.SequenceEqual(b),
            RegistryValueHint.MultiString => current is string[] arr && op.Value is string[] want && arr.SequenceEqual(want),
            _ => string.Equals(current?.ToString(), op.Value?.ToString(), StringComparison.OrdinalIgnoreCase)
        };
    }

    private static RegistryKey OpenRoot(string hive) => hive switch
    {
        RegistryOperation.HKLM => Registry.LocalMachine,
        RegistryOperation.HKCU => Registry.CurrentUser,
        _ => throw new ArgumentException($"Unknown hive {hive}")
    };

    private static string? Serialize(object value) => value switch
    {
        byte[] bytes => Convert.ToBase64String(bytes),
        string[] arr => string.Join('\u001F', arr),
        int i => i.ToString(),
        long l => l.ToString(),
        _ => value.ToString()
    };

    private static object? Deserialize(RegistryValueHint kind, string? value) => kind switch
    {
        RegistryValueHint.DWord when int.TryParse(value, out var i) => i,
        RegistryValueHint.QWord when long.TryParse(value, out var l) => l,
        RegistryValueHint.Binary when !string.IsNullOrEmpty(value) => Convert.FromBase64String(value),
        RegistryValueHint.MultiString when value is not null => value.Split('\u001F'),
        _ => value
    };
}
