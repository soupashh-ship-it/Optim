# Optim — Design Document

Optim is a Windows optimization utility for Windows 10 (19041+) and Windows 11.
It lets users debloat, tune performance, harden privacy, and inspect their system
through a modern Fluent interface. This is an original, clean-room implementation:
no code is taken from any existing optimizer. All rights belong to the project owner.

## Goals

1. **Full module parity** with the feature set users expect from a modern optimizer:
   system info, performance tweaks, privacy hardening, Windows feature toggles,
   app debloat, service/process/startup management, network config, security
   posture, group-policy scan, repair tooling, and package updates.
2. **Safety first**: every change is journaled and reversible with one click.
3. **Original code, fully owned**: permissive license chosen by the owner (default MIT).

## Non-goals (v1)

- No driver installation, no kernel drivers, no antivirus features.
- No "one-click magic boost" claims; every tweak is explicit and documented in-app.
- No MSIX Store packaging in v1 (unpackaged app with self-contained WinAppSDK runtime).

## Tech Stack

| Layer      | Choice                                   |
|------------|------------------------------------------|
| UI         | WinUI 3 (Windows App SDK), Fluent, Mica  |
| Runtime    | .NET 10, C# (LTS), CsWinRT interop       |
| Pattern    | MVVM (CommunityToolkit.Mvvm), DI host    |
| Interop    | Registry API, WMI (System.Management), ServiceController, P/Invoke |
| Packaging  | Unpackaged; self-contained WinAppSDK     |
| Tests      | xUnit for core engines                   |

## Architecture

```
Optim.slnx
├── src/Optim.App            → WinUI 3 executable (Views, DI host, shell)
├── src/Optim.Core           → Engines: registry, packages, services, network, info
│   └── TweakCatalog         → Declarative definitions of every tweak
└── tests/Optim.Core.Tests   → Unit tests for engines and catalog integrity
```

Layering rule: **Views → ViewModels → App services → Core engines → Win32**.
Core has no UI references; everything is testable headlessly.

### The two central abstractions (original design)

**1. Tweak catalog** — every tunable is a data definition, not scattered code:

```csharp
public sealed record TweakDefinition(
    string Id,                      // "performance.menu-delay"
    TweakCategory Category,         // Optimize, Privacy, Features
    string Title, string Description,
    IReadOnlyList<RegistryOperation> Apply,
    IReadOnlyList<RegistryOperation> Revert,
    Func<IServiceProvider, ValueTask<TweakState>>? Detect = null);

public sealed record RegistryOperation(
    RegistryHive Hive, string KeyPath, string? ValueName,
    RegistryValueKind Kind, object? Value);
```

Pages render toggle lists *from the catalog*. Adding a tweak = adding one record
+ optional detector. The UI, journaling, and revert logic are generic.

**2. Change journal** — before any mutation, the engine records the previous
value into `%LOCALAPPDATA%\Optim\journal.json`. "Revert all" replays the journal
in reverse. If a key didn't exist before, revert deletes it. This guarantees the
safe harbor users expect, independent of catalog correctness.

### Elevation model

The app requests `requireAdministrator` in its manifest (optimizer operations
need HKLM and SCM access). A pre-flight check verifies write access before
applying batches; failures are reported per-tweak, never partially silent.

## Modules (full parity)

| Page       | Function                                                                 |
|------------|--------------------------------------------------------------------------|
| Home       | Live CPU/RAM/disk usage graph, counts of apps/services, quick actions    |
| QuickTweaks | Preset bundles (Minimal/Standard/Advanced) derived live from the catalog |
| Optimize   | Catalog-driven performance toggles (visual effects, menu delay, etc.)    |
| Privacy    | Telemetry, advertising ID, activity history, tracking toggles            |
| Features   | Catalog tweaks plus real DISM optional-feature toggles                    |
| Debloat    | Installed Appx list, multi-select uninstall, provisioned-package removal |
| Services   | SCM list with start/stop/restart and start-type changes; boot-critical refuse-list, journaled |
| Processes  | Live process list, CPU/RAM columns, priority/affinity, guarded end-task  |
| Startup    | Startup folder + Run keys + approved startup entries, enable/disable     |
| Device     | OS/CPU/GPU/RAM/disk inventory, driver export button                      |
| Network    | Adapter list with enable/disable, custom DNS, one-click profiles, flush  |
| Security   | Posture dashboard plus Defender / SmartScreen / UAC policy controls      |
| Policies   | Scan registry for policy overrides (Groups policy keys), export report   |
| Repair     | SFC, DISM restore health, system file assoc refresh (run + live output)  |
| Packages   | winget-based upgrade list, upgrade selected                              |
| Settings   | Theme, language (EN/DE), logs viewer, restore-point helper, **Revert all** |

Each module owns a small engine class in `Optim.Core` (e.g. `ServiceEngine`,
`PackageEngine`, `NetworkEngine`) exposing async operations returning DTOs.

## Safety rules baked into the engines

1. Restore-point creation offered before first apply in a session.
2. Journal write precedes every registry/SCM mutation (crash-safe ordering).
3. Debloat keeps a protected list (Store, ShellExperienceHost, etc.) that is
   never uninstallable through the UI.
4. Service changes restricted to a vetted id list; unknown services are
   read-only in safe mode.
5. Repair operations run external tools with output streaming and cancellation.

## Build & run

```
dotnet build Optim.sln -c Debug
dotnet run --project src/Optim.App
dotnet test tests/Optim.Core.Tests
```

## License

MIT (c) — owned entirely by the project owner. No third-party UI code.
