{ Windows 95/98-era wizard styling. Inno's real navigation and install engine remain intact. }
const
  ClassicFace = $C0C0C0;
  BannerBlue = $800000;
var
  ImportPage: TInputDirWizardPage;
  ImportChoice: TInputOptionWizardPage;
  PercentLabel: TNewStaticText;

function SetWindowTheme(Window: HWND; SubAppName, SubIdList: String): Integer;
  external 'SetWindowTheme@uxtheme.dll stdcall';

procedure ClassicControl(Control: TWinControl);
begin
  SetWindowTheme(Control.Handle,' ',' ');
end;

#include "CopyAnimation.iss"

procedure Banner(Parent: TWinControl);
var Image: TBitmapImage; Name,VersionText: TNewStaticText;
    Bitmap: TBitmap; I,H,W,R,G,B: Integer;
begin
  W:=ScaleX(164); H:=Parent.Height;
  Image:=TBitmapImage.Create(WizardForm); Image.Parent:=Parent; Image.SetBounds(0,0,W,H);
  Bitmap:=TBitmap.Create;
  try
    Bitmap.Width:=W; Bitmap.Height:=H;
    for I:=0 to H-1 do begin
      R:=0; G:=0; B:=128+(I*50 div H);
      Bitmap.Canvas.Pen.Color:=R+(G shl 8)+(B shl 16);
      Bitmap.Canvas.MoveTo(0,I); Bitmap.Canvas.LineTo(W,I);
    end;
    Bitmap.Canvas.Pen.Color:=$D3A96C;
    Bitmap.Canvas.Brush.Style:=bsClear;
    Bitmap.Canvas.Rectangle(ScaleX(46),H-ScaleY(148),W+ScaleX(30),H-ScaleY(74));
    Bitmap.Canvas.Rectangle(ScaleX(80),H-ScaleY(172),W+ScaleX(44),H-ScaleY(96));
    Image.Bitmap:=Bitmap;
  finally Bitmap.Free; end;
  Name:=TNewStaticText.Create(WizardForm); Name.Parent:=Parent;
  Name.AutoSize:=False; Name.SetBounds(ScaleX(18),H-ScaleY(62),W-ScaleX(28),ScaleY(28));
  Name.Caption:='AgitCon'; Name.Font.Name:='MS Sans Serif'; Name.Font.Height:=-ScaleY(20);
  Name.Font.Style:=[fsBold]; Name.Font.Color:=clWhite; Name.Color:=BannerBlue;
  VersionText:=TNewStaticText.Create(WizardForm); VersionText.Parent:=Parent;
  VersionText.SetBounds(ScaleX(19),H-ScaleY(30),W-ScaleX(22),ScaleY(18));
  VersionText.Caption:='Version {#Version}  /  {#Architecture}';
  VersionText.Font.Name:='Tahoma'; VersionText.Font.Height:=-ScaleY(11);
  VersionText.Font.Color:=clWhite; VersionText.Color:=BannerBlue;
end;

procedure SetupTheme;
var I,L,W: Integer;
begin
  WizardForm.Caption:='아지트콘 설치';
  WizardForm.Color:=ClassicFace;
  WizardForm.Bevel.Visible:=True; WizardForm.Bevel1.Visible:=True;
  WizardForm.MainPanel.Color:=ClassicFace; WizardForm.WizardSmallBitmapImage.Visible:=False;
  WizardForm.Font.Name:='굴림'; WizardForm.Font.Size:=9;
  WizardForm.WizardBitmapImage.Visible:=False; WizardForm.WizardBitmapImage2.Visible:=False;
  WizardForm.WelcomePage.Color:=ClassicFace; WizardForm.FinishedPage.Color:=ClassicFace;
  for I:=0 to WizardForm.InnerNotebook.PageCount-1 do WizardForm.InnerNotebook.Pages[I].Color:=ClassicFace;
  ImportChoice.Surface.Color:=ClassicFace; ImportPage.Surface.Color:=ClassicFace;
  ImportChoice.CheckListBox.Color:=ClassicFace; WizardForm.TasksList.Color:=ClassicFace;
  Banner(WizardForm.WelcomePage); Banner(WizardForm.FinishedPage);
  L:=ScaleX(190); W:=WizardForm.WelcomePage.Width-L-ScaleX(24);
  WizardForm.WelcomeLabel1.SetBounds(L,ScaleY(22),W,ScaleY(62));
  WizardForm.WelcomeLabel1.Font.Name:='굴림'; WizardForm.WelcomeLabel1.Font.Size:=12;
  WizardForm.WelcomeLabel1.Font.Style:=[fsBold];
  WizardForm.WelcomeLabel1.Caption:='아지트콘 설치 마법사';
  WizardForm.WelcomeLabel2.SetBounds(L,ScaleY(108),W,WizardForm.WelcomePage.Height-ScaleY(232));
  WizardForm.WelcomeLabel2.Caption:='아지트콘 {#Version} 설치를 시작할게요.'#13#10#13#10+
    '디시콘·아카콘 전송과 GIF·WebP 자동재생을 하나의 프로그램으로 사용할 수 있어요.'#13#10#13#10+
    '사용하던 콘과 별칭도 가져올 수 있어요. AI 모델은 선택 기능이라 설치할 때 내려받지 않아요.'#13#10#13#10+
    '계속하려면 [다음]을 눌러주세요.';
  WizardForm.FinishedHeadingLabel.SetBounds(L,ScaleY(22),W,ScaleY(62));
  WizardForm.FinishedHeadingLabel.Font.Name:='굴림'; WizardForm.FinishedHeadingLabel.Font.Size:=12;
  WizardForm.FinishedHeadingLabel.Caption:='아지트콘 설치 완료';
  WizardForm.FinishedLabel.SetBounds(L,ScaleY(104),W,ScaleY(116));
  WizardForm.FinishedLabel.Caption:='아지트콘 설치가 끝났어요.'#13#10#13#10+
    '아래 항목을 선택하면 바로 실행해요. 움짤 자동재생은 앱 설정에서 시작할 수 있어요.'#13#10#13#10+
    '설치 마법사를 닫으려면 [마침]을 눌러주세요.';
  WizardForm.RunList.SetBounds(L,ScaleY(244),W,ScaleY(50));
  WizardForm.RunList.Color:=ClassicFace;
  WizardForm.BeveledLabel.Caption:='AgitCon {#Version}  ·  Windows {#Architecture}';
  ClassicControl(WizardForm.NextButton); ClassicControl(WizardForm.BackButton); ClassicControl(WizardForm.CancelButton);
  ClassicControl(WizardForm.DirBrowseButton); ClassicControl(WizardForm.ProgressGauge);
  PrepareCopy;
  WizardForm.StatusLabel.SetBounds(0,ScaleY(96),WizardForm.InstallingPage.Width,ScaleY(22));
  WizardForm.FilenameLabel.SetBounds(0,ScaleY(121),WizardForm.InstallingPage.Width,ScaleY(22));
  WizardForm.ProgressGauge.SetBounds(0,ScaleY(154),WizardForm.InstallingPage.Width,ScaleY(20));
  PercentLabel:=TNewStaticText.Create(WizardForm); PercentLabel.Parent:=WizardForm.InstallingPage;
  PercentLabel.AutoSize:=False;
  PercentLabel.SetBounds(WizardForm.ProgressGauge.Left,WizardForm.ProgressGauge.Top+WizardForm.ProgressGauge.Height+ScaleY(10),WizardForm.ProgressGauge.Width,ScaleY(20));
  PercentLabel.Alignment:=taRightJustify; PercentLabel.Caption:='0%';
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  WizardForm.NextButton.Caption:='다음(&N) >';
  WizardForm.BackButton.Caption:='< 이전(&B)';
  WizardForm.CancelButton.Caption:='취소';
  if CurPageID=wpReady then WizardForm.NextButton.Caption:='설치(&I)';
  if CurPageID=wpFinished then WizardForm.NextButton.Caption:='마침(&F)';
  ClassicControl(WizardForm.NextButton);
  ChangeCopyPage(CurPageID);
end;

procedure CurInstallProgressChanged(CurProgress,MaxProgress: Integer);
begin
  if (PercentLabel<>nil) and (MaxProgress>0) then PercentLabel.Caption:=IntToStr((Int64(CurProgress)*100) div MaxProgress)+'%';
end;
