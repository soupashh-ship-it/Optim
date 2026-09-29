; Inno Setup script for Optim.
;
; MSIX cannot carry this app's elevation model: an MSIX package runs with the
; user's token and cannot request UAC, while Optim writes HKLM and controls
; services. A classic installer keeps requireAdministrator intact, which is why
; it is the shipping format while MSIX stays a later milestone.
;
; Build with scripts\build-installer.ps1 (passes /DVersion), which publishes the
; self-contained app into release\Optim-portable-<version> first.

#ifndef Version
  #define Version "0.0.0"
#endif

#define AppName "Optim"
#define AppExe "Optim.App.exe"
#define AppPublisher "Optim"
#define AppUrl "https://github.com/soupashh-ship-it/windows-optimizer"

[Setup]
; Stable identity: never change this GUID, or upgrades install side by side.
AppId={{8F3A2C1E-5B4D-4A9F-9E2C-7D6B1A0C4E88}
AppName={#AppName}
AppVersion={#Version}
AppVerName={#AppName} {#Version}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE.md
OutputDir=..\release
OutputBaseFilename={#AppName}-{#Version}-setup
SetupIconFile=..\src\Optim.App\Assets\AppIcon.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Optim writes HKLM and controls services, so the whole install is elevated and
; the first-run user never has to answer a surprise UAC prompt for the app.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
; The portable publish directory holds the complete self-contained app.
Source: "..\release\Optim-portable-{#Version}\*"; DestDir: "{app}"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; runascurrentuser is required: the app manifest demands requireAdministrator,
; and post-install entries otherwise launch with the ORIGINAL (de-elevated)
; user token when Setup itself is elevated. CreateProcess refuses that with
; error 740 ("The requested operation requires elevation"). This flag reuses
; Setup's admin token, so the finish-page launch works without a second UAC
; prompt.
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; \
    Flags: nowait postinstall skipifsilent runascurrentuser
