@echo off
setlocal
cd /d "%~dp0"
rem Run in a Native Tools Command Prompt for Visual Studio (Desktop development with C++).
rem x64 prompt: Assets\Plugins\RaceAudio\Windows      (x64 Editor / Player)
rem ARM64 prompt (vcvarsarm64.bat): Assets\Plugins\RaceAudio\WindowsArm64 (Windows on ARM Editor / Player)
set "OUTDIR=..\..\Assets\Plugins\RaceAudio\Windows"
if /i "%VSCMD_ARG_TGT_ARCH%"=="arm64" set "OUTDIR=..\..\Assets\Plugins\RaceAudio\WindowsArm64"
if not exist "%OUTDIR%" mkdir "%OUTDIR%"
rem NOMINMAX keeps windows.h from replacing std::min / std::max.
cl /nologo /std:c++17 /O2 /EHsc /DNOMINMAX /LD RaceAudio.cpp /Fo"%TEMP%\RaceAudio.obj" /link /OUT:"%OUTDIR%\RaceAudio.dll" /IMPLIB:"%TEMP%\RaceAudio.lib" ole32.lib
exit /b %errorlevel%
