import { Suspense, lazy, type ComponentType } from "react";

import { LoadingBlock } from "../ui";

/**
 * Eine Route, deren Seite erst beim Betreten geladen wird.
 *
 * <strong>Warum überhaupt.</strong> Mit MUI wiegt das Bündel 762 kB (236 kB
 * gepackt) — vorher waren es 439 kB. Wer die Startseite öffnet, lud damit auch
 * den Editor für Arbeitsproben, die Kandidatensuche und die Löschseite mit.
 * Aufgeteilt zahlt jede Seite nur, was sie braucht.
 *
 * <strong>Und warum ein eigener Ladezustand statt eines leeren Bildschirms.</strong>
 * `React.lazy` braucht ein `Suspense` darüber; ohne eines wirft React. Mit einem
 * leeren Rumpf sähe ein langsamer Nachladevorgang aus wie eine kaputte Seite —
 * gerade auf einem Mobilfunkanschluss, wo das Nachladen tatsächlich dauert.
 *
 * <strong>Das `Suspense` liegt je Route</strong> und nicht einmal über der
 * ganzen Anwendung: so bleibt die Kopfzeile stehen, während der Inhalt kommt.
 * Läge es weiter oben, verschwände beim Navigieren die halbe Seite.
 */
export function lazyRoute(
  laden: () => Promise<Record<string, ComponentType>>,
  name: string
): React.ReactElement {
  // `lazy` erwartet einen Vorgabeexport; unsere Seiten sind benannte Exporte,
  // weil ein benannter Export beim Umbenennen mitzieht und ein Vorgabeexport
  // still zu etwas anderem wird.
  const Seite = lazy(async () => {
    const modul = await laden();
    const komponente = modul[name];

    if (komponente === undefined) {
      throw new Error(`Die Seite ${name} exportiert sich nicht unter diesem Namen.`);
    }

    return { default: komponente };
  });

  return (
    <Suspense fallback={<LoadingBlock label="Seite wird geladen …" />}>
      <Seite />
    </Suspense>
  );
}
