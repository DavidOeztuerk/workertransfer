import type { ReactNode } from "react";
import { useId } from "react";

export interface PageProps {
  title: string;
  /** Ein Satz unter der Überschrift: worum es hier geht. */
  lead?: ReactNode;
  /** Nachsatz unter dem Inhalt — meist eine Einschränkung oder ein Verweis. */
  note?: ReactNode;
  /**
   * Der Weg zurück.
   *
   * Gebraucht, sobald ein Formular eine eigene Route hat: ein `<main>` ohne
   * Rückweg ist auf einem Deep-Link eine Sackgasse.
   */
  back?: ReactNode;
  /** Schmale Spalte — für Seiten, die im Kern ein Formular sind. */
  narrow?: boolean;
  children: ReactNode;
  className?: string;
}

/**
 * Das Gerüst einer Seite: `<main>`, eine `<h1>`, optional Vorspann und
 * Nachsatz.
 *
 * Das `<main>` wird über `aria-labelledby` nach der Überschrift benannt — ohne
 * das heißt der Hauptbereich für einen Screenreader nur „main", und bei 26
 * Seiten hilft das niemandem beim Erkennen, wo er ist.
 */
export function Page({ title, lead, note, back, narrow = false, children, className }: PageProps) {
  const headingId = useId();
  const classes = ["wt-page", narrow ? "wt-page--narrow" : null, className]
    .filter(Boolean)
    .join(" ");

  return (
    <main className={classes} aria-labelledby={headingId}>
      <header className="wt-page__header">
        {back !== undefined ? <div className="wt-page__back">{back}</div> : null}
        <h1 className="wt-page__title" id={headingId}>
          {title}
        </h1>
        {lead !== undefined ? <p className="wt-page__lead">{lead}</p> : null}
      </header>
      {children}
      {note !== undefined ? <p className="wt-page__note">{note}</p> : null}
    </main>
  );
}
