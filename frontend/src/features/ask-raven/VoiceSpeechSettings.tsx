import { useEffect, useRef, useState } from "react";
import { synthesizeGeminiSpeech } from "../../api/speech";
import { defaultGeminiVoice, geminiVoiceNames, getGeminiVoice, type SpeechPreferences, type SpeechOutputProvider } from "./speechPreferences";
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
const previewText = "This is a preview of the selected RAVEN voice.";
type MicrophoneTestState = "idle" | "requesting" | "running";
type PreviewState = "idle" | "loading" | "playing";

export function VoiceSpeechSettings({
  preferences,
  onChange,
  geminiConfigured,
  disabled = false,
}: {
  preferences: SpeechPreferences;
  onChange: (preferences: SpeechPreferences) => void;
  geminiConfigured: boolean;
  disabled?: boolean;
}) {
  const [voices, setVoices] = useState<SpeechSynthesisVoice[]>([]);
  const [devices, setDevices] = useState<MediaDeviceInfo[]>([]);
  const [status, setStatus] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [level, setLevel] = useState(0);
  const [microphoneTestState, setMicrophoneTestState] = useState<MicrophoneTestState>("idle");
  const [previewState, setPreviewState] = useState<PreviewState>("idle");
  const selectedVoice = preferences.outputProvider === "gemini" ? getGeminiVoice(preferences.voice) : preferences.voice;
  const streamRef = useRef<MediaStream | null>(null);
  const audioContextRef = useRef<AudioContext | null>(null);
  const meterRef = useRef<number | null>(null);
  const microphoneTestVersionRef = useRef(0);
  const previewVersionRef = useRef(0);
  const previewAudioRef = useRef<HTMLAudioElement | null>(null);
  const previewUrlRef = useRef<string | null>(null);
  const mountedRef = useRef(true);

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

  const releaseMicrophoneResources = () => {
    if (meterRef.current !== null) window.clearInterval(meterRef.current);
    meterRef.current = null;
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
    if (audioContextRef.current) void audioContextRef.current.close().catch(() => undefined);
    audioContextRef.current = null;
  };

  const stopTest = (announce = true) => {
    microphoneTestVersionRef.current += 1;
    const wasActive = microphoneTestState !== "idle";
    releaseMicrophoneResources();
    setLevel(0);
    setMicrophoneTestState("idle");
    if (announce && wasActive) setStatus("Microphone test stopped.");
  };

  const releasePreviewAudio = () => {
    const audio = previewAudioRef.current;
    if (audio) {
      audio.onended = null;
      audio.onerror = null;
      audio.pause();
      audio.removeAttribute("src");
      audio.load();
      previewAudioRef.current = null;
    }
    if (previewUrlRef.current) URL.revokeObjectURL(previewUrlRef.current);
    previewUrlRef.current = null;
  };

  const finishPreview = (version: number) => {
    if (version !== previewVersionRef.current) return;
    releasePreviewAudio();
    setPreviewState("idle");
  };

  const stopPreview = () => {
    previewVersionRef.current += 1;
    if ("speechSynthesis" in window) window.speechSynthesis.cancel();
    releasePreviewAudio();
    setPreviewState("idle");
  };

  const update = (changes: Partial<SpeechPreferences>) => {
    if (previewState !== "idle") stopPreview();
    onChange({ ...preferences, ...changes });
    setError(null);
    setStatus("");
  };

  const changeOutputProvider = (provider: SpeechOutputProvider) => {
    if (provider === preferences.outputProvider) return;
    update({ outputProvider: provider, voice: provider === "gemini" ? defaultGeminiVoice : "" });
  };

  useEffect(() => {
    // StrictMode intentionally runs an extra setup/cleanup cycle in development.
    // Re-mark the live instance on setup so async Gemini previews aren't mistaken
    // for responses from an unmounted page after that verification cycle.
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
      microphoneTestVersionRef.current += 1;
      previewVersionRef.current += 1;
      releaseMicrophoneResources();
      if ("speechSynthesis" in window) window.speechSynthesis.cancel();
      releasePreviewAudio();
    };
  }, []);

  const testMicrophone = async () => {
    stopTest(false);
    setError(null);
    setStatus("");
    setMicrophoneTestState("requesting");
    const testVersion = microphoneTestVersionRef.current;
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: preferences.deviceId ? { deviceId: { exact: preferences.deviceId } } : true,
      });
      if (testVersion !== microphoneTestVersionRef.current) {
        stream.getTracks().forEach((track) => track.stop());
        return;
      }
      streamRef.current = stream;
      const availableDevices = await navigator.mediaDevices.enumerateDevices();
      if (testVersion !== microphoneTestVersionRef.current) {
        stream.getTracks().forEach((track) => track.stop());
        return;
      }
      setDevices(availableDevices.filter((item) => item.kind === "audioinput"));
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
        if (testVersion !== microphoneTestVersionRef.current) return;
        analyser.getByteTimeDomainData(samples);
        const rms = Math.sqrt(samples.reduce((sum, sample) => sum + ((sample - 128) / 128) ** 2, 0) / samples.length);
        setLevel(Math.min(1, rms * 7));
      }, 100);
      setMicrophoneTestState("running");
      setStatus("Microphone is ready. Speak to check the input level.");
    } catch {
      if (testVersion !== microphoneTestVersionRef.current) return;
      releaseMicrophoneResources();
      setLevel(0);
      setMicrophoneTestState("idle");
      setError("Could not access this microphone. Check browser permission and device connection.");
    }
  };

  const previewVoice = async () => {
    if (previewState === "playing") {
      stopPreview();
      return;
    }
    if (previewState !== "idle" || disabled) return;
    const version = ++previewVersionRef.current;
    setError(null);
    setPreviewState("loading");
    try {
      if (preferences.outputProvider === "browser") {
        if (!("speechSynthesis" in window)) throw new Error("Speech playback is not available in this browser.");
        window.speechSynthesis.cancel();
        const utterance = new SpeechSynthesisUtterance(previewText);
        const voice = voices.find((item) => item.voiceURI === preferences.voice);
        if (voice) utterance.voice = voice;
        utterance.onstart = () => {
          if (version !== previewVersionRef.current) return;
          setPreviewState("playing");
        };
        utterance.onend = () => finishPreview(version);
        utterance.onerror = () => {
          if (version !== previewVersionRef.current) return;
          setPreviewState("idle");
          setError("The selected browser voice could not be played.");
        };
        window.speechSynthesis.speak(utterance);
      } else {
        const result = await synthesizeGeminiSpeech(previewText, selectedVoice || defaultGeminiVoice);
        if (!mountedRef.current || version !== previewVersionRef.current) return;
        const url = URL.createObjectURL(new Blob([Uint8Array.from(atob(result.audioBase64), (character) => character.charCodeAt(0))], { type: result.mimeType }));
        previewUrlRef.current = url;
        const audio = new Audio(url);
        previewAudioRef.current = audio;
        audio.onended = () => finishPreview(version);
        audio.onerror = () => {
          if (version !== previewVersionRef.current) return;
          releasePreviewAudio();
          setPreviewState("idle");
          setError("Gemini could not play the voice preview.");
        };
        await audio.play();
        if (version !== previewVersionRef.current) return;
        setPreviewState("playing");
      }
    } catch (reason) {
      if (version !== previewVersionRef.current) return;
      releasePreviewAudio();
      setPreviewState("idle");
      setError(reason instanceof Error ? reason.message : "The voice preview could not be played.");
    }
  };

  return <div className={styles.settings}>
    <section className={styles.group} aria-labelledby="speech-recognition-heading">
      <h3 id="speech-recognition-heading">Dictation</h3>
      <fieldset>
        <legend>Speech recognition</legend>
        <label className={!geminiConfigured ? styles.unavailable : undefined}><input type="radio" name="speech-recognition-provider" checked={preferences.recognitionProvider === "geminiLive"} disabled={!geminiConfigured || disabled} onChange={() => update({ recognitionProvider: "geminiLive" })} /><span><strong>Gemini Transcribe Live {geminiConfigured ? <small className={styles.recommended}>Recommended</small> : null}</strong><small>{geminiConfigured ? "Multilingual live transcription through Gemini. Microphone audio is sent to Google; free-tier requests may be used to improve Google products." : "Not available on this RAVEN server."}</small></span></label>
        <label><input type="radio" name="speech-recognition-provider" checked={preferences.recognitionProvider === "browser"} disabled={disabled} onChange={() => update({ recognitionProvider: "browser" })} /><span><strong>Browser recognition <small className={styles.fallback}>Fallback</small></strong><small>Uses the browser speech-recognition service when Gemini is unavailable or not preferred.</small></span></label>
      </fieldset>
      <div className={styles.controls}>
        <label>Language
          <span className={styles.selectWrap}><select value={preferences.language} disabled={disabled} onChange={(event) => update({ language: event.target.value })}>{languageOptions.map(([value, label]) => <option value={value} key={value || "auto"}>{label}</option>)}</select></span>
        </label>
        <label>Microphone
          <span className={styles.deviceControl}><span className={`${styles.selectWrap} ${styles.selectGrow}`}><select value={preferences.deviceId} disabled={disabled} onChange={(event) => { stopTest(false); update({ deviceId: event.target.value }); }}>
            <option value="">System default</option>
            {devices.filter((device) => device.deviceId && device.deviceId !== "default").map((device, index) => <option key={device.deviceId} value={device.deviceId}>{device.label || `Microphone ${index + 1}`}</option>)}
          </select></span><button type="button" aria-pressed={microphoneTestState !== "idle"} disabled={disabled && microphoneTestState === "idle"} onClick={() => microphoneTestState === "idle" ? void testMicrophone() : stopTest()}>{microphoneTestState === "idle" ? "Test microphone" : "Stop test"}</button></span>
        </label>
      </div>
      {preferences.recognitionProvider === "browser" ? <small className={styles.note}>Browser recognition uses the system default microphone. The selected device is used for Gemini capture and microphone testing.</small> : null}
      <div className={styles.meter} role="meter" aria-label="Microphone input level" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(level * 100)}><span style={{ width: `${microphoneTestState === "running" ? Math.max(2, level * 100) : 0}%` }} /></div>
      {microphoneTestState !== "idle" ? <small className={styles.testStatus} role="status">{microphoneTestState === "requesting" ? "Waiting for microphone permission…" : "Testing microphone…"}</small> : null}
    </section>

    <section className={styles.group} aria-labelledby="read-aloud-heading">
      <h3 id="read-aloud-heading">Read aloud</h3>
      <fieldset>
        <legend>Speech provider</legend>
        <label className={!geminiConfigured ? styles.unavailable : undefined}><input type="radio" name="speech-output-provider" checked={preferences.outputProvider === "gemini"} disabled={!geminiConfigured || disabled} onChange={() => changeOutputProvider("gemini")} /><span><strong>Gemini TTS {geminiConfigured ? <small className={styles.recommended}>Recommended</small> : null}</strong><small>{geminiConfigured ? "Gemini-generated speech. Response text is sent to Google; free-tier requests may be used to improve Google products." : "Not available on this RAVEN server."}</small></span></label>
        <label><input type="radio" name="speech-output-provider" checked={preferences.outputProvider === "browser"} disabled={disabled} onChange={() => changeOutputProvider("browser")} /><span><strong>Browser speech synthesis <small className={styles.fallback}>Fallback</small></strong><small>Uses voices installed for this browser or system when Gemini is unavailable or not preferred.</small></span></label>
      </fieldset>
      <label className={styles.voiceControl}>Voice
        <span className={styles.deviceControl}><span className={`${styles.selectWrap} ${styles.selectGrow}`}><select value={selectedVoice} disabled={disabled} onChange={(event) => update({ voice: event.target.value })}>
          <option value="">Default voice</option>
          {preferences.outputProvider === "browser" ? voices.map((voice) => <option value={voice.voiceURI} key={voice.voiceURI}>{voice.name} · {voice.lang}</option>)
            : geminiVoiceNames.map((voice) => <option value={voice} key={voice}>{voice}</option>)}
        </select></span><button type="button" onClick={() => void previewVoice()} disabled={previewState === "loading" || (disabled && previewState !== "playing")} aria-label={previewState === "playing" ? "Stop voice preview" : previewState === "loading" ? "Preparing voice preview" : "Preview voice"}>{previewState === "playing" ? "Stop preview" : previewState === "loading" ? "Preparing…" : "Preview voice"}</button></span>
      </label>
      {previewState !== "idle" ? <div className={`${styles.previewStatus} ${previewState === "playing" ? styles.previewStatusPlaying : styles.previewStatusLoading}`} role="status"><span aria-hidden="true" /><small>{previewState === "loading" ? preferences.outputProvider === "gemini" ? "Generating Gemini voice preview…" : "Starting browser voice preview…" : "Playing voice preview…"}</small></div> : null}
      <label className={styles.toggle}><input type="checkbox" checked={preferences.readAloudEnabled} disabled={disabled} onChange={(event) => update({ readAloudEnabled: event.target.checked })} /><span>Show Read aloud on Ask RAVEN responses</span></label>
    </section>
    {status ? <p className={styles.status} role="status">{status}</p> : null}
    {error ? <p className={styles.error} role="alert">{error}</p> : null}
  </div>;
}
