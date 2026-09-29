# Optim

**Streamline. Tune. Own your Windows.**

Optim is an original, open-source Windows optimizer for Windows 10 (19041+) and
Windows 11, built with WinUI 3 and .NET 10. It brings debloating, performance
tuning, privacy hardening, and deep system insight into one clean Fluent app.

> ✨ 100% original code — clean-room implementation, MIT licensed.

## Features

| Module      | What it does                                                              |
|-------------|---------------------------------------------------------------------------|
| 🏠 Home      | Live CPU / memory / disk usage with quick actions (temp clean, recycle bin, DNS flush, restore point) |
| ✨ Quick Tweaks | Curated bundles (Minimal / Standard / Advanced) applied and reverted in one click, composed live from the tweak catalog |
| ⚡ Optimize   | Performance toggles (basic + advanced tiers) + power-plan switcher        |
| 🛡️ Privacy   | Telemetry, advertising ID, activity history, Recall, Cortana, location    |
| 🧩 Features  | Real DISM optional-feature toggles + registry feature tweaks (widgets, Copilot, hibernation, classic context menu) |
| 🧹 Debloat   | Installed appx list with multi-select uninstall, protected components, and a **reinstall** escape hatch |
| ⚙️ Services  | Start/stop/restart and start-type changes (Automatic/Manual/Disabled/Delayed) for every service — boot-critical ones stay read-only, changes journaled; Task Manager-style "hide Microsoft services" filter |
| 📋 Processes | Live process list with priority classes, per-CPU affinity, and guarded end-task |
| 🚀 Startup   | User + machine Run keys and startup folder with enable/disable/delete     |
| 📦 Packages  | winget-driven upgrade discovery and one-click upgrades (with confirm)     |
| 🌐 Network   | Adapter overview with enable/disable, custom DNS (v4/v6), one-click profiles, flush |
| 🔒 Security  | Posture dashboard plus Defender / real-time / SmartScreen / UAC policy controls — restoring Windows defaults, tamper-aware |
| 📜 Policies  | Scans every registry policy override with plain-language labels, journaled one-click removal, exportable report |
| 🔧 Repair    | SFC, DISM (check/scan/restore), chkdsk — with live output and cancel      |
| 💻 Device    | Full hardware/OS inventory + driver export via pnputil                    |
| ℹ️ About     | Version, safety model, restore-point helper, diagnostics                  |
| ⚙️ Settings  | Theme (whole window incl. title bar), language (English/German), log viewer, restore-point helper and **Revert all changes** |

## Safety model

- **Change journal** — every registry mutation is journaled *before* it is
  written (crash-safe ordering). **Revert all changes** replays the journal in
  reverse, restoring the exact previous state, deleting keys/values that did
  not exist before.
- **Protected apps** — Store, shell hosts, and runtime frameworks can never be
  uninstalled through the UI.
- **Vetted services** — only a curated allow-list of services can be modified.
- **Read-only security** — the Security page only reports; it never flips.
- **Restore points** — a helper is one click away on Home.

## Building

Requires **Windows 10 19041+ / Windows 11** and the **.NET 10 SDK**
(WindowsAppSDK is self-contained on publish).

```bash
dotnet build Optim.slnx -c Debug
dotnet run --project src/Optim.App
dotnet test tests/Optim.Core.Tests
```

The app requests administrator elevation at launch (registry + service
operations require it). For docs tooling, `Optim.App.exe --page <tag>` opens a
specific page directly (e.g. `--page optimize`).

## Architecture

```
Optim.slnx
├── src/Optim.App     → WinUI 3 executable (Views, DI host, shell)
├── src/Optim.Core    → Engines: tweaks, debloat, services, network, repair…
│   └── TweakCatalog  → every tweak is one declarative record
└── tests             → xUnit tests for the core engines
```

Adding a tweak = adding one `TweakDefinition` to `TweakCatalog.cs`. UI,
journaling, apply, revert and detect are all generic. Mark system-level
tweaks advanced by adding their id to `AdvancedIds`. See
[DESIGN.md](DESIGN.md) for the full design document.

## Localization

UI strings live in `src/Optim.App/Strings/en-US/Resources.resw`. XAML reads them
through `x:Uid` (an element with `x:Uid="Nav_Home"` reads `Nav_Home.Content`);
code reads them through `Optim.App.Localization.Loc.Get`, which always takes an
English fallback so a missing key can never blank the UI.

To add a language, copy the file to `Strings/<bcp-47-tag>/Resources.resw` (for
example `Strings/de-DE/Resources.resw`) and translate the `<value>` elements.
Keys must stay identical; any key a translation omits falls back to English, so
shipping a partial translation is safe. No code changes are needed — the build
picks the file up and MRT Core resolves it by the user's language. The
`Tweak_*` entries are generated from `TweakCatalog` — never edit them by hand;
run `pwsh scripts/generate-tweak-resw.ps1` after changing the catalog, and a
test fails when the committed file drifts.

Coverage spans the shell, the Settings page and every catalog tweak (all 111
titles and descriptions).

## Releases

```powershell
# Portable self-contained build (unzip and run elevated)
.\scripts\publish-portable.ps1

# Installer (requires Inno Setup: winget install JRSoftware.InnoSetup)
.\scripts\build-installer.ps1
```

Optim writes HKLM and controls services, so it must keep requesting
administrator. An MSIX package runs with the user's token and cannot raise
UAC, which is why the shipping format is a classic elevated installer that
installs the self-contained payload; the portable zip stays available for
people who would rather not install anything.

### Releases automation

CI builds and tests every push to `main` (`.github/workflows/build.yml`).
`.github/workflows/release.yml` runs on a `v*` tag (or on demand, with an
optional tag input) and:

- publishes the portable zip and the installer, named after the tag
- signs the installer through **Azure Artifact Signing** when the
  `AZURE_TENANT_ID` / `AZURE_CLIENT_ID` / `AZURE_CLIENT_SECRET` /
  `AZURE_CODESIGNING_ENDPOINT` / `AZURE_CODESIGNING_ACCOUNT` /
  `AZURE_CODESIGNING_PROFILE` secrets are configured — and fails the build if
  a signature does not verify. Without the secrets it skips signing with a
  notice in the run summary (a self-signed certificate would not change
  SmartScreen's verdict, so it is not offered)
- regenerates the release body from the commits between the previous tag and
  this one (`scripts/generate-release-notes.ps1`), creates or updates the
  release, and attaches the files

The app icon is generated from scratch by `scripts/generate-icon.ps1` — no
external artwork.

## License

[MIT](LICENSE.md) — free to use, modify and distribute.
