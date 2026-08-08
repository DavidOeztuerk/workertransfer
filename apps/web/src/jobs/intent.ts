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
  gemerktAm: number;
}

function speicher(): Storage | null {
  // Safari im privaten Modus und abgeschaltete Speicher werfen beim Zugriff.
  // Eine gemerkte Stelle ist Komfort — sie darf nichts umwerfen.
  try {
    return typeof window === "undefined" ? null : window.localStorage;
  } catch {
    return null;
  }
}

export function merkeStelle(jobId: string): void {
  if (!UUID.test(jobId)) return;
  const s = speicher();
  if (s === null) return;
  try {
    s.setItem(SCHLUESSEL, JSON.stringify({ jobId, gemerktAm: Date.now() } satisfies Gemerkt));
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
    return wert.jobId;
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
