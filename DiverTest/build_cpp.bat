echo Build MCURuntime for debug use.
REM @echo off

REM Step 1: Use vswhere to get the Visual Studio installation path
for /f "delims=" %%i in ('"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -property installationPath') do set IDIR=%%i

REM Check if VSINSTALLDIR was found
if "%IDIR%"=="" (
    echo Error: Visual Studio installation not found.
    exit /b 1
)

REM Step 2: Locate vsdevcmd.bat
set VC_CMD="%IDIR%\VC\Auxiliary\Build\vcvars64.bat"
if not exist %VC_CMD% (
    echo Error: vcvars64.bat not found.
    exit /b 1
)
 
REM Step 3: Call vsdevcmd.bat and compile using cl.exe

call %VC_CMD%
echo %PROCESSOR_ARCHITECTURE% == %OutputPath%

REM Step 4: C++ compilation(DEBUG)
cl /W0 /LD /MDd /I"." /Zi /EHsc ../MCURuntime/mcu_runtime.c /Fe:%OutputPath%mcuruntime.dll /link /DEBUG