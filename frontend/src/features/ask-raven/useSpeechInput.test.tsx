import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useState } from "react";
import { useSpeechInput } from "./useSpeechInput";

class FakeRecognition {
  static current: FakeRecognition;
  continuous = false;
  interimResults = false;
  lang = "";
  onresult: ((event: { results: { isFinal: boolean; 0: { transcript: string } }[] }) => void) | null = null;
  onerror: ((event: { error: string }) => void) | null = null;
  onend: (() => void) | null = null;
  start = vi.fn();
  stop = vi.fn();
  abort = vi.fn();
  constructor() { FakeRecognition.current = this; }
}

function setupBrowser() {
  const stop = vi.fn();
  Object.defineProperty(window, "webkitSpeechRecognition", { configurable: true, value: FakeRecognition });
  Object.defineProperty(navigator, "mediaDevices", { configurable: true, value: { getUserMedia: vi.fn().mockResolvedValue({ getTracks: () => [{ stop }] }) } });
  Object.defineProperty(window, "AudioContext", { configurable: true, value: class {
    createMediaStreamSource() { return { connect: vi.fn() }; }
    createAnalyser() { return { fftSize: 0, getByteTimeDomainData: (samples: Uint8Array) => samples.fill(128) }; }
    close() { return Promise.resolve(); }
  } });
  return stop;
}

function useComposer(initial: string) {
  const [text, setText] = useState(initial);
  return { text, speech: useSpeechInput(text, setText) };
}

describe("browser dictation", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    delete (window as Window & { webkitSpeechRecognition?: unknown }).webkitSpeechRecognition;
  });

  it("degrades when speech recognition is unavailable", () => {
    const { result } = renderHook(() => useComposer("Typed question"));
    expect(result.current.speech.supported).toBe(false);
  });

  it("appends editable dictation and never sends it", async () => {
    const stop = setupBrowser();
    const { result } = renderHook(() => useComposer("Compare this with"));
    await act(async () => { await result.current.speech.start("en-US"); });
    expect(result.current.speech.listening).toBe(true);
    expect(FakeRecognition.current.lang).toBe("en-US");
    act(() => { FakeRecognition.current.onresult?.({ results: [{ isFinal: true, 0: { transcript: "their competitors" } }] }); });
    expect(result.current.text).toBe("Compare this with their competitors");
    act(() => { result.current.speech.finish(); });
    expect(result.current.speech.listening).toBe(false);
    expect(result.current.text).toBe("Compare this with their competitors");
    expect(stop).toHaveBeenCalled();
  });

  it("restores the original draft on cancel", async () => {
    setupBrowser();
    const { result } = renderHook(() => useComposer("Original"));
    await act(async () => { await result.current.speech.start(""); });
    act(() => { FakeRecognition.current.onresult?.({ results: [{ isFinal: false, 0: { transcript: "spoken words" } }] }); });
    expect(result.current.text).toBe("Original spoken words");
    act(() => { result.current.speech.cancel(); });
    expect(result.current.text).toBe("Original");
    expect(FakeRecognition.current.abort).toHaveBeenCalled();
  });

  it("reports permission denial without changing the draft", async () => {
    setupBrowser();
    vi.mocked(navigator.mediaDevices.getUserMedia).mockRejectedValue(new DOMException("Denied", "NotAllowedError"));
    const { result } = renderHook(() => useComposer("Original"));
    await act(async () => { await result.current.speech.start(""); });
    expect(result.current.speech.error).toBe("Microphone permission was denied.");
    expect(result.current.text).toBe("Original");
  });
});
