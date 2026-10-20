@echo off
set path=%PATH%;C:\Program Files\7-Zip;
set path=%path%;C:\Program Files (x86)\Inno Setup 6\;

rd publish /q /s
rd output /q /s

md output
md publish

dotnet publish ..\CatMQ.Service -c Release -o publish\win-x64 --runtime win-x64 --self-contained false -f net10.0
del publish\win-x64\*.pdb /q
REM Linux, self contained (it brings its own .NET runtime, so nothing needs to be installed on the machine).
dotnet publish ..\CatMQ.Service -c Release -o publish\linux-x64 --runtime linux-x64 --self-contained true -f net10.0
del publish\linux-x64\*.pdb /q

dotnet publish ..\CatMQ.Service -c Release -o publish\linux-arm64 --runtime linux-arm64 --self-contained true -f net10.0
del publish\linux-arm64\*.pdb /q

7z.exe a -tzip -r -mx9 ".\output\CatMQ.linux.x64.zip" ".\publish\linux-x64\*.*"
7z.exe a -tzip -r -mx9 ".\output\CatMQ.linux.arm64.zip" ".\publish\linux-arm64\*.*"

iscc Windows.Installer.iss
rd publish /q /s

pause
