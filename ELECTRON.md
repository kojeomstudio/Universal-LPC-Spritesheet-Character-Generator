# LPC Sprite Generator — Electron 빌드/사용 가이드

이 문서는 LPC Sprite Generator를 Electron 데스크톱 앱(GUI + 헤드리스 CLI)으로
빌드하고 사용하는 방법을 설명합니다.

## 개요

두 가지 모드를 모두 지원하는 단일 실행 프로그램:
- **GUI 모드** (기본): 브라우저 UI 그대로 데스크톱 앱에서 동작. 부품 클릭으로 조합.
- **헤드리스 CLI 모드** (`--headless`): 창 없이 배치로 캐릭터 스프라이트 생성.

## 필수 요구사항

- Node.js 20+
- npm (의존성 설치)
- Electron은 첫 실행 시 자동 다운로드 (~90MB)

## 개발 모드 (소스에서 실행)

```bash
cd tools/lpc-sprite-generator
npm install                    # 의존성 설치 (최초 1회)

# GUI 모드
npm run electron:dev           # 빌드 + Electron 실행 (브라우저 UI)
# GUI 모드 + 저장 디렉토리 지정 (다운로드 시 다이얼로그 없이 자동 저장)
npx electron . --save-dir ./my-characters

# 헤드리스 CLI 모드
npm run electron:headless -- --list-items          # 사용 가능한 부품 목록
npm run electron:random -- --output char.png       # 랜덤 캐릭터 1개 생성
npm run electron:random -- --count 5 --output-dir ./out  # 5개 배치 생성
```

## 헤드리스 CLI 사용법

```
electron . --headless [옵션]
```

### 옵션

| 옵션 | 설명 | 예시 |
|------|------|------|
| `--list-items` | 사용 가능한 부품(itemId) 목록 출력 | `--list-items` |
| `--random` | 랜덤 파츠 조합으로 캐릭터 생성 | `--random` |
| `--selections <path>` | 지정한 selections JSON으로 생성 | `--selections my-char.json` |
| `--output <path>` | 출력 PNG 경로 (단일 생성 시, GUI/헤드리스 공통) | `--output char.png` |
| `--output-dir <dir>` | 출력 디렉토리 (배치 생성 시) | `--output-dir ./out` |
| `--save-dir <dir>` | **GUI 모드** 다운로드 기본 디렉토리 (다이얼로그 없이 자동 저장) | `--save-dir ./my-chars` |
| `--count <N>` | 생성할 캐릭터 수 (기본 1) | `--count 10` |
| `--seed <N>` | 랜덤 시드 (재현 가능한 생성) | `--seed 42` |
| `--body-type <type>` | 체형 (male/female/teen/child/muscular/pregnant) | `--body-type female` |

### 사용 예시

```bash
# 랜덤 캐릭터 1개
npx electron . --headless --random --output hero.png

# 시드 고정 랜덤 (재현 가능)
npx electron . --headless --random --seed 42 --output hero.png

# 20개 배치 생성 (각각 다른 부품 조합)
npx electron . --headless --random --count 20 --output-dir ./characters

# 여성 체형 5개
npx electron . --headless --random --count 5 --body-type female --output-dir ./females

# 사용 가능한 부품 확인
npx electron . --headless --list-items
```

### selections JSON 형식 (--selections 사용 시)

```json
{
  "selections": {
    "body": { "itemId": "body", "name": "Body", "variant": "", "recolor": "light" },
    "head": { "itemId": "heads_human_male", "name": "Human", "variant": "", "recolor": "light" },
    "hair": { "itemId": "hair_page", "name": "Page", "variant": "", "recolor": "" }
  },
  "bodyType": "male"
}
```

## 바이너리 빌드 (단일 .exe)

### Windows

```bash
# 단일 portable .exe 생성 (~86MB, spritesheets 제외)
scripts\build-electron.bat

# 디렉토리 형태로만 빌드 (빠름, 테스트용)
scripts\build-electron.bat dir
```

출력: `../../bins/lpc-sprite-generator/LPC-SpriteGenerator-0.0.0-portable.exe`
(워크스페이스 루트의 `bins/` 디렉토리 — 모든 도구가 공유)

### Unix/macOS/Linux

```bash
scripts/build-electron.sh        # 기본 빌드
scripts/build-electron.sh dir    # 디렉토리 형태
```

### spritesheets 분리 (중요)

**spritesheets(365MB, 11만 파일)는 바이너리에서 분리되어 있습니다.** 빌드 시간과
크기를 합리적으로 유지하기 위한 조치입니다. 실행 시 spritesheets를 참조하는
3가지 방법:

1. **exe와 같은 디렉토리에 배치** (권장):
   ```
   my-app/
   ├─ LPC-SpriteGenerator-portable.exe
   └─ spritesheets/          ← tools/lpc-sprite-generator/spritesheets 복사
   ```

2. **`--spritesheets` 플래그로 경로 지정**:
   ```bash
   LPC-SpriteGenerator-portable.exe --spritesheets /path/to/spritesheets
   ```

3. **개발 모드** (`npx electron .`): 서브모듈 원본 `spritesheets/` 자동 참조.

### 패키징된 exe에서 헤드리스 모드

```bash
# spritesheets 경로 지정 필수 (또는 exe와 같은 디렉토리에 배치)
LPC-SpriteGenerator-0.0.0-portable.exe --spritesheets ../tools/lpc-sprite-generator/spritesheets --headless --random --output char.png
LPC-SpriteGenerator-0.0.0-portable.exe --headless --random --count 5 --output-dir ./out
```

## 출력 스프라이트 시트 형식

생성된 PNG는 **LPC 표준 시트** (832×3456px):
- 폭 832px = 13프레임 × 64px
- 높이 3456px = 54행 × 64px
- 행별 애니메이션: spellcast, thrust, walk, slash, shoot, hurt, idle, jump, run 등
- 각 애니메이션은 4행(방향: up/left/down/right) × 프레임

자세한 레이아웃은 `documents/game/lpc-sprite-generator-analysis.md` 참조.

## 라이선스 / 크레딧

- 랜덤 생성 시 사용된 부품의 크레딧이 stdout에 출력됨
- LPC 콘텐츠는 CC-BY-SA/OGA-BY (출처 표기 의무)
- 상업적 사용 가능하되 생성물의 크레딧을 별도로 기록할 것

## 아키텍처

```
electron/
├── main.js          # 메인 프로세스 (GUI/헤드리스 분기, IPC, 파일 저장)
└── preload.mjs      # 컨텍스트 브릿지 (electronAPI, __isElectron 마커)

sources/
└── electron-bridge.ts  # renderer 측 브릿지 (__lpcAPI 노출: 랜덤 생성, 렌더, 목록)

scripts/
├── build-electron.bat  # Windows 빌드 스크립트
└── build-electron.sh   # Unix 빌드 스크립트
```

### 동작 흐름 (헤드리스)

```
1. main.js: process.argv 파싱 → --headless 감지 → GPU 비활성화
2. hidden BrowserWindow 생성 → dist/index.html 로드
3. renderer 부트: main.ts → installElectronBridge() → window.__lpcAPI 등록
4. main: __lpcAPIReady 폴링 → 준비 시 executeJavaScript로 호출
5. renderer: getRandomSelections() (카탈로그에서 랜덤 부품 선택)
6. renderer: renderToBuffer() (Canvas 합성 → PNG arrayBuffer)
7. main: Buffer.from(arrayBuffer) → fs.writeFileSync → app.quit()
```

## 문제 해결

- **`__lpcAPI 준비 타임아웃`**: `npm run build`로 dist/ 최신화 후 재시도
- **WebGL 경고**: 소프트웨어 렌더링(SwiftShader) 사용, CPU 폴백으로 자동 전환 (정상)
- **`Failed to load image`**: 랜덤 선택한 일부 변형 파일이 없음 (해당 레이어 스킵, 치명적 아님)
- **패키징 느림**: 첫 빌드 시 Electron 바이너리 다운로드, 이후 캐시 사용
