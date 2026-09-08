import type { InputHTMLAttributes } from "react";

interface TextInputProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string;
  hint?: string;
  error?: string;
}

export function TextInput({ label, hint, error, id, required, ...props }: TextInputProps) {
  const inputId = id ?? props.name;

  return (
    <div className="field">
      <label htmlFor={inputId}>
        {label}{required ? <span aria-hidden="true"> *</span> : null}
      </label>
      {hint ? <p className="field__hint" id={inputId ? `${inputId}-hint` : undefined}>{hint}</p> : null}
      <input
        id={inputId}
        className={`text-input${error ? " text-input--error" : ""}`}
        required={required}
        aria-describedby={hint && inputId ? `${inputId}-hint` : undefined}
        aria-invalid={Boolean(error)}
        {...props}
      />
      {error ? <p className="field__error" role="alert">{error}</p> : null}
    </div>
  );
}
