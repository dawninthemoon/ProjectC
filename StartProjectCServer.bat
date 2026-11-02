@echo off
setlocal

set "PROJECT_ROOT=%~dp0"
set "BUILD_DIR=%PROJECT_ROOT%build\windows-server"

where cmake >nul 2>&1
if errorlevel 1 (
    echo CMake was not found. Install CMake and add it to PATH.
    exit /b 1
)

if not exist "%BUILD_DIR%\CMakeCache.txt" (
    echo Configuring the ProjectC server build...
    cmake -S "%PROJECT_ROOT%ProjectC.Server" -B "%BUILD_DIR%"
    if errorlevel 1 (
        echo CMake configuration failed.
        exit /b 1
    )
)

echo Building ProjectCNetworkServer...
cmake --build "%BUILD_DIR%" --config Debug --target ProjectCNetworkServer --parallel
if errorlevel 1 (
    echo Server build failed.
    exit /b 1
)

set "SERVER_EXE=%BUILD_DIR%\Debug\ProjectCNetworkServer.exe"
if not exist "%SERVER_EXE%" set "SERVER_EXE=%BUILD_DIR%\ProjectCNetworkServer.exe"
if not exist "%SERVER_EXE%" (
    echo Could not find the built ProjectCNetworkServer executable.
    exit /b 1
)

echo Starting ProjectC server. Press Ctrl+C to stop it.
"%SERVER_EXE%" --data "%PROJECT_ROOT%ProjectC.Server\Data\HyakkiYagyo" %*
exit /b %ERRORLEVEL%
