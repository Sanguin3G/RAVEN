import { useEffect, useRef, useState } from "react";
import { CaretDown, Microphone } from "@phosphor-icons/react";
import { readSpeechPreferences, saveSpeechPreferences, type SpeechPreferences } from "./speechPreferences";
import styles from "./ask-raven.module.css";

interface Props { supported: boolean; disabled: boolean; onStart: (preferences: SpeechPreferences) => void; }
const languages = [["", "Automatic"], ["en-US", "English"], ["vi-VN", "Tiếng Việt"], ["ja-JP", "Japanese"], ["ko-KR", "Korean"], ["zh-CN", "Chinese"]];

export function SpeechDeviceMenu({ supported, disabled, onStart }: Props) {
  const [open, setOpen] = useState(false);
  const anchorRef = useRef<HTMLDivElement | null>(null);
  const triggerRef = useRef<HTMLButtonElement | null>(null);
  const [preferences, setPreferences] = useState(readSpeechPreferences);
  const [devices, setDevices] = useState<MediaDeviceInfo[]>([]);
  const [level, setLevel] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const audioRef = useRef<AudioContext | null>(null);
  const timerRef = useRef<number | null>(null);

  const save = (next: SpeechPreferences) => { setPreferences(next); saveSpeechPreferences(next); };
  const stopTest = () => {
    if (timerRef.current !== null) window.clearInterval(timerRef.current);
    timerRef.current = null;
    streamRef.current?.getTracks().forEach((track) => track.stop()); streamRef.current = null;
    if (audioRef.current) void audioRef.current.close(); audioRef.current = null;
    setLevel(0);
  };
  useEffect(() => () => {
    if (timerRef.current !== null) window.clearInterval(timerRef.current);
    streamRef.current?.getTracks().forEach((track) => track.stop());
    if (audioRef.current) void audioRef.current.close();
  }, []);
  useEffect(() => {
    if (!open) return;
    const close = () => { setOpen(false); stopTest(); };
    const closeOnOutsidePointer = (event: PointerEvent) => {
      if (!anchorRef.current?.contains(event.target as Node)) close();
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      event.preventDefault();
      close();
      triggerRef.current?.focus();
    };
    document.addEventListener("pointerdown", closeOnOutsidePointer);
    document.addEventListener("keydown", closeOnEscape);
    return () => {
      document.removeEventListener("pointerdown", closeOnOutsidePointer);
      document.removeEventListener("keydown", closeOnEscape);
    };
  }, [open]);
  const toggle = () => {
    if (open) stopTest();
    else if (navigator.mediaDevices?.enumerateDevices) void navigator.mediaDevices.enumerateDevices().then((items) => setDevices(items.filter((item) => item.kind === "audioinput"))).catch(() => undefined);
    setOpen(!open);
  };
  const testMicrophone = async () => {
    stopTest(); setError(null);
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: preferences.deviceId ? { deviceId: { exact: preferences.deviceId } } : true });
      streamRef.current = stream;
      setDevices((await navigator.mediaDevices.enumerateDevices()).filter((item) => item.kind === "audioinput"));
      const audio = new AudioContext(); audioRef.current = audio;
      const analyser = audio.createAnalyser(); analyser.fftSize = 256;
      const mutedOutput = audio.createGain(); mutedOutput.gain.value = 0;
      audio.createMediaStreamSource(stream).connect(analyser);
      analyser.connect(mutedOutput); mutedOutput.connect(audio.destination);
      const samples = new Uint8Array(analyser.fftSize);
      timerRef.current = window.setInterval(() => {
        analyser.getByteTimeDomainData(samples);
        const rms = Math.sqrt(samples.reduce((sum, sample) => sum + ((sample - 128) / 128) ** 2, 0) / samples.length);
        setLevel(Math.min(1, rms * 7));
      }, 100);
    } catch { setError("Could not access this microphone."); }
  };

  return <div className={styles.speechMenuAnchor} ref={anchorRef}>
    <button type="button" className={styles.speechButton} aria-label="Dictate question" disabled={disabled || !supported} onClick={() => { stopTest(); setOpen(false); onStart(preferences); }} title={supported ? "Dictate into the question" : "Selected speech recognition is not supported here"}><Microphone size={17} /></button>
    <button type="button" ref={triggerRef} className={styles.speechMenuTrigger} aria-label="Microphone options" aria-expanded={open} onClick={toggle}><CaretDown size={11} /></button>
    {open ? <div className={styles.speechDevicePanel} aria-label="Microphone options">
      <strong>Microphone</strong>
      <small>{preferences.recognitionProvider === "browser" ? "Browser recognition uses the system default microphone." : "Gemini uses the microphone selected here."}</small>
      {!supported ? <small role="status">{preferences.recognitionProvider === "browser" ? "Browser speech recognition is not supported here. You can still type your question." : "Gemini transcription is not supported in this browser. You can still type your question."}</small> : null}
      <label>Input device
        <select value={preferences.deviceId} onChange={(event) => { stopTest(); save({ ...preferences, deviceId: event.target.value }); }}>
          <option value="">System default</option>
          {devices.filter((device) => device.deviceId && device.deviceId !== "default").map((device, index) => <option value={device.deviceId} key={device.deviceId}>{device.label || `Microphone ${index + 1}`}</option>)}
        </select>
      </label>
      <div className={styles.speechLevel} aria-label="Microphone input level"><span style={{ width: `${Math.max(2, level * 100)}%` }} /></div>
      <button type="button" onClick={() => void testMicrophone()}>{streamRef.current ? "Restart test" : "Test microphone"}</button>
      <label>Recognition language
        <select value={preferences.language} onChange={(event) => save({ ...preferences, language: event.target.value })}>
          {languages.map(([value, label]) => <option value={value} key={value || "automatic"}>{label}</option>)}
        </select>
      </label>
      <small>Recognition: {preferences.recognitionProvider === "browser" ? "Browser recognition" : "Gemini Transcribe Live"}</small>
      {error ? <small role="alert">{error}</small> : null}
    </div> : null}
  </div>;
}
