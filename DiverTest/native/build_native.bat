@echo off
setlocal EnableDelayedExpansion

set "NATIVE_DIR=%~dp0"
set "CODE_FILE=%NATIVE_DIR%code.c"

if not exist "%CODE_FILE%" (
    echo [build_native] Missing code.c at "%CODE_FILE%"
    exit /b 1
)

set "SCRIPT_FILE=%NATIVE_DIR%compile_native_binary.py"

if not exist "%SCRIPT_FILE%" (
    echo [build_native] Missing compile script at "%SCRIPT_FILE%"
    exit /b 1
)

set "PYTHON_EXE="
for %%P in (py python python3) do (
    where %%P >nul 2>nul
    if not errorlevel 1 (
        set "PYTHON_EXE=%%P"
        goto :found_python
    )
)

echo [build_native] Python executable not found on PATH.
exit /b 1

:found_python
echo [build_native] Using Python: %PYTHON_EXE%
pushd "%NATIVE_DIR%" >nul

%PYTHON_EXE% "%SCRIPT_FILE%" "%NATIVE_DIR%code" "%CODE_FILE%"
set "ERR=%ERRORLEVEL%"

popd >nul
exit /b %ERR%
