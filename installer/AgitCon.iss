#ifndef Version
  #error Version required
#endif
#ifndef Architecture
  #error Architecture required
#endif
#ifndef Payload
  #error Payload required
#endif
[Setup]
#ifdef TestMode
AppId=AgitCon.OfflineInstallerTest.{#Architecture}
#else
AppId={{9FE1749B-E6C9-4524-BE9C-C83C495BA52B}
#endif
SetupArchitecture={#Architecture}
AppName=아지트콘
AppVersion={#Version}
AppPublisher=Ch0m1n
AppPublisherURL=https://github.com/Ch0m1n/KakaoTalk-emoji-sender
AppSupportURL=https://github.com/Ch0m1n/KakaoTalk-emoji-sender/issues
DefaultDirName={localappdata}\Programs\AgitCon
DefaultGroupName=아지트콘
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
#ifndef TestMode
AppMutex=Local\AjitCon.Personal
#endif
CloseApplications=no
RestartApplications=no
MinVersion=10.0
#if Architecture == "x64"
ArchitecturesAllowed=x64compatible
#else
ArchitecturesAllowed=x86compatible
#endif
#ifdef TestMode
OutputDir=..\artifacts\tests\installer-build
#else
OutputDir=..\releases\{#Version}
#endif
OutputBaseFilename=AgitCon-Setup-{#Version}-win-{#Architecture}
SetupIconFile=..\assets\AgitCon.ico
WizardSmallImageFile=..\assets\AgitCon.png
UninstallDisplayIcon={app}\AgitCon.exe
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=classic light
WizardSizePercent=120,120
WizardResizable=no
DisableWelcomePage=no
DefaultDialogFontName=굴림
VersionInfoVersion={#Version}.0

[Languages]
Name: korean; MessagesFile: compiler:Languages\Korean.isl
Name: english; MessagesFile: compiler:Default.isl

[Tasks]
Name: desktopicon; Description: 바탕 화면에 바로가기 만들기; Flags: unchecked
Name: startup; Description: Windows 로그인 시 아지트콘 실행; Flags: unchecked

[Files]
Source: "assets\filecopy.avi"; Flags: dontcopy
Source: "..\third_party\Microsoft-VCSamples-license.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "assets\source.json"; DestDir: "{app}\licenses"; DestName: "filecopy-source.json"; Flags: ignoreversion
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

#ifndef TestMode
[Icons]
Name: "{userprograms}\아지트콘"; Filename: "{app}\AgitCon.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\아지트콘"; Filename: "{app}\AgitCon.exe"; Tasks: desktopicon
Name: "{userstartup}\AgitCon"; Filename: "{app}\AgitCon.exe"; WorkingDir: "{app}"; Tasks: startup

[Run]
Filename: "{app}\AgitCon.exe"; Description: 아지트콘 실행; Flags: nowait postinstall skipifsilent; Check: not IsAgitUpdate

Filename: "{app}\AgitCon.exe"; Flags: nowait; Check: IsAgitUpdate
#endif

[Code]
#include "Theme.iss"

function IsAgitUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATEFROMAGIT|0}') = '1';
end;

procedure InitializeWizard;
begin
  ImportChoice := CreateInputOptionPage(wpSelectDir, '쓰던 콘을 가져올까요?', '처음 설치라면 그대로 계속 눌러주세요.', '설치판 업데이트는 기존 데이터를 자동으로 유지해요.', True, False);
  ImportChoice.Add('처음 설치 / 설치판 업데이트');
  ImportChoice.Add('예전 압축판 폴더에서 콘과 설정 가져오기');
  ImportChoice.SelectedValueIndex := 0;
  ImportPage := CreateInputDirPage(ImportChoice.ID, '기존 콘과 설정 가져오기 (선택)',
    '예전 압축판을 사용했다면 폴더를 선택해주세요.',
    '예전 AgitCon.exe가 있는 폴더를 선택해주세요. 받은 콘과 별칭을 복사하며 원본은 그대로 둬요. 선택 AI 모델은 새 앱에서 다시 준비해야 해요.', False, '');
  ImportPage.Add('기존 아지트콘 폴더:');
  SetupTheme;
#ifdef DesignPreview
  WizardForm.CenterOnShow := False; WizardForm.Left := -22000; WizardForm.Top := -22000;
#endif
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = ImportPage.ID) and (ImportChoice.SelectedValueIndex <> 1);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
#ifdef DesignPreview
  if CurPageID = wpReady then begin Result := False; Exit; end;
#endif
  if (CurPageID = ImportPage.ID) and (ImportPage.Values[0] <> '') then
    if not FileExists(AddBackslash(ImportPage.Values[0]) + 'data\catalog.json') then begin
      MsgBox('선택한 폴더에 data\catalog.json이 없어요.', mbError, MB_OK);
      Result := False;
    end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Code: Integer;
begin
  if (CurStep = ssPostInstall) and (ImportPage.Values[0] <> '') then begin
    if not Exec(ExpandConstant('{app}\runtime\python\python.exe'),
      '"' + ExpandConstant('{app}\codecs\import-portable.py') + '" "' + ImportPage.Values[0] + '" "' + ExpandConstant('{app}') + '"',
      ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      MsgBox('기존 데이터 가져오기를 마치지 못했어요. 원본은 변경하지 않았어요. 설치 폴더의 migration-result.txt를 확인해주세요.', mbError, MB_OK);
  end;
end;

{ No UninstallDelete: received media, catalog, models and playback state are user data. }

function UpdateReadyMemo(Space,NewLine,MemoUserInfoInfo,MemoDirInfo,MemoTypeInfo,MemoComponentsInfo,MemoGroupInfo,MemoTasksInfo: String): String;
begin
  Result:='설치 위치'+NewLine+WizardForm.DirEdit.Text+NewLine+NewLine;
  if ImportChoice.SelectedValueIndex=1 then Result:=Result+'기존 콘과 별칭 가져오기'+NewLine+ImportPage.Values[0]+NewLine+NewLine
  else Result:=Result+'기존 데이터'+NewLine+'설치된 설정과 콘은 그대로 유지해요.'+NewLine+NewLine;
  if MemoTasksInfo<>'' then Result:=Result+MemoTasksInfo else Result:=Result+'시작 옵션'+NewLine+'추가 바로가기와 로그인 자동 실행은 만들지 않아요.';
end;
