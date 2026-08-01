import type { ReactNode, TextareaHTMLAttributes } from "react";
import { useId } from "react";

export type TextAreaProps = Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, "id"> & {
  label: ReactNode;
  hint?: ReactNode;
  error?: ReactNode;
};

/** Wie `Field`, nur mehrzeilig — dieselben Verknüpfungen für Screenreader. */
export function TextArea({ label, hint, error, className, ...props }: TextAreaProps) {
  const id = useId();
  const hintId = `${id}-hint`;
  const errorId = `${id}-error`;
  // Beides verknüpfen: aria-describedby ersetzt sonst das eine durch das andere.
  const describedBy = [hint ? hintId : null, error ? errorId : null].filter(Boolean).join(" ");

  return (
    <div className={["wt-field", className].filter(Boolean).join(" ")}>
      <label className="wt-field__label" htmlFor={id}>
        {label}
      </label>
      <textarea
        id={id}
        className="wt-field__input wt-field__input--multiline"
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
