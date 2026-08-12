import type { InputHTMLAttributes, ReactNode } from "react";
import { useId } from "react";

export type CheckboxProps = Omit<InputHTMLAttributes<HTMLInputElement>, "id" | "type"> & {
  label: ReactNode;
  hint?: ReactNode;
};

/**
 * Ein Kästchen in einem Formular — gilt mit dem Absenden.
 *
 * Das ist der Unterschied zu `Switch`, und er ist keine Kosmetik: ein `Switch`
 * ist ein `button[role="switch"]` und wirkt SOFORT, eine Checkbox verspricht
 * „gilt, wenn du absendest". Bei einer Einwilligung entscheidet dieses
 * Versprechen, was die Person glaubt getan zu haben. Wer hier eine Checkbox
 * einsetzt, muss also ein Absenden anbieten.
 */
export function Checkbox({ label, hint, className, ...props }: CheckboxProps) {
  const id = useId();
  const hintId = `${id}-hint`;

  return (
    <div className={["wt-checkbox", className].filter(Boolean).join(" ")}>
      <input
        id={id}
        className="wt-checkbox__box"
        type="checkbox"
        aria-describedby={hint ? hintId : undefined}
        {...props}
      />
      <label className="wt-checkbox__label" htmlFor={id}>
        {label}
      </label>
      {hint ? (
        <p className="wt-checkbox__hint" id={hintId}>
          {hint}
        </p>
      ) : null}
    </div>
  );
}
