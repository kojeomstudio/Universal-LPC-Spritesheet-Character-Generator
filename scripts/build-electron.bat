@echo off
REM LPC Sprite Generator - Electron 바이너리 빌드 스크립트 (Windows)
REM 단일 portable .exe 생성.
REM
REM 사용법:
REM   scripts\build-electron.bat              기본 빌드 (portable exe)
REM   scripts\build-electron.bat dir          디렉토리 형태로만 빌드 (테스트용, 빠름)
REM
REM 출력: release\LPC-SpriteGenerator-0.0.0-portable.exe

setlocal

echo === LPC Sprite Generator Electron 빌드 ===
echo.

cd /d "%~dp0\.." || goto :error

echo [1/2] Vite 빌드 (renderer + 메타데이터)...
call npm run build
if errorlevel 1 goto :error

echo.
echo [2/2] Electron 빌드...
if /i "%1"=="dir" (
  call npm run electron:build:dir
) else (
  call npm run electron:build
)
if errorlevel 1 goto :error

echo.
echo === 빌드 완료 ===
if exist "release\*.exe" (
  for %%f in (release\*.exe) do echo 산출물: release\%%~nxf ^(%%~zf bytes^)
) else if exist "release\win-unpacked" (
  echo 산출물: release\win-unpacked\ ^(디렉토리 형태^)
) else (
  echo 경고: 산출물을 찾을 수 없습니다. release\ 확인.
)

exit /b 0

:error
echo.
echo === 빌드 실패 (오류 코드 %errorlevel%) ===
exit /b 1
