; Inno Setup script — builds ProjectPilot-Setup.exe (run by GitHub Actions)
#define MyAppName "ProjectPilot"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

[Setup]
AppId={{8F3C2A51-6B7D-4E2A-9C1F-3D5E7A9B1C24}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=ProjectPilot
DefaultDirName={autopf}\ProjectPilot
DefaultGroupName=ProjectPilot
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=ProjectPilot-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern
UninstallDisplayIcon={app}\ProjectPilot.exe

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\ProjectPilot.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\ProjectPilot"; Filename: "{app}\ProjectPilot.exe"
Name: "{group}\Uninstall ProjectPilot"; Filename: "{uninstallexe}"
Name: "{autodesktop}\ProjectPilot"; Filename: "{app}\ProjectPilot.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ProjectPilot.exe"; Description: "{cm:LaunchProgram,ProjectPilot}"; Flags: nowait postinstall skipifsilent
