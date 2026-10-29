#ifndef AppVersion
 #define AppVersion "0.2.1"
#endif
[Setup]
AppId={{EA049E32-11ED-4EFA-B053-242223FC0D3E}
AppName=AudioStretch
AppVersion={#AppVersion}
AppPublisher=Ian Rastall
DefaultDirName={localappdata}\Programs\AudioStretch
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.26100
UninstallDisplayIcon={app}\AudioStretch.exe
UninstallDisplayName=AudioStretch
SetupIconFile=..\src\AudioStretch.App\Assets\AudioStretch.ico
OutputDir={#OutputDir}
OutputBaseFilename=AudioStretch-{#AppVersion}-Setup
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
InfoAfterFile=..\docs\installation.txt
AppMutex=Jupiter.AudioStretch
[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autoprograms}\AudioStretch"; Filename: "{app}\AudioStretch.exe"; AppUserModelID: "Jupiter.AudioStretch"
[Run]
Filename: "{app}\AudioStretch.exe"; Description: "Launch AudioStretch"; Flags: nowait postinstall skipifsilent
; Settings are stored outside {app}; upgrades and uninstall preserve settings and user audio.
