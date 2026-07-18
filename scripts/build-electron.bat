@echo off
REM LPC Sprite Generator - Electron 바이너리 빌드 스크립트 (Windows)
REM 단일 portable .exe 생성.
REM
REM 사용법:
REM   scripts\build-electron.bat              기본 빌드 (portable exe)
REM   scripts\build-electron.bat dir          디렉토리 형태로만 빌드 (테스트용, 빠름)
REM
REM 출력: ..\..\bins\lpc-sprite-generator\LPC-SpriteGenerator-0.0.0-portable.exe
REM       (워크스페이스 루트의 bins/ 디렉토리, 모든 도구가 공유)

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
echo 출력 디렉토리: ..\..\bins\lpc-sprite-generator\
if exist "..\..\bins\lpc-sprite-generator\*.exe" (
  for %%f in (..\..\bins\lpc-sprite-generator\*.exe) do echo 산출물: %%~nxf ^(%%~zf bytes^)
) else if exist "..\..\bins\lpc-sprite-generator\win-unpacked" (
  echo 산출물: ..\..\bins\lpc-sprite-generator\win-unpacked\ ^(디렉토리 형태^)
) else (
  echo 경고: 산출물을 찾을 수 없습니다. ..\..\bins\lpc-sprite-generator\ 확인.
)

exit /b 0

:error
echo.
echo === 빌드 실패 (오류 코드 %errorlevel%) ===
exit /b 1
