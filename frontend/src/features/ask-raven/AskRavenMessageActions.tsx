import { useEffect, useRef, useState } from "react";
import { ArrowClockwise, Check, CircleNotch, Copy, Pause, PencilSimple, Play, SpeakerHigh, Stop as StopIcon } from "@phosphor-icons/react";
import { synthesizeGeminiSpeech } from "../../api/speech";
import { getGeminiVoice, readSpeechPreferences } from "./speechPreferences";
import styles from "./ask-raven-message-actions.module.css";

const audioCache = new Map<string, string>();
let activePlayback: { owner: string; stop: () => void } | null = null;

function cacheAudio(key: string, url: string) {
  audioCache.set(key, url);
  if (audioCache.size > 20) {
    const oldest = audioCache.keys().next().value;
    if (oldest) {
      const evicted = audioCache.get(oldest);
      if (evicted) URL.revokeObjectURL(evicted);
      audioCache.delete(oldest);
    }
  }
}

function formatMessageDate(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.valueOf())) return { visible: "", detail: "" };
  const visible = new Intl.DateTimeFormat(undefined, {
    month: "short",
    day: "numeric",
    ...(date.getFullYear() === new Date().getFullYear() ? {} : { year: "numeric" as const }),
  }).format(date);
  const detail = new Intl.DateTimeFormat(undefined, {
    year: "numeric", month: "long", day: "numeric", hour: "numeric", minute: "2-digit", timeZoneName: "short",
  }).format(date);
  return { visible, detail };
}

export function ChatMessageTimestamp({ createdAt }: { createdAt: string }) {
  const formatted = formatMessageDate(createdAt);
  return formatted.visible ? <time className={styles.timestamp} dateTime={createdAt} title={formatted.detail}>{formatted.visible}</time> : null;
}

function CopyAction({ content }: { content: string }) {
  const [copied, setCopied] = useState(false);
  const timerRef = useRef<number | null>(null);
  useEffect(() => () => { if (timerRef.current !== null) window.clearTimeout(timerRef.current); }, []);
  const copy = async () => {
    await navigator.clipboard.writeText(content);
    setCopied(true);
    if (timerRef.current !== null) window.clearTimeout(timerRef.current);
    timerRef.current = window.setTimeout(() => setCopied(false), 1_500);
  };
  return <button type="button" onClick={() => void copy()} aria-label={copied ? "Copied" : "Copy message"} title={copied ? "Copied" : "Copy"}>
    {copied ? <Check size={16} weight="bold" aria-hidden="true" /> : <Copy size={16} aria-hidden="true" />}
  </button>;
}

interface AssistantActionsProps {
  messageId: string;
  content: string;
  createdAt: string;
  disabled?: boolean;
  onRetry: () => void;
}

export function AskRavenMessageActions({ messageId, content, createdAt, disabled, onRetry }: AssistantActionsProps) {
  const preferences = readSpeechPreferences();
  const [mode, setMode] = useState<"idle" | "loading" | "browser" | "gemini">("idle");
  const [paused, setPaused] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const requestVersionRef = useRef(0);
  const ownerRef = useRef(`message-${messageId}`);
  const outputVoice = preferences.outputProvider === "gemini" ? getGeminiVoice(preferences.voice) : preferences.voice;
  const cacheKey = `${messageId}:${outputVoice || "default"}`;
  const supportsBrowserSpeech = typeof window !== "undefined" && "speechSynthesis" in window;

  const stop = () => {
    requestVersionRef.current += 1;
    audioRef.current?.pause();
    audioRef.current = null;
    if (supportsBrowserSpeech) window.speechSynthesis.cancel();
    if (activePlayback?.owner === ownerRef.current) activePlayback = null;
    setMode("idle");
    setPaused(false);
  };

  useEffect(() => () => {
    requestVersionRef.current += 1;
    audioRef.current?.pause();
    if (activePlayback?.owner === ownerRef.current) {
      if (typeof window !== "undefined" && "speechSynthesis" in window) window.speechSynthesis.cancel();
      activePlayback = null;
    }
  }, []);

  const read = async () => {
    activePlayback?.stop();
    setError(null);
    const requestVersion = ++requestVersionRef.current;
    activePlayback = { owner: ownerRef.current, stop };
    try {
      if (preferences.outputProvider === "browser") {
        if (!supportsBrowserSpeech) throw new Error("Browser speech synthesis is not available here.");
        const utterance = new SpeechSynthesisUtterance(content);
        const voice = window.speechSynthesis.getVoices().find((item) => item.voiceURI === preferences.voice);
        if (voice) utterance.voice = voice;
        utterance.onend = () => { if (requestVersion === requestVersionRef.current) stop(); };
        utterance.onerror = () => { if (requestVersion === requestVersionRef.current) { stop(); setError("The browser could not read this response aloud."); } };
        window.speechSynthesis.speak(utterance);
        setMode("browser");
        return;
      }

      setMode("loading");
      let url = audioCache.get(cacheKey);
      if (!url) {
        const result = await synthesizeGeminiSpeech(content.slice(0, 4_000), outputVoice);
        if (requestVersion !== requestVersionRef.current) return;
        const bytes = Uint8Array.from(atob(result.audioBase64), (character) => character.charCodeAt(0));
        url = URL.createObjectURL(new Blob([bytes], { type: result.mimeType }));
        cacheAudio(cacheKey, url);
        if (content.length > 4_000) setError("Gemini read aloud covers the first 4,000 characters of this response.");
      }
      if (requestVersion !== requestVersionRef.current) return;
      const audio = new Audio(url);
      audioRef.current = audio;
      audio.onended = stop;
      audio.onerror = () => { stop(); setError("Gemini speech audio could not be played."); };
      await audio.play();
      if (requestVersion === requestVersionRef.current) setMode("gemini");
    } catch (reason) {
      if (requestVersion !== requestVersionRef.current) return;
      stop();
      setError(reason instanceof Error ? reason.message : "Read aloud could not start.");
    }
  };

  const togglePause = () => {
    if (mode === "browser") {
      const wasPaused = window.speechSynthesis.paused;
      if (wasPaused) window.speechSynthesis.resume(); else window.speechSynthesis.pause();
      setPaused(!wasPaused);
      return;
    }
    const audio = audioRef.current;
    if (!audio) return;
    if (audio.paused) { void audio.play(); setPaused(false); } else { audio.pause(); setPaused(true); }
  };

  return <div className={styles.wrapper}>
    <div className={styles.actions} aria-label="Response actions">
      <CopyAction content={content} />
      {preferences.readAloudEnabled ? mode === "idle" ? <button type="button" onClick={() => void read()} aria-label="Read response aloud" title="Read aloud"><SpeakerHigh size={16} aria-hidden="true" /></button>
        : mode === "loading" ? <span className={styles.loading} role="status" aria-label="Preparing audio"><CircleNotch size={16} aria-hidden="true" /></span>
          : <><button type="button" onClick={togglePause} aria-label={paused ? "Resume reading" : "Pause reading"} title={paused ? "Resume" : "Pause"}>{paused ? <Play size={16} weight="fill" aria-hidden="true" /> : <Pause size={16} weight="fill" aria-hidden="true" />}</button>
            <button type="button" onClick={stop} aria-label="Stop reading" title="Stop"><StopIcon size={16} weight="fill" aria-hidden="true" /></button></> : null}
      <button type="button" disabled={disabled} onClick={onRetry} aria-label="Retry response" title="Retry response"><ArrowClockwise size={16} aria-hidden="true" /></button>
      <ChatMessageTimestamp createdAt={createdAt} />
    </div>
    {error ? <span className={styles.error} role="alert">{error}</span> : null}
  </div>;
}

export function AskRavenUserMessageActions({ content, createdAt, disabled, onRetry, onEdit }: { content: string; createdAt: string; disabled?: boolean; onRetry: () => void; onEdit: () => void }) {
  return <div className={`${styles.actions} ${styles.userActions}`} aria-label="Message actions">
    <ChatMessageTimestamp createdAt={createdAt} />
    <button type="button" disabled={disabled} onClick={onRetry} aria-label="Ask again" title="Ask again"><ArrowClockwise size={15} aria-hidden="true" /></button>
    <button type="button" disabled={disabled} onClick={onEdit} aria-label="Edit and send again" title="Edit"><PencilSimple size={15} aria-hidden="true" /></button>
    <CopyAction content={content} />
  </div>;
}
