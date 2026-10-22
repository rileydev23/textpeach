; Windows installer: textpeach-windows-x64-setup.exe (Inno Setup 6). Per user, no administrator: installs the same portable exe
; into %LOCALAPPDATA%\Programs\TextPeach, adds it to the Start menu (so Windows search finds it) and to
; Settings > Apps with its uninstaller. Settings in %APPDATA%\peachapps\TextPeach are kept on uninstall.
; Build after build.cmd:  iscc /DAppVersion=1.2.3 installer\TextPeach.iss   ->  dist\textpeach-windows-x64-setup.exe
; The app updates itself by running a newer setup with /SILENT; installed.txt tells it that it was installed.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{24866AE8-21D6-4F65-9789-ACA1036C3C40}
AppName=TextPeach
AppVersion={#AppVersion}
AppVerName=TextPeach {#AppVersion}
AppPublisher=TextPeach
AppPublisherURL=https://github.com/rileydev23/textpeach
AppSupportURL=https://github.com/rileydev23/textpeach/issues
AppUpdatesURL=https://github.com/rileydev23/textpeach/releases
DefaultDirName={localappdata}\Programs\TextPeach
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=textpeach-windows-x64-setup
SetupIconFile=..\src\TextPeach\Assets\app.ico
UninstallDisplayIcon={app}\TextPeach.exe
UninstallDisplayName=TextPeach
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Closes a running TextPeach before replacing it (the app also exits by itself when it updates).
CloseApplications=force
RestartApplications=no

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\TextPeach.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "installed.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\TextPeach"; Filename: "{app}\TextPeach.exe"
Name: "{userdesktop}\TextPeach"; Filename: "{app}\TextPeach.exe"; Tasks: desktopicon

[Run]
; Interactive install: a "Launch TextPeach" checkbox. Silent install (an update from the app): reopen it right away.
Filename: "{app}\TextPeach.exe"; Description: "{cm:LaunchProgram,TextPeach}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\TextPeach.exe"; Flags: nowait; Check: WizardSilent

[UninstallDelete]
; Left behind by an update that replaced the exe in place.
Type: files; Name: "{app}\TextPeach.exe.old"
Type: files; Name: "{app}\TextPeach.exe.new"
Type: dirifempty; Name: "{app}"
