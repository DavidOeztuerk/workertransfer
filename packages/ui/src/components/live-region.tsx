import { useCallback, useState } from "react";

export interface LiveRegionProps {
  /** Leerer String heißt: nichts zu sagen. Die Region bleibt trotzdem stehen. */
  message: string;
  className?: string;
}

/**
 * Eine höfliche Live-Region.
 *
 * Sie muss von Anfang an im Baum stehen: wird sie erst mit der Nachricht
 * eingefügt, liest der Screenreader sie nicht vor — eine Live-Region muss
 * existieren, bevor sich ihr Inhalt ändert. Deshalb rendert dieses Bauteil
 * auch bei leerer Nachricht einen Knoten.
 */
export function LiveRegion({ message, className }: LiveRegionProps) {
  return (
    <p
      className={["wt-visually-hidden", className].filter(Boolean).join(" ")}
      role="status"
      aria-live="polite"
    >
      {message}
    </p>
  );
}

/** Ein unsichtbares Zeichen — ändert den Text, ohne ihn zu ändern. */
const NONCE = "​";

/**
 * Sagt Ergebnisse an, die man sonst nur sieht.
 *
 * Der Zähler ist nicht Zierde: derselbe Text zweimal hintereinander ändert den
 * DOM-Knoten nicht, und dann bleibt die zweite Ansage stumm. Bei jeder zweiten
 * Ansage hängt deshalb ein Zero-Width-Space an — sichtbar identisch, für den
 * Vorleser eine Änderung.
 */
export function useAnnounce(): { message: string; announce: (text: string) => void } {
  const [state, setState] = useState({ text: "", turn: 0 });

  const announce = useCallback((text: string) => {
    setState((previous) => ({ text, turn: previous.turn + 1 }));
  }, []);

  const message = state.text === "" ? "" : state.turn % 2 === 0 ? state.text : state.text + NONCE;

  return { message, announce };
}
