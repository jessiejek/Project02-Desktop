; ClinicNode.iss - Inno Setup script for the Clinic Node Windows installer.
; JJQR - AI (Nov 22, 2026) - owner: free installer, no certificate needed. Installs for the current user only (NO administrator password), adds Start-menu + desktop shortcuts, an uninstaller, and starts the app.
; Built by build-installer.ps1 (it passes AppVersion and SourceDir).
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\ClinicNode"
#endif
#ifndef ExeName
  #define ExeName "ClinicNode.App.exe"
#endif

[Setup]
AppId={{6F2E8A3C-4B1D-4C55-9E52-0C1A5D7B9A31}
AppName=Clinic Node
AppVersion={#AppVersion}
AppPublisher=Grace Medical Clinic
DefaultDirName={localappdata}\Programs\Clinic Node
DefaultGroupName=Clinic Node
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\out
OutputBaseFilename=ClinicNode-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#ExeName}
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: checkedonce

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\Clinic Node"; Filename: "{app}\{#ExeName}"
Name: "{autodesktop}\Clinic Node"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#ExeName}"; Description: "Start Clinic Node"; Flags: nowait postinstall skipifsilent
