import { useEffect, useRef, useState } from "react";
import { synthesizeGeminiSpeech } from "../../api/speech";
import { readSpeechPreferences } from "./speechPreferences";
import styles from "./ask-raven-message-actions.module.css";

const audioCache = new Map<string, string>();

function cacheAudio(key: string, url: string) {
  audioCache.set(key, url);
  if (audioCache.size > 20) {
    const oldest = audioCache.keys().next().value;
    if (oldest) { const evicted = audioCache.get(oldest); if (evicted) URL.revokeObjectURL(evicted); audioCache.delete(oldest); }
  }
}

export function AskRavenMessageActions({ messageId, content }: { messageId: string; content: string }) {
  const preferences = readSpeechPreferences();
  const [mode, setMode] = useState<"idle" | "loading" | "browser" | "gemini">("idle");
  const [paused, setPaused] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const cacheKey = `${messageId}:${preferences.voice || "default"}`;
  const supportsBrowserSpeech = typeof window !== "undefined" && "speechSynthesis" in window;

  useEffect(() => () => {
    if (audioRef.current) { audioRef.current.pause(); audioRef.current = null; }
    if (typeof window !== "undefined" && "speechSynthesis" in window) window.speechSynthesis.cancel();
  }, []);

  const stop = () => {
    audioRef.current?.pause(); audioRef.current = null;
    if (supportsBrowserSpeech) window.speechSynthesis.cancel();
    setMode("idle"); setPaused(false);
  };

  const read = async () => {
    setError(null);
    try {
      if (preferences.outputProvider === "browser") {
        if (!supportsBrowserSpeech) throw new Error("Browser speech synthesis is not available here.");
        const utterance = new SpeechSynthesisUtterance(content);
        const voice = window.speechSynthesis.getVoices().find((item) => item.voiceURI === preferences.voice);
        if (voice) utterance.voice = voice;
        utterance.onend = () => { setMode("idle"); setPaused(false); };
        utterance.onerror = () => { setMode("idle"); setError("The browser could not read this response aloud."); };
        window.speechSynthesis.cancel(); window.speechSynthesis.speak(utterance); setMode("browser");
        return;
      }
      setMode("loading");
      let url = audioCache.get(cacheKey);
      if (!url) {
        const result = await synthesizeGeminiSpeech(content.slice(0, 4_000), preferences.voice || "Kore");
        const bytes = Uint8Array.from(atob(result.audioBase64), (character) => character.charCodeAt(0));
        url = URL.createObjectURL(new Blob([bytes], { type: result.mimeType }));
        cacheAudio(cacheKey, url);
        if (content.length > 4_000) setError("Gemini read aloud covers the first 4,000 characters of this response.");
      }
      const audio = new Audio(url); audioRef.current = audio;
      audio.onended = () => { audioRef.current = null; setMode("idle"); setPaused(false); };
      audio.onerror = () => { audioRef.current = null; setMode("idle"); setError("Gemini speech audio could not be played."); };
      await audio.play(); setMode("gemini");
    } catch (reason) {
      setMode("idle"); setError(reason instanceof Error ? reason.message : "Read aloud could not start.");
    }
  };

  const togglePause = () => {
    if (mode === "browser") {
      const wasPaused = window.speechSynthesis.paused;
      if (wasPaused) window.speechSynthesis.resume(); else window.speechSynthesis.pause();
      setPaused(!wasPaused);
      return;
    }
    const audio = audioRef.current; if (!audio) return;
    if (audio.paused) { void audio.play(); setPaused(false); } else { audio.pause(); setPaused(true); }
  };

  if (!preferences.readAloudEnabled || !content.trim()) return null;
  return <span className={styles.actions}>
    {mode === "idle" ? <button type="button" onClick={() => void read()}>Read aloud</button> : null}
    {mode === "loading" ? <span role="status">Preparing audio…</span> : null}
    {mode === "browser" || mode === "gemini" ? <><button type="button" onClick={togglePause}>{paused ? "Resume" : "Pause"}</button><button type="button" onClick={stop}>Stop</button></> : null}
    {error ? <span role="alert">{error}</span> : null}
  </span>;
}
