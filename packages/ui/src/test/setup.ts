import "@testing-library/jest-dom/vitest";

/**
 * Ersatz für `<dialog>`, weil jsdom es nicht implementiert.
 *
 * Gemessen mit jsdom 30: `dialog.showModal is not a function` (offener
 * jsdom-Issue #3294). Ohne diesen Ersatz lässt sich am `Dialog` nichts prüfen,
 * weil das Element stumm bleibt.
 *
 * Der Ersatz setzt genau das `open`-Attribut und löst beim Schließen das
 * `close`-Ereignis aus — nicht mehr. Er ahmt insbesondere **nicht** die
 * Fokusfalle, Esc oder den inerten Hintergrund nach: das wären dann unsere
 * Behauptungen über unseren eigenen Ersatz und kein Beweis über den Browser.
 * Diese drei Dinge liefert die Plattform, und sie werden in echtem Chromium
 * geprüft, sobald ein Dialog einen Verbraucher hat (E3).
 *
 * Gepatcht wird der Prototyp, den jsdom für `<dialog>` tatsächlich benutzt —
 * ermittelt, nicht geraten. Wäre das `HTMLElement.prototype`, bekäme jedes
 * Element diese Methoden; dann bricht diese Datei lieber laut ab.
 */
const probe = document.createElement("dialog");
const dialogPrototype = Object.getPrototypeOf(probe) as HTMLElement & {
  showModal?: () => void;
  show?: () => void;
  close?: () => void;
};

if (typeof dialogPrototype.showModal !== "function") {
  if (dialogPrototype === HTMLElement.prototype) {
    throw new Error(
      "jsdom bildet <dialog> auf HTMLElement.prototype ab. Diesen Prototyp zu " +
        "patchen würde jedem Element showModal() geben — der Ersatz wäre dann " +
        "schlimmer als die Lücke. Siehe packages/ui/src/test/setup.ts."
    );
  }

  dialogPrototype.showModal = function showModal(this: HTMLElement) {
    this.setAttribute("open", "");
  };
  dialogPrototype.show = function show(this: HTMLElement) {
    this.setAttribute("open", "");
  };
  dialogPrototype.close = function close(this: HTMLElement) {
    if (!this.hasAttribute("open")) return;
    this.removeAttribute("open");
    this.dispatchEvent(new Event("close"));
  };
}
