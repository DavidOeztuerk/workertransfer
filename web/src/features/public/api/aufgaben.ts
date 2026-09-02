import { RESUME_BASE_URL, TRANSFER_BASE_URL } from "../../../env";
import { request } from "../../../core/api/client";

/**
 * Was auf jemanden wartet — zusammengesetzt im Browser, nicht im Backend.
 *
 * Ein Dienst, der diese Übersicht lieferte, müsste über vier Dienstgrenzen
 * hinweg lesen; genau das schliesst ADR-0004 aus. Die Oberfläche fragt jeden
 * Dienst nach dem, wofür er zuständig ist, und legt die Antworten nebeneinander.
 *
 * <strong>Gezählt wird nur, was eine HANDLUNG erwartet.</strong> Eine Übersicht,
 * die auch anzeigt, was gerade von selbst läuft, ist eine Liste — und eine Liste
 * übersieht man.
 *
 * <strong>Gezählt werden Vorgänge, nie Personen</strong> (ADR-0022/0026).
 *
 * Die vier Lesezugriffe stehen hier und nicht in einem geteilten Client: die
 * alten Module (`src/market/client.ts`, `src/resume/client.ts`,
 * `src/transfers/client.ts`) wandern nach `features/person` und `features/work`
 * und gehören anderen. Ein Import dorthin wäre eine Kopplung an Code, den
 * dieser Bereich nicht sieht. Preis: die Zählregeln stehen vorerst zweimal.
 */

/** Der Draht ist snake_case — hier steht er unverändert. */
type AnfrageStatus = "PENDING" | "GRANTED" | "DECLINED";

interface AnfrageZeile {
  status: AnfrageStatus;
}

type VorgangStatus =
  | "interested"
  | "talking"
  | "offered"
  | "accepted"
  | "completed"
  | "declined"
  | "withdrawn";

interface VorgangZeile {
  status: VorgangStatus;
  requires_release: boolean;
  release_confirmed: boolean;
}

export interface Aufgabenstand {
  /** Unternehmen, die wissen wollen, ob jemand ansprechbar ist. */
  marktanfragen: number;
  /** Anfragen nach dem eigenen Lebenslauf. */
  lebenslaufanfragen: number;
  /** Gespräche, in denen die Person am Zug ist. */
  eigeneGespraeche: number;
  /** Vorgänge, in denen das Unternehmen am Zug ist. `0` ohne aktives Unternehmen. */
  firmenvorgaenge: number;
  /**
   * Mindestens eine Abfrage ist gescheitert.
   *
   * Dann wird NICHTS gezählt statt null gezählt: eine Zahl auf einer
   * fehlgeschlagenen Abfrage wiegt in Sicherheit, und „nichts liegt an" ist
   * genau die Aussage, die dann falsch wäre.
   */
  unvollstaendig: boolean;
}

const LEER: Aufgabenstand = {
  marktanfragen: 0,
  lebenslaufanfragen: 0,
  eigeneGespraeche: 0,
  firmenvorgaenge: 0,
  unvollstaendig: false,
};

/**
 * Die Person ist am Zug: sie hat Interesse zu beantworten, ein Angebot vor sich,
 * oder eine zugesagte Freigabe steht noch aus.
 */
function wartetAufDiePerson(vorgang: VorgangZeile): boolean {
  return (
    vorgang.status === "interested" ||
    vorgang.status === "offered" ||
    (vorgang.status === "accepted" && vorgang.requires_release && !vorgang.release_confirmed)
  );
}

/** Das Unternehmen ist am Zug. */
function wartetAufDasUnternehmen(vorgang: VorgangZeile): boolean {
  return (
    vorgang.status === "talking" || (vorgang.status === "accepted" && !vorgang.requires_release)
  );
}

const offen = (zeilen: AnfrageZeile[]): number =>
  zeilen.filter((zeile) => zeile.status === "PENDING").length;

/**
 * Fragt alle vier Quellen und legt die Antworten nebeneinander.
 *
 * `mitFirma` entscheidet, ob die Firmenliste überhaupt gefragt wird: ohne
 * aktives Unternehmen antwortet der Dienst 403, und ein Fehler, den die Anfrage
 * selbst erzeugt hat, dürfte die Übersicht nicht als unvollständig markieren.
 */
export async function ladeAufgaben(
  mitFirma: boolean,
  signal?: AbortSignal
): Promise<Aufgabenstand> {
  const [markt, lebenslauf, eigene, firma] = await Promise.all([
    request<AnfrageZeile[]>(TRANSFER_BASE_URL, "/market/me/requests", { signal }),
    request<AnfrageZeile[]>(RESUME_BASE_URL, "/resumes/me/requests", { signal }),
    request<VorgangZeile[]>(TRANSFER_BASE_URL, "/transfers/me", { signal }),
    mitFirma
      ? request<VorgangZeile[]>(TRANSFER_BASE_URL, "/transfers", { signal })
      : Promise.resolve({ ok: true as const, value: [] as VorgangZeile[] }),
  ]);

  return {
    ...LEER,
    marktanfragen: markt.ok ? offen(markt.value ?? []) : 0,
    lebenslaufanfragen: lebenslauf.ok ? offen(lebenslauf.value ?? []) : 0,
    eigeneGespraeche: eigene.ok ? (eigene.value ?? []).filter(wartetAufDiePerson).length : 0,
    firmenvorgaenge: firma.ok ? (firma.value ?? []).filter(wartetAufDasUnternehmen).length : 0,
    unvollstaendig: !markt.ok || !lebenslauf.ok || !eigene.ok || !firma.ok,
  };
}
