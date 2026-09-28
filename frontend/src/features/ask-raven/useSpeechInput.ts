import { useEffect, useRef, useState } from "react";

interface RecognitionResult { isFinal: boolean; 0: { transcript: string } }
interface RecognitionEvent { results: ArrayLike<RecognitionResult> }
interface RecognitionInstance {
  continuous: boolean;
  interimResults: boolean;
  lang: string;
  onresult: ((event: RecognitionEvent) => void) | null;
  onerror: ((event: { error: string }) => void) | null;
  onend: (() => void) | null;
  start(): void;
  stop(): void;
  abort(): void;
}
type RecognitionConstructor = new () => RecognitionInstance;

function recognitionConstructor(): RecognitionConstructor | null {
  const browser = window as Window & { SpeechRecognition?: RecognitionConstructor; webkitSpeechRecognition?: RecognitionConstructor };
  return browser.SpeechRecognition ?? browser.webkitSpeechRecognition ?? null;
}

export function useSpeechInput(value: string, onChange: (value: string) => void) {
  const [supported] = useState(() => !!recognitionConstructor() && !!navigator.mediaDevices?.getUserMedia);
  const [listening, setListening] = useState(false);
  const [level, setLevel] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const recognitionRef = useRef<RecognitionInstance | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const audioRef = useRef<AudioContext | null>(null);
  const meterRef = useRef<number | null>(null);
  const originalRef = useRef("");
  const cancelledRef = useRef(false);

  const stopMeter = () => {
    if (meterRef.current !== null) window.clearInterval(meterRef.current);
    meterRef.current = null;
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
    if (audioRef.current) void audioRef.current.close();
    audioRef.current = null;
    setLevel(0);
  };

  const start = async (language: string) => {
    const Constructor = recognitionConstructor();
    if (!Constructor || !navigator.mediaDevices?.getUserMedia || listening) return;
    originalRef.current = value;
    cancelledRef.current = false;
    setError(null);
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      streamRef.current = stream;
      const audio = new AudioContext();
      audioRef.current = audio;
      const source = audio.createMediaStreamSource(stream);
      const analyser = audio.createAnalyser();
      analyser.fftSize = 256;
      source.connect(analyser);
      const samples = new Uint8Array(analyser.fftSize);
      meterRef.current = window.setInterval(() => {
        analyser.getByteTimeDomainData(samples);
        const rms = Math.sqrt(samples.reduce((sum, sample) => sum + ((sample - 128) / 128) ** 2, 0) / samples.length);
        setLevel(Math.min(1, rms * 7));
      }, 100);
      const recognition = new Constructor();
      recognitionRef.current = recognition;
      recognition.continuous = true;
      recognition.interimResults = true;
      if (language) recognition.lang = language;
      recognition.onresult = (event) => {
        if (cancelledRef.current) return;
        const spoken = Array.from(event.results).map((result) => result[0]?.transcript ?? "").join(" ").trim();
        onChange([originalRef.current.trimEnd(), spoken].filter(Boolean).join(" "));
      };
      recognition.onerror = (event) => {
        if (!cancelledRef.current) setError(event.error === "not-allowed" ? "Microphone permission was denied." : `Speech recognition stopped: ${event.error}.`);
      };
      recognition.onend = () => { setListening(false); stopMeter(); recognitionRef.current = null; };
      recognition.start();
      setListening(true);
    } catch (reason) {
      stopMeter();
      setError(reason instanceof DOMException && reason.name === "NotAllowedError" ? "Microphone permission was denied." : "Could not start browser speech recognition.");
    }
  };

  const finish = () => { recognitionRef.current?.stop(); setListening(false); stopMeter(); };
  const cancel = () => {
    cancelledRef.current = true;
    recognitionRef.current?.abort();
    onChange(originalRef.current);
    setListening(false);
    stopMeter();
  };

  useEffect(() => () => {
    cancelledRef.current = true;
    recognitionRef.current?.abort();
    if (meterRef.current !== null) window.clearInterval(meterRef.current);
    streamRef.current?.getTracks().forEach((track) => track.stop());
    if (audioRef.current) void audioRef.current.close();
  }, []);

  return { supported, listening, level, error, start, finish, cancel };
}
