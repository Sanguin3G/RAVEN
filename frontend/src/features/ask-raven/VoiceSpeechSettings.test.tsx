import { act, render, screen, waitFor } from "@testing-library/react";
import { StrictMode } from "react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it, vi } from "vitest";
import { synthesizeGeminiSpeech, type GeminiSpeechAudio } from "../../api/speech";
import { ApiError } from "../../api/client";
import { defaultSpeechPreferences } from "./speechPreferences";
import { VoiceSpeechSettings } from "./VoiceSpeechSettings";

vi.mock("../../api/speech", () => ({ synthesizeGeminiSpeech: vi.fn() }));

afterEach(() => vi.restoreAllMocks());

it("plays a successful Gemini preview when mounted under the app's StrictMode", async () => {
  const user = userEvent.setup();
  vi.mocked(synthesizeGeminiSpeech).mockResolvedValue({ audioBase64: "AQ==", mimeType: "audio/wav" });
  vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:voice-preview");
  vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => undefined);
  vi.spyOn(HTMLMediaElement.prototype, "play").mockResolvedValue(undefined);
  vi.spyOn(HTMLMediaElement.prototype, "pause").mockImplementation(() => undefined);
  vi.spyOn(HTMLMediaElement.prototype, "load").mockImplementation(() => undefined);

  const view = render(<StrictMode><VoiceSpeechSettings
    preferences={{ ...defaultSpeechPreferences, outputProvider: "gemini", voice: "browser-voice-id" }}
    onChange={vi.fn()}
    geminiConfigured
  /></StrictMode>);

  await user.click(screen.getByRole("button", { name: "Preview voice" }));
  expect(synthesizeGeminiSpeech).toHaveBeenCalledWith("This is a preview of the selected RAVEN voice.", "Kore");
  await waitFor(() => expect(HTMLMediaElement.prototype.play).toHaveBeenCalledTimes(1));
  expect(await screen.findByText("Playing voice preview…")).toBeInTheDocument();
  view.unmount();
});

it("locks Gemini voice preview while synthesis is pending to prevent duplicate requests", async () => {
  const user = userEvent.setup();
  let resolveAudio!: (audio: GeminiSpeechAudio) => void;
  const pendingAudio = new Promise<GeminiSpeechAudio>((resolve) => { resolveAudio = resolve; });
  vi.mocked(synthesizeGeminiSpeech).mockReturnValue(pendingAudio);
  const view = render(<VoiceSpeechSettings
    preferences={{ ...defaultSpeechPreferences, outputProvider: "gemini" }}
    onChange={vi.fn()}
    geminiConfigured
  />);

  await user.click(screen.getByRole("button", { name: "Preview voice" }));
  expect(await screen.findByRole("status")).toHaveTextContent(/Generating Gemini voice preview/);
  expect(screen.getByRole("button", { name: "Preparing voice preview" })).toBeDisabled();
  await user.click(screen.getByRole("button", { name: "Preparing voice preview" }));
  expect(synthesizeGeminiSpeech).toHaveBeenCalledTimes(1);

  view.unmount();
  await act(async () => resolveAudio({ audioBase64: "AQ==", mimeType: "audio/wav" }));
});

it("shows Gemini rate limits and returns the preview control to idle", async () => {
  const user = userEvent.setup();
  vi.mocked(synthesizeGeminiSpeech).mockRejectedValue(new ApiError(429, { detail: "Gemini speech is rate limited. Try again later." }));
  render(<VoiceSpeechSettings
    preferences={{ ...defaultSpeechPreferences, outputProvider: "gemini" }}
    onChange={vi.fn()}
    geminiConfigured
  />);

  await user.click(screen.getByRole("button", { name: "Preview voice" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("Gemini speech is rate limited. Try again later.");
  expect(screen.getByRole("button", { name: "Preview voice" })).toBeEnabled();
});
