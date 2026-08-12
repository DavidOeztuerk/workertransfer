import type { ReactNode } from "react";
import { useEffect, useId, useRef } from "react";

export interface DialogProps {
  open: boolean;
  /**
   * Wird gerufen, wenn der Dialog geschlossen wurde — durch den Knopf **oder**
   * durch Esc. Der Aufrufer setzt daraufhin `open` auf `false`.
   */
  onClose: () => void;
  title: string;
  children: ReactNode;
  /** Text des Schließen-Knopfes. */
  closeLabel?: string;
  className?: string;
}

/**
 * Ein modaler Dialog — auf dem **nativen** `<dialog>` mit `showModal()`.
 *
 * Vom Browser kommen: Fokuseinschluss, Esc schließt, inerter Hintergrund,
 * `aria-modal="true"`, Top-Layer und `::backdrop`. Selbst geschrieben sind nur
 * die drei Dinge, die die Plattform nicht übernimmt: Fokus zurück zum
 * auslösenden Element, `aria-labelledby`, ein echter Schließen-Knopf.
 *
 * `role="dialog"` und `aria-modal` werden absichtlich NICHT gesetzt —
 * `showModal()` setzt sie, und beides doppelt zu setzen ist der häufigste
 * Fehler an diesem Element.
 *
 * Geschlossen wird **nichts gerendert**, statt ein verstecktes `<dialog>`
 * stehen zu lassen: so liegen die Inhalte samt ihrer fokussierbaren Elemente
 * gar nicht im Baum, und es kann keinen Tabstopp in etwas Unsichtbarem geben.
 *
 * **Anfangsfokus** setzt dieses Bauteil nicht selbst. `showModal()` legt ihn auf
 * das erste fokussierbare Element im Dialog; wer etwas anderes braucht, setzt
 * `autofocus` auf sein eigenes Element in `children` — das ist der native Weg
 * und die Entscheidung des Aufrufers. Ein `autoFocus` hier hätte den Fokus
 * gestohlen, **bevor** der Effekt den Auslöser merken kann, und damit die
 * Fokusrückgabe kaputt gemacht.
 *
 * ACHTUNG beim Testen: jsdom implementiert `showModal()` nicht (Issue #3294).
 * `src/test/setup.ts` ersetzt nur `open` und das `close`-Ereignis. Fokusfalle
 * und Esc sind in jsdom deshalb NICHT geprüft — sie gehören in eine
 * Browser-Prüfung und kommen mit dem ersten Verbraucher in E3.
 */
export function Dialog({
  open,
  onClose,
  title,
  children,
  closeLabel = "Schließen",
  className,
}: DialogProps) {
  const ref = useRef<HTMLDialogElement>(null);
  const headingId = useId();
  // Wer den Dialog geöffnet hat. Ohne diese Merkung steht der Fokus nach dem
  // Schließen am Seitenanfang, und man tabbt sich zurück zu der Stelle, an der
  // man war.
  const opener = useRef<Element | null>(null);

  // Abhängig von `open`, nicht von `[]`: geschlossen ist das Element gar nicht
  // im Baum, ein Effekt beim Mounten käme also nie an ein `<dialog>`.
  useEffect(() => {
    const dialog = ref.current;
    if (dialog === null || dialog.hasAttribute("open")) return;

    // Merken, BEVOR showModal() den Fokus in den Dialog zieht.
    opener.current = document.activeElement;
    dialog.showModal();
  }, [open]);

  useEffect(() => {
    const dialog = ref.current;
    if (dialog === null) return;

    // Esc kommt hier an, nicht am Knopf: der Browser schließt selbst und meldet
    // es über `close`. Wer nur den Knopf verdrahtet, verliert den Zustand,
    // sobald jemand Esc drückt.
    function handleClose() {
      const previous = opener.current;
      opener.current = null;
      if (previous instanceof HTMLElement) previous.focus();
      onClose();
    }

    dialog.addEventListener("close", handleClose);
    return () => dialog.removeEventListener("close", handleClose);
  }, [onClose, open]);

  if (!open) return null;

  return (
    <dialog
      className={["wt-dialog", className].filter(Boolean).join(" ")}
      ref={ref}
      aria-labelledby={headingId}
    >
      <h2 className="wt-dialog__title" id={headingId}>
        {title}
      </h2>
      <div className="wt-dialog__body">{children}</div>
      {/* Ein Dialog, den man nur mit Esc verlassen kann, ist für jemanden ohne
          Tastatur eine Falle. Der Knopf schließt über die Plattform
          (`close()`), damit Esc und Klick denselben Weg nehmen. */}
      <button
        type="button"
        className="wt-button wt-button--quiet wt-dialog__close"
        onClick={() => ref.current?.close()}
      >
        {closeLabel}
      </button>
    </dialog>
  );
}
