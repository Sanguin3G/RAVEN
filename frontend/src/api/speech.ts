import { request } from "./client";

export interface GeminiLiveToken { token: string }
export interface GeminiSpeechAudio { audioBase64: string; mimeType: string }

export const createGeminiLiveToken = (language: string) => request<GeminiLiveToken>("/api/speech/live-token", {
  method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ language: language || null }),
});

export const synthesizeGeminiSpeech = (text: string, voice: string) => request<GeminiSpeechAudio>("/api/speech/synthesize", {
  method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ text, voice }),
});
