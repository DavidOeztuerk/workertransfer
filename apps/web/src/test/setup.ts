import "@testing-library/jest-dom/vitest";

/**
 * Ein eigener, berechenbarer `localStorage` für die Testreihe.
 *
 * Nicht aus Bequemlichkeit, sondern weil er sonst von der Node-Version abhängt:
 * **Node 25 bringt ein globales `localStorage` mit, das ohne
 * `--localstorage-file` keine Methoden hat.** jsdom übernimmt es, und
 * `window.localStorage.getItem` ist dann `undefined`. Lokal auf Node 24 fiel
 * das nicht auf — in der CI (Node 25) fielen 18 Tests in fünf Dateien um, mit
 * `TypeError: s.getItem is not a function`.
 *
 * Die Alternative wäre gewesen, den Ersatz nur einzusetzen, wenn der
 * vorhandene kaputt ist. Dann prüfte die CI aber etwas anderes als die
 * Entwicklungsmaschine — und im schlechteren Fall gar nichts mehr, weil der
 * Produktivcode einen unbrauchbaren Speicher still überspringt und jeder Test
 * trivial grün würde. Ein Ersatz für alle ist ehrlicher.
 *
 * Der Produktivcode schützt sich unabhängig davon selbst (`intent.ts`): er
 * prüft, ob der Speicher seine Methoden wirklich hat, und verzichtet sonst.
 */
function inMemoryStorage(): Storage {
  const daten = new Map<string, string>();
  return {
    get length() {
      return daten.size;
    },
    clear: () => daten.clear(),
    getItem: (schluessel: string) => daten.get(schluessel) ?? null,
    key: (index: number) => [...daten.keys()][index] ?? null,
    removeItem: (schluessel: string) => void daten.delete(schluessel),
    setItem: (schluessel: string, wert: string) => void daten.set(schluessel, String(wert)),
  };
}

for (const ziel of [globalThis, globalThis.window].filter(Boolean)) {
  try {
    Object.defineProperty(ziel, "localStorage", {
      value: inMemoryStorage(),
      configurable: true,
      writable: true,
    });
  } catch {
    // Lässt sich der Eintrag nicht überschreiben, bleibt es beim vorhandenen.
    // Ob der taugt, entscheidet die Prüfung darunter.
  }
}

// Selbstprüfung, mit einem Satz statt achtzehn Rätseln.
//
// Ohne sie äußert sich ein nicht überschreibbarer Speicher als `TypeError:
// s.getItem is not a function` in fünf verschiedenen Dateien — genau das Bild,
// das diese Datei aufräumt, und man sucht es im Produktivcode. Ein klarer
// Abbruch hier nennt stattdessen die Ursache.
if (typeof globalThis.localStorage?.getItem !== "function") {
  throw new Error(
    "localStorage ist in dieser Testumgebung unbrauchbar und liess sich nicht " +
      "ersetzen. Bekannte Ursache: Node >= 25 stellt ein globales localStorage " +
      "ohne Methoden bereit (nur mit --localstorage-file nutzbar). " +
      "Siehe apps/web/src/test/setup.ts."
  );
}
