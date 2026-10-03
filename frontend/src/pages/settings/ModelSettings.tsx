import { Panel } from "../../components/Panel";
import type { GeminiModelOption, ResearchSettings } from "../../api/settings";
import styles from "../settings.module.css";

type Props = {
  draft: ResearchSettings;
  disabled: boolean;
  updateDraft: (changes: Partial<ResearchSettings>) => void;
  roleOptions: (value: string) => ModelOption[];
  selectedModel: (value: string) => ModelOption | undefined;
  modelAvailabilityVerified: boolean;
  modelAvailabilityMessage: string;
  noCompatibleModelsAvailable: boolean;
};

type ModelOption = { value: string; label: string; description: string; availability: GeminiModelOption["availability"] };

export function ModelSettings({ draft, disabled, updateDraft, roleOptions, selectedModel, modelAvailabilityVerified, modelAvailabilityMessage, noCompatibleModelsAvailable }: Props) {
  return (
<Panel title="AI models" eyebrow="GEMINI · RAVEN TASKS" className={styles.section}>
            <div className={styles.sectionIntro}><p>Choose from RAVEN-tested Gemini models that support the structured outputs used by these tasks. {modelAvailabilityVerified ? noCompatibleModelsAvailable ? "Project access was checked." : modelAvailabilityMessage : modelAvailabilityMessage || "Project availability is unverified; RAVEN's compatible choices remain available."}</p></div>
            {noCompatibleModelsAvailable ? <div className={styles.modelWarning} role="alert"><strong>No RAVEN-supported Gemini models were listed for this API key/project.</strong><span>Check that GEMINI_API_KEY belongs to the intended Google AI project, Gemini API access is enabled, and the key is allowed to use the Gemini API. RAVEN does not send a generation request to check this.</span></div> : null}
            <div className={styles.modelGrid}>
              <div className={styles.modelRole}><label htmlFor="grounding-model">Company matching</label><small>Resolves ambiguous identity and ranks relevant sources.</small><select id="grounding-model" value={draft.groundingModel} disabled={disabled} onChange={event => updateDraft({ groundingModel: event.target.value })}>{roleOptions(draft.groundingModel).map(model => <option key={model.value} value={model.value} disabled={model.availability === "Unavailable"}>{model.label}{model.availability === "Unavailable" ? " · unavailable to this project" : ""}</option>)}</select><small>{selectedModel(draft.groundingModel)?.description}{selectedModel(draft.groundingModel)?.availability === "Unavailable" ? " Not available to the configured project; choose another model." : ""}</small></div>
              <div className={styles.modelRole}><label htmlFor="profile-model">Company Profile</label><small>Generates structured, evidence-backed Company Profiles.</small><select id="profile-model" value={draft.profileModel} disabled={disabled} onChange={event => updateDraft({ profileModel: event.target.value })}>{roleOptions(draft.profileModel).map(model => <option key={model.value} value={model.value} disabled={model.availability === "Unavailable"}>{model.label}{model.availability === "Unavailable" ? " · unavailable to this project" : ""}</option>)}</select><small>{selectedModel(draft.profileModel)?.description}{selectedModel(draft.profileModel)?.availability === "Unavailable" ? " Not available to the configured project; choose another model." : ""}</small></div>
              <div className={styles.modelRole}><label htmlFor="chat-model">Ask RAVEN & research question</label><small>Answers chat questions and prepares the editable Managed Deep Research question.</small><select id="chat-model" value={draft.chatModel} disabled={disabled} onChange={event => updateDraft({ chatModel: event.target.value })}>{roleOptions(draft.chatModel).map(model => <option key={model.value} value={model.value} disabled={model.availability === "Unavailable"}>{model.label}{model.availability === "Unavailable" ? " · unavailable to this project" : ""}</option>)}</select><small>{selectedModel(draft.chatModel)?.description}{selectedModel(draft.chatModel)?.availability === "Unavailable" ? " Not available to the configured project; choose another model." : ""}</small></div>
              <div className={styles.modelRole}><label htmlFor="synthesis-model">Briefings & RAVEN analysis</label><small>Briefing synthesis, Investigation analysis and RAVEN-run Deep Research. This does not select the model behind Exa Agent.</small><select id="synthesis-model" value={draft.deepResearchModel} disabled={disabled} onChange={event => updateDraft({ deepResearchModel: event.target.value })}>{roleOptions(draft.deepResearchModel).map(model => <option key={model.value} value={model.value} disabled={model.availability === "Unavailable"}>{model.label}{model.availability === "Unavailable" ? " · unavailable to this project" : ""}</option>)}</select><small>{selectedModel(draft.deepResearchModel)?.description}{selectedModel(draft.deepResearchModel)?.availability === "Unavailable" ? " Not available to the configured project; choose another model." : ""}</small></div>
            </div>
            <p className={styles.catalogNote}>RAVEN exposes only its compatibility-tested model catalog. Project discovery checks model access and GenerateContent support; structured-output compatibility is maintained by RAVEN. Exact quotas remain visible in Google AI Studio.</p>
          </Panel>
  );
}
