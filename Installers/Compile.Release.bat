@echo off
@set path=C:\Program Files (x86)\Inno Setup 6\;%path%

cd 
md publish
dotnet publish ..\CatMQ.Service -c Release -o publish
iscc Installer.iss
rd publish /q /s
