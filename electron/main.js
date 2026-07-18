/**
 * LPC Sprite Generator — Electron 메인 프로세스.
 *
 * 두 가지 모드 지원:
 *  - GUI 모드 (기본): BrowserWindow에서 dist/index.html 로드, 브라우저 UI 그대로.
 *  - 헤드리스 CLI 모드 (--headless): hidden window에서 렌더러 구동 후 PNG 저장.
 *
 * CLI 사용법 (헤드리스):
 *   electron . --headless --random --output character.png
 *   electron . --headless --random --count 5 --output-dir ./out
 *   electron . --headless --selections ./my-char.json --output char.png
 *   electron . --headless --list-items          # 사용 가능한 itemId 목록 출력
 *
 * 빌드 시 main 필드(package.json)가 이 파일을 가리킴.
 */
import { app, BrowserWindow, ipcMain, dialog } from "electron";
import * as path from "node:path";
import * as fs from "node:fs";
import { pathToFileURL } from "node:url";

// EPIPE 방지: stdout/stderr가 닫힌 경우 에러 이벤트 무시 (헤드리스 파이프 종료)
process.stdout?.on?.("error", (e) => { if (e.code === "EPIPE") return; throw e; });
process.stderr?.on?.("error", (e) => { if (e.code === "EPIPE") return; throw e; });

const __dirname = path.dirname(new URL(import.meta.url).pathname.replace(/^\//, ""));

// ────────────────────────────────────────────────────────────────────────────
// CLI 인자 파싱
// ────────────────────────────────────────────────────────────────────────────
function parseArgs(argv) {
  // Electron은 개발 모드와 패키지 모드에서 argv 형태가 다름. --headless 이후만 본다.
  const headlessIdx = argv.indexOf("--headless");
  const isHeadless = headlessIdx !== -1;
  const cliArgs = isHeadless ? argv.slice(headlessIdx + 1) : [];

  const get = (flag) => {
    const i = cliArgs.indexOf(flag);
    return i !== -1 && i + 1 < cliArgs.length ? cliArgs[i + 1] : null;
  };
  const has = (flag) => cliArgs.includes(flag);

  return {
    isHeadless,
    random: has("--random"),
    listItems: has("--list-items"),
    selections: get("--selections"),
    output: get("--output"),
    outputDir: get("--output-dir"),
    count: parseInt(get("--count") ?? "1", 10),
    seed: get("--seed") ? parseInt(get("--seed"), 10) : null,
    bodyType: get("--body-type") ?? "male",
    // GUI 모드: 다운로드 기본 디렉토리 (--save-dir). 없으면 다이얼로그.
    saveDir: get("--save-dir"),
    width: parseInt(get("--width") ?? "1280", 10),
    height: parseInt(get("--height") ?? "3456", 10),
  };
}

const args = parseArgs(process.argv);

// ────────────────────────────────────────────────────────────────────────────
// 헤드리스 모드: GPU 비활성화 (소프트웨어 렌더링 — SwiftShader / CPU 폴백)
// ────────────────────────────────────────────────────────────────────────────
if (args.isHeadless) {
  app.disableHardwareAcceleration();
  // 헤드리스에선 GPU 블록리스트 무시하고 SwiftShader 강제 시도
  app.commandLine.appendSwitch("use-gl", "angle");
  app.commandLine.appendSwitch("use-angle", "swiftshader");
  app.commandLine.appendSwitch("ignore-gpu-blocklist");
  app.commandLine.appendSwitch("disable-gpu");
}

// dist 경로 해석 (개발: 프로젝트 루트/dist, 패키지: app.asar 내부의 dist)
// Electron은 app.asar를 투명하게 처리하므로 app.getAppPath() 기준으로 해석.
function resolveDistDir() {
  if (app.isPackaged) {
    // 패키징 시: app.asar 내부의 dist/ (appPath = resources/app.asar)
    return path.join(app.getAppPath(), "dist");
  }
  return path.join(__dirname, "..", "dist");
}

// spritesheets 경로 해석 (바이너리에서 분리, 원본 위치 참조).
// 개발: 서브모듈 루트/spritesheets (원본 그대로)
// 패키지: exe와 같은 디렉토리의 spritesheets/ (사용자가 배포 시 동반)
// CLI 옵션 --spritesheets <path>로 임의 경로 지정 가능
function resolveSpritesheetsDir() {
  // CLI에서 명시적 지정 시 최우선
  const cliSpritesheetsIdx = process.argv.indexOf("--spritesheets");
  if (cliSpritesheetsIdx !== -1 && cliSpritesheetsIdx + 1 < process.argv.length) {
    return path.resolve(process.argv[cliSpritesheetsIdx + 1]);
  }
  if (app.isPackaged) {
    // 패키지: exe와 같은 디렉토리의 spritesheets/
    return path.join(path.dirname(app.getPath("exe")), "spritesheets");
  }
  // 개발: 서브모듈 루트의 spritesheets/
  return path.join(__dirname, "..", "spritesheets");
}

// ────────────────────────────────────────────────────────────────────────────
// IPC: 렌더러 → 메인
// ────────────────────────────────────────────────────────────────────────────

// PNG 버퍼를 파일로 저장 (GUI 다운로드 인터셉트).
// --save-dir 지정 시 다이얼로그 없이 자동 저장. 없으면 저장 다이얼로그.
ipcMain.handle("save-buffer", async (_event, arrayBuffer, defaultName) => {
  const name = defaultName ?? "character-spritesheet.png";
  let outPath;
  if (args.saveDir) {
    // 자동 저장 모드: saveDir/name 에 저장
    try {
      fs.mkdirSync(args.saveDir, { recursive: true });
    } catch { /* 이미 존재 */ }
    outPath = path.join(args.saveDir, name);
  } else {
    // 다이얼로그 모드
    const defaultPath = args.saveDir ? path.join(args.saveDir, name) : name;
    const result = await dialog.showSaveDialog({
      defaultPath,
      filters: [{ name: "PNG", extensions: ["png"] }],
    });
    if (result.canceled || !result.filePath) return { ok: false, canceled: true };
    outPath = result.filePath;
  }
  try {
    fs.writeFileSync(outPath, Buffer.from(arrayBuffer));
    return { ok: true, path: outPath };
  } catch (e) {
    return { ok: false, error: e.message };
  }
});

// 헤드리스: PNG 버퍼를 지정 경로에 저장 (다이얼로그 없음)
ipcMain.handle("write-buffer", async (_event, arrayBuffer, filePath) => {
  try {
    fs.writeFileSync(filePath, Buffer.from(arrayBuffer));
    return { ok: true };
  } catch (e) {
    return { ok: false, error: e.message };
  }
});

// ────────────────────────────────────────────────────────────────────────────
// 앱 준비 → 모드 분기
// ────────────────────────────────────────────────────────────────────────────
function createWindow({ show = true } = {}) {
  const win = new BrowserWindow({
    width: 1400,
    height: 900,
    show,
    webPreferences: {
      preload: path.join(__dirname, "preload.mjs"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false, // preload에서 Node API 사용 (fs는 main에서만)
      webgl: true,
    },
  });

  // 헤드리스 디버깅: renderer 콘솔 로그를 main stdout으로 포워딩.
  // EPIPE 방지: stdout이 닫힌 경우 안전하게 무시.
  if (args.isHeadless) {
    win.webContents.on("console-message", (e) => {
      const msg = e?.message ?? "(no message)";
      try {
        if (process.stdout.writable) {
          process.stdout.write(`[renderer] ${msg}\n`);
        }
      } catch {
        // stdout 닫힘 (EPIPE 등) — 무시
      }
    });
  }

  const distDir = resolveDistDir();
  const indexPath = path.join(distDir, "index.html");
  win.loadFile(indexPath).catch((e) => {
    console.error(`[main] index.html 로드 실패: ${indexPath}\n  ${e.message}`);
    console.error(`[main] 먼저 'npm run build'를 실행해 dist/를 생성하세요.`);
    app.quit(1);
  });

  // spritesheets 절대경로를 renderer에 주입.
  // renderer의 load-image.ts가 file:// URL로 변환하여 사용.
  // spritesheets는 바이너리에서 분리되어 서브모듈/배포 위치의 원본을 참조.
  const spritesheetsDir = resolveSpritesheetsDir();
  win.webContents.once("dom-ready", () => {
    const spritesheetsPath = pathToFileURL(spritesheetsDir).href + "/";
    win.webContents.executeJavaScript(
      `window.__SPRITESHEETS_BASE__ = ${JSON.stringify(spritesheetsPath)};`,
    ).catch(() => {});
  });

  return win;
}

// ────────────────────────────────────────────────────────────────────────────
// 헤드리스 렌더링 파이프라인
// ────────────────────────────────────────────────────────────────────────────
async function runHeadless() {
  const startTime = Date.now();
  console.log("[headless] 시작");

  // --list-items: 카탈로그의 itemId 목록만 출력하고 종료
  if (args.listItems) {
    return runWithHiddenWindow(async (win) => {
      const items = await win.webContents.executeJavaScript(
        `window.__lpcAPI.listItems()`,
        true,
      );
      const list = Array.isArray(items) ? items : [];
      console.log(`[headless] 사용 가능한 itemId ${list.length}개:`);
      for (const id of list.slice(0, 50)) console.log(`  ${id}`);
      if (list.length > 50) console.log(`  ... 외 ${list.length - 50}개`);
    });
  }

  // 출력 디렉토리 보장
  if (args.outputDir) {
    fs.mkdirSync(args.outputDir, { recursive: true });
  }

  const count = Math.max(1, args.count);
  // 단일 hidden window 재사용으로 배치 생성
  await runWithHiddenWindow(async (win) => {
    for (let i = 0; i < count; i++) {
      let selectionsPayload;
      if (args.random) {
        // 시드가 있으면 매 회 다른 시드 사용 (다양성)
        const seedArg = args.seed != null ? args.seed + i : "null";
        selectionsPayload = await win.webContents.executeJavaScript(
          `window.__lpcAPI.getRandomSelections(${seedArg}, "${args.bodyType}")`,
          true,
        );
      } else if (args.selections) {
        const raw = fs.readFileSync(args.selections, "utf8");
        selectionsPayload = JSON.parse(raw);
      } else {
        console.error("[headless] --random 또는 --selections 중 하나 필요");
        return;
      }

      // 렌더러에서 캔버스 합성 실행
      const arrayBuffer = await win.webContents.executeJavaScript(
        `window.__lpcAPI.renderToBuffer(${JSON.stringify(selectionsPayload.selections)}, "${selectionsPayload.bodyType ?? args.bodyType}")`,
        true,
      );

      if (!arrayBuffer) {
        console.error(`[headless] 렌더링 실패 (캐릭터 ${i + 1})`);
        continue;
      }

      // 출력 경로 결정
      let outPath;
      if (args.outputDir) {
        const name = `character-${String(i + 1).padStart(3, "0")}.png`;
        outPath = path.join(args.outputDir, name);
      } else {
        outPath = args.output ?? "character.png";
      }

      fs.writeFileSync(outPath, Buffer.from(arrayBuffer));
      console.log(`[headless] 저장: ${outPath} (${(arrayBuffer.byteLength / 1024).toFixed(0)}KB)`);

      // 크레딧 출력 (랜덤 생성 시, 첫 캐릭터만)
      if (args.random && selectionsPayload.credits && i === 0) {
        console.log(`[headless] 크레딧:`);
        const seen = new Set();
        for (const c of selectionsPayload.credits) {
          const key = `${c.author}|${c.license}`;
          if (!seen.has(key)) {
            console.log(`  - ${c.author} (${c.license}): ${c.name ?? ""}`);
            seen.add(key);
          }
        }
      }
    }
  });

  console.log(`[headless] 완료 (${count}개, ${((Date.now() - startTime) / 1000).toFixed(1)}초)`);
}

async function runWithHiddenWindow(task) {
  await new Promise((resolve) => {
    const win = createWindow({ show: false });
    win.webContents.on("did-finish-load", async () => {
      try {
        // 렌더러가 카탈로그 메타데이터를 비동기 로드할 시간을 줌.
        // __lpcAPI가 등록되고 카탈로그가 ready 상태가 될 때까지 폴링.
        const ready = await win.webContents.executeJavaScript(
          `(async () => {
            for (let i = 0; i < 60; i++) {
              if (window.__lpcAPI && window.__lpcAPIReady) return true;
              await new Promise(r => setTimeout(r, 500));
            }
            return false;
          })()`,
          true,
        );
        if (!ready) {
          console.error("[headless] __lpcAPI 준비 타임아웃 (10초 대기 후 실패)");
          console.error("[headless] renderer 로그를 확인하세요. 카탈로그 로드 실패일 수 있음.");
          return;
        }
        await task(win);
      } catch (e) {
        console.error(`[headless] 작업 오류: ${e.message}`);
        console.error(e.stack);
      } finally {
        win.destroy();
        resolve();
      }
    });
    win.webContents.on("did-fail-load", (_e, code, desc) => {
      console.error(`[headless] 로드 실패: ${code} ${desc}`);
      win.destroy();
      resolve();
    });
  });
}

// ────────────────────────────────────────────────────────────────────────────
// 앱 생명주기
// ────────────────────────────────────────────────────────────────────────────
app.whenReady().then(async () => {
  if (args.isHeadless) {
    try {
      await runHeadless();
    } catch (e) {
      console.error(`[headless] 치명적 오류: ${e.message}`);
      console.error(e.stack);
    }
    app.quit();
  } else {
    createWindow({ show: true });
    app.on("activate", () => {
      if (BrowserWindow.getAllWindows().length === 0) createWindow({ show: true });
    });
  }
});

app.on("window-all-closed", () => {
  if (process.platform !== "darwin" || args.isHeadless) app.quit();
});
