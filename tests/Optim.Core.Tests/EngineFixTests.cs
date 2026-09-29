using Microsoft.Win32;
using Optim.Core.Debloat;
using Optim.Core.Policies;
using Optim.Core.Processes;
using Optim.Core.Security;
using Optim.Core.ServicesMgmt;
using Optim.Core.Tweaks;
using Xunit;

namespace Optim.Core.Tests;

/// <summary>Regression tests for the deep-audit fixes. Registry tests use a
/// throwaway HKCU key that is always cleaned up.</summary>
public class EngineFixTests : IDisposable
{
    private const string TestRoot = @"Software\OptimAuditTests";
    private readonly string _journalDir = Path.Combine(Path.GetTempPath(), "optim-audit-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(TestRoot, throwOnMissingSubKey: false); } catch { }
        try { Directory.Delete(_journalDir, recursive: true); } catch { }
    }

    private RegistryTweakEngine Engine()
    {
        Directory.CreateDirectory(_journalDir);
        return new RegistryTweakEngine(new ChangeJournal(Path.Combine(_journalDir, "journal.json")));
    }

    private static TweakDefinition DwordTweak(string id, string valueName) => new(
        id, TweakCategory.Optimize, id, "audit",
        new[] { new RegistryOperation(RegistryOperation.HKCU, TestRoot, valueName, RegistryValueHint.DWord, 1) },
        new[] { new RegistryOperation(RegistryOperation.HKCU, TestRoot, valueName, RegistryValueHint.DWord, 0) });

    [Fact]
    public void Repeated_apply_detect_revert_does_not_break_registry_access()
    {
        // Guards the shared-hive dispose bug: the 2nd operation used to throw
        // ObjectDisposedException because the engine disposed Registry.CurrentUser.
        var engine = Engine();
        var tweak = DwordTweak("audit.repeat", "Repeat");
        for (var i = 0; i < 3; i++)
        {
            engine.Apply(tweak);
            Assert.Equal(TweakState.Applied, engine.Detect(tweak));
            engine.Revert(tweak);
            Assert.Equal(TweakState.NotApplied, engine.Detect(tweak));
        }
    }

    [Fact]
    public void Failed_write_does_not_pollute_journal()
    {
        var engine = Engine();
        var before = engine.Journal.Snapshot().Count;
        var bad = new TweakDefinition("audit.bad", TweakCategory.Optimize, "bad", "audit",
            new[] { new RegistryOperation("BOGUS", TestRoot, "V", RegistryValueHint.DWord, 1) },
            new[] { new RegistryOperation("BOGUS", TestRoot, "V", RegistryValueHint.DWord, 0) });
        Assert.Throws<ArgumentException>(() => engine.Apply(bad));
        Assert.Equal(before, engine.Journal.Snapshot().Count);
    }

    [Fact]
    public void Revert_all_restores_values_and_clears_journal()
    {
        var engine = Engine();
        var tweak = DwordTweak("audit.revertall", "RevertAll");
        engine.Apply(tweak);
        engine.RevertAllFromJournal();
        Assert.Empty(engine.Journal.Snapshot());
        using var key = Registry.CurrentUser.OpenSubKey(TestRoot);
        Assert.Null(key?.GetValue("RevertAll"));
    }

    [Fact]
    public void Selective_revert_restores_only_requested_tweaks()
    {
        var engine = Engine();
        var first = DwordTweak("audit.sel-a", "SelA");
        var second = DwordTweak("audit.sel-b", "SelB");
        engine.Apply(first);
        engine.Apply(second);

        var replayed = engine.RevertTweaksFromJournal(new HashSet<string> { "audit.sel-a" });
        Assert.Equal(1, replayed);

        // SelA restored to absent; SelB untouched and still journaled.
        using var key = Registry.CurrentUser.OpenSubKey(TestRoot);
        Assert.Null(key?.GetValue("SelA"));
        Assert.Equal(1, (int)key!.GetValue("SelB")!);
        Assert.Single(engine.Journal.Snapshot());
        Assert.Equal("audit.sel-b", engine.Journal.Snapshot()[0].TweakId);
    }

    [Fact]
    public void Corrupt_journal_is_quarantined_not_silently_dropped()
    {
        Directory.CreateDirectory(_journalDir);
        var path = Path.Combine(_journalDir, "journal.json");
        File.WriteAllText(path, "{ corrupt");
        var wasEnabled = Optim.Core.Logging.FileLogger.Enabled;
        Optim.Core.Logging.FileLogger.Enabled = false;
        try
        {
            var journal = new ChangeJournal(path);
            Assert.Empty(journal.Snapshot());
            Assert.False(File.Exists(path));
            Assert.Single(Directory.GetFiles(_journalDir, "*.corrupt-*"));
        }
        finally
        {
            Optim.Core.Logging.FileLogger.Enabled = wasEnabled;
        }
    }

    [Fact]
    public void Journal_truncate_keeps_prefix()
    {
        Directory.CreateDirectory(_journalDir);
        var journal = new ChangeJournal(Path.Combine(_journalDir, "journal.json"));
        journal.Append(new JournalEntry("a", "HKCU", "K", null, RegistryValueHint.DWord, "1", true));
        journal.Append(new JournalEntry("b", "HKCU", "K", null, RegistryValueHint.DWord, "2", true));
        journal.TruncateTo(1);
        var snap = journal.Snapshot();
        var single = Assert.Single(snap);
        Assert.Equal("a", single.TweakId);
    }

    [Fact]
    public void Service_vetting_rejects_null_and_blank()
    {
        Assert.False(ServiceEngine.IsVetted(null));
        Assert.False(ServiceEngine.IsVetted(""));
        Assert.False(ServiceEngine.IsVetted("RpcSs"));
        Assert.True(ServiceEngine.IsVetted("DiagTrack"));
    }

    [Fact]
    public void Service_set_rejects_unknown_mode_before_touching_registry()
    {
        var engine = new ServiceEngine();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => engine.SetStartType("DiagTrack", (System.ServiceProcess.ServiceStartMode)99));
    }

    [Fact]
    public void Service_set_reports_missing_service_as_failure()
    {
        var engine = new ServiceEngine();
        // A service that does not exist is not boot-critical, so the guard
        // passes and the registry lookup reports failure instead of throwing.
        Assert.False(engine.SetStartType("NoSuchServiceXYZ", System.ServiceProcess.ServiceStartMode.Manual));
    }

    [Fact]
    public void Service_set_refuses_boot_critical_services()
    {
        var engine = new ServiceEngine();
        Assert.Throws<InvalidOperationException>(
            () => engine.SetStartType("RpcSs", System.ServiceProcess.ServiceStartMode.Manual));
        Assert.Throws<InvalidOperationException>(
            () => engine.SetStartType("WinDefend", System.ServiceProcess.ServiceStartMode.Disabled));
    }

    [Fact]
    public void Service_critical_set_covers_boot_and_security_services()
    {
        Assert.True(ServiceEngine.IsCritical("RpcSs"));
        Assert.True(ServiceEngine.IsCritical("DcomLaunch"));
        Assert.True(ServiceEngine.IsCritical("SamSs"));
        Assert.True(ServiceEngine.IsCritical("WinDefend"));
        Assert.True(ServiceEngine.IsCritical("MpsSvc"));
        Assert.True(ServiceEngine.IsCritical("EventLog"));
        Assert.False(ServiceEngine.IsCritical("DiagTrack"));
        Assert.False(ServiceEngine.IsCritical("SysMain"));
        Assert.False(ServiceEngine.IsCritical(null));
        Assert.False(ServiceEngine.IsCritical(""));
    }

    [Fact]
    public void Security_read_returns_all_items_without_throwing()
    {
        var items = SecurityPostureEngine.Read();
        Assert.Equal(6, items.Count);
        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.State)));
    }

    [Fact]
    public void Process_critical_guard_covers_system_ids_and_names()
    {
        Assert.True(ProcessEngine.IsCritical(0, "System"));
        Assert.True(ProcessEngine.IsCritical(4, "System"));
        Assert.True(ProcessEngine.IsCritical(1234, "csrss"));
        Assert.True(ProcessEngine.IsCritical(1234, "lsass"));
        Assert.False(ProcessEngine.IsCritical(1234, "notepad"));
        Assert.False(ProcessEngine.IsCritical(1234, null));
    }

    [Fact]
    public void Debloat_protects_real_family_names_with_publisher_suffix()
    {
        // Real family names carry "_publisher" (underscore, not dot): the guard
        // must match on the bare name or protection fails open.
        Assert.True(DebloatEngine.IsProtected("Microsoft.WindowsStore_8wekyb3d8bbwe"));
        Assert.True(DebloatEngine.IsProtected("Microsoft.Windows.ShellExperienceHost_10.0.22621.3672_neutral_neutral_cw5n1h2txyewy"));
        Assert.True(DebloatEngine.IsProtected("Microsoft.WindowsStore"));
        Assert.True(DebloatEngine.IsProtected("Microsoft.Windows.Photos_8wekyb3d8bbwe"));
        Assert.False(DebloatEngine.IsProtected("Microsoft.WindowsStoreFake_8wekyb3d8bbwe"));
        Assert.False(DebloatEngine.IsProtected("SomeRandomApp_8wekyb3d8bbwe"));
        Assert.False(DebloatEngine.IsProtected(""));
    }

    [Fact]
    public void Empty_string_default_value_detect_reports_applied()
    {
        // Guards the classic-context-menu marker: an empty REG_SZ default value
        // reads back as "" while the op may carry null/"" — Detect must agree.
        var engine = Engine();
        var sub = TestRoot + @"\EmptyMarker";
        var tweak = new TweakDefinition("audit.empty", TweakCategory.Features, "empty", "audit",
            new[] { new RegistryOperation(RegistryOperation.HKCU, sub, null, RegistryValueHint.String, "") },
            new[] { new RegistryOperation(RegistryOperation.HKCU, sub, null, RegistryValueHint.String, null, DeleteKey: true) });
        engine.Apply(tweak);
        Assert.Equal(TweakState.Applied, engine.Detect(tweak));
        engine.Revert(tweak);
        Assert.Equal(TweakState.NotApplied, engine.Detect(tweak));
    }

    [Fact]
    public void Policy_remove_is_journaled_and_revertable()
    {
        // Removal must delete the value, journal the old content, and revert
        // must restore it. Runs under the throwaway test root because the
        // real Policies trees need elevation the test runner does not have.
        const string rel = TestRoot + @"\PolicyProbe";
        const string path = @"HKCU\" + rel;
        using (var key = Registry.CurrentUser.CreateSubKey(rel, writable: true))
        {
            key!.SetValue("Probe", 1, RegistryValueKind.DWord);
        }

        var engine = Engine();
        var finding = new PolicyFinding(path, "Probe", 1, true);
        var (ok, _) = PolicyScanEngine.Remove(finding, engine);
        Assert.True(ok);
        using (var key = Registry.CurrentUser.OpenSubKey(rel))
        {
            Assert.Null(key?.GetValue("Probe"));
        }
        Assert.Contains(engine.Journal.Snapshot(),
            e => e.TweakId == PolicyScanEngine.PolicyIdFor(finding));

        engine.RevertAllFromJournal();
        using (var restored = Registry.CurrentUser.OpenSubKey(rel))
        {
            Assert.Equal(1, (int)restored!.GetValue("Probe")!);
        }
    }

    [Fact]
    public void Empty_policy_key_pruning_respects_guarded_roots()
    {
        var engine = Engine();
        const string rel = TestRoot + @"\EmptyProbe";
        Registry.CurrentUser.CreateSubKey(rel, writable: true)?.Dispose();

        // Strictly below a guarded root and empty: pruned.
        Assert.True(engine.DeleteKeyTreeIfEmpty("HKCU", rel, new[] { TestRoot }));
        Assert.Null(Registry.CurrentUser.OpenSubKey(rel));

        // Outside every guarded root: never touched.
        const string rel2 = TestRoot + @"\EmptyProbe2";
        Registry.CurrentUser.CreateSubKey(rel2, writable: true)?.Dispose();
        try
        {
            Assert.False(engine.DeleteKeyTreeIfEmpty("HKCU", rel2, new[] { @"Software\Nope" }));
            Assert.NotNull(Registry.CurrentUser.OpenSubKey(rel2));
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(rel2, throwOnMissingSubKey: false); } catch { }
        }

        // Roots themselves are never removed.
        Assert.False(engine.DeleteKeyTreeIfEmpty("HKCU", TestRoot, new[] { TestRoot }));
    }

    [Fact]
    public void Policy_friendly_names_cover_common_values()
    {
        Assert.Equal("Diagnostic data (telemetry) level",
            PolicyScanEngine.FriendlyNameFor(new PolicyFinding(
                @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 1, true)));
        Assert.Null(PolicyScanEngine.FriendlyNameFor(new PolicyFinding(
            @"HKCU\Software\Policies\Microsoft\Windows", "SomeUnknownValue", 1, true)));
    }
}
