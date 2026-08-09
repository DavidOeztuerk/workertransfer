import type { ReactNode } from "react";

export interface DescriptionListItem {
  term: ReactNode;
  description: ReactNode;
}

export interface DescriptionListProps {
  items: DescriptionListItem[];
  className?: string;
}

/**
 * Begriff und Wert in Paaren — `<dl>` mit `<dt>`/`<dd>`.
 *
 * Nicht zwei Spalten aus `div`: die Zuordnung von Begriff zu Wert ist genau
 * das, was hier zählt, und ein `<dl>` trägt sie im Markup statt im Layout.
 */
export function DescriptionList({ items, className }: DescriptionListProps) {
  return (
    <dl className={["wt-dl", className].filter(Boolean).join(" ")}>
      {items.map((item, index) => (
        // Der Index als Schlüssel ist hier richtig: die Liste ist eine
        // Momentaufnahme ohne eigene Identität je Zeile, und `term` darf ein
        // beliebiger ReactNode sein, also kein brauchbarer Schlüssel.
        <div className="wt-dl__pair" key={index}>
          <dt className="wt-dl__term">{item.term}</dt>
          <dd className="wt-dl__description">{item.description}</dd>
        </div>
      ))}
    </dl>
  );
}
