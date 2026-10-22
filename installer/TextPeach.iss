; Windows installer: textpeach-windows-x64-setup.exe (Inno Setup 6). Per user, no administrator: installs the same portable exe
; into %LOCALAPPDATA%\Programs\TextPeach, adds it to the Start menu (so Windows search finds it) and to
; Settings > Apps with its uninstaller. Settings in %APPDATA%\peachapps\TextPeach are kept on uninstall.
; Build after build.cmd:  iscc /DAppVersion=1.2.3 installer\TextPeach.iss   ->  dist\textpeach-windows-x64-setup.exe
; The app updates itself by running a newer setup with /SILENT; installed.txt tells it that it was installed.
; Explorer integration (optional tasks, all per user under HKCU): "Abrir con TextPeach" on files and folders, and
; TextPeach registered for the text and code types below, so it shows up in "Abrir con" and in Settings > Default apps.
; Windows lets only the user pick the default app, so the setup offers to open that page instead of taking it over.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

; Types TextPeach registers for (text and code only: images, documents and binaries keep their own apps).
#define FileTypes "txt text log md markdown mdx rst tex csv tsv json jsonc json5 xml xsd xsl xaml axaml yaml yml toml " + \
  "ini cfg conf config env properties editorconfig gitignore gitattributes dockerignore npmignore dockerfile " + \
  "cs csx csproj fsproj vbproj props targets fs fsi fsx vb razor cshtml resx nuspec manifest " + \
  "java kt kts scala groovy gradle js mjs cjs jsx ts mts cts tsx vue svelte html htm xhtml css scss sass less " + \
  "py pyw pyi go rs c h cpp cc cxx hpp hh hxx ino m mm swift dart php rb rake gemspec lua pl pm r jl hs " + \
  "clj cljs edn erl ex exs sql graphql gql proto sh bash zsh fish ps1 psm1 psd1 bat cmd cmake mk diff patch " + \
  "plist code-workspace"

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
; Tells the Explorer to pick up the new menu entries and file types right away.
ChangesAssociations=yes

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
es.ExplorerGroup=Integración con el Explorador de archivos:
en.ExplorerGroup=File Explorer integration:
es.TaskContextFiles=Agregar "Abrir con TextPeach" al menú contextual de los archivos
en.TaskContextFiles=Add "Open with TextPeach" to the context menu of files
es.TaskContextFolders=Agregar "Abrir con TextPeach" al menú contextual de las carpetas
en.TaskContextFolders=Add "Open with TextPeach" to the context menu of folders
es.TaskAssociate=Registrar TextPeach para los archivos de texto y código (aparece en "Abrir con")
en.TaskAssociate=Register TextPeach for text and code files (it shows up in "Open with")
es.ChooseDefault=Elegir TextPeach como aplicación predeterminada
en.ChooseDefault=Choose TextPeach as the default app
es.OpenWith=Abrir con TextPeach
en.OpenWith=Open with TextPeach
es.FileTypeName=Archivo %1
en.FileTypeName=%1 File
es.AppDescription=Visor y analizador de código
en.AppDescription=Code viewer and analyzer

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "contextfiles"; Description: "{cm:TaskContextFiles}"; GroupDescription: "{cm:ExplorerGroup}"
Name: "contextfolders"; Description: "{cm:TaskContextFolders}"; GroupDescription: "{cm:ExplorerGroup}"
Name: "associate"; Description: "{cm:TaskAssociate}"; GroupDescription: "{cm:ExplorerGroup}"

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
; Settings > Default apps on TextPeach's own page (Windows 11; older versions open the general page).
Filename: "ms-settings:defaultapps?registeredAppUser=TextPeach"; Description: "{cm:ChooseDefault}"; Flags: postinstall shellexec nowait skipifsilent unchecked; Tasks: associate

[UninstallDelete]
; Left behind by an update that replaced the exe in place.
Type: files; Name: "{app}\TextPeach.exe.old"
Type: files; Name: "{app}\TextPeach.exe.new"
Type: dirifempty; Name: "{app}"

[Code]
function Exe(): String;
begin
  Result := ExpandConstant('{app}\TextPeach.exe');
end;

{ One "Abrir con TextPeach" entry of the Explorer's context menu. Arg: %1 is the file clicked, %V the folder. }
procedure SetMenuEntry(Key, Arg: String; Enable: Boolean);
begin
  Key := 'Software\Classes\' + Key + '\shell\TextPeach';
  if not Enable then
  begin
    RegDeleteKeyIncludingSubkeys(HKCU, Key);
    Exit;
  end;
  RegWriteStringValue(HKCU, Key, '', CustomMessage('OpenWith'));
  RegWriteStringValue(HKCU, Key, 'Icon', '"' + Exe() + '"');
  RegWriteStringValue(HKCU, Key + '\command', '', '"' + Exe() + '" "' + Arg + '"');
end;

{ A ProgID per type (TextPeach.md...) listed in the type's OpenWithProgids, plus the Capabilities that put TextPeach
  in Settings > Default apps. The default stays whatever the user had until they choose TextPeach themselves. }
procedure SetFileTypes(Enable: Boolean);
var
  Rest, Ext, ProgId: String;
  P: Integer;
begin
  Rest := '{#FileTypes} ';
  while Rest <> '' do
  begin
    P := Pos(' ', Rest);
    Ext := Copy(Rest, 1, P - 1);
    Delete(Rest, 1, P);
    if Ext = '' then Continue;
    ProgId := 'TextPeach.' + Ext;
    if Enable then
    begin
      RegWriteStringValue(HKCU, 'Software\Classes\' + ProgId, '', FmtMessage(CustomMessage('FileTypeName'), [Uppercase(Ext)]));
      RegWriteStringValue(HKCU, 'Software\Classes\' + ProgId + '\DefaultIcon', '', '"' + Exe() + '",0');
      RegWriteStringValue(HKCU, 'Software\Classes\' + ProgId + '\shell\open\command', '', '"' + Exe() + '" "%1"');
      RegWriteStringValue(HKCU, 'Software\Classes\.' + Ext + '\OpenWithProgids', ProgId, '');
      RegWriteStringValue(HKCU, 'Software\TextPeach\Capabilities\FileAssociations', '.' + Ext, ProgId);
      RegWriteStringValue(HKCU, 'Software\Classes\Applications\TextPeach.exe\SupportedTypes', '.' + Ext, '');
    end
    else
    begin
      RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\' + ProgId);
      RegDeleteValue(HKCU, 'Software\Classes\.' + Ext + '\OpenWithProgids', ProgId);
      RegDeleteKeyIfEmpty(HKCU, 'Software\Classes\.' + Ext + '\OpenWithProgids');
      RegDeleteKeyIfEmpty(HKCU, 'Software\Classes\.' + Ext);
    end;
  end;
  if Enable then
  begin
    RegWriteStringValue(HKCU, 'Software\TextPeach\Capabilities', 'ApplicationName', 'TextPeach');
    RegWriteStringValue(HKCU, 'Software\TextPeach\Capabilities', 'ApplicationDescription', CustomMessage('AppDescription'));
    RegWriteStringValue(HKCU, 'Software\RegisteredApplications', 'TextPeach', 'Software\TextPeach\Capabilities');
    RegWriteStringValue(HKCU, 'Software\Classes\Applications\TextPeach.exe', 'FriendlyAppName', 'TextPeach');
    RegWriteStringValue(HKCU, 'Software\Classes\Applications\TextPeach.exe\DefaultIcon', '', '"' + Exe() + '",0');
    RegWriteStringValue(HKCU, 'Software\Classes\Applications\TextPeach.exe\shell\open\command', '', '"' + Exe() + '" "%1"');
  end
  else
  begin
    RegDeleteValue(HKCU, 'Software\RegisteredApplications', 'TextPeach');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\TextPeach');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Applications\TextPeach.exe');
  end;
end;

{ Written (or removed) after every install, so unticking a task on a reinstall takes its entries away too.
  A silent update from the app keeps the choices of the previous install. }
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then Exit;
  SetMenuEntry('*', '%1', WizardIsTaskSelected('contextfiles'));
  SetMenuEntry('Directory', '%V', WizardIsTaskSelected('contextfolders'));
  SetMenuEntry('Directory\Background', '%V', WizardIsTaskSelected('contextfolders'));
  SetFileTypes(WizardIsTaskSelected('associate'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usUninstall then Exit;
  SetMenuEntry('*', '', False);
  SetMenuEntry('Directory', '', False);
  SetMenuEntry('Directory\Background', '', False);
  SetFileTypes(False);
end;
