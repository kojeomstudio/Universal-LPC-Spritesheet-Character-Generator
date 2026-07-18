#!/usr/bin/env bash
# LPC Sprite Generator - Electron 바이너리 빌드 스크립트 (Unix/macOS/Linux)
# 단일 실행 파일 생성 (플랫폼별: macOS .app, Linux AppImage, Windows는 .bat 사용).
#
# 사용법:
#   scripts/build-electron.sh            기본 빌드
#   scripts/build-electron.sh dir        디렉토리 형태로만 빌드 (테스트용, 빠름)
#
# 출력: release/

set -euo pipefail

echo "=== LPC Sprite Generator Electron 빌드 ==="
echo

cd "$(dirname "$0")/.."

echo "[1/2] Vite 빌드 (renderer + 메타데이터)..."
npm run build

echo
echo "[2/2] Electron 빌드..."
if [ "${1:-}" = "dir" ]; then
  npm run electron:build:dir
else
  npm run electron:build
fi

echo
echo "=== 빌드 완료 ==="
if compgen -G "release/*.exe" > /dev/null || compgen -G "release/*.AppImage" > /dev/null || compgen -G "release/*.dmg" > /dev/null; then
  ls -lh release/*.{exe,AppImage,dmg} 2>/dev/null || true
elif [ -d "release/win-unpacked" ] || [ -d "release/linux-unpacked" ] || [ -d "release/mac" ]; then
  echo "산출물: release/*/ (디렉토리 형태)"
  ls -d release/*-unpacked release/mac 2>/dev/null || true
else
  echo "경고: 산출물을 찾을 수 없습니다. release/ 확인."
fi
