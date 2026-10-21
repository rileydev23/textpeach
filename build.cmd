@echo off
rem Builds a single-file, self-contained, portable TextPeach.exe into .\dist
rem Optional version (build.cmd 1.2.0), stamped into the exe; without it the csproj's <Version> is used.
setlocal
cd /d "%~dp0"
set VERSIONARG=
if not "%~1"=="" set VERSIONARG=-p:Version=%~1
if exist dist rmdir /s /q dist
rem Always from scratch: a stale incremental Release build can ship without the compiled XAML (the exe dies at startup).
if exist src\TextPeach\obj\Release rmdir /s /q src\TextPeach\obj\Release
dotnet publish src\TextPeach\TextPeach.csproj -c Release -r win-x64 --self-contained ^
  -p:PublishSingleFile=true ^
  -p:PublishReadyToRun=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none -p:DebugSymbols=false %VERSIONARG% ^
  -o dist
if errorlevel 1 exit /b 1
echo.
echo Listo: %~dp0dist\TextPeach.exe
echo Para modo 100%% portable (datos junto al exe) crea un archivo "portable.txt" al lado del exe.
