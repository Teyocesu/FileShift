; FileShift — Inno Setup 6 Installer Script for Windows 11 (x64)
; Registra tanto el Sparse Package MSIX (IExplorerCommand moderno) como la limpieza completa al desinstalar.

#define MyAppName "FileShift"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "FileShift Open Source"
#define MyAppExeName "FileShift.App.exe"
#define MyCliExeName "fileshift.exe"

[Setup]
AppId={{8F9C32E1-45B2-4F81-A09E-7C5D1B90E112}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
LicenseFile=..\LICENSE
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
ChangesEnvironment=yes
OutputDir=..\dist\installer
OutputBaseFilename=FileShift-Setup-v{#MyAppVersion}-win-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\dist\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "AppxManifest.xml"; DestDir: "{app}"; Flags: ignoreversion
Source: "Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Register-SparsePackage.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "Unregister-SparsePackage.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "classicmenu"; Description: "Registrar también menú contextual clásico de respaldo (Show more options)"; GroupDescription: "Integración con Windows Explorer:"

[Registry]
; Respaldo reversible opcional en el menú clásico (HKCU, sin requerir UAC elevado)
Root: HKCU; Subkey: "Software\Classes\*\shell\FileShift"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Convertir a WebP con FileShift"; Flags: uninsdeletekey; Tasks: classicmenu
Root: HKCU; Subkey: "Software\Classes\*\shell\FileShift"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Flags: uninsdeletekey; Tasks: classicmenu
Root: HKCU; Subkey: "Software\Classes\*\shell\FileShift\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyCliExeName}"" convert --to webp -- ""%1"""; Flags: uninsdeletekey; Tasks: classicmenu

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Register-SparsePackage.ps1"" -InstallDir ""{app}"""; StatusMsg: "Registrando integración con el menú contextual moderno de Windows 11..."; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Unregister-SparsePackage.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterFileShiftSparsePkg"
