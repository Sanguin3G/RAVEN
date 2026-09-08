import { Panel } from "../components/Panel";
import { ThemeSelector } from "../components/ThemeSelector";

export function SettingsPage() {
  return (
    <div className="narrow-page page-stack">
      <div>
        <p className="eyebrow">WORKSPACE SETTINGS</p>
        <h1>Appearance</h1>
        <p className="page-intro">Set the RAVEN workspace to match your device or your desk.</p>
      </div>
      <Panel title="Appearance" eyebrow="PREFERENCES">
        <ThemeSelector />
      </Panel>
      <Panel className="future-settings" title="Providers" eyebrow="COMING LATER">
        <p>Provider configuration will appear after provider integration exists.</p>
      </Panel>
      <Panel className="future-settings" title="Sound" eyebrow="COMING LATER">
        <p>RAVEN has no sound events to configure yet.</p>
      </Panel>
    </div>
  );
}
