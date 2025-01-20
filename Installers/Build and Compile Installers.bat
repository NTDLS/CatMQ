@echo off
set path=%PATH%;C:\Program Files\Vroom Performance Technologies\SQL Script Generator;C:\Program Files\7-Zip;
set path=C:\Program Files (x86)\Inno Setup 6\;%path%

rd publish /q /s
rd output /q /s

md output
md publish

dotnet publish ..\CatMQ.Service -c Release -o publish\win-x64 --runtime win-x64 --self-contained false
del publish\win-x64\*.pdb /q
dotnet publish ..\CatMQ.Service -c Release -o publish\linux-x64 --runtime linux-x64 --self-contained false
del publish\linux-x64\*.pdb /q

7z.exe a -tzip -r -mx9 ".\output\CatMQ.linux.x64.zip" ".\publish\linux-x64\*.*"

iscc Windows.Installer.iss
rd publish /q /s
