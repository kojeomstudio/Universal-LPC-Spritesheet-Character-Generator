/**
 * LPC Sprite Generator — Electron 프리로드 스크립트.
 *
 * 컨텍스트 격리 유지 (nodeIntegration=false).
 * - electronAPI: GUI 모드 다운로드 인터셉트 (save-buffer IPC).
 *   contextBridge로 안전 노출.
 * - __lpcAPIReadySignal: 헤드리스 모드에서 renderer가 API 등록 완료를 알리는 신호.
 *   contextBridge 객체는 읽기 전용이라 renderer가 직접 속성을 못 바꾸므로,
 *   마커용 플래그만 노출. 실제 __lpcAPI는 electron-bridge.ts가
 *   window에 직접 설정 (sandbox=false이므로 window 접근 가능).
 */
import { contextBridge, ipcRenderer } from "electron";

// GUI 모드: download.ts에서 사용
contextBridge.exposeInMainWorld("electronAPI", {
  saveBuffer: (arrayBuffer, defaultName) =>
    ipcRenderer.invoke("save-buffer", arrayBuffer, defaultName),
});

// 헤드리스 마커: Electron 환경임을 renderer에 알림.
// electron-bridge.ts가 이 플래그를 보고 __lpcAPI를 설정함.
// (객체 자체는 읽기 전용이지만, electron-bridge.ts는 자체 window.__lpcAPI를 새로 만듦)
contextBridge.exposeInMainWorld("__isElectron", true);
