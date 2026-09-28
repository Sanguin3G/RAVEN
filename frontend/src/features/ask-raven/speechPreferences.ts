export type RecognitionProvider = "browser" | "geminiLive";
export type SpeechOutputProvider = "browser" | "gemini";

export const geminiVoiceNames = ["Kore", "Puck", "Charon", "Fenrir", "Aoede", "Leda", "Orus", "Zephyr"] as const;
export const defaultGeminiVoice = "Kore";

export function getGeminiVoice(voice: string) {
  return geminiVoiceNames.some((name) => name === voice) ? voice : defaultGeminiVoice;
}

export interface SpeechPreferences {
  recognitionProvider: RecognitionProvider;
  language: string;
  deviceId: string;
  outputProvider: SpeechOutputProvider;
  voice: string;
  readAloudEnabled: boolean;
}

export const speechPreferencesKey = "raven:speech-preferences";

export const defaultSpeechPreferences: SpeechPreferences = {
  recognitionProvider: "browser",
  language: "",
  deviceId: "",
  outputProvider: "browser",
  voice: "",
  readAloudEnabled: true,
};

export function readSpeechPreferences(): SpeechPreferences {
  if (typeof window === "undefined") return defaultSpeechPreferences;
  try {
    const value: unknown = JSON.parse(window.localStorage.getItem(speechPreferencesKey) ?? "{}");
    if (!value || typeof value !== "object" || Array.isArray(value)) return defaultSpeechPreferences;
    const candidate = value as Partial<SpeechPreferences>;
    return {
      recognitionProvider: candidate.recognitionProvider === "geminiLive" ? "geminiLive" : "browser",
      language: typeof candidate.language === "string" ? candidate.language : "",
      deviceId: typeof candidate.deviceId === "string" ? candidate.deviceId : "",
      outputProvider: candidate.outputProvider === "gemini" ? "gemini" : "browser",
      voice: typeof candidate.voice === "string" ? candidate.voice : "",
      readAloudEnabled: typeof candidate.readAloudEnabled === "boolean" ? candidate.readAloudEnabled : true,
    };
  } catch {
    return defaultSpeechPreferences;
  }
}

export function saveSpeechPreferences(preferences: SpeechPreferences) {
  try { window.localStorage.setItem(speechPreferencesKey, JSON.stringify(preferences)); } catch { /* Device preferences stay optional. */ }
}
