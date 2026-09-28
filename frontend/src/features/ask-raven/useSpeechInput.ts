import { useEffect, useRef, useState } from "react";
import { createGeminiLiveToken } from "../../api/speech";
import { readSpeechPreferences, type SpeechPreferences } from "./speechPreferences";

interface RecognitionResult { isFinal: boolean; 0: { transcript: string } }
interface RecognitionEvent { results: ArrayLike<RecognitionResult> }
interface RecognitionInstance {
  continuous: boolean; interimResults: boolean; lang: string;
  onresult: ((event: RecognitionEvent) => void) | null;
  onerror: ((event: { error: string }) => void) | null;
  onend: (() => void) | null;
  start(): void; stop(): void; abort(): void;
}
type RecognitionConstructor = new () => RecognitionInstance;

function recognitionConstructor(): RecognitionConstructor | null {
  const browser = window as Window & { SpeechRecognition?: RecognitionConstructor; webkitSpeechRecognition?: RecognitionConstructor };
  return browser.SpeechRecognition ?? browser.webkitSpeechRecognition ?? null;
}

const pcmWorklet = `
class RavenPcmCapture extends AudioWorkletProcessor {
  constructor() { super(); this.pending = []; }
  process(inputs) {
    const input = inputs[0] && inputs[0][0];
    if (!input) return true;
    const ratio = sampleRate / 16000;
    for (let cursor = 0; cursor < input.length;) {
      const end = Math.min(input.length, Math.max(cursor + 1, Math.floor((Math.floor(cursor / ratio) + 1) * ratio)));
      let total = 0;
      for (let index = cursor; index < end; index++) total += input[index];
      this.pending.push(Math.max(-1, Math.min(1, total / (end - cursor))));
      cursor = end;
      if (this.pending.length >= 1600) {
        const samples = new Int16Array(this.pending.length);
        for (let index = 0; index < samples.length; index++) samples[index] = this.pending[index] < 0 ? this.pending[index] * 32768 : this.pending[index] * 32767;
        this.port.postMessage(samples.buffer, [samples.buffer]);
        this.pending = [];
      }
    }
    return true;
  }
}
registerProcessor("raven-pcm-capture", RavenPcmCapture);`;

function base64FromBuffer(buffer: ArrayBuffer) {
  const bytes = new Uint8Array(buffer);
  let binary = "";
  for (let offset = 0; offset < bytes.length; offset += 0x8000) binary += String.fromCharCode(...bytes.subarray(offset, offset + 0x8000));
  return btoa(binary);
}

export function useSpeechInput(value: string, onChange: (value: string) => void) {
  const preferences = readSpeechPreferences();
  const supported = typeof navigator !== "undefined" && !!navigator.mediaDevices?.getUserMedia &&
    (preferences.recognitionProvider === "browser"
      ? !!recognitionConstructor()
      : typeof window !== "undefined" && !!window.WebSocket && typeof AudioWorkletNode !== "undefined");
  const [listening, setListening] = useState(false);
  const [starting, setStarting] = useState(false);
  const [level, setLevel] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const recognitionRef = useRef<RecognitionInstance | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const audioRef = useRef<AudioContext | null>(null);
  const sourceRef = useRef<MediaStreamAudioSourceNode | null>(null);
  const analyserRef = useRef<AnalyserNode | null>(null);
  const workletRef = useRef<AudioWorkletNode | null>(null);
  const workletUrlRef = useRef<string | null>(null);
  const meterRef = useRef<number | null>(null);
  const socketRef = useRef<WebSocket | null>(null);
  const finishTimerRef = useRef<number | null>(null);
  const originalRef = useRef("");
  const finalizedRef = useRef<string[]>([]);
  const interimRef = useRef("");
  const cancelledRef = useRef(false);
  const finishingRef = useRef(false);
  const providerRef = useRef<SpeechPreferences["recognitionProvider"]>("browser");

  const applyTranscript = () => {
    const transcript = [...finalizedRef.current, interimRef.current].filter(Boolean).join(" ").trim();
    onChange([originalRef.current.trimEnd(), transcript].filter(Boolean).join(" "));
  };

  const stopCapture = () => {
    if (meterRef.current !== null) window.clearInterval(meterRef.current);
    meterRef.current = null;
    workletRef.current?.disconnect(); workletRef.current = null;
    sourceRef.current?.disconnect(); sourceRef.current = null;
    analyserRef.current?.disconnect(); analyserRef.current = null;
    streamRef.current?.getTracks().forEach((track) => track.stop()); streamRef.current = null;
    if (audioRef.current) void audioRef.current.close(); audioRef.current = null;
    if (workletUrlRef.current) URL.revokeObjectURL(workletUrlRef.current); workletUrlRef.current = null;
    setLevel(0);
  };

  const closeSocket = (sendEnd = false) => {
    if (finishTimerRef.current !== null) window.clearTimeout(finishTimerRef.current);
    finishTimerRef.current = null;
    const socket = socketRef.current;
    socketRef.current = null;
    if (socket) {
      socket.onclose = null; socket.onerror = null; socket.onmessage = null;
      if (sendEnd && socket.readyState === WebSocket.OPEN) {
        try { socket.send(JSON.stringify({ realtimeInput: { audioStreamEnd: true } })); } catch { /* The connection may already be closing. */ }
      }
      if (socket.readyState < WebSocket.CLOSING) socket.close();
    }
  };

  const cleanup = () => { stopCapture(); closeSocket(); };

  const fail = (message: string) => {
    cancelledRef.current = true;
    onChange(originalRef.current);
    recognitionRef.current?.abort(); recognitionRef.current = null;
    cleanup(); setListening(false); setStarting(false); setError(message);
  };

  const startMeter = (stream: MediaStream, context: AudioContext, source: MediaStreamAudioSourceNode) => {
    const analyser = context.createAnalyser(); analyser.fftSize = 256;
    const silentOutput = context.createGain(); silentOutput.gain.value = 0;
    source.connect(analyser); analyser.connect(silentOutput); silentOutput.connect(context.destination); analyserRef.current = analyser;
    const samples = new Uint8Array(analyser.fftSize);
    meterRef.current = window.setInterval(() => {
      analyser.getByteTimeDomainData(samples);
      const rms = Math.sqrt(samples.reduce((sum, sample) => sum + ((sample - 128) / 128) ** 2, 0) / samples.length);
      setLevel(Math.min(1, rms * 7));
    }, 100);
    streamRef.current = stream; audioRef.current = context; sourceRef.current = source;
  };

  const startBrowser = async (language: string) => {
    const Constructor = recognitionConstructor();
    if (!Constructor) throw new Error("Browser speech recognition is not available in this browser. Choose Gemini Transcribe Live in Voice & speech settings.");
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    streamRef.current = stream;
    const context = new AudioContext();
    const source = context.createMediaStreamSource(stream);
    startMeter(stream, context, source);
    const recognition = new Constructor(); recognitionRef.current = recognition;
    recognition.continuous = true; recognition.interimResults = true; if (language) recognition.lang = language;
    recognition.onresult = (event) => {
      if (cancelledRef.current) return;
      const spoken = Array.from(event.results).map((result) => result[0]?.transcript ?? "").join(" ").trim();
      interimRef.current = spoken; applyTranscript();
    };
    recognition.onerror = (event) => {
      if (!cancelledRef.current) fail(event.error === "not-allowed" ? "Microphone permission was denied." : `Browser recognition stopped: ${event.error}.`);
    };
    recognition.onend = () => {
      if (!cancelledRef.current) { setListening(false); stopCapture(); }
      recognitionRef.current = null;
    };
    recognition.start(); setListening(true);
  };

  const startGemini = async (preferences: SpeechPreferences) => {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: preferences.deviceId ? { deviceId: { exact: preferences.deviceId } } : true });
    streamRef.current = stream;
    const { token } = await createGeminiLiveToken(preferences.language);
    const url = new URL("wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent");
    url.searchParams.set("access_token", token);
    const socket = new WebSocket(url.toString()); socketRef.current = socket;
    await new Promise<void>((resolve, reject) => {
      const timeout = window.setTimeout(() => reject(new Error("Gemini transcription connection timed out.")), 15_000);
      socket.onerror = () => { window.clearTimeout(timeout); reject(new Error("Could not connect to Gemini Transcribe Live.")); };
      socket.onopen = () => {
        window.clearTimeout(timeout);
        socket.send(JSON.stringify({ setup: {
          model: "models/gemini-3.5-transcribe-live",
          generationConfig: { responseModalities: ["TEXT"] },
          inputAudioTranscription: { languageCodes: preferences.language ? [preferences.language] : [] },
        } }));
        resolve();
      };
    });
    socket.onerror = () => { if (!finishingRef.current && !cancelledRef.current) fail("Gemini transcription connection failed. Your original text was restored."); };
    socket.onclose = () => { if (!finishingRef.current && !cancelledRef.current) fail("Gemini transcription ended unexpectedly. Your original text was restored."); };
    socket.onmessage = (event) => {
      try {
        const message = JSON.parse(String(event.data)) as { serverContent?: { interimInputTranscription?: { text?: string }; inputTranscription?: { text?: string } } };
        const content = message.serverContent;
        if (content?.inputTranscription?.text) {
          finalizedRef.current.push(content.inputTranscription.text.trim()); interimRef.current = ""; applyTranscript();
        } else if (content?.interimInputTranscription?.text !== undefined) {
          interimRef.current = content.interimInputTranscription.text.trim(); applyTranscript();
        }
      } catch { /* Ignore non-transcription protocol frames. */ }
    };

    const context = new AudioContext();
    const workletUrl = URL.createObjectURL(new Blob([pcmWorklet], { type: "text/javascript" }));
    workletUrlRef.current = workletUrl;
    await context.audioWorklet.addModule(workletUrl);
    const source = context.createMediaStreamSource(stream);
    startMeter(stream, context, source);
    const worklet = new AudioWorkletNode(context, "raven-pcm-capture"); workletRef.current = worklet;
    const silent = context.createGain(); silent.gain.value = 0;
    source.connect(worklet); worklet.connect(silent); silent.connect(context.destination);
    worklet.port.onmessage = (event: MessageEvent<ArrayBuffer>) => {
      if (socket.readyState === WebSocket.OPEN && !finishingRef.current) {
        socket.send(JSON.stringify({ realtimeInput: { audio: { data: base64FromBuffer(event.data), mimeType: "audio/pcm;rate=16000" } } }));
      }
    };
    await context.resume();
  };

  const start = async (languageOrPreferences: string | SpeechPreferences) => {
    if (starting || listening) return;
    const preferences = typeof languageOrPreferences === "string"
      ? { ...readSpeechPreferences(), language: languageOrPreferences }
      : languageOrPreferences;
    originalRef.current = value; finalizedRef.current = []; interimRef.current = "";
    cancelledRef.current = false; finishingRef.current = false; providerRef.current = preferences.recognitionProvider;
    setError(null); setStarting(true);
    try {
      if (preferences.recognitionProvider === "geminiLive") await startGemini(preferences);
      else await startBrowser(preferences.language);
      setListening(true);
    } catch (reason) {
      const denied = reason instanceof DOMException && reason.name === "NotAllowedError";
      fail(denied ? "Microphone permission was denied." : reason instanceof Error ? reason.message : "Speech recognition could not start.");
    } finally { setStarting(false); }
  };

  const finish = () => {
    if (providerRef.current === "browser") {
      recognitionRef.current?.stop(); setListening(false); stopCapture(); return;
    }
    finishingRef.current = true;
    const socket = socketRef.current;
    if (socket?.readyState === WebSocket.OPEN) {
      try { socket.send(JSON.stringify({ realtimeInput: { audioStreamEnd: true } })); } catch { /* Close below. */ }
    }
    stopCapture(); setListening(false);
    finishTimerRef.current = window.setTimeout(() => { closeSocket(); finishingRef.current = false; }, 900);
  };

  const cancel = () => {
    cancelledRef.current = true; finishingRef.current = false;
    recognitionRef.current?.abort(); recognitionRef.current = null;
    onChange(originalRef.current); setListening(false); setStarting(false); setError(null); cleanup();
  };

  useEffect(() => () => {
    cancelledRef.current = true; recognitionRef.current?.abort(); recognitionRef.current = null; cleanup();
  }, []);

  return { supported, listening, starting, level, error, start, finish, cancel };
}
