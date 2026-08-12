import type { ReactNode } from "react";

export interface RowListProps {
  children: ReactNode;
  className?: string;
}

/**
 * Die Liste um `Row`.
 *
 * Ein echtes `<ul>`: ein Screenreader sagt dann „Liste, 3 Einträge", und die
 * Person weiß, wie viel kommt. Drei `div` sagen nichts.
 */
export function RowList({ children, className }: RowListProps) {
  return <ul className={["wt-row-list", className].filter(Boolean).join(" ")}>{children}</ul>;
}

export interface RowProps {
  title: ReactNode;
  /** Datum, Status, Herkunft — was die Zeile einordnet. */
  meta?: ReactNode;
  /** Knöpfe rechts. Was hier steht, handelt an genau dieser Zeile. */
  actions?: ReactNode;
  className?: string;
}

/** Eine Zeile je Vorgang. Gehört in eine `RowList`, weil sie ein `<li>` ist. */
export function Row({ title, meta, actions, className }: RowProps) {
  return (
    <li className={["wt-row", className].filter(Boolean).join(" ")}>
      <div className="wt-row__body">
        <p className="wt-row__title">{title}</p>
        {meta !== undefined ? <p className="wt-row__meta">{meta}</p> : null}
      </div>
      {actions !== undefined ? <div className="wt-row__actions">{actions}</div> : null}
    </li>
  );
}
