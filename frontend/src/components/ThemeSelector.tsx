import { type ThemePreference, useTheme } from "../app/theme";

const choices: Array<{ value: ThemePreference; title: string; description: string }> = [
  { value: "system", title: "System", description: "Follow this device" },
  { value: "light", title: "Light", description: "Use the daylight scheme" },
  { value: "dark", title: "Dark", description: "Use the night scheme" },
];

export function ThemeSelector({
  preference,
  disabled = false,
  onChange,
}: {
  preference: ThemePreference;
  disabled?: boolean;
  onChange: (preference: ThemePreference) => void;
}) {
  const { resolvedTheme } = useTheme();

  return (
    <fieldset className="theme-selector">
      <legend>Choose your appearance</legend>
      <p className="theme-selector__hint">
        System currently resolves to <strong>{resolvedTheme}</strong>. Save your selection to apply it.
      </p>
      <div className="theme-selector__choices">
        {choices.map((choice) => (
          <label className="theme-choice" key={choice.value}>
            <input
              type="radio"
              name="theme-preference"
              value={choice.value}
              checked={preference === choice.value}
              disabled={disabled}
              onChange={() => onChange(choice.value)}
            />
            <span className={`theme-choice__swatch theme-choice__swatch--${choice.value}`} aria-hidden="true"><i /><i /></span>
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
