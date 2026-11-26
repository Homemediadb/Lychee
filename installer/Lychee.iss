#define MyAppName       "Lychee"
#ifndef MyAppVersion
#define MyAppVersion    "1.0.0"
#endif
#define MyAppId         "{B7E2C9A4-5D31-4F8E-A6C0-3E9D1F27B854}"
#define MyAppPublisher  "rlaphoenix"
#define MyAppURL        "https://github.com/Homemediadb/Lychee"
#define MyAppExeName    "Lychee.exe"

[Setup]
AppId={{#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
LicenseFile=..\LICENSE
OutputDir=Output
OutputBaseFilename=Lychee-{#MyAppVersion}-setup
SetupIconFile=..\src\Lychee.Gui\Assets\icon.ico
; ISCC is 32-bit and ultra64 needs ~700 MB, so compress in a separate process
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.17763
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
{ Remove any previous install first, so an update, downgrade or reinstall never
  leaves stale files behind (Inno only overwrites, never deletes obsolete ones). }

const
  UninstKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppId}_is1';

function ReadUninstValue(const ValueName: String; var Value: String): Boolean;
begin
  Result := RegQueryStringValue(HKLM,   UninstKey, ValueName, Value)
         or RegQueryStringValue(HKLM32, UninstKey, ValueName, Value)
         or RegQueryStringValue(HKCU,   UninstKey, ValueName, Value);
end;

function PreviousInstallPresent: Boolean;
begin
  Result := RegKeyExists(HKLM, UninstKey) or RegKeyExists(HKLM32, UninstKey) or RegKeyExists(HKCU, UninstKey);
end;

{ Dotted numeric version compare: 1 if A>B, -1 if A<B, 0 if equal. }
function CompareVersions(A, B: String): Integer;
var SA, SB: String;
begin
  Result := 0;
  while ((A <> '') or (B <> '')) and (Result = 0) do
  begin
    if Pos('.', A) > 0 then begin SA := Copy(A, 1, Pos('.', A) - 1); Delete(A, 1, Pos('.', A)); end
    else begin SA := A; A := ''; end;
    if Pos('.', B) > 0 then begin SB := Copy(B, 1, Pos('.', B) - 1); Delete(B, 1, Pos('.', B)); end
    else begin SB := B; B := ''; end;
    if StrToIntDef(SA, 0) > StrToIntDef(SB, 0) then Result := 1
    else if StrToIntDef(SA, 0) < StrToIntDef(SB, 0) then Result := -1;
  end;
end;

{ Warn before a downgrade; a silent install proceeds. }
function InitializeSetup: Boolean;
var InstalledVer: String;
begin
  Result := True;
  if ReadUninstValue('DisplayVersion', InstalledVer) and (InstalledVer <> '') then
    if CompareVersions(InstalledVer, '{#MyAppVersion}') > 0 then
      Result := SuppressibleMsgBox('A newer version of Lychee (' + InstalledVer + ') is already installed.'
        + #13#10#13#10 + 'This installer will DOWNGRADE it to {#MyAppVersion}. Continue?',
        mbConfirmation, MB_YESNO, IDYES) = IDYES;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  UninstStr: String;
  Code, Waited: Integer;
begin
  Result := '';
  if PreviousInstallPresent and ReadUninstValue('UninstallString', UninstStr) and (UninstStr <> '') then
    if Exec(RemoveQuotes(UninstStr), '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, Code) then
    begin
      { The uninstaller relaunches a copy of itself, so Exec returns early;
        wait until its registry key disappears (up to ~20s). }
      Waited := 0;
      while PreviousInstallPresent and (Waited < 20000) do
      begin
        Sleep(200);
        Waited := Waited + 200;
      end;
    end;
end;
