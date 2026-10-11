#if Ver < 0x06040000 || Ver >= 0x07000000
  #error Inno Setup 6.4 or later in the 6.x series is required
#endif

#ifndef AppVersion
  #define AppVersion "1.3.0"
#endif
#ifndef AppSourceDirectory
  #define AppSourceDirectory "..\dist\AirPodsMicBridge"
#endif
#ifndef OutputRoot
  #define OutputRoot "..\dist"
#endif

[Setup]
#ifdef SmokeRoot
AppId=AirPodsMicBridge-Smoke
DefaultDirName={#SmokeRoot}\app
UsePreviousAppDir=no
OutputBaseFilename=AirPodsMicBridge-{#AppVersion}-Setup-Smoke
SetupMutex=Local\AirPodsMicBridge.Setup.Smoke.v1
#else
AppId={{3D4DFF23-C707-4DF5-ACD1-F62A2C3D1E68}
DefaultDirName={autopf}\AirPodsMicBridge
OutputBaseFilename=AirPodsMicBridge-{#AppVersion}-Setup
AppMutex=Local\AirPodsMicBridge.Singleton.v1
SetupMutex=Local\AirPodsMicBridge.Setup.v1
#endif
AppName=AirPods Mic Bridge
AppVersion={#AppVersion}
AppVerName=AirPods Mic Bridge {#AppVersion}
DefaultGroupName=AirPods Mic Bridge
DisableWelcomePage=yes
DisableDirPage=no
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=no
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\AirPodsMicBridge.exe
SetupIconFile=..\assets\airpods-mic.ico
WizardSmallImageFile=..\assets\installer-logo.png
WizardImageFile=..\assets\installer-sidebar.png
OutputDir={#OutputRoot}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=100
DisableFinishedPage=no
LanguageDetectionMethod=uilanguage
ShowLanguageDialog=auto

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "pt"; MessagesFile: "compiler:Languages\Portuguese.isl"
Name: "ptBR"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "id"; MessagesFile: "languages\Indonesian.isl"

[Files]
Source: "{#AppSourceDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#ifdef SmokeRoot
Source: "{#AppSourceDirectory}\AirPodsMicBridge.exe"; DestName: "AirPodsMicBridge-Stop.exe"; Flags: dontcopy
#endif

[Icons]
Name: "{code:GetProgramsDirectory}\AirPods Mic Bridge"; Filename: "{app}\AirPodsMicBridge.exe"; WorkingDir: "{app}"
Name: "{code:GetProgramsDirectory}\{cm:UninstallProgram,AirPods Mic Bridge}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\AirPodsMicBridge.exe"; Description: "{cm:LaunchProgram,AirPods Mic Bridge}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallDelete]
Type: files; Name: "{code:GetStartupDirectory}\AirPods Mic Bridge.lnk"

[CustomMessages]
en.StartupOption=Start AirPods Mic Bridge when signing in to Windows
en.StartupFailed=Could not update automatic startup. Please try again.
en.AppRunning=Please close AirPods Mic Bridge and try again.
en.InvalidStartup=The STARTUP option must be yes or no.
ru.StartupOption=Запускать AirPods Mic Bridge при входе в Windows
ru.StartupFailed=Не удалось изменить автозапуск. Повторите попытку.
ru.AppRunning=Закройте AirPods Mic Bridge и повторите попытку.
ru.InvalidStartup=Параметр STARTUP должен иметь значение yes или no.
es.StartupOption=Iniciar AirPods Mic Bridge al iniciar sesión en Windows
es.StartupFailed=No se pudo actualizar el inicio automático. Inténtelo de nuevo.
es.AppRunning=Cierre AirPods Mic Bridge e inténtelo de nuevo.
es.InvalidStartup=La opción STARTUP debe ser yes o no.
ptBR.StartupOption=Iniciar o AirPods Mic Bridge ao entrar no Windows
ptBR.StartupFailed=Não foi possível atualizar a inicialização automática. Tente novamente.
ptBR.AppRunning=Feche o AirPods Mic Bridge e tente novamente.
ptBR.InvalidStartup=A opção STARTUP deve ser yes ou no.
pt.StartupOption=Iniciar o AirPods Mic Bridge ao iniciar sessão no Windows
pt.StartupFailed=Não foi possível atualizar o arranque automático. Tente novamente.
pt.AppRunning=Feche o AirPods Mic Bridge e tente novamente.
pt.InvalidStartup=A opção STARTUP deve ser yes ou no.
id.StartupOption=Jalankan AirPods Mic Bridge saat masuk ke Windows
id.StartupFailed=Tidak dapat memperbarui startup otomatis. Silakan coba lagi.
id.AppRunning=Tutup AirPods Mic Bridge dan coba lagi.
id.InvalidStartup=Opsi STARTUP harus bernilai yes atau no.

[Code]
const
  AppMutexName = 'Local\AirPodsMicBridge.Singleton.v1';
  StopEventName = 'Local\AirPodsMicBridge.Stop.v1';
  EventModifyState = $0002;

var
  StartupCheckBox: TNewCheckBox;
  StartupEnabled: Boolean;
  StartupApplied: Boolean;
  StartupFailed: Boolean;
  StartupControlsPositioned: Boolean;
  Uninstalling: Boolean;

function OpenEvent(DesiredAccess: Longword; InheritHandle: Boolean; Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(EventHandle: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function SetEnvironmentVariable(Name, Value: String): Boolean;
  external 'SetEnvironmentVariableW@kernel32.dll stdcall';

function GetProgramsDirectory(Param: String): String;
begin
#ifdef SmokeRoot
  Result := '{#SmokeRoot}\startmenu\AirPods Mic Bridge';
#else
  Result := ExpandConstant('{group}');
#endif
end;

function GetStartupDirectory(Param: String): String;
begin
#ifdef SmokeRoot
  if IsAdminInstallMode then Result := '{#SmokeRoot}\data\common-startup'
  else Result := '{#SmokeRoot}\data\startup';
#else
  Result := ExpandConstant('{autostartup}');
#endif
end;

procedure InitializeFixtureEnvironment;
begin
#ifdef SmokeRoot
  if not SetEnvironmentVariable('AIRPODS_MIC_BRIDGE_DATA', '{#SmokeRoot}\data') then
    RaiseException('Cannot initialize the isolated installer fixture.');
#endif
end;

function StopRunningApp: Boolean;
var
  EventHandle: THandle;
  Attempt: Integer;
#ifdef SmokeRoot
  StopExecutable: String;
  ExitCode: Integer;
#endif
begin
#ifdef SmokeRoot
  if Uninstalling then StopExecutable := ExpandConstant('{app}\AirPodsMicBridge.exe')
  else
  begin
    ExtractTemporaryFile('AirPodsMicBridge-Stop.exe');
    StopExecutable := ExpandConstant('{tmp}\AirPodsMicBridge-Stop.exe');
  end;
  if not FileExists(StopExecutable) then
  begin
    Result := True;
    exit;
  end;
  Result := Exec(StopExecutable, '--stop', ExtractFileDir(StopExecutable),
    SW_HIDE, ewWaitUntilTerminated, ExitCode);
  if Result then Result := ExitCode = 0;
  exit;
#endif
  Result := not CheckForMutexes(AppMutexName);
  if Result then exit;
  Log('Requesting AirPods Mic Bridge to exit gracefully.');
  EventHandle := OpenEvent(EventModifyState, False, StopEventName);
  if EventHandle <> 0 then
  begin
    SetEvent(EventHandle);
    CloseHandle(EventHandle);
  end;
  for Attempt := 1 to 100 do
  begin
    if not CheckForMutexes(AppMutexName) then
    begin
      Result := True;
      exit;
    end;
    Sleep(100);
  end;
end;

function InitializeSetup: Boolean;
var
  Choice: String;
begin
  InitializeFixtureEnvironment;
  Choice := Lowercase(Trim(ExpandConstant('{param:STARTUP|yes}')));
  Result := (Choice = 'yes') or (Choice = 'no');
  if not Result then
  begin
    SuppressibleMsgBox(CustomMessage('InvalidStartup'), mbError, MB_OK, IDOK);
    exit;
  end;
  StartupEnabled := Choice = 'yes';
  Result := StopRunningApp;
  if not Result then
    SuppressibleMsgBox(CustomMessage('AppRunning'), mbError, MB_OK, IDOK);
end;

procedure InitializeWizard;
begin
  StartupCheckBox := TNewCheckBox.Create(WizardForm);
  StartupCheckBox.Parent := WizardForm.FinishedPage;
  StartupCheckBox.Left := WizardForm.RunList.Left;
  StartupCheckBox.Width := WizardForm.RunList.Width;
  StartupCheckBox.Height := ScaleY(34);
  StartupCheckBox.Caption := CustomMessage('StartupOption');
  StartupCheckBox.Checked := StartupEnabled;
  StartupCheckBox.Visible := False;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
  begin
    if not StartupControlsPositioned then
    begin
      StartupCheckBox.Top := WizardForm.RunList.Top;
      WizardForm.RunList.Top := WizardForm.RunList.Top + StartupCheckBox.Height + ScaleY(6);
      WizardForm.RunList.Height := WizardForm.RunList.Height - StartupCheckBox.Height - ScaleY(6);
      StartupControlsPositioned := True;
      Log(Format('Finished page controls: startup top=%d height=%d; launch top=%d height=%d; page height=%d', [StartupCheckBox.Top, StartupCheckBox.Height, WizardForm.RunList.Top,
         WizardForm.RunList.Height, WizardForm.FinishedPage.Height]));
    end;
    StartupCheckBox.Visible := True;
  end;
end;

function ApplyStartupChoice: Boolean;
var
  Parameters: String;
  ExitCode: Integer;
begin
  if StartupApplied then
  begin
    Result := True;
    exit;
  end;
  if StartupCheckBox <> nil then StartupEnabled := StartupCheckBox.Checked;
  if IsAdminInstallMode then
  begin
    if StartupEnabled then Parameters := '--startup-enable-all-users'
    else Parameters := '--startup-disable-all-users';
  end
  else
  begin
    if StartupEnabled then Parameters := '--startup-enable'
    else Parameters := '--startup-disable';
  end;
  Result := Exec(ExpandConstant('{app}\AirPodsMicBridge.exe'), Parameters,
    ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ExitCode);
  if Result then Result := ExitCode = 0;
  StartupApplied := Result;
  StartupFailed := not Result;
  if not Result then
  begin
    Log(Format('Automatic startup update failed: %d', [ExitCode]));
    SuppressibleMsgBox(CustomMessage('StartupFailed'), mbError, MB_OK, IDOK);
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpFinished then Result := ApplyStartupChoice;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if ((CurStep = ssPostInstall) and WizardSilent) or
     ((CurStep = ssDone) and not StartupApplied) then ApplyStartupChoice;
end;

function GetCustomSetupExitCode: Integer;
begin
  if StartupFailed then Result := 12 else Result := 0;
end;

function InitializeUninstall: Boolean;
begin
  Uninstalling := True;
  InitializeFixtureEnvironment;
  Result := StopRunningApp;
  if not Result then
    SuppressibleMsgBox(CustomMessage('AppRunning'), mbError, MB_OK, IDOK);
end;
