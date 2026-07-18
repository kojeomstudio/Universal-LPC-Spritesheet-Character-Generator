#!/usr/bin/env bash
# LPC Sprite Generator - Electron 바이너리 빌드 스크립트 (Unix/macOS/Linux)
# spritesheets는 바이너리에서 분리됨.
#
# 사용법:
#   scripts/build-electron.sh            기본 빌드 (portable)
#   scripts/build-electron.sh dir        디렉토리 형태로만 빌드 (테스트용, 빠름)
#
# 출력: ../../bins/lpc-sprite-generator/
#
# 주의: spritesheets는 바이너리에 포함되지 않습니다.
# 실행 시 --spritesheets <path>로 경로 지정하거나,
# exe와 같은 디렉토리에 spritesheets/ 폴더를 배치하세요.

set -euo pipefail

echo "=== LPC Sprite Generator Electron 빌드 ==="
echo

cd "$(dirname "$0")/.."

echo "[1/2] Vite 빌드 (renderer + 메타데이터)..."
npm run build

echo
echo "[2/2] Electron 빌드 (spritesheets 제외, 단일 바이너리)..."
if [ "${1:-}" = "dir" ]; then
  npm run electron:build:dir
else
  npm run electron:build
fi

echo
echo "=== 빌드 완료 ==="
echo
OUTDIR="../../bins/lpc-sprite-generator"
echo "산출물: ${OUTDIR}/"
if compgen -G "${OUTDIR}/*.exe" > /dev/null || compgen -G "${OUTDIR}/*.AppImage" > /dev/null || compgen -G "${OUTDIR}/*.dmg" > /dev/null; then
  ls -lh "${OUTDIR}"/*.{exe,AppImage,dmg} 2>/dev/null || true
elif [ -d "${OUTDIR}/win-unpacked" ] || [ -d "${OUTDIR}/linux-unpacked" ] || [ -d "${OUTDIR}/mac" ]; then
  echo "  ${OUTDIR}/*-unpacked (디렉토리 형태)"
fi
echo
echo "주의: spritesheets는 별도 배포 필요."
echo "  방법1: 바이너리와 같은 디렉토리에 spritesheets/ 폴더 배치"
echo "  방법2: 실행 시 --spritesheets <경로> 지정"
echo "  기본 spritesheets 위치: tools/lpc-sprite-generator/spritesheets/"
