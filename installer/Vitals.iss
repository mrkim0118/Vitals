; Vitals installer (Inno Setup 6). Built by scripts\Build-Installer.ps1, which passes
; AppVersion, SourceDir (the publish folder) and OutputDir with /D.

#ifndef AppVersion
  #error AppVersion is not defined. Run scripts\Build-Installer.ps1.
#endif

[Setup]
AppId={{E625A122-32F1-4151-AC7C-6A87A1EAAA61}
AppName=Vitals
AppVersion={#AppVersion}
AppVerName=Vitals {#AppVersion}
AppPublisher=Manggdy
DefaultDirName={autopf}\Vitals
DefaultGroupName=Vitals
DisableProgramGroupPage=yes
; Machine-wide install in Program Files.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
SetupIconFile=..\src\Vitals.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\Vitals.exe
OutputDir={#OutputDir}
OutputBaseFilename=Vitals-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
; Vitals holds this mutex while running (Program.cs), so setup asks to close it before replacing files.
AppMutex=Local\Manggdy.Vitals
#ifdef Sign
; Build-Installer.ps1 defines the "manggdysign" tool (installer\sign.ps1, "Manggdy" certificate).
SignTool=manggdysign
SignedUninstaller=yes
#endif

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Vitals"; Filename: "{app}\Vitals.exe"
Name: "{autodesktop}\Vitals"; Filename: "{app}\Vitals.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Vitals.exe"; Description: "{cm:LaunchProgram,Vitals}"; Flags: nowait postinstall skipifsilent runasoriginaluser

; Installs the .NET 8 Desktop Runtime when it is missing (the app is framework-dependent).
#define DotNetVersion "8"
#include "WindowsDesktopRuntime.iss"
