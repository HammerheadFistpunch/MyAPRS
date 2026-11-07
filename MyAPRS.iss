[Setup]
AppId=MyAPRS
AppName=MyAPRS
AppVersion=1.0.0
DefaultDirName={autopf}\MyAPRS
DisableProgramGroupPage=yes
DisableStartupPrompt=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=dist
OutputBaseFilename=MyAPRS-Setup
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\MyAPRS.exe
WizardStyle=modern

[Files]
Source: "publish\MyAPRS.exe"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
Name: "{commonappdata}\MyAPRS"; Permissions: users-readexec; Flags: uninsneveruninstall
Name: "{commonappdata}\MyAPRS\data"; Permissions: users-readexec; Flags: uninsneveruninstall

[Icons]
Name: "{commonprograms}\MyAPRS"; Filename: "{app}\MyAPRS.exe"

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MyAPRS"; ValueData: """{app}\MyAPRS.exe"" --tray"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "AprsTracker"; Flags: deletevalue

[Run]
Filename: "{app}\MyAPRS.exe"; Description: "Open MyAPRS"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
var
  StationPage: TInputQueryWizardPage;
  ImportPage: TInputFileWizardPage;
  YaesuPage: TInputOptionWizardPage;

function SettingsPath: String;
begin
  Result := ExpandConstant('{commonappdata}\MyAPRS\settings.txt');
end;

procedure InitializeWizard;
var
  Lines: TArrayOfString;
begin
  StationPage := CreateInputQueryPage(wpSelectDir, 'APRS station', 'Choose the station to log',
    'Enter the complete station callsign, including its SSID if used.');
  StationPage.Add('Callsign (for example, KI7NNK-9):', False);
  YaesuPage := CreateInputOptionPage(StationPage.ID, 'Radio settings', 'Mic-E altitude units',
    'Enable the existing correction if your radio reports Mic-E altitude in metres.',
    False, False);
  YaesuPage.Add('Apply Yaesu FTM-400 Mic-E altitude correction');
  YaesuPage.Values[0] := True;
  if LoadStringsFromFile(SettingsPath, Lines) then
  begin
    if GetArrayLength(Lines) > 0 then StationPage.Values[0] := Lines[0];
    if GetArrayLength(Lines) > 1 then YaesuPage.Values[0] := Lines[1] <> '0';
  end;
  if ExpandConstant('{param:CALLSIGN|}') <> '' then
    StationPage.Values[0] := ExpandConstant('{param:CALLSIGN|}');
  ImportPage := CreateInputFilePage(YaesuPage.ID, 'Existing APRS log', 'Import an optional CSV',
    'Choose the old aprs_log.csv for this station or leave the field blank.');
  ImportPage.Add('Existing CSV:', 'CSV files|*.csv|All files|*.*', '.csv');
end;

function ValidCallsign(Callsign: String): Boolean;
var
  Base, Suffix: String;
  I, Dash, Ssid: Integer;
begin
  Result := False;
  Base := Callsign;
  Dash := Pos('-', Callsign);
  if Dash > 0 then
  begin
    Base := Copy(Callsign, 1, Dash - 1);
    Suffix := Copy(Callsign, Dash + 1, Length(Callsign));
    Ssid := StrToIntDef(Suffix, -1);
    if (Ssid < 1) or (Ssid > 15) or (Suffix <> IntToStr(Ssid)) then Exit;
  end;
  if (Length(Base) < 1) or (Length(Base) > 6) then Exit;
  for I := 1 to Length(Base) do
    if not (((Base[I] >= 'A') and (Base[I] <= 'Z')) or
            ((Base[I] >= '0') and (Base[I] <= '9'))) then Exit;
  Result := True;
end;

function NextButtonClick(PageID: Integer): Boolean;
begin
  Result := True;
  if PageID = StationPage.ID then
  begin
    StationPage.Values[0] := Uppercase(Trim(StationPage.Values[0]));
    Result := ValidCallsign(StationPage.Values[0]);
    if not Result then MsgBox('Enter a valid callsign and optional SSID 1-15.', mbError, MB_OK);
  end;
end;

function RunTool(Name, Arguments: String): Integer;
begin
  if not Exec(ExpandConstant('{sys}\') + Name, Arguments, '', SW_HIDE, ewWaitUntilTerminated, Result) then
    Result := -1;
end;

procedure CheckTool(Name, Arguments, Action: String);
var
  Code: Integer;
begin
  Code := RunTool(Name, Arguments);
  if Code <> 0 then RaiseException(Action + ' failed (exit code ' + IntToStr(Code) + ').');
end;

function StopInstalledService: Boolean;
var
  Code: Integer;
begin
  Code := RunTool('sc.exe', 'query MyAPRS');
  if Code = 1060 then
    Result := True
  else if Code <> 0 then
    Result := False
  else
    Result := RunTool('WindowsPowerShell\v1.0\powershell.exe',
      '-NoProfile -NonInteractive -Command "$ErrorActionPreference=''Stop''; ' +
      '$s=Get-Service MyAPRS; if ($s.Status -ne ''Stopped'') { Stop-Service -InputObject $s; ' +
      '$s.WaitForStatus(''Stopped'', [TimeSpan]::FromSeconds(20)) }"') = 0;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  StationPage.Values[0] := Uppercase(Trim(StationPage.Values[0]));
  if not ValidCallsign(StationPage.Values[0]) then
    Result := 'A valid callsign is required. For silent setup use /CALLSIGN=YOURCALL-9.'
  else if not StopInstalledService then
    Result := 'MyAPRS Setup could not stop the existing logger.';
end;

procedure CurStepChanged(Step: TSetupStep);
var
  DataDir, CsvPath, Flag, Mode, CommandLine: String;
  Code: Integer;
begin
  if Step <> ssPostInstall then Exit;
  DataDir := ExpandConstant('{commonappdata}\MyAPRS\data');
  CsvPath := DataDir + '\' + StationPage.Values[0] + '.csv';
  if (ImportPage.Values[0] <> '') and not FileExists(CsvPath) then
    if not FileCopy(ImportPage.Values[0], CsvPath, True) then
      RaiseException('Could not import the existing CSV.');

  CheckTool('icacls.exe', '"' + DataDir + '" /grant "*S-1-5-19:(OI)(CI)M" /T',
    'Granting the logger access to the archive');
  Flag := '0';
  if YaesuPage.Values[0] then Flag := '1';
  if not SaveStringToFile(SettingsPath, StationPage.Values[0] + #13#10 + Flag + #13#10, False) then
    RaiseException('Could not save MyAPRS settings.');

  CheckTool('icacls.exe', '"' + ExpandConstant('{commonappdata}\MyAPRS') + '" /grant "*S-1-5-19:RX"',
    'Granting the logger access to its data directory');
  CheckTool('icacls.exe', '"' + SettingsPath + '" /grant "*S-1-5-19:R"',
    'Granting the logger access to its settings');

  Code := RunTool('sc.exe', 'query MyAPRS');
  if Code = 1060 then Mode := 'create'
  else if Code = 0 then Mode := 'config'
  else RaiseException('Could not query the MyAPRS Windows service.');
  CommandLine := Mode + ' MyAPRS binPath= "\"' + ExpandConstant('{app}\MyAPRS.exe') +
    '\" --service" start= auto obj= "NT AUTHORITY\LocalService" DisplayName= "MyAPRS"';
  CheckTool('sc.exe', CommandLine, 'Registering the MyAPRS Windows service');
  CheckTool('sc.exe', 'failure MyAPRS reset= 86400 actions= restart/5000/restart/15000/restart/30000',
    'Configuring logger recovery');
  CheckTool('sc.exe', 'failureflag MyAPRS 1', 'Enabling logger recovery');
  CheckTool('sc.exe', 'start MyAPRS', 'Starting the MyAPRS logger');
end;

function InitializeUninstall: Boolean;
var
  Code: Integer;
begin
  Result := False;
  if not StopInstalledService then
  begin
    MsgBox('The MyAPRS logger could not be stopped.', mbError, MB_OK);
    Exit;
  end;
  Code := RunTool('sc.exe', 'delete MyAPRS');
  if (Code <> 0) and (Code <> 1060) and (Code <> 1072) then
  begin
    MsgBox('The MyAPRS service could not be removed.', mbError, MB_OK);
    Exit;
  end;
  RunTool('taskkill.exe', '/F /IM MyAPRS.exe');
  Result := True;
end;
