[Setup]
AppName=VRChatLotteryTool
AppVersion=1.0.0
DefaultDirName={autopf}\VRChatLotteryTool
DefaultGroupName=nanoha_778
OutputDir=output
OutputBaseFilename=VRChatLotteryToolSetup
Compression=lzma
SolidCompression=yes
SetupIconFile=app.ico
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Files]
Source: "bin\Release\net10.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\VRChatLotteryTool"; Filename: "{app}\VRChatLotteryTool.exe"
Name: "{commondesktop}\VRChatLotteryTool"; Filename: "{app}\VRChatLotteryTool.exe"

[Run]
Filename: "{app}\VRChatLotteryTool.exe"; Description: "アプリを起動"; Flags: nowait postinstall skipifsilent

[Code]
var
  DeleteUserData: Boolean;

function InitializeUninstall(): Boolean;
begin
  DeleteUserData := MsgBox(
    '設定ファイルやログも削除しますか？',
    mbConfirmation,
    MB_YESNO
  ) = IDYES;

  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usUninstall) and DeleteUserData then
  begin
    DelTree(ExpandConstant('{localappdata}\VRChatLotteryTool'), True, True, True);
  end;
end;