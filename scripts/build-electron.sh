#!/usr/bin/env bash
# LPC Sprite Generator - Electron 바이너리 빌드 스크립트 (Unix/macOS/Linux)
#
# 사용법:
#   scripts/build-electron.sh            기본 빌드
#   scripts/build-electron.sh dir        디렉토리 형태로만 빌드 (테스트용, 빠름)
#
# 출력: ../../bins/lpc-sprite-generator/ (워크스페이스 루트의 bins/, 모든 도구가 공유)

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
OUTDIR="../../bins/lpc-sprite-generator"
echo "출력 디렉토리: ${OUTDIR}/"
if compgen -G "${OUTDIR}/*.exe" > /dev/null || compgen -G "${OUTDIR}/*.AppImage" > /dev/null || compgen -G "${OUTDIR}/*.dmg" > /dev/null; then
  ls -lh "${OUTDIR}"/*.{exe,AppImage,dmg} 2>/dev/null || true
elif [ -d "${OUTDIR}/win-unpacked" ] || [ -d "${OUTDIR}/linux-unpacked" ] || [ -d "${OUTDIR}/mac" ]; then
  echo "산출물: ${OUTDIR}/*-unpacked (디렉토리 형태)"
  ls -d "${OUTDIR}"/*-unpacked "${OUTDIR}"/mac 2>/dev/null || true
else
  echo "경고: 산출물을 찾을 수 없습니다. ${OUTDIR}/ 확인."
fi
