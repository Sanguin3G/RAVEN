import { type ThemePreference, useTheme } from "../app/theme";

const choices: Array<{ value: ThemePreference; title: string; description: string }> = [
  { value: "system", title: "System", description: "Follow this device" },
  { value: "light", title: "Light", description: "Use the daylight scheme" },
  { value: "dark", title: "Dark", description: "Use the night scheme" },
];

export function ThemeSelector() {
  const { preference, resolvedTheme, setPreference } = useTheme();

  return (
    <fieldset className="theme-selector">
      <legend>Choose your appearance</legend>
      <p className="theme-selector__hint">
        System currently resolves to <strong>{resolvedTheme}</strong>.
      </p>
      <div className="theme-selector__choices">
        {choices.map((choice) => (
          <label className="theme-choice" key={choice.value}>
            <input
              type="radio"
              name="theme-preference"
              value={choice.value}
              checked={preference === choice.value}
              onChange={() => setPreference(choice.value)}
            />
            <span>
              <strong>{choice.title}</strong>
              <small>{choice.description}</small>
            </span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}
