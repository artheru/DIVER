@echo off
setlocal

set PDN=FRLD-DIVERBK-V2
set SCONS_OPTS=PDN=%PDN% ENABLE_DIVER_RUNTIME=1 -j 12 debug=1
set TESTDIVER_EXE=.\build\TestDIVER.exe
set TESTDIVER_PORT=COM18
set TESTDIVER_BAUD=1000000
set TESTDIVER_BIN=D:\Documents\Coral\DIVER\3rd\CoralinkerHost\data\assets\generated\TestLogic.bin

if "%1"=="" goto usage
if "%1"=="build" goto build
if "%1"=="flash" goto flash
if "%1"=="rtt" goto rtt
if "%1"=="test" goto test
if "%1"=="all" goto all
goto usage

:build
echo === Building MCU firmware ===
scons %SCONS_OPTS%
goto end

:flash
echo === Flashing MCU firmware ===
scons %SCONS_OPTS% flash
goto end

:rtt
echo === Starting RTT viewer ===
scons %SCONS_OPTS% rtt
goto end

:test
echo === Running TestDIVER ===
echo Port: %TESTDIVER_PORT%, Baud: %TESTDIVER_BAUD%
echo Bin: %TESTDIVER_BIN%
%TESTDIVER_EXE% %TESTDIVER_PORT% %TESTDIVER_BAUD% %TESTDIVER_BIN%
goto end

:all
echo === Build + Flash + Test ===
scons %SCONS_OPTS%
if errorlevel 1 goto end
scons %SCONS_OPTS% flash
if errorlevel 1 goto end
timeout /t 2 /nobreak >nul
%TESTDIVER_EXE% %TESTDIVER_PORT% %TESTDIVER_BAUD% %TESTDIVER_BIN%
goto end

:usage
echo Usage: dev.bat [command]
echo.
echo Commands:
echo   build  - Build MCU firmware
echo   flash  - Flash firmware to MCU
echo   rtt    - Start RTT log viewer
echo   test   - Run PC-side TestDIVER
echo   all    - Build, flash, then test
echo.
echo Config:
echo   PDN=%PDN%
echo   Port=%TESTDIVER_PORT%, Baud=%TESTDIVER_BAUD%
goto end

:end
endlocal
