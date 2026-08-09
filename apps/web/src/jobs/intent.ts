// Die gemerkte Stelle: „ich wollte mich auf diese hier bewerben".
//
// Warum eine ID und kein Pfad: das übliche Muster wäre `?weiter=/irgendwohin`,
// und genau das zählt OWASP als Angriffsfläche für Open Redirect auf. Hier wird
// gar kein Ziel gespeichert, sondern eine Stellen-ID; wohin navigiert wird,
// entscheidet die Anwendung selbst. Die Lücke kann damit nicht entstehen,
// statt geprüft werden zu müssen.
//
// Warum localStorage und nicht sessionStorage: der Weg über die Registrierung
// führt durch eine E-Mail, und der Bestätigungslink landet oft in einem anderen
// Tab. sessionStorage gilt je Tab und wäre dort leer.
//
// Warum trotzdem NICHT über den Bestätigungslink oder den Server: beides würde
// vermerken, dass diese Adresse sich auf diese Stelle bewerben wollte — in
// einem Postfach, das in jedem Backup liegt, bzw. in einer Datenbank, bevor das
// Konto überhaupt bestätigt ist. Der Preis dafür ist ehrlich: Mail auf dem
// Handy geöffnet, Bewerbung am Rechner begonnen — dann ist die Absicht weg und
// man landet auf der Stellenliste. Kein Fehler, nur kein Komfort.

const SCHLUESSEL = "wt.gemerkte-stelle";

/**
 * 24 Stunden — dieselbe Spanne wie der Bestätigungslink.
 *
 * Ohne Verfall poppte eine drei Wochen alte Absicht nach dem nächsten Anmelden
 * wieder auf, und niemand wüsste warum.
 */
const GUELTIG_MS = 24 * 60 * 60 * 1000;

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

interface Gemerkt {
  jobId: string;
  /**
   * Nur für den Hinweis „Danach geht es zurück zu …".
   *
   * Bewusst mitgespeichert statt nachgeladen: sonst bräuchte die Anmeldeseite
   * eine Abfrage und damit einen QueryClient, nur um einen Satz anzuzeigen.
   * Dass der Titel veralten kann, ist hier folgenlos — er ist die Notiz einer
   * Person an sich selbst, sie verfällt nach 24 Stunden, und der richtige
   * Titel steht danach auf der Stelle selbst. Für die NAVIGATION zählt allein
   * die ID; der Titel entscheidet nichts.
   */
  titel: string;
  gemerktAm: number;
}

export interface GemerkteStelle {
  jobId: string;
  titel: string;
}

function speicher(): Storage | null {
  // Zwei verschiedene Arten, kaputt zu sein — und die zweite hat mich erwischt:
  //
  // 1. Der Zugriff WIRFT: Safari im privaten Modus, abgeschaltete Speicher.
  // 2. Das Objekt IST DA, kann aber nichts. Node 25 bringt ein eigenes
  //    globales `localStorage` mit, das ohne `--localstorage-file` keine
  //    Methoden hat; jsdom übernimmt es, und `window.localStorage` ist dann
  //    ein Objekt ohne `getItem`. Der erste Schutz allein reicht dagegen
  //    nicht — es wirft ja nichts, es kann nur nichts.
  //
  // Eine gemerkte Stelle ist Komfort. Sie darf nichts umwerfen, in keinem
  // der beiden Fälle.
  try {
    if (typeof window === "undefined") return null;
    const s: Storage | undefined = window.localStorage;
    return typeof s?.getItem === "function" ? s : null;
  } catch {
    return null;
  }
}

export function merkeStelle(jobId: string, titel = ""): void {
  if (!UUID.test(jobId)) return;
  const s = speicher();
  if (s === null) return;
  try {
    s.setItem(
      SCHLUESSEL,
      JSON.stringify({ jobId, titel: titel.slice(0, 160), gemerktAm: Date.now() } satisfies Gemerkt)
    );
  } catch {
    // Voller Speicher: dann eben ohne Merken.
  }
}

/**
 * Die gemerkte Stelle — oder `null`.
 *
 * Prüft die ID auch beim LESEN. localStorage ist von jedem Skript auf der Seite
 * beschreibbar; sich darauf zu verlassen, dass nur wir hineinschreiben, wäre
 * dieselbe Annahme, die Open Redirect erst möglich macht.
 */
export function gemerkteStelle(): string | null {
  return gemerkteStelleMitTitel()?.jobId ?? null;
}

/** Wie `gemerkteStelle`, aber mit dem Titel für den Hinweis. */
export function gemerkteStelleMitTitel(): GemerkteStelle | null {
  const s = speicher();
  if (s === null) return null;
  const roh = s.getItem(SCHLUESSEL);
  if (roh === null) return null;
  try {
    const wert = JSON.parse(roh) as Partial<Gemerkt>;
    if (typeof wert.jobId !== "string" || !UUID.test(wert.jobId)) {
      vergissStelle();
      return null;
    }
    if (typeof wert.gemerktAm !== "number" || Date.now() - wert.gemerktAm > GUELTIG_MS) {
      vergissStelle();
      return null;
    }
    return { jobId: wert.jobId, titel: typeof wert.titel === "string" ? wert.titel : "" };
  } catch {
    vergissStelle();
    return null;
  }
}

export function vergissStelle(): void {
  try {
    speicher()?.removeItem(SCHLUESSEL);
  } catch {
    // siehe oben
  }
}
