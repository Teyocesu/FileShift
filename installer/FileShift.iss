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

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\dist\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "classicmenu"; Description: "Registrar también menú contextual clásico de respaldo (Show more options)"; GroupDescription: "Integración con Windows Explorer:"; Flags: unchecked

[Registry]
; Respaldo reversible opcional en el menú clásico (HKCU, sin requerir UAC elevado)
Root: HKCU; Subkey: "Software\Classes\*\shell\FileShift"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Convertir a WebP con FileShift"; Flags: uninsdeletekey; Tasks: classicmenu
Root: HKCU; Subkey: "Software\Classes\*\shell\FileShift"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Flags: uninsdeletekey; Tasks: classicmenu
Root: HKCU; Subkey: "Software\Classes\*\shell\FileShift\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyCliExeName}"" convert --to webp -- ""%1"""; Flags: uninsdeletekey; Tasks: classicmenu

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
procedure RunIntegrationScript(const Script: String);
var
  ResultCode: Integer;
begin
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\') + Script + '"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    RaiseException('FileShift Explorer integration failed. Check engine prerequisites and MSIX certificate trust.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    RunIntegrationScript('Register-SparsePackage.ps1');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RunIntegrationScript('Unregister-SparsePackage.ps1');
end;
