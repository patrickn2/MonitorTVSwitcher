#define MyAppName "Monitor/TV Switcher"
#define MyAppNameSafe "Monitor TV Switcher"
#define MyAppPublisher "Monitor/TV Switcher"
#define MyAppExeName "MonitorTVSwitcher.exe"
#define MyAppMutex "Local\MonitorTVSwitcher.SingleInstance"

#ifndef MyAppVersion
  #define MyAppVersion "1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif
#ifndef SetupIcon
  #define SetupIcon "..\Assets\tray.ico"
#endif

[Setup]
AppId={{B8E4F2A1-3C7D-4E9F-A1B2-6D8C0E4F7A91}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppMutex={#MyAppMutex}
DefaultDirName={localappdata}\Programs\MonitorTVSwitcher
DefaultGroupName={#MyAppNameSafe}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#SetupIcon}
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
OutputDir={#OutputDir}
OutputBaseFilename=MonitorTVSwitcher-Setup-{#MyAppVersion}
CloseApplications=force
CloseApplicationsFilter={#MyAppExeName}
UsePreviousAppDir=yes
ChangesAssociations=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Start with Windows"; GroupDescription: "Additional options:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\{#MyAppNameSafe}"; Filename: "{app}\{#MyAppExeName}"; Comment: "Switch between monitor and TV"
Name: "{autodesktop}\{#MyAppNameSafe}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; Comment: "Switch between monitor and TV"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MonitorTVSwitcher"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#MyAppExeName} /F /T"; RunOnceId: "StopMonitorTVSwitcher"; Flags: runhidden skipifdoesntexist
