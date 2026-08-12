import type { ReactNode } from "react";
import { useId } from "react";

export interface FieldsetProps {
  legend: ReactNode;
  hint?: ReactNode;
  children: ReactNode;
  className?: string;
}

/**
 * Eine benannte Gruppe von Feldern — natives `<fieldset>` mit `<legend>`.
 *
 * Kein `div` mit `role="group"` und `aria-label`: die Legende ist sichtbarer
 * Text und zugänglicher Name in einem, und genau das will man hier.
 */
export function Fieldset({ legend, hint, children, className }: FieldsetProps) {
  const id = useId();
  const hintId = `${id}-hint`;

  return (
    <fieldset
      className={["wt-fieldset", className].filter(Boolean).join(" ")}
      aria-describedby={hint !== undefined ? hintId : undefined}
    >
      <legend className="wt-fieldset__legend">{legend}</legend>
      {hint !== undefined ? (
        <p className="wt-fieldset__hint" id={hintId}>
          {hint}
        </p>
      ) : null}
      {children}
    </fieldset>
  );
}

export interface RadioOption {
  value: string;
  label: ReactNode;
  /** Was diese Wahl bedeutet. Drei Wörter sind keine drei Entscheidungen. */
  hint?: ReactNode;
}

export interface RadioGroupProps {
  legend: ReactNode;
  /** Der gemeinsame Name — er ist es, der den Browser die Gruppe bilden lässt. */
  name: string;
  value: string;
  /** Bekommt den gewählten Wert, nicht das Ereignis. */
  onChange: (value: string) => void;
  options: RadioOption[];
  hint?: ReactNode;
  className?: string;
}

/**
 * Eine Auswahl aus wenigen benannten Zuständen.
 *
 * Native `<input type="radio">` mit gemeinsamem `name`: erst dadurch bewegen
 * die Pfeiltasten den Fokus innerhalb der Gruppe, und die Gruppe verhält sich
 * beim Tabben wie ein Element. Nachgebaut wäre beides Handarbeit.
 */
export function RadioGroup({
  legend,
  name,
  value,
  onChange,
  options,
  hint,
  className,
}: RadioGroupProps) {
  const id = useId();

  return (
    <Fieldset
      legend={legend}
      hint={hint}
      className={["wt-radio-group", className].filter(Boolean).join(" ")}
    >
      {options.map((option) => {
        const optionId = `${id}-${option.value}`;
        const hintId = `${optionId}-hint`;
        return (
          <div className="wt-radio" key={option.value}>
            <input
              className="wt-radio__input"
              id={optionId}
              type="radio"
              name={name}
              value={option.value}
              checked={option.value === value}
              aria-describedby={option.hint !== undefined ? hintId : undefined}
              onChange={() => onChange(option.value)}
            />
            <label className="wt-radio__label" htmlFor={optionId}>
              {option.label}
            </label>
            {option.hint !== undefined ? (
              <p className="wt-radio__hint" id={hintId}>
                {option.hint}
              </p>
            ) : null}
          </div>
        );
      })}
    </Fieldset>
  );
}
