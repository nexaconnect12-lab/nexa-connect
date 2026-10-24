#ifndef PayloadZip
  #error PayloadZip is required
#endif
#ifndef PackageVersion
  #error PackageVersion is required
#endif

[Setup]
AppId={{58CE7A20-856A-47CA-B19B-6759DCA06C70}
AppName=NexaConnect POS
AppVersion={#PackageVersion}
AppPublisher=NexaConnect
DefaultDirName={localappdata}\NexaConnect\Terminal
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
AppMutex=NexaConnect.POS.SingleInstance
SetupMutex=NexaConnect.POS.Setup
CloseApplications=no
RestartApplications=no
RestartIfNeededByRun=no
AlwaysRestart=no
UsePreviousAppDir=no
AllowCancelDuringInstall=no
OutputDir={#InstallerOutput}
OutputBaseFilename=NexaConnect-POS-{#PackageVersion}-win-x64-setup
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\NexaConnect.POS.exe
#ifdef SignedBuild
SignTool=publisher
SignedUninstaller=yes
#else
SignedUninstaller=no
#endif

[Files]
Source: "{#PayloadZip}"; DestName: "payload.zip"; Flags: dontcopy
Source: "{tmp}\preflight\*"; DestDir: "{app}"; Excludes: "appsettings.json,installer-owner.txt"; Flags: external recursesubdirs createallsubdirs ignoreversion
Source: "{tmp}\preflight\appsettings.json"; DestDir: "{app}"; Flags: external ignoreversion uninsneveruninstall
Source: "{tmp}\preflight\installer-owner.txt"; DestDir: "{app}"; Flags: external ignoreversion uninsneveruninstall

[Icons]
Name: "{userprograms}\NexaConnect POS"; Filename: "{app}\NexaConnect.POS.exe"

[Registry]
Root: HKCU; Subkey: "Software\Classes\nexaconnect-pos"; ValueType: string; ValueData: "URL:NexaConnect POS callback"
Root: HKCU; Subkey: "Software\Classes\nexaconnect-pos"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\nexaconnect-pos\shell\open\command"; ValueType: string; ValueData: """{app}\NexaConnect.POS.exe"" ""%1"""

[Code]
var
  Profiles: TInputFileWizardPage;
  Takeover: TInputOptionWizardPage;
  Staged: Boolean;

procedure InitializeWizard;
begin
  Profiles := CreateInputFilePage(wpWelcome, 'Terminal configuration',
    'Select the approved hosted terminal profile',
    'Setup keeps the original Windows recovery. Changing identity realm or terminal custody requires reconciliation first.');
  Profiles.Add('New public configuration (no passwords or tokens):', 'JSON files|*.json', '.json');
  Profiles.Add('Previous configuration for an unpackaged terminal (if any):', 'JSON files|*.json', '.json');
  Profiles.Values[0] := ExpandConstant('{param:PROFILE|}');
  Profiles.Values[1] := ExpandConstant('{param:PREVIOUSCONFIG|}');
  Takeover := CreateInputOptionPage(Profiles.ID, 'Sign-in callback', 'Review callback ownership',
    'A terminal in another install folder may already own sign-in. Replacement is allowed only after preserving and reconciling that installation.', False, False);
  Takeover.Add('I authorize replacing another installation''s POS callback.');
  Takeover.Values[0] := ExpandConstant('{param:TAKEOVERCALLBACK|0}') = '1';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  VersionMS, VersionLS: Cardinal;
  Owner: AnsiString;
  Prior, ExistingCommand, ExpectedCommand, PsCode, StagePath, ZipPath: String;
begin
  Result := '';
  CreateMutex('NexaConnect.POS.Maintenance');
  if CheckForMutexes('NexaConnect.POS.SingleInstance') then begin
    Result := 'Close POS yourself before maintenance. Setup never closes a shift, kills POS or discards pending work.'; exit;
  end;
  if CompareText(ExpandConstant('{app}'), ExpandConstant('{localappdata}\NexaConnect\Terminal')) <> 0 then begin
    Result := 'This per-user installer requires its managed terminal folder.'; exit;
  end;
  if DirExists(ExpandConstant('{app}')) then begin
    if not LoadStringFromFile(ExpandConstant('{app}\installer-owner.txt'), Owner) or
      (Trim(String(Owner)) <> 'nexaconnect-terminal-installer-v1') then begin
      Result := 'The target directory is not owned by this installer.'; exit;
    end;
    if GetVersionNumbers(ExpandConstant('{app}\NexaConnect.POS.exe'), VersionMS, VersionLS) then
      if (VersionMS > {#VersionMajor} * 65536 + {#VersionMinor}) or
        ((VersionMS = {#VersionMajor} * 65536 + {#VersionMinor}) and (VersionLS > {#VersionPatch} * 65536)) then begin
        Result := 'Downgrades are not supported. Use a compatible forward update.'; exit;
      end;
  end;
  if not FileExists(Profiles.Values[0]) then begin
    Result := 'Select an approved hosted terminal configuration.'; exit;
  end;
  ExpectedCommand := '"' + ExpandConstant('{app}\NexaConnect.POS.exe') + '" "%1"';
  if RegQueryStringValue(HKCU, 'Software\Classes\nexaconnect-pos\shell\open\command', '', ExistingCommand) then
    if (ExistingCommand <> ExpectedCommand) and not Takeover.Values[0] then begin
      Result := 'Another installation owns the sign-in callback. Review migration and explicitly authorize replacement.'; exit;
    end;
  Prior := ExpandConstant('{app}\appsettings.json');
  if not FileExists(Prior) then Prior := Profiles.Values[1];
  if Prior = '' then Prior := '-';
  if not Staged then begin
    ExtractTemporaryFile('payload.zip');
    StagePath := ExpandConstant('{tmp}\preflight');
    ZipPath := ExpandConstant('{tmp}\payload.zip');
    StringChangeEx(StagePath, '''', '''''', True);
    StringChangeEx(ZipPath, '''', '''''', True);
    PsCode := 'Add-Type -AssemblyName System.IO.Compression.FileSystem; [IO.Compression.ZipFile]::ExtractToDirectory(''' + ZipPath + ''',''' + StagePath + ''')';
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), '-NoProfile -NonInteractive -Command "' + PsCode + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then begin
      Result := 'Payload extraction failed; no installation was changed.'; exit;
    end;
    Staged := True;
  end;
  if not CopyFile(Profiles.Values[0], ExpandConstant('{tmp}\preflight\appsettings.json'), False) then begin
    Result := 'Could not stage the public terminal profile.'; exit;
  end;
  if not Exec(ExpandConstant('{tmp}\preflight\NexaConnect.POS.exe'),
    '--validate-package "' + ExpandConstant('{tmp}\preflight\appsettings.json') + '" "' + Prior + '" "' + ExpandConstant('{app}') + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then begin
    Result := 'Configuration or protected recovery is incompatible. Preserve the original configuration and recovery; do not reset them.'; exit;
  end;
end;

function InitializeUninstall: Boolean;
begin
  CreateMutex('NexaConnect.POS.Maintenance');
  Result := not CheckForMutexes('NexaConnect.POS.SingleInstance');
  if not Result then MsgBox('Close POS yourself before uninstalling. Recovery is retained.', mbError, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, 'Software\Classes\nexaconnect-pos\shell\open\command', '', Command) then
      if Command = '"' + ExpandConstant('{app}\NexaConnect.POS.exe') + '" "%1"' then
        RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\nexaconnect-pos');
end;
