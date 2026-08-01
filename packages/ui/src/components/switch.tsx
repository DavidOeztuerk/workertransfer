import type { ReactNode } from "react";
import { useId } from "react";

export interface SwitchProps {
  label: ReactNode;
  checked: boolean;
  /** Bekommt den Zustand, in den geschaltet werden soll — nicht den aktuellen. */
  onChange: (next: boolean) => void;
  disabled?: boolean;
  /** Erklärt, was der Schalter bewirkt oder warum er gerade nicht geht. */
  hint?: ReactNode;
  className?: string;
}

/**
 * Ein Schalter mit sofortiger Wirkung — kein Formularfeld.
 *
 * Bewusst ein `<button role="switch">` und keine Checkbox: eine Checkbox
 * verspricht, dass die Änderung erst mit dem Absenden gilt. Hier gilt sie
 * sofort, und der Unterschied ist bei einer Einwilligung keine Kosmetik.
 */
export function Switch({
  label,
  checked,
  onChange,
  disabled = false,
  hint,
  className,
}: SwitchProps) {
  const id = useId();
  const hintId = `${id}-hint`;

  return (
    <div className={["wt-switch", className].filter(Boolean).join(" ")}>
      <button
        type="button"
        role="switch"
        id={id}
        aria-checked={checked}
        aria-describedby={hint ? hintId : undefined}
        disabled={disabled}
        className="wt-switch__control"
        onClick={() => onChange(!checked)}
      >
        <span className="wt-switch__track" aria-hidden="true">
          <span className="wt-switch__thumb" />
        </span>
        <span className="wt-switch__label">{label}</span>
      </button>
      {hint ? (
        <p className="wt-switch__hint" id={hintId}>
          {hint}
        </p>
      ) : null}
    </div>
  );
}
