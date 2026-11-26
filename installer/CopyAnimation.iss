{ Unmodified Microsoft VCSamples FILECOPY.AVI in Windows SysAnimate32.
  Only our two native host windows are subclassed to supply the dialog background. }
var
  CopyWelcome,CopyInstalling,CopyHostWelcome,CopyHostInstalling: HWND;
  CopyBrush: NativeUInt;
  OriginalCopyProc: NativeInt;
  CopyUses64BitPointers: Boolean;

procedure InitCommonControls;
  external 'InitCommonControls@comctl32.dll stdcall';
function CreateWindowEx(ExStyle: DWORD; ClassName,WindowName: String; Style: DWORD; X,Y,W,H: Integer;
  Parent: HWND; Menu: NativeUInt; Instance: NativeUInt; Param: NativeInt): HWND;
  external 'CreateWindowExW@user32.dll stdcall';
function AnimationOpen(Window: HWND; Msg: UINT; WParam: WPARAM; FileName: String): LRESULT;
  external 'SendMessageW@user32.dll stdcall';
function SetWindowPos(Window,After: HWND; X,Y,W,H: Integer; Flags: UINT): BOOL;
  external 'SetWindowPos@user32.dll stdcall';
function DestroyWindow(Window: HWND): BOOL;
  external 'DestroyWindow@user32.dll stdcall';
function CurrentModule(Name: NativeInt): NativeUInt;
  external 'GetModuleHandleW@kernel32.dll stdcall';
function CurrentProcess: THandle;
  external 'GetCurrentProcess@kernel32.dll stdcall';
function IsWow64Process(Process: THandle; var Wow64: BOOL): BOOL;
  external 'IsWow64Process@kernel32.dll stdcall';
function ReplaceProc32(Window: HWND; Index,Value: LongInt): LongInt;
  external 'SetWindowLongW@user32.dll stdcall';
function ReplaceProc64(Window: HWND; Index: Integer; Value: NativeInt): NativeInt;
  external 'SetWindowLongPtrW@user32.dll stdcall delayload';
function CallOriginal(Proc: NativeInt; Window: HWND; Msg: UINT; WParam: WPARAM; LParam: LPARAM): LRESULT;
  external 'CallWindowProcW@user32.dll stdcall';
function DefaultWindowProc(Window: HWND; Msg: UINT; WParam: WPARAM; LParam: LPARAM): LRESULT;
  external 'DefWindowProcW@user32.dll stdcall';
function SolidBrush(Color: DWORD): NativeUInt;
  external 'CreateSolidBrush@gdi32.dll stdcall';
function DeleteGdiObject(ObjectHandle: NativeUInt): BOOL;
  external 'DeleteObject@gdi32.dll stdcall';
function BackgroundColor(DC: NativeUInt; Color: DWORD): DWORD;
  external 'SetBkColor@gdi32.dll stdcall';
function ClientRect(Window: HWND; var R: TRect): BOOL;
  external 'GetClientRect@user32.dll stdcall';
function FillBackground(DC: NativeUInt; var R: TRect; Brush: NativeUInt): Integer;
  external 'FillRect@user32.dll stdcall';
function GetDC(Window: HWND): NativeUInt;
  external 'GetDC@user32.dll stdcall';
function ReleaseDC(Window: HWND; DC: NativeUInt): Integer;
  external 'ReleaseDC@user32.dll stdcall';
function ValidateRect(Window: HWND; Rect: NativeInt): BOOL;
  external 'ValidateRect@user32.dll stdcall';

function CopyHostProc(Window: HWND; Msg: UINT; WParam: WPARAM; LParam: LPARAM): LRESULT;
var R: TRect; DC: NativeUInt;
begin
  if Msg=$138 then begin
    BackgroundColor(WParam,ClassicFace); Result:=CopyBrush; Exit;
  end;
  if (Msg=$14) or (Msg=$318) then begin
    ClientRect(Window,R); FillBackground(WParam,R,CopyBrush); Result:=1; Exit;
  end;
  if Msg=$F then begin
    DC:=GetDC(Window); ClientRect(Window,R); FillBackground(DC,R,CopyBrush);
    ReleaseDC(Window,DC); ValidateRect(Window,0); Result:=0; Exit;
  end;
  if OriginalCopyProc<>0 then Result:=CallOriginal(OriginalCopyProc,Window,Msg,WParam,LParam)
  else Result:=DefaultWindowProc(Window,Msg,WParam,LParam);
end;

function CreateCopy(Parent: TWinControl; X,Y: Integer; var Host: HWND): HWND;
var Callback,Old: NativeInt;
begin
  Result:=0;
  Host:=CreateWindowEx(0,'STATIC','AgitCon original animation host',$50000000,X,Y,ScaleX(260),ScaleY(40),Parent.Handle,0,CurrentModule(0),0);
  if Host=0 then Exit;
  Callback:=CreateCallback(@CopyHostProc);
  if CopyUses64BitPointers then Old:=ReplaceProc64(Host,-4,Callback) else Old:=ReplaceProc32(Host,-4,Callback);
  if Old=0 then begin DestroyWindow(Host); Host:=0; Exit; end;
  OriginalCopyProc:=Old;
  Result:=CreateWindowEx(0,'SysAnimate32','파일 복사 애니메이션',$50000003,0,0,ScaleX(260),ScaleY(40),Host,0,CurrentModule(0),0);
  if Result=0 then Exit;
  if AnimationOpen(Result,$467,0,ExpandConstant('{tmp}\filecopy.avi'))=0 then begin
    DestroyWindow(Result); Result:=0; Exit;
  end;
  SetWindowPos(Result,0,0,0,ScaleX(260),ScaleY(40),$14);
end;

procedure PrepareCopy;
var Wow: BOOL;
begin
  if WizardSilent then Exit;
  CopyUses64BitPointers:=False;
  if IsWin64 then begin
    if not IsWow64Process(CurrentProcess,Wow) then Exit;
    CopyUses64BitPointers:=not Wow;
  end;
  InitCommonControls; ExtractTemporaryFile('filecopy.avi');
  CopyBrush:=SolidBrush(ClassicFace);
  CopyWelcome:=CreateCopy(WizardForm.WelcomePage,ScaleX(196),WizardForm.WelcomePage.Height-ScaleY(78),CopyHostWelcome);
  CopyInstalling:=CreateCopy(WizardForm.InstallingPage,(WizardForm.InstallingPage.Width-ScaleX(260)) div 2,ScaleY(24),CopyHostInstalling);
end;

procedure ChangeCopyPage(PageID: Integer);
begin
  if CopyWelcome<>0 then SendMessage(CopyWelcome,$466,0,0);
  if CopyInstalling<>0 then SendMessage(CopyInstalling,$466,0,0);
  if (PageID=wpWelcome) and (CopyWelcome<>0) then SendMessage(CopyWelcome,$465,$FFFFFFFF,-65536);
  if (PageID=wpInstalling) and (CopyInstalling<>0) then SendMessage(CopyInstalling,$465,$FFFFFFFF,-65536);
end;

procedure DeinitializeSetup;
begin
  if CopyHostWelcome<>0 then begin DestroyWindow(CopyHostWelcome); CopyHostWelcome:=0; end;
  if CopyHostInstalling<>0 then begin DestroyWindow(CopyHostInstalling); CopyHostInstalling:=0; end;
  if CopyBrush<>0 then begin DeleteGdiObject(CopyBrush); CopyBrush:=0; end;
end;
