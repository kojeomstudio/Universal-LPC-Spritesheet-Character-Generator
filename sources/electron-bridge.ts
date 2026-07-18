/**
 * LPC Sprite Generator — Electron renderer 브릿지.
 *
 * 렌더러가 자신의 카탈로그/렌더러 API를 window.__lpcAPI로 노출.
 * 헤드리스 모드에서 main 프로세스가 executeJavaScript로 이 메서드들을 호출.
 *
 * 이 모듈은 main.ts에서 import되어 렌더러 부트 시 실행됨.
 * Electron 환경에서만 동작 (window.__lpcAPIShim 존재 여부로 감지).
 */
import { renderCharacter, getCanvas } from "./canvas/renderer.ts";
import { defaultCatalog, getCategoryTree, getItemMerged } from "./state/catalog.ts";
import { ANIMATION_OFFSETS } from "./state/constants.ts";
import type { Selections, Selection } from "./state/state.ts";

// 랜덤 생성 시 포함할 카테고리(type_name 기준)
// 각 카테고리마다 0~1개 아이템을 무작위 선택
const RANDOM_CATEGORIES = [
  "head",       // 머리 (필수)
  "hair",       // 헤어스타일
  "ears",       // 귀 (엘프 등)
  "facial_eyes",// 눈
  "expression", // 표정
  "clothes",    // 상의
  "legs",       // 하의
  "shoes",      // 신발
  "hat",        // 모자 (선택)
  "weapon",     // 무기 (선택)
  "shield",     // 방패 (선택)
  "cape",       // 망토 (선택)
  "neck",       // 목 장신구
  "belt",       // 벨트
];

// 결정적 RNG (시드 지원)
function mulberry32(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a |= 0;
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function pick<T>(arr: T[], rng: () => number): T | null {
  if (arr.length === 0) return null;
  return arr[Math.floor(rng() * arr.length)];
}

// 카탈로그에서 type_name별 itemId 인덱스 구축
function buildTypeIndex(
  itemMetadata: Record<string, any>,
): Map<string, string[]> {
  const idx = new Map<string, string[]>();
  for (const [itemId, meta] of Object.entries(itemMetadata)) {
    const tn = meta.type_name;
    if (!tn) continue;
    if (!idx.has(tn)) idx.set(tn, []);
    idx.get(tn)!.push(itemId);
  }
  return idx;
}

// body 항목은 type_name이 "body"가 아니라 별도 처리 — body는 항상 포함
const BODY_ITEM_ID = "body";

export interface RandomResult {
  selections: Selections;
  bodyType: string;
  credits: Array<{ author: string; license: string; name?: string }>;
}

/**
 * 랜덤 selections 생성.
 * @param seed 시드 (null이면 Math.random 사용)
 * @param bodyType 강제 bodyType (기본 "male")
 */
export async function getRandomSelections(
  seed: number | null,
  bodyType = "male",
): Promise<RandomResult> {
  const rng = seed != null ? mulberry32(seed) : Math.random;

  // 카탈로그 전체 준비 대기 + itemId 수집 (listItems가 onAllReady await)
  const itemIds = await listItems();

  // type_name별 인덱스 — getItemMerged로 순회
  const typeIndex = new Map<string, string[]>();
  for (const itemId of itemIds) {
    const r = getItemMerged(itemId);
    if (r.isErr()) continue;
    const tn = (r.value as any).type_name;
    if (!tn) continue;
    if (!typeIndex.has(tn)) typeIndex.set(tn, []);
    typeIndex.get(tn)!.push(itemId);
  }

  const selections: Selections = {};
  const credits: RandomResult["credits"] = [];

  // body는 항상 포함 (기본)
  const bodyVariants = ["light", "tan", "dark"];
  const bodyVariant = pick(bodyVariants, rng) ?? "light";
  selections["body"] = {
    itemId: BODY_ITEM_ID,
    name: "Body",
    variant: "",
    recolor: bodyVariant,
  };

  // 각 카테고리에서 랜덤 선택 (70% 확률로 포함)
  for (const typeName of RANDOM_CATEGORIES) {
    if (typeName === "body") continue;
    if (rng() < 0.3) continue; // 30% 확률로 스킵 (모자/무기 등은 자주 빠짐)

    const candidates = typeIndex.get(typeName);
    if (!candidates || candidates.length === 0) continue;

    const itemId = pick(candidates, rng);
    if (!itemId) continue;

    const meta = getItemMerged(itemId);
    if (meta.isErr()) continue;
    const m = meta.value as any;

    // bodyType 호환성 필터
    const required = m.required as string[] | undefined;
    if (required && !required.includes(bodyType)) continue;

    // variant/recolor 무작위 선택
    let variant = "";
    let recolor = "";
    const variants = m.variants as string[] | undefined;
    if (variants && variants.length > 0) {
      variant = pick(variants, rng) ?? "";
    }
    const recolors = m.recolors as any[] | undefined;
    if (recolors && recolors.length > 0) {
      const r0 = recolors[0];
      if (r0 && r0.variants && r0.variants.length > 0) {
        recolor = pick(r0.variants, rng) ?? "";
      }
    }

    selections[typeName] = {
      itemId,
      name: m.name ?? itemId,
      variant,
      recolor,
    };

    // 크레딧 수집
    if (m.credits) {
      for (const c of m.credits) {
        if (c.authors) {
          for (const a of c.authors) {
            credits.push({
              author: a,
              license: (c.licenses && c.licenses[0]) || "CC-BY",
              name: m.name,
            });
          }
        }
      }
    }
  }

  return { selections, bodyType, credits };
}

/**
 * selections로 캔버스 합성 후 PNG arrayBuffer 반환.
 */
export async function renderToBuffer(
  selections: Selections,
  bodyType: string,
): Promise<ArrayBuffer | null> {
  await renderCharacter(selections, bodyType);
  const canvasResult = getCanvas();
  if (canvasResult.isErr()) {
    console.error("[bridge] 캔버스 없음:", canvasResult.error);
    return null;
  }
  const canvas = canvasResult.value;
  // 캔버스 → PNG blob → arrayBuffer
  const blob: Blob = await new Promise((resolve, reject) => {
    canvas.toBlob(
      (b: Blob | null) => (b ? resolve(b) : reject(new Error("toBlob null"))),
      "image/png",
    );
  });
  return await blob.arrayBuffer();
}

/**
 * 카탈로그의 모든 itemId 목록 반환 (비동기 — 카탈로그 ready 대기).
 * getCategoryTree의 Result(neverthrow)를 언랩하여 트리 순회.
 */
export async function listItems(): Promise<string[]> {
  await defaultCatalog.ready.onAllReady;
  const treeResult = getCategoryTree();
  // neverthrow: isOk/isErr는 메서드
  const isOk = typeof treeResult.isOk === "function" ? treeResult.isOk() : treeResult.isOk;
  if (!isOk) return [];
  const tree = (treeResult as any)._unsafeUnwrap?.() ?? (treeResult as any).value;
  if (!tree) return [];
  const ids: string[] = [];
  function walk(node: any) {
    if (!node || typeof node !== "object") return;
    for (const key of Object.keys(node)) {
      const val = node[key];
      if (Array.isArray(val)) {
        for (const c of val) {
          if (typeof c === "string") ids.push(c);
          else if (c && typeof c === "object") {
            if (c.name && typeof c.name === "string") ids.push(c.name);
            if (c.itemId) ids.push(c.itemId);
            walk(c);
          }
        }
      } else if (val && typeof val === "object") {
        walk(val);
      }
    }
  }
  walk(tree);
  return [...new Set(ids)];
}

/**
 * Electron 환경 감지 및 window.__lpcAPI 등록.
 * main.ts에서 부트 시 호출.
 *
 * preload.mjs는 window.__isElectron = true만 설정 (contextBridge 객체는 읽기 전용).
 * 이 모듈은 sandbox=false이므로 window에 직접 __lpcAPI와 준비 플래그를 설정.
 */
export function installElectronBridge() {
  if (typeof window === "undefined" || !(window as any).__isElectron) return;

  (window as any).__lpcAPI = {
    getRandomSelections,
    renderToBuffer,
    listItems,
  };
  // 준비 신호 (별도 객체로 설정하여 읽기 전용 제약 회피)
  (window as any).__lpcAPIReady = true;
  console.log("[bridge] __lpcAPI 등록 완료 (Electron 헤드리스 준비)");
}
