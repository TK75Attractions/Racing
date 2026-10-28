@echo off
setlocal
cd /d "%~dp0"
if /i "%VSCMD_ARG_TGT_ARCH%"=="x64" goto compile
set "RACING_VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%RACING_VSWHERE%" goto missing_tools
for /f "usebackq tokens=*" %%i in (`"%RACING_VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "RACING_VS=%%i"
if not defined RACING_VS goto missing_tools
call "%RACING_VS%\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1
:compile
if not exist "..\..\Assets\Plugins\RaceAudio\Windows" mkdir "..\..\Assets\Plugins\RaceAudio\Windows"
rem Static C++ runtime: the game PC does not need a separate Visual C++ runtime.
rem Build outside Assets, then replace only on success. Close Unity to release the DLL.
set "RACING_AUDIO_TEMP=%TEMP%\RacingAudio-%RANDOM%-%RANDOM%"
mkdir "%RACING_AUDIO_TEMP%"
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /O2 /MT /EHsc /LD RaceAudio.cpp /Fo"%RACING_AUDIO_TEMP%\RaceAudio.obj" /link /OUT:"%RACING_AUDIO_TEMP%\RaceAudio.dll" /IMPLIB:"%RACING_AUDIO_TEMP%\RaceAudio.lib" ole32.lib
if errorlevel 1 goto build_failed
copy /y "%RACING_AUDIO_TEMP%\RaceAudio.dll" "..\..\Assets\Plugins\RaceAudio\Windows\RaceAudio.dll" >nul
if errorlevel 1 goto copy_failed
rmdir /s /q "%RACING_AUDIO_TEMP%"
echo Windows x64 RaceAudio.dll rebuilt successfully.
exit /b 0
:copy_failed
echo Close Unity Editor and the game, then run this script again.
:build_failed
rmdir /s /q "%RACING_AUDIO_TEMP%"
exit /b 1
:missing_tools
echo Install Visual Studio Build Tools with Desktop development with C++ and a Windows SDK.
exit /b 1
