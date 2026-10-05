@echo off
setlocal
cd /d "%~dp0"
if not exist "..\..\Assets\Plugins\RaceAudio\Windows" mkdir "..\..\Assets\Plugins\RaceAudio\Windows"
rem Run in an x64 Native Tools Command Prompt for Visual Studio (Desktop development with C++).
cl /nologo /std:c++17 /O2 /EHsc /LD RaceAudio.cpp /link /OUT:..\..\Assets\Plugins\RaceAudio\Windows\RaceAudio.dll ole32.lib
exit /b %errorlevel%
