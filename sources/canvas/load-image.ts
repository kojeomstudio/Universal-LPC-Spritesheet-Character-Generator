import { debugWarn } from "../utils/debug.ts";

let loadedImages: Record<string, HTMLImageElement> = {};
/** In-flight loads: same `src` shares one `Image` and one profiler span. */
const inFlight = new Map<string, Promise<HTMLImageElement>>();

/** Profiler is attached to `window.profiler` by `main.js`; absent in tests / Node. */
type WindowWithProfiler = Window & {
  profiler?: {
    mark: (name: string) => void;
    measure: (name: string, start: string, end: string) => void;
  };
};

/**
 * Clears the in-memory image cache. Browser tests call this so a stubbed
 * `Image` constructor cannot poison later specs that share the same module.
 */
export function resetImageLoadCache(): void {
  loadedImages = {};
  inFlight.clear();
}

/**
 * Electron 환경에서 spritesheets 절대경로로 변환.
 * 바이너리에서 spritesheets를 분리했으므로, Electron main 프로세스가
 * window.__SPRITESHEETS_BASE__에 file:// URL prefix를 주입.
 * 브라우저에서는 기존 상대경로 유지.
 */
function resolveSrc(src: string): string {
  const base = (window as any).__SPRITESHEETS_BASE__;
  if (base && src.startsWith("spritesheets/")) {
    return base + src.slice("spritesheets/".length);
  }
  return src;
}

/** Load an image. Rejects with `Error("Failed to load <src>")` on error. */
export function loadImage(src: string): Promise<HTMLImageElement> {
  const resolved = resolveSrc(src);
  if (loadedImages[resolved]) {
    return Promise.resolve(loadedImages[resolved]);
  }
  const existing = inFlight.get(resolved);
  if (existing) {
    return existing;
  }

  // Register in-flight *before* creating the Image. The Promise constructor runs
  // the executor synchronously; if we only `set` after `new Promise(...)`, a
  // second concurrent `loadImage(src)` can miss `inFlight` and create a second
  // `Image` for the same `src` (fails "share one in-flight request" in tests).
  let resolve!: (img: HTMLImageElement) => void;
  let reject!: (err: Error) => void;
  const p = new Promise<HTMLImageElement>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  inFlight.set(resolved, p);

  // Mark start of image load (span is actual fetch/decode)
  const profiler = (window as WindowWithProfiler).profiler;
  if (profiler) {
    profiler.mark(`image-load:${resolved}:start`);
  }

  const img = new Image();
  img.onload = () => {
    loadedImages[resolved] = img;
    inFlight.delete(resolved);

    if (profiler) {
      profiler.mark(`image-load:${resolved}:end`);
      profiler.measure(
        `image-load:${resolved}`,
        `image-load:${resolved}:start`,
        `image-load:${resolved}:end`,
      );
    }

    resolve(img);
  };
  img.onerror = () => {
    inFlight.delete(resolved);
    console.error(`Failed to load image: ${resolved}`);
    reject(new Error(`Failed to load ${resolved}`));
  };
  img.src = resolved;

  return p;
}

export type LoadedImage<T> = {
  item: T;
  img: HTMLImageElement | null;
  success: boolean;
};

/** Load multiple images in parallel, swallowing per-image errors. */
export async function loadImagesInParallel<T>(
  items: T[],
  getPath: (item: T) => string = (item) =>
    (item as { spritePath: string }).spritePath,
): Promise<LoadedImage<T>[]> {
  const promises = items.map(
    (item): Promise<LoadedImage<T>> =>
      loadImage(getPath(item))
        .then((img): LoadedImage<T> => ({ item, img, success: true }))
        .catch(() => {
          debugWarn(`Failed to load sprite: ${getPath(item)}`);
          return { item, img: null, success: false };
        }),
  );

  return Promise.all(promises);
}
