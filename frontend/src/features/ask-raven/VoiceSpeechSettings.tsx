import { useEffect, useRef, useState } from "react";
import { synthesizeGeminiSpeech } from "../../api/speech";
import { readSpeechPreferences, saveSpeechPreferences, type SpeechPreferences } from "./speechPreferences";
import styles from "./voice-speech-settings.module.css";

const languageOptions = [
  ["", "Automatic"],
  ["en-US", "English"],
  ["vi-VN", "Vietnamese"],
  ["ja-JP", "Japanese"],
  ["ko-KR", "Korean"],
  ["zh-CN", "Chinese"],
  ["fr-FR", "French"],
  ["de-DE", "German"],
  ["es-ES", "Spanish"],
] as const;
const geminiVoices = ["Kore", "Puck", "Charon", "Fenrir", "Aoede", "Leda", "Orus", "Zephyr"];

export function VoiceSpeechSettings({ geminiConfigured }: { geminiConfigured: boolean }) {
  const [preferences, setPreferences] = useState<SpeechPreferences>(readSpeechPreferences);
  const [voices, setVoices] = useState<SpeechSynthesisVoice[]>([]);
  const [devices, setDevices] = useState<MediaDeviceInfo[]>([]);
  const [status, setStatus] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [level, setLevel] = useState(0);
  const streamRef = useRef<MediaStream | null>(null);
  const audioContextRef = useRef<AudioContext | null>(null);
  const meterRef = useRef<number | null>(null);

  const update = (changes: Partial<SpeechPreferences>) => {
    setPreferences((current) => {
      const next = { ...current, ...changes };
      saveSpeechPreferences(next);
      return next;
    });
    setError(null);
    setStatus("");
  };

  useEffect(() => {
    if (!("speechSynthesis" in window)) return;
    const refresh = () => setVoices(window.speechSynthesis.getVoices());
    refresh();
    window.speechSynthesis.addEventListener("voiceschanged", refresh);
    return () => {
      window.speechSynthesis.removeEventListener("voiceschanged", refresh);
      window.speechSynthesis.cancel();
    };
  }, []);

  useEffect(() => {
    let active = true;
    void navigator.mediaDevices?.enumerateDevices().then((items) => {
      if (active) setDevices(items.filter((item) => item.kind === "audioinput"));
    }).catch(() => undefined);
    return () => { active = false; };
  }, []);

  const stopTest = () => {
    if (meterRef.current !== null) window.clearInterval(meterRef.current);
    meterRef.current = null;
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
    if (audioContextRef.current) void audioContextRef.current.close();
    audioContextRef.current = null;
    setLevel(0);
  };

  useEffect(() => () => stopTest(), []);

  const testMicrophone = async () => {
    stopTest();
    setError(null);
    setStatus("");
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: preferences.deviceId ? { deviceId: { exact: preferences.deviceId } } : true,
      });
      streamRef.current = stream;
      setDevices((await navigator.mediaDevices.enumerateDevices()).filter((item) => item.kind === "audioinput"));
      const context = new AudioContext();
      audioContextRef.current = context;
      const analyser = context.createAnalyser();
      analyser.fftSize = 256;
      const mutedOutput = context.createGain();
      mutedOutput.gain.value = 0;
      context.createMediaStreamSource(stream).connect(analyser);
      analyser.connect(mutedOutput);
      mutedOutput.connect(context.destination);
      const samples = new Uint8Array(analyser.fftSize);
      meterRef.current = window.setInterval(() => {
        analyser.getByteTimeDomainData(samples);
        const rms = Math.sqrt(samples.reduce((sum, sample) => sum + ((sample - 128) / 128) ** 2, 0) / samples.length);
        setLevel(Math.min(1, rms * 7));
      }, 100);
      setStatus("Microphone is ready. Speak to check the input level.");
    } catch {
      setError("Could not access this microphone. Check browser permission and device connection.");
    }
  };

  const previewVoice = async () => {
    setError(null);
    setStatus("");
    try {
      if (preferences.outputProvider === "browser") {
        if (!("speechSynthesis" in window)) throw new Error("Speech playback is not available in this browser.");
        window.speechSynthesis.cancel();
        const utterance = new SpeechSynthesisUtterance("This is a preview of the selected RAVEN voice.");
        const voice = voices.find((item) => item.voiceURI === preferences.voice);
        if (voice) utterance.voice = voice;
        utterance.onerror = () => setError("The selected browser voice could not be played.");
        window.speechSynthesis.speak(utterance);
      } else {
        const result = await synthesizeGeminiSpeech("This is a preview of the selected RAVEN voice.", preferences.voice || "Kore");
        const url = URL.createObjectURL(new Blob([Uint8Array.from(atob(result.audioBase64), (character) => character.charCodeAt(0))], { type: result.mimeType }));
        const audio = new Audio(url);
        audio.onended = () => URL.revokeObjectURL(url);
        audio.onerror = () => { URL.revokeObjectURL(url); setError("Gemini could not play the voice preview."); };
        try { await audio.play(); } catch (reason) { URL.revokeObjectURL(url); throw reason; }
      }
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "The voice preview could not be played.");
    }
  };

  return <div className={styles.settings}>
    <section className={styles.group} aria-labelledby="speech-recognition-heading">
      <h3 id="speech-recognition-heading">Dictation</h3>
      <fieldset>
        <legend>Speech recognition</legend>
        <label><input type="radio" name="speech-recognition-provider" checked={preferences.recognitionProvider === "browser"} onChange={() => update({ recognitionProvider: "browser" })} /><span><strong>Browser recognition</strong><small>Uses the browser speech-recognition service.</small></span></label>
        <label className={!geminiConfigured ? styles.unavailable : undefined}><input type="radio" name="speech-recognition-provider" checked={preferences.recognitionProvider === "geminiLive"} disabled={!geminiConfigured} onChange={() => update({ recognitionProvider: "geminiLive" })} /><span><strong>Gemini Transcribe Live</strong><small>{geminiConfigured ? "Live multilingual transcription through Gemini. Microphone audio is sent to Google; free-tier requests may be used to improve Google products." : "Not available on this RAVEN server."}</small></span></label>
      </fieldset>
      <div className={styles.controls}>
        <label>Language
          <select value={preferences.language} onChange={(event) => update({ language: event.target.value })}>{languageOptions.map(([value, label]) => <option value={value} key={value || "auto"}>{label}</option>)}</select>
        </label>
        <label>Microphone
          <span className={styles.deviceControl}><select value={preferences.deviceId} onChange={(event) => update({ deviceId: event.target.value })}>
            <option value="">System default</option>
            {devices.filter((device) => device.deviceId && device.deviceId !== "default").map((device, index) => <option key={device.deviceId} value={device.deviceId}>{device.label || `Microphone ${index + 1}`}</option>)}
          </select><button type="button" onClick={() => void testMicrophone()}>Test</button></span>
        </label>
      </div>
      {preferences.recognitionProvider === "browser" ? <small className={styles.note}>Browser recognition uses the system default microphone. The selected device is used for Gemini capture and microphone testing.</small> : null}
      <div className={styles.meter} aria-label="Microphone input level"><span style={{ width: `${Math.max(2, level * 100)}%` }} /></div>
    </section>

    <section className={styles.group} aria-labelledby="read-aloud-heading">
      <h3 id="read-aloud-heading">Read aloud</h3>
      <fieldset>
        <legend>Speech provider</legend>
        <label><input type="radio" name="speech-output-provider" checked={preferences.outputProvider === "browser"} onChange={() => update({ outputProvider: "browser" })} /><span><strong>Browser speech synthesis</strong><small>Uses voices installed for this browser or system.</small></span></label>
        <label className={!geminiConfigured ? styles.unavailable : undefined}><input type="radio" name="speech-output-provider" checked={preferences.outputProvider === "gemini"} disabled={!geminiConfigured} onChange={() => update({ outputProvider: "gemini" })} /><span><strong>Gemini TTS</strong><small>{geminiConfigured ? "Gemini-generated speech. Response text is sent to Google; free-tier requests may be used to improve Google products." : "Not available on this RAVEN server."}</small></span></label>
      </fieldset>
      <label className={styles.voiceControl}>Voice
        <span className={styles.deviceControl}><select value={preferences.voice} onChange={(event) => update({ voice: event.target.value })}>
          <option value="">Default voice</option>
          {preferences.outputProvider === "browser" ? voices.map((voice) => <option value={voice.voiceURI} key={voice.voiceURI}>{voice.name} · {voice.lang}</option>)
            : geminiVoices.map((voice) => <option value={voice} key={voice}>{voice}</option>)}
        </select><button type="button" onClick={() => void previewVoice()}>Preview</button></span>
      </label>
      <label className={styles.toggle}><input type="checkbox" checked={preferences.readAloudEnabled} onChange={(event) => update({ readAloudEnabled: event.target.checked })} /><span>Show Read aloud on Ask RAVEN responses</span></label>
    </section>
    {status ? <p className={styles.status} role="status">{status}</p> : null}
    {error ? <p className={styles.error} role="alert">{error}</p> : null}
  </div>;
}
