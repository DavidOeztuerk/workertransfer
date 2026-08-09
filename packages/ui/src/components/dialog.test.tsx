import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";

import { Dialog } from "./dialog";

describe("Dialog", () => {
  it("stays shut while closed", () => {
    render(
      <Dialog open={false} onClose={() => {}} title="Wirklich löschen?">
        <p>Das lässt sich nicht zurücknehmen.</p>
      </Dialog>
    );

    expect(screen.queryByRole("dialog")).toBeNull();
  });

  // aria-labelledby ist unsere Pflicht: showModal() setzt role und aria-modal,
  // aber den Namen nicht. Ohne ihn heißt der Dialog für einen Vorleser nur
  // „Dialog".
  it("is named after its heading", () => {
    render(
      <Dialog open onClose={() => {}} title="Wirklich löschen?">
        <p>Das lässt sich nicht zurücknehmen.</p>
      </Dialog>
    );

    expect(screen.getByRole("dialog", { name: "Wirklich löschen?" })).toBeInTheDocument();
  });

  // Ein Dialog, den man nur mit Esc verlassen kann, ist für jemanden ohne
  // Tastatur eine Falle. Der Knopf ist die verlässliche Tür.
  it("always offers an explicit way out", async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    render(
      <Dialog open onClose={onClose} title="Wirklich löschen?">
        <p>Das lässt sich nicht zurücknehmen.</p>
      </Dialog>
    );

    await user.click(screen.getByRole("button", { name: "Schließen" }));

    expect(onClose).toHaveBeenCalled();
  });

  // Das `close`-Ereignis ist der Weg, auf dem auch Esc ankommt: der Browser
  // schließt selbst und meldet es. Wer nur den Knopf verdrahtet, verliert den
  // Zustand, sobald jemand Esc drückt — die Anwendung glaubt dann, der Dialog
  // sei noch offen.
  it("reports a close that the platform performed", () => {
    const onClose = vi.fn();
    render(
      <Dialog open onClose={onClose} title="Wirklich löschen?">
        <p>Das lässt sich nicht zurücknehmen.</p>
      </Dialog>
    );

    screen.getByRole("dialog").dispatchEvent(new Event("close"));

    expect(onClose).toHaveBeenCalled();
  });

  // Fokus zurück zum Auslöser: sonst steht der Fokus nach dem Schließen am
  // Seitenanfang, und man tabbt sich zurück zu der Stelle, an der man war.
  it("gives focus back to the element that opened it", async () => {
    const user = userEvent.setup();

    function Harness() {
      const [open, setOpen] = useState(false);
      return (
        <>
          <button type="button" onClick={() => setOpen(true)}>
            Löschen
          </button>
          <Dialog open={open} onClose={() => setOpen(false)} title="Wirklich löschen?">
            <p>Das lässt sich nicht zurücknehmen.</p>
          </Dialog>
        </>
      );
    }

    render(<Harness />);
    const trigger = screen.getByRole("button", { name: "Löschen" });
    await user.click(trigger);
    await user.click(screen.getByRole("button", { name: "Schließen" }));

    expect(trigger).toHaveFocus();
  });

  // showModal() setzt role="dialog" und aria-modal="true" selbst. Beides von
  // Hand zu setzen ist der häufigste Fehler an diesem Element.
  it("does not set role or aria-modal by hand", () => {
    render(
      <Dialog open onClose={() => {}} title="Wirklich löschen?">
        <p>Das lässt sich nicht zurücknehmen.</p>
      </Dialog>
    );

    const dialog = screen.getByRole("dialog");
    expect(dialog).not.toHaveAttribute("role");
    expect(dialog).not.toHaveAttribute("aria-modal");
  });
});
