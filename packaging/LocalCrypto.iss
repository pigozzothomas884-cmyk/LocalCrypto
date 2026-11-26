[Setup]
AppId={{8F4C2A1E-7B3D-4E9A-9C1F-2D6E8A4B5C30}
AppName=LocalCrypto
AppVersion=1.0.0
AppPublisher=CiaoDev
DefaultDirName={localappdata}\Programs\LocalCrypto
DefaultGroupName=LocalCrypto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\InstallerIcon.ico
UninstallDisplayIcon={app}\LocalCrypto.exe
OutputDir=..\dist
OutputBaseFilename=LocalCrypto-Setup-1.0.0
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\LocalCrypto"; Filename: "{app}\LocalCrypto.exe"
Name: "{group}\{cm:UninstallProgram,LocalCrypto}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\LocalCrypto"; Filename: "{app}\LocalCrypto.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\LocalCrypto.exe"; Description: "{cm:LaunchProgram,LocalCrypto}"; Flags: nowait postinstall skipifsilent
