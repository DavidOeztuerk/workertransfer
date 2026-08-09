import type { ReactNode, SelectHTMLAttributes } from "react";
import { useId } from "react";

export type SelectProps = Omit<SelectHTMLAttributes<HTMLSelectElement>, "id"> & {
  label: ReactNode;
  /** Erklärt die Wahl, bevor jemand die falsche trifft. */
  hint?: ReactNode;
  /** Fehlermeldung; setzt zugleich aria-invalid und die Beschreibung. */
  error?: ReactNode;
  /** Die `<option>`-Elemente. */
  children: ReactNode;
};

/**
 * Ein Wertwähler — als **natives** `<select>`.
 *
 * Kein nachgebautes Listenfeld: das APG-Muster für eine Combobox verlangt
 * `role="combobox"`, `aria-controls`, `aria-expanded`, `aria-autocomplete` und
 * `aria-activedescendant`. Keiner der vier Wähler in dieser Anwendung braucht
 * Filtern oder Autocomplete — es sind Wertwähler. Ein hübsches Listenfeld, das
 * man mit der Tastatur nicht bedienen kann, ist schlechter als ein hässliches,
 * das man bedienen kann.
 *
 * Hülle und aria-Verknüpfung sind absichtlich identisch zu `Field`, samt der
 * Falle, dass `aria-describedby` den Hinweis durch den Fehler ersetzt, wenn man
 * nur eines von beiden setzt.
 */
export function Select({ label, hint, error, className, children, ...props }: SelectProps) {
  const id = useId();
  const hintId = `${id}-hint`;
  const errorId = `${id}-error`;
  // Beides verknüpfen, damit ein Screenreader Hinweis UND Fehler vorliest.
  const describedBy = [hint ? hintId : null, error ? errorId : null].filter(Boolean).join(" ");

  return (
    <div className={["wt-field", className].filter(Boolean).join(" ")}>
      <label className="wt-field__label" htmlFor={id}>
        {label}
      </label>
      <select
        id={id}
        className="wt-field__input wt-field__input--select"
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy === "" ? undefined : describedBy}
        {...props}
      >
        {children}
      </select>
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
