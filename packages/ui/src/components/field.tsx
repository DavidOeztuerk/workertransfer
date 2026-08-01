import type { InputHTMLAttributes, ReactNode } from "react";
import { useId } from "react";

export type FieldProps = Omit<InputHTMLAttributes<HTMLInputElement>, "id"> & {
  label: ReactNode;
  /** Erklärt das Feld, bevor jemand einen Fehler macht. */
  hint?: ReactNode;
  /** Fehlermeldung; setzt zugleich aria-invalid und die Beschreibung. */
  error?: ReactNode;
};

export function Field({ label, hint, error, className, ...props }: FieldProps) {
  // useId statt eines Zählers: stabil über Server- und Client-Render hinweg und
  // kollisionsfrei, wenn dasselbe Feld zweimal auf einer Seite steht.
  const id = useId();
  const hintId = `${id}-hint`;
  const errorId = `${id}-error`;
  // Beides verknüpfen, damit ein Screenreader Hinweis UND Fehler vorliest —
  // aria-describedby ersetzt sonst das eine durch das andere.
  const describedBy = [hint ? hintId : null, error ? errorId : null].filter(Boolean).join(" ");

  return (
    <div className={["wt-field", className].filter(Boolean).join(" ")}>
      <label className="wt-field__label" htmlFor={id}>
        {label}
      </label>
      <input
        id={id}
        className="wt-field__input"
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy === "" ? undefined : describedBy}
        {...props}
      />
      {hint ? (
        <p className="wt-field__hint" id={hintId}>
          {hint}
        </p>
      ) : null}
      {error ? (
        <p className="wt-field__error" id={errorId} role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
}
