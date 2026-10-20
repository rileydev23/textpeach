@echo off
rem Builds a single-file, self-contained, portable TextPeach.exe into .\dist
setlocal
cd /d "%~dp0"
if exist dist rmdir /s /q dist
dotnet publish src\TextPeach\TextPeach.csproj -c Release -r win-x64 --self-contained ^
  -p:PublishSingleFile=true ^
  -p:PublishReadyToRun=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none -p:DebugSymbols=false ^
  -o dist
if errorlevel 1 exit /b 1
echo.
echo Listo: %~dp0dist\TextPeach.exe
echo Para modo 100%% portable (datos junto al exe) crea un archivo "portable.txt" al lado del exe.
