import type { ReactNode } from "react";
import { useId } from "react";
import { Card } from "@workertransfer/ui";

import "./auth-layout.css";

export interface AuthLayoutProps {
  title: string;
  /** Ein Satz unter der Überschrift. Erklärt, worauf man sich einlässt. */
  lead?: ReactNode;
  /** Die Aussage im Markenpanel — je Seite eine andere, damit es nicht Tapete wird. */
  claim: string;
  support: string;
  children: ReactNode;
  /** Fußzeile des Formulars: der jeweils andere Weg. */
  note?: ReactNode;
}

export function AuthLayout({ title, lead, claim, support, children, note }: AuthLayoutProps) {
  // useId statt einer aus dem Titel gebauten ID. Der alte Weg
  // (`title.replace(/\s+/g, "-").toLowerCase()`) erzeugte aus „Konto erstellen"
  // die ID „konto-erstellen-title" — lesbar, aber zwei Seiten mit gleichem
  // Titel hätten dieselbe ID, und Umlaute landeten unverändert in einem
  // Attribut, das nur ASCII sicher trägt. useId ist stabil und kollisionsfrei
  // und ist ohnehin schon das Mittel in Field, TextArea, Switch und Page.
  const headingId = useId();

  return (
    <main className="auth">
      {/* Das Formular steht ZUERST im DOM, das Markenpanel danach — die
          Rasterspalten stellen es visuell trotzdem nach links. So beginnt
          Tastatur- und Screenreader-Bedienung bei der Aufgabe statt bei einem
          Werbesatz, ohne den Satz jemandem vorzuenthalten (aria-hidden wäre
          bequem, würde aber echten Inhalt unterschlagen).

          Diese Reihenfolge ist beim Umbauen leicht zu verlieren, weil man
          „links" intuitiv als „zuerst" schreibt. Die Rasterposition steht
          deshalb ausdrücklich in auth-layout.css. */}
      <div className="auth__panel">
        <Card className="auth__form" aria-labelledby={headingId}>
          <h1 id={headingId}>{title}</h1>
          {lead !== undefined ? <p className="auth__lead">{lead}</p> : null}
          {children}
          {note !== undefined ? <p className="auth__note">{note}</p> : null}
        </Card>
      </div>
      <aside className="auth__aside">
        <p className="auth__claim">{claim}</p>
        <p className="auth__support">{support}</p>
      </aside>
    </main>
  );
}
