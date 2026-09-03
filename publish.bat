@echo off
rem ---------------------------------------------------------------------------
rem Build the one file Waybill.exe into dist\.
rem
rem Double click it here, or make a shortcut to it on the desktop: the script
rem finds its own folder, so it does not care what the current directory is.
rem If it has been copied somewhere else entirely, the fallback path below is
rem the one it falls back on.
rem ---------------------------------------------------------------------------
setlocal

set "REPO=%~dp0"
if "%REPO:~-1%"=="\" set "REPO=%REPO:~0,-1%"
if not exist "%REPO%\src\Waybill\Waybill.csproj" set "REPO=C:\Users\filip\Documents\projekty\WayBill"

if not exist "%REPO%\src\Waybill\Waybill.csproj" (
    echo Waybill was not found at "%REPO%".
    echo Put this file in the project folder, or fix the path inside it.
    echo.
    pause
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo The dotnet command was not found. Install the .NET 9 SDK first.
    echo.
    pause
    exit /b 1
)

rem A running Waybill holds its own exe open and the publish fails halfway with
rem a file lock, which reads as a build error and is not one.
"%SystemRoot%\System32\tasklist.exe" /fi "imagename eq Waybill.exe" 2>nul | "%SystemRoot%\System32\find.exe" /i "Waybill.exe" >nul
if not errorlevel 1 (
    echo Waybill is running. Close it first, then run this again.
    echo.
    pause
    exit /b 1
)

echo Building Waybill into "%REPO%\dist" ...
echo.
pushd "%REPO%"
dotnet publish src/Waybill -c Release -r win-x64 -p:PublishSingleFile=true -o dist
set "RESULT=%ERRORLEVEL%"
popd

echo.
if not "%RESULT%"=="0" (
    echo The build failed. The reason is in the lines above.
    echo.
    pause
    exit /b %RESULT%
)

echo Done. The new exe is "%REPO%\dist\Waybill.exe".
echo.
rem Y opens the folder, anything else just closes. The prompt is also what keeps
rem the window on screen long enough to read when this was double clicked.
choice /c YN /n /m "Open the dist folder? [Y/N] "
if errorlevel 1 if not errorlevel 2 start "" "%REPO%\dist"

endlocal
