[Setup]
AppName=VRChatLotteryTool
AppVersion=1.0.0
DefaultDirName={autopf}\VRChatLotteryTool
DefaultGroupName=VRChatLotteryTool
OutputDir=output
OutputBaseFilename=VRChatLotteryToolSetup
Compression=lzma
SolidCompression=yes
SetupIconFile=app.ico
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Files]
Source: "bin\Release\net10.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Code]
var
  DeleteUserData: Boolean;

function InitializeUninstall(): Boolean;
begin
  DeleteUserData :=
    MsgBox(
      '設定・ログ・履歴データも削除しますか？',
      mbConfirmation,
      MB_YESNO
    ) = IDYES;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  TargetDir: string;
begin
  if (CurUninstallStep = usUninstall) and DeleteUserData then
  begin
    TargetDir := ExpandConstant('{userappdata}\VRChatLotteryTool');
    DelTree(TargetDir, True, True, True);
  end;
end;

[Icons]
Name: "{group}\VRChatLotteryTool"; Filename: "{app}\VRChatLotteryTool.exe"
Name: "{commondesktop}\VRChatLotteryTool"; Filename: "{app}\VRChatLotteryTool.exe"

[Run]
Filename: "{app}\VRChatLotteryTool.exe"; Description: "アプリを起動"; Flags: nowait postinstall skipifsilent

