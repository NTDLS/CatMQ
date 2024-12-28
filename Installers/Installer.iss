#define AppVersion "1.0.0"

[Setup]
;-- Main Setup Information
 AppName                          = CatMQ
 AppVersion                       = {#AppVersion}
 AppVerName                       = CatMQ {#AppVersion}
 AppCopyright                     = Copyright © 1995-2025 NetworkDLS.
 DefaultDirName                   = {commonpf}\NetworkDLS\CatMQ
 DefaultGroupName                 = NetworkDLS\CatMQ
 UninstallDisplayIcon             = {app}\CatMQService.exe
 SetupIconFile                    = "..\Images\Logo.ico"
 PrivilegesRequired               = admin
 Uninstallable                    = Yes
 MinVersion                       = 0.0,7.0
 Compression                      = bZIP/9
 ChangesAssociations              = Yes
 OutputBaseFilename               = CatMQ {#AppVersion}
 ArchitecturesInstallIn64BitMode  = x64compatible
 AppPublisher                     = NetworkDLS
 AppPublisherURL                  = http://www.NetworkDLS.com/
 AppUpdatesURL                    = http://www.NetworkDLS.com/

[Files]
 Source: "..\CatMQService\bin\Release\net9.0\runtimes\*.*"; DestDir: "{app}\runtimes"; Flags: IgnoreVersion recursesubdirs;
 Source: "..\CatMQService\bin\Release\net9.0\*.exe"; DestDir: "{app}"; Flags: IgnoreVersion;
 Source: "..\CatMQService\bin\Release\net9.0\*.dll"; DestDir: "{app}"; Flags: IgnoreVersion;
 Source: "..\CatMQService\bin\Release\net9.0\*.json"; DestDir: "{app}"; Flags: IgnoreVersion;
  
[Icons]
 Name: "{commondesktop}\CatMQ Manager"; Filename: "http://127.0.0.1:45783/";
 Name: "{group}\CatMQ Manager"; Filename: "http://127.0.0.1:45783/";

[Run]
 Filename: "{app}\CatMQService.exe"; Parameters: "install"; Flags: runhidden; StatusMsg: "Installing service...";
 Filename: "{app}\CatMQService.exe"; Parameters: "start"; Flags: runhidden; StatusMsg: "Starting service...";
 Filename: "http://127.0.0.1:45783/"; Description: "Run CatMQ Manager now?"; Flags: postinstall nowait skipifsilent shellexec;

[UninstallRun]
 Filename: "{app}\CatMQService.exe"; Parameters: "uninstall"; Flags: runhidden; StatusMsg: "Installing service..."; RunOnceId: "ServiceRemoval";
