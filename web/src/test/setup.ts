import "@testing-library/jest-dom/vitest";
import { configure } from "@testing-library/react";

/**
 * Die Wartegrenze von `findBy*` und `waitFor` — 1000 ms wären zu knapp.
 *
 * Sie ist NICHT dasselbe wie Vitests `testTimeout` (in vite.config.ts auf 20 s,
 * mit derselben Begründung): `testTimeout` beendet den ganzen Test,
 * `asyncUtilTimeout` beendet eine einzelne Wartezeit. Ein Test kann also 3,8
 * Sekunden laufen und trotzdem an einer 1-Sekunden-Wartezeit scheitern — genau
 * das war der Befund: ein Volllauf mit 2 roten Tests, der nächste mit 7, jedes
 * Mal andere Dateien, und alle grün, sobald sie einzeln laufen.
 *
 * Eine Testsuite, deren Ergebnis von der Maschinenlast abhängt, ist keine. Die
 * höhere Grenze verlangsamt keinen grünen Lauf: sie greift nur dort, wo vorher
 * abgebrochen wurde.
 *
 * WIEDER 5 SEKUNDEN. Am 09.09.2026 standen hier 10, mit der Begründung,
 * `DraftPage` scheitere auf dem Läufer an einer zu langen Abrufkette. Das war
 * falsch: die Erhöhung änderte nichts, der Test fiel im nächsten Lauf genauso.
 * Die Ursache lag in `features/work/lib/kontext.ts` — eine Antwort, die nach
 * dem Verwerfen ankam, füllte den Zwischenspeicher wieder. Es war nie zu
 * langsam, es war der falsche Wert.
 *
 * Die Lehre ist die Zahl selbst: eine Wartegrenze hochzusetzen sieht immer
 * nach einer Lösung aus und verbirgt genauso oft eine.
 */
configure({ asyncUtilTimeout: 5_000 });

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
    getItem: (key: string) => daten.get(key) ?? null,
    key: (index: number) => [...daten.keys()][index] ?? null,
    removeItem: (key: string) => void daten.delete(key),
    setItem: (key: string, value: string) => void daten.set(key, String(value)),
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
