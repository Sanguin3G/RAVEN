import { useEffect, useRef, useState } from "react";
import { CaretDown, Microphone } from "@phosphor-icons/react";
import styles from "./ask-raven.module.css";

const preferenceKey = "raven:speech-preferences";
interface Preferences { deviceId: string; language: string }
function readPreferences(): Preferences {
  try {
    const value = JSON.parse(localStorage.getItem(preferenceKey) ?? "{}");
    return { deviceId: typeof value.deviceId === "string" ? value.deviceId : "", language: typeof value.language === "string" ? value.language : "" };
  } catch { return { deviceId: "", language: "" }; }
}

interface Props { supported: boolean; disabled: boolean; onStart: (language: string) => void; }
export function SpeechDeviceMenu({ supported, disabled, onStart }: Props) {
  const [open, setOpen] = useState(false);
  const [preferences, setPreferences] = useState(readPreferences);
  const [devices, setDevices] = useState<MediaDeviceInfo[]>([]);
  const [level, setLevel] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const audioRef = useRef<AudioContext | null>(null);
  const timerRef = useRef<number | null>(null);

  const save = (next: Preferences) => {
    setPreferences(next);
    try { localStorage.setItem(preferenceKey, JSON.stringify(next)); } catch { /* Device preferences are optional. */ }
  };
  const stopTest = () => {
    if (timerRef.current !== null) window.clearInterval(timerRef.current);
    timerRef.current = null;
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
    if (audioRef.current) void audioRef.current.close();
    audioRef.current = null;
    setLevel(0);
  };
  useEffect(() => () => {
    if (timerRef.current !== null) window.clearInterval(timerRef.current);
    streamRef.current?.getTracks().forEach((track) => track.stop());
    if (audioRef.current) void audioRef.current.close();
  }, []);
  const toggle = () => {
    if (open) stopTest();
    else if (navigator.mediaDevices?.enumerateDevices) void navigator.mediaDevices.enumerateDevices().then((items) => setDevices(items.filter((item) => item.kind === "audioinput"))).catch(() => undefined);
    setOpen(!open);
  };
  const testMicrophone = async () => {
    stopTest();
    setError(null);
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: preferences.deviceId ? { deviceId: { ideal: preferences.deviceId } } : true });
      streamRef.current = stream;
      setDevices((await navigator.mediaDevices.enumerateDevices()).filter((item) => item.kind === "audioinput"));
      const audio = new AudioContext();
      audioRef.current = audio;
      const analyser = audio.createAnalyser();
      analyser.fftSize = 256;
      audio.createMediaStreamSource(stream).connect(analyser);
      const samples = new Uint8Array(analyser.fftSize);
      timerRef.current = window.setInterval(() => {
        analyser.getByteTimeDomainData(samples);
        const rms = Math.sqrt(samples.reduce((sum, sample) => sum + ((sample - 128) / 128) ** 2, 0) / samples.length);
        setLevel(Math.min(1, rms * 7));
      }, 100);
    } catch { setError("Could not access this microphone."); }
  };

  return <div className={styles.speechMenuAnchor}>
    <button type="button" className={styles.speechButton} aria-label="Dictate question" disabled={disabled || !supported} onClick={() => { stopTest(); setOpen(false); onStart(preferences.language); }} title={supported ? "Dictate into the question" : "Browser speech recognition is unavailable"}><Microphone size={17} /></button>
    <button type="button" className={styles.speechMenuTrigger} aria-label="Microphone options" aria-expanded={open} onClick={toggle}><CaretDown size={11} /></button>
    {open ? <div className={styles.speechDevicePanel} aria-label="Microphone options">
      <strong>Microphone</strong>
      <small>Browser dictation uses your system default microphone.</small>
      <label>Test input
        <select value={preferences.deviceId} onChange={(event) => { stopTest(); save({ ...preferences, deviceId: event.target.value }); }}>
          <option value="">System default</option>
          {devices.filter((device) => device.deviceId && device.deviceId !== "default").map((device, index) => <option value={device.deviceId} key={device.deviceId}>{device.label || `Microphone ${index + 1}`}</option>)}
        </select>
      </label>
      <div className={styles.speechLevel} aria-label="Microphone input level"><span style={{ width: `${Math.max(2, level * 100)}%` }} /></div>
      <button type="button" onClick={() => void testMicrophone()}>{streamRef.current ? "Restart test" : "Test microphone"}</button>
      <label>Recognition language
        <select value={preferences.language} onChange={(event) => save({ ...preferences, language: event.target.value })}>
          <option value="">Automatic</option><option value="en-US">English</option><option value="vi-VN">Tiếng Việt</option>
        </select>
      </label>
      {error ? <small role="alert">{error}</small> : null}
    </div> : null}
  </div>;
}
