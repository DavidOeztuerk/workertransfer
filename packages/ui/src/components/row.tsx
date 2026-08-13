import type { HTMLAttributes, ReactNode } from "react";

export type RowListProps = Omit<HTMLAttributes<HTMLUListElement>, "children"> & {
  children: ReactNode;
};

/**
 * Die Liste um `Row`.
 *
 * Ein echtes `<ul>`: ein Screenreader sagt dann „Liste, 3 Einträge", und die
 * Person weiß, wie viel kommt. Drei `div` sagen nichts.
 */
export function RowList({ children, className, ...rest }: RowListProps) {
  return (
    // Übrige Attribute werden durchgereicht — wie bei `Field` und `Select`. Ohne
    // das verschluckt das Bauteil still, was der Aufrufer gesetzt hat (ein
    // `data-testid` etwa), und der Fehler zeigt sich erst als roter Test an
    // ganz anderer Stelle.
    <ul className={["wt-row-list", className].filter(Boolean).join(" ")} {...rest}>
      {children}
    </ul>
  );
}

export type RowProps = Omit<HTMLAttributes<HTMLLIElement>, "title"> & {
  title: ReactNode;
  /** Datum, Status, Herkunft — was die Zeile einordnet. */
  meta?: ReactNode;
  /** Knöpfe rechts. Was hier steht, handelt an genau dieser Zeile. */
  actions?: ReactNode;
};

/** Eine Zeile je Vorgang. Gehört in eine `RowList`, weil sie ein `<li>` ist. */
export function Row({ title, meta, actions, className, ...rest }: RowProps) {
  return (
    <li className={["wt-row", className].filter(Boolean).join(" ")} {...rest}>
      <div className="wt-row__body">
        <p className="wt-row__title">{title}</p>
        {meta !== undefined ? <p className="wt-row__meta">{meta}</p> : null}
      </div>
      {actions !== undefined ? <div className="wt-row__actions">{actions}</div> : null}
    </li>
  );
}
