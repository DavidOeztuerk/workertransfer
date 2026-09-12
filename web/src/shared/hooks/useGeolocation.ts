import { useCallback, useState } from "react";
import { useTranslation } from "react-i18next";

/** Wie genau eine Position das Haus verlässt. */
const STELLEN = 2;

/**
 * Ein Ort, so ungenau wie er sein darf.
 *
 * Zwei Nachkommastellen sind gut ein Kilometer. Feiner wäre nicht besser,
 * sondern nur mehr: die Stellen tragen einen ORTSNAMEN, und ihre Koordinaten
 * sind die des Stadtmittelpunkts — eine genauere Position könnte an keiner
 * Antwort etwas ändern. Sie stünde nur in der Adresszeile und damit in den
 * Zugriffsprotokollen.
 */
export interface Standort {
  lat: number;
  lon: number;
}

export interface Ortung {
  /** Der gerundete Standort, oder `null`, solange keiner erfragt wurde. */
  standort: Standort | null;
  /** Läuft gerade eine Abfrage? */
  laedt: boolean;
  /** Was schiefging, in einem Satz für Menschen. `null`, wenn nichts. */
  fehler: string | null;
  /** Kann dieser Browser das überhaupt? */
  moeglich: boolean;
  /** Fragt den Browser — der fragt die Person. */
  frage: () => void;
  /** Vergisst die Position wieder. */
  vergiss: () => void;
}

/**
 * Der eigene Standort — auf Knopfdruck, nie von selbst.
 *
 * <strong>Es gibt keinen Aufruf beim Laden der Seite.</strong> Der Browser
 * fragt die Person um Erlaubnis, sobald `getCurrentPosition` läuft; ein Abruf
 * im Effekt hiesse also, ungefragt einen Systemdialog aufzuklappen, bevor
 * jemand gesagt hat, dass er nach Entfernung filtern will. Der Knopf IST die
 * Einwilligung — dieselbe Regel wie beim Entwurfsknopf (ADR-0024).
 *
 * <strong>Nichts wird gespeichert.</strong> Die Position lebt in diesem Zustand
 * und endet mit der Seite: kein `localStorage`, kein Profilfeld, keine Zeile in
 * einer Tabelle. Ein gemerkter Wohnort wäre ein personenbezogenes Datum, das
 * niemand angelegt hat — und über das die Auskunft dann schweigen würde.
 *
 * <strong>Gerundet, bevor sie irgendwohin geht.</strong> Siehe `Standort`.
 */
export function useGeolocation(): Ortung {
  const { t } = useTranslation();
  const [standort, setStandort] = useState<Standort | null>(null);
  const [laedt, setLaedt] = useState(false);
  const [fehler, setFehler] = useState<string | null>(null);

  const moeglich = typeof navigator !== "undefined" && "geolocation" in navigator;

  const frage = useCallback(() => {
    if (!moeglich) {
      setFehler(t("standort.nichtMoeglich"));
      return;
    }

    setLaedt(true);
    setFehler(null);

    navigator.geolocation.getCurrentPosition(
      (position) => {
        const runde = (wert: number) => Number(wert.toFixed(STELLEN));

        setStandort({
          lat: runde(position.coords.latitude),
          lon: runde(position.coords.longitude),
        });
        setLaedt(false);
      },
      (ausfall) => {
        // Drei Fälle, drei Sätze. Ein gemeinsames „Standort nicht verfügbar"
        // liesse offen, ob die Person selbst abgelehnt hat — dann wäre der
        // Ratschlag „nochmal versuchen" schlicht falsch.
        setFehler(
          ausfall.code === ausfall.PERMISSION_DENIED
            ? t("standort.abgelehnt")
            : ausfall.code === ausfall.TIMEOUT
              ? t("standort.zuLange")
              : t("standort.unbekannt")
        );
        setLaedt(false);
      },
      {
        // Kein GPS-Fix: die Antwort wird ohnehin auf einen Kilometer gerundet,
        // und `enableHighAccuracy` kostet auf einem Telefon spürbar Strom für
        // eine Genauigkeit, die hier weggeworfen wird.
        enableHighAccuracy: false,
        timeout: 10000,
        maximumAge: 300000,
      }
    );
  }, [moeglich, t]);

  const vergiss = useCallback(() => {
    setStandort(null);
    setFehler(null);
  }, []);

  return { standort, laedt, fehler, moeglich, frage, vergiss };
}
