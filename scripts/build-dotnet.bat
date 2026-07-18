@echo off
REM Build single-file Windows binaries for the C# port of LPC Sprite Generator.
REM Output: bins/lpc-sprite-generator-dotnet/{headless,wpf}/
REM
REM Run from anywhere — it locates the dotnet/ solution relative to its own path.
setlocal
set "SCRIPT_DIR=%~dp0"
set "SOLUTION=%SCRIPT_DIR%..\dotnet\LpcSpriteGen.sln"

echo === Restoring + building solution (Debug) ===
dotnet build "%SOLUTION%" -c Release
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)

echo.
echo === Publishing headless CLI (single-file, win-x64) ===
dotnet publish "%SCRIPT_DIR%..\dotnet\src\LpcSpriteGen.Headless\LpcSpriteGen.Headless.csproj" ^
    /p:PublishProfile=win-x64.pubxml
if errorlevel 1 (
    echo Headless publish failed.
    exit /b 1
)

echo.
echo === Publishing WPF GUI (single-file, win-x64) ===
dotnet publish "%SCRIPT_DIR%..\dotnet\src\LpcSpriteGen.Wpf\LpcSpriteGen.Wpf.csproj" ^
    /p:PublishProfile=win-x64.pubxml
if errorlevel 1 (
    echo WPF publish failed.
    exit /b 1
)

echo.
echo === Done. Binaries in bins/lpc-sprite-generator-dotnet/ ===
endlocal
