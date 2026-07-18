import type { ResultAsync } from "neverthrow";
import { canvasToBlob } from "./canvas-utils.ts";
import { getCanvas, type CanvasNotInitialized } from "./renderer.ts";

type GetCanvasBlobFn = () => ResultAsync<Blob, CanvasNotInitialized>;

/**
 * Download canvas as PNG (exports the offscreen canvas directly).
 * `getCanvasBlobFunc` defaults to the real renderer canvas; tests inject a stub.
 *
 * Electron 환경에서는 electronAPI.saveBuffer를 통해 네이티브 저장 다이얼로그 사용.
 * 브라우저에서는 기존 <a download>.click() 경로.
 */
export async function downloadAsPNG(
  filename: string = "character-spritesheet.png",
  getCanvasBlobFunc: GetCanvasBlobFn = () => getCanvas().asyncMap(canvasToBlob),
): Promise<void> {
  const blobResult = await getCanvasBlobFunc();
  if (blobResult.isErr()) {
    console.error("Error downloading PNG:", blobResult.error);
    return;
  }

  // Electron 환경: IPC로 네이티브 저장 다이얼로그 + fs.writeFileSync
  const electronAPI = (window as any).electronAPI;
  if (electronAPI?.saveBuffer) {
    const arrayBuffer = await blobResult.value.arrayBuffer();
    const result = await electronAPI.saveBuffer(arrayBuffer, filename);
    if (!result.ok && !result.canceled) {
      console.error("Electron 저장 실패:", result.error);
    }
    return;
  }

  // 브라우저 환경: 기존 anchor click 경로
  const url = URL.createObjectURL(blobResult.value);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}

export function downloadFile(
  content: string,
  filename: string,
  type: string = "text/plain",
): void {
  const blob = new Blob([content], { type });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}
