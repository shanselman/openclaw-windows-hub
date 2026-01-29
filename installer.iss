; Moltbot Tray Inno Setup Script
#define MyAppName "Moltbot Tray"
#define MyAppPublisher "Scott Hanselman"
#define MyAppURL "https://github.com/shanselman/moltbot-windows-hub"
#define MyAppExeName "Moltbot.Tray.exe"

[Setup]
AppId={{M0LTB0T-TRAY-4PP1-D3N7}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL=https://github.com/shanselman/moltbot-windows-hub/issues
AppUpdatesURL=https://github.com/shanselman/moltbot-windows-hub/releases
DefaultDirName={localappdata}\MoltbotTray
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputBaseFilename=MoltbotTray-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
SetupIconFile=src\Moltbot.Tray\moltbot.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Start Moltbot Tray when Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
