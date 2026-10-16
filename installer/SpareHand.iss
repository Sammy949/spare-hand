; Spare Hand installer (Inno Setup 6). Per-user: no admin prompt, installs to %LOCALAPPDATA%\Programs\Spare Hand.
; Build: ISCC.exe /DAppVersion=0.1.0 /DSourceDir=<publish folder> installer\SpareHand.iss
; Output: installer\out\SpareHand-Setup.exe (the release workflow does all of this).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\rel\Spare Hand"
#endif

[Setup]
; Never change AppId: Windows uses it to recognise upgrades and the uninstall entry.
AppId={{6F2C8D3A-4B1E-4E7A-9C55-2D8B1F0A7E31}
AppName=Spare Hand
AppVersion={#AppVersion}
AppVerName=Spare Hand {#AppVersion}
AppPublisher=Spare Hand contributors
AppPublisherURL=https://github.com/Sammy949/spare-hand
AppSupportURL=https://github.com/Sammy949/spare-hand/issues
AppUpdatesURL=https://github.com/Sammy949/spare-hand/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\Spare Hand
DefaultGroupName=Spare Hand
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
WizardStyle=modern
WizardSizePercent=100
SetupIconFile=..\src\SpareHand\Assets\SpareHand.ico
UninstallDisplayIcon={app}\SpareHand.exe
UninstallDisplayName=Spare Hand
OutputDir=out
OutputBaseFilename=SpareHand-Setup
Compression=lzma2/max
SolidCompression=yes
; Spare Hand is closed by PrepareToInstall below; this keeps the Restart Manager from asking about it too.
CloseApplications=no

[Tasks]
Name: startup; Description: "Start Spare Hand when I sign in"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Spare Hand"; Filename: "{app}\SpareHand.exe"

[Registry]
; The same value the app's own "Start with Windows" setting reads and writes (SystemIntegration.cs).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SpareHand"; ValueData: """{app}\SpareHand.exe"" --background"; Tasks: startup

[Run]
Filename: "{app}\SpareHand.exe"; Description: "Open Spare Hand"; Flags: nowait postinstall skipifsilent

[Code]
procedure CloseSpareHand();
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM SpareHand.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseSpareHand();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Value: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    CloseSpareHand();
    // Remove the startup entry whether the installer or the settings page added it, but only if it is ours.
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'SpareHand', Value) and
       (Pos(Lowercase(ExpandConstant('{app}')), Lowercase(Value)) > 0) then
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'SpareHand');
  end;

  // Settings and the Hold list (links only, never your files) are kept unless you say otherwise.
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent and
     DirExists(ExpandConstant('{localappdata}\SpareHand')) and
     (MsgBox('Also remove your Spare Hand settings and Hold list?' + #13#10#13#10 +
             'Hold only keeps links, so none of your files are touched either way.',
             mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
    DelTree(ExpandConstant('{localappdata}\SpareHand'), True, True, True);
end;
