#define AppVersion "2.3.1"

[Setup]
;-- Main Setup Information
 AppName                          = CatMQ
 AppVersion                       = {#AppVersion}
 AppVerName                       = CatMQ {#AppVersion}
 AppCopyright                     = Copyright © 1995-2025 NetworkDLS.
 DefaultDirName                   = {commonpf}\NetworkDLS\CatMQ
 DefaultGroupName                 = NetworkDLS\CatMQ
 UninstallDisplayIcon             = {app}\CatMQ.Service.exe
 SetupIconFile                    = "..\Images\Logo.ico"
 PrivilegesRequired               = admin
 Uninstallable                    = Yes
 MinVersion                       = 0.0,7.0
 Compression                      = bZIP/9
 ChangesAssociations              = Yes
 OutputBaseFilename               = CatMQ.windows.x64
 ArchitecturesInstallIn64BitMode  = x64compatible
 AppPublisher                     = NetworkDLS
 AppPublisherURL                  = http://www.NetworkDLS.com/
 AppUpdatesURL                    = http://www.NetworkDLS.com/

[Files]
  Source: "publish\win-x64\wwwroot\*.*"; DestDir: "{app}\wwwroot"; Flags: IgnoreVersion recursesubdirs;
 Source: "publish\win-x64\*.exe"; DestDir: "{app}"; Flags: IgnoreVersion;
 Source: "publish\win-x64\*.dll"; DestDir: "{app}"; Flags: IgnoreVersion;
 Source: "publish\win-x64\*.json"; DestDir: "{app}"; Flags: IgnoreVersion;
 Source: "..\Images\Logo.ico"; DestDir: "{app}"; Flags: IgnoreVersion;

[Icons]
 Name: "{commondesktop}\CatMQ Manager"; Filename: "http://localhost:45783/"; IconFilename: "{app}\Logo.ico"
 Name: "{group}\CatMQ Manager"; Filename: "http://localhost:45783/"; IconFilename: "{app}\Logo.ico"

[Run]
 Filename: "{app}\CatMQ.Service.exe"; Parameters: "install"; Flags: runhidden; StatusMsg: "Installing service...";
 Filename: "{app}\CatMQ.Service.exe"; Parameters: "start"; Flags: runhidden; StatusMsg: "Starting service...";
 Filename: "http://localhost:45783/"; Description: "Run CatMQ Manager now?"; Flags: postinstall nowait skipifsilent shellexec;

[UninstallRun]
 Filename: "{app}\CatMQ.Service.exe"; Parameters: "uninstall"; Flags: runhidden; StatusMsg: "Installing service..."; RunOnceId: "ServiceRemoval";
