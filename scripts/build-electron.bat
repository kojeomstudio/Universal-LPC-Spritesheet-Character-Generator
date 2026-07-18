@echo off
REM LPC Sprite Generator - Electron 바이너리 빌드 스크립트 (Windows)
REM 단일 portable .exe 생성 (spritesheets는 바이너리에서 분리).
REM
REM 사용법:
REM   scripts\build-electron.bat              기본 빌드 (portable exe)
REM   scripts\build-electron.bat dir          디렉토리 형태로만 빌드 (테스트용, 빠름)
REM
REM 출력:
REM   ..\..\bins\lpc-sprite-generator\LPC-SpriteGenerator-0.0.0-portable.exe
REM
REM 주의: spritesheets는 바이너리에 포함되지 않습니다.
REM 실행 시 --spritesheets <path>로 경로 지정하거나,
REM exe와 같은 디렉토리에 spritesheets\ 폴더를 배치하세요.

setlocal

echo === LPC Sprite Generator Electron 빌드 ===
echo.

cd /d "%~dp0\.." || goto :error

echo [1/2] Vite 빌드 (renderer + 메타데이터)...
call npm run build
if errorlevel 1 goto :error

echo.
echo [2/2] Electron 빌드 (spritesheets 제외, 단일 .exe)...
if /i "%1"=="dir" (
  call npm run electron:build:dir
) else (
  call npm run electron:build
)
if errorlevel 1 goto :error

echo.
echo === 빌드 완료 ===
echo.
echo 산출물: ..\..\bins\lpc-sprite-generator\
if exist "..\..\bins\lpc-sprite-generator\*.exe" (
  for %%f in (..\..\bins\lpc-sprite-generator\*.exe) do echo   %%~nxf ^(%%~zf bytes^)
) else if exist "..\..\bins\lpc-sprite-generator\win-unpacked\LPC Sprite Generator.exe" (
  echo   win-unpacked\ ^(디렉토리 형태^)
) else (
  echo   경고: 산출물을 찾을 수 없습니다.
)
echo.
echo 주의: spritesheets는 별도 배포 필요.
echo   방법1: exe와 같은 디렉토리에 spritesheets\ 폴더 배치
echo   방법2: 실행 시 --spritesheets ^<경로^> 지정
echo   기본 spritesheets 위치: tools\lpc-sprite-generator\spritesheets\

exit /b 0

:error
echo.
echo === 빌드 실패 (오류 코드 %errorlevel%) ===
exit /b 1
