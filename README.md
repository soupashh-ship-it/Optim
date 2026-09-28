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
| ⚡ Optimize   | Performance toggles (basic + advanced tiers) + power-plan switcher        |
| 🛡️ Privacy   | Telemetry, advertising ID, activity history, Recall, Cortana, location    |
| 🧩 Features  | Widgets, Copilot, hibernation, classic context menu, taskbar search       |
| 🧹 Debloat   | Installed appx list with multi-select uninstall + protected-components    |
| ⚙️ Services  | Vetted services switchable (Automatic/Manual/Disabled) with live status; everything else is read-only, all changes journaled |
| 📋 Processes | Live process list with RAM usage and guarded end-task                     |
| 🚀 Startup   | User + machine Run keys and startup folder with enable/disable/delete     |
| 📦 Packages  | winget-driven upgrade discovery and one-click upgrades (with confirm)     |
| 🌐 Network   | Adapter overview, one-click DNS profiles (Cloudflare, Quad9, …), flush    |
| 🔒 Security  | Read-only posture view: Defender, SmartScreen, UAC, all firewall profiles |
| 📜 Policies  | Scans every registry policy override with plain-language labels, journaled one-click removal, exportable report |
| 🔧 Repair    | SFC, DISM (check/scan/restore), chkdsk — with live output and cancel      |
| 💻 Device    | Full hardware/OS inventory + driver export via pnputil                    |
| ℹ️ About     | Version, safety model, restore-point helper, diagnostics                  |
| ⚙️ Settings  | Theme (whole window incl. title bar), log viewer, restore-point helper and **Revert all changes** |

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
operations require it).

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
picks the file up and MRT Core resolves it by the user's language.

Current coverage is the shell and Settings surface (navigation labels, page
headings, settings rows). Tweak titles and descriptions are still English-only.

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

CI builds and tests every push to `main` (`.github/workflows/build.yml`), and
`.github/workflows/release.yml` publishes the portable build and the installer
on a `v*` tag (or on demand). The app icon is generated from scratch by
`scripts/generate-icon.ps1` — no external artwork.

## License

[MIT](LICENSE.md) — free to use, modify and distribute.
