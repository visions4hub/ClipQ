@echo off
rem Builds ClipQ in Arabic and English, each with its own installer, using the C# compiler
rem that ships with Windows (.NET Framework 4.8). No SDK needed.
rem Output: dist\ClipQ-Setup-AR.exe and dist\ClipQ-Setup-EN.exe
cd /d "%~dp0"
set CSC="%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 /win32icon:assets\ClipQ.ico

if not exist build\AR mkdir build\AR
if not exist build\EN mkdir build\EN
if not exist dist mkdir dist

call :variant AR "" || exit /b 1
call :variant EN "/define:EN" || exit /b 1
echo Built dist\ClipQ-Setup-AR.exe and dist\ClipQ-Setup-EN.exe
exit /b 0

:variant
%CSC% %~2 /out:build\%1\ClipQ.exe /resource:assets\logo.png,logo.png /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs || exit /b 1
%CSC% %~2 /win32manifest:installer\app.manifest /resource:build\%1\ClipQ.exe,ClipQ.exe /out:dist\ClipQ-Setup-%1.exe /r:System.Windows.Forms.dll installer\Setup.cs src\Lang.cs || exit /b 1
exit /b 0
