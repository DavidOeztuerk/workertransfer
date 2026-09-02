import { vi } from "vitest";

/**
 * Eine Attrappe für `fetch` — und der Grund, warum sie hier steht statt einer
 * `vi.mock()`-Zeile über den API-Modulen.
 *
 * Die alten Tests haben die Client-Module ersetzt. Damit prüft ein Test das
 * Verhalten der Seite, aber nie den <strong>Draht</strong>: welchen Pfad sie
 * ruft, mit welcher Methode, und mit welchen Feldnamen im Rumpf. Genau dort
 * versteckte sich der teuerste Fehler dieser Migration — die Oberfläche schickte
 * `remote_ok`, der Server band `remoteOk`, und das Häkchen „Remote möglich" ging
 * beim Speichern still verloren. Kein einziger Test konnte das sehen.
 *
 * Deshalb liegt die Attrappe eine Ebene tiefer: die echten API-Module laufen
 * mit, und `aufrufe` hält fest, was tatsächlich hinausging.
 */
export interface Aufruf {
  method: string;
  /** Nur der Pfad, ohne Ursprung — die Basis-URL hängt an der Umgebung. */
  path: string;
  /** Der Pfad samt Abfrageteil, für Tests, die die Filter prüfen. */
  url: string;
  /** Der geschickte Rumpf, schon geparst. `undefined`, wenn keiner dabei war. */
  body: unknown;
}

export interface Antwort {
  status?: number;
  body?: unknown;
}

type Reaktion = Antwort | ((aufruf: Aufruf) => Antwort | Promise<Antwort>);

/** Schlüssel sind `"<METHODE> <pfad>"`, zum Beispiel `"GET /profiles/me"`. */
export type Reaktionen = Record<string, Reaktion>;

export interface Netz {
  /** Alles, was hinausging — in der Reihenfolge des Aufrufs. */
  aufrufe: Aufruf[];
  /** Der letzte Aufruf auf diesen Schlüssel, oder `undefined`. */
  letzter: (schluessel: string) => Aufruf | undefined;
  /** Eine Reaktion nachträglich ändern (etwa für den zweiten Klick). */
  setze: (schluessel: string, reaktion: Reaktion) => void;
}

function pfadVon(input: string): string {
  // Absolute URL (der Normalfall — `request()` setzt die Basis davor) oder,
  // im Notfall, schon ein reiner Pfad.
  try {
    return new URL(input).pathname + new URL(input).search;
  } catch {
    return input;
  }
}

/**
 * Hängt eine Attrappe an `globalThis.fetch` und gibt das Protokoll zurück.
 *
 * Ein Aufruf ohne passende Reaktion <strong>wirft</strong>, mit dem gesuchten
 * Schlüssel im Text. Eine stille 404 wäre schlimmer: die Seite zeigte dann
 * „nichts vorhanden", und der Test wäre grün aus dem falschen Grund — genau die
 * Verwechslung, gegen die dieser ganze Bereich gebaut ist.
 */
export function netz(reaktionen: Reaktionen = {}): Netz {
  const tabelle = new Map<string, Reaktion>(Object.entries(reaktionen));
  const aufrufe: Aufruf[] = [];

  vi.stubGlobal("fetch", async (input: RequestInfo | URL, init?: RequestInit) => {
    const roh = typeof input === "string" ? input : input.toString();
    const url = pfadVon(roh);
    const path = url.split("?")[0] ?? url;
    const method = (init?.method ?? "GET").toUpperCase();

    let body: unknown;
    if (typeof init?.body === "string") {
      try {
        body = JSON.parse(init.body);
      } catch {
        body = init.body;
      }
    } else if (init?.body !== undefined && init.body !== null) {
      // FormData und Ähnliches: unverändert durchreichen, damit ein Test die
      // Datei ansehen kann.
      body = init.body;
    }

    const aufruf: Aufruf = { method, path, url, body };
    aufrufe.push(aufruf);

    const reaktion = tabelle.get(`${method} ${path}`);
    if (reaktion === undefined) {
      throw new Error(
        `Kein Eintrag im Netz für "${method} ${path}". ` +
          `Vorhanden: ${[...tabelle.keys()].join(", ") || "(keiner)"}`
      );
    }

    const { status = 200, body: rumpf } =
      typeof reaktion === "function" ? await reaktion(aufruf) : reaktion;

    return new Response(rumpf === undefined ? null : JSON.stringify(rumpf), {
      status,
      headers: rumpf === undefined ? undefined : { "content-type": "application/json" },
    });
  });

  return {
    aufrufe,
    letzter: (schluessel) => {
      const [method, path] = schluessel.split(" ");
      return [...aufrufe].reverse().find((a) => a.method === method && a.path === path);
    },
    setze: (schluessel, reaktion) => tabelle.set(schluessel, reaktion),
  };
}
