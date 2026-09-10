/**
 * Was das Anmeldeformular schon im Browser sehen kann.
 *
 * Der Browser entscheidet nichts — die Absage spricht der Server (422). Nötig,
 * weil das Formular `noValidate` trägt und `type="email"` damit wirkungslos ist.
 */

/**
 * Die Mindestlänge, wie sie der Server kennt (`PasswordPolicy`). Bewusst
 * doppelt: der Server bleibt die Autorität, eine Abweichung kostet nur einen
 * Hinweis zur falschen Zeit.
 */
export const PASSWORT_MINDESTLAENGE = 12;

/** Die Obergrenze aus bcrypt: 72 BYTES, nicht Zeichen — „ä" ist zwei. */
export const PASSWORT_HOECHSTBYTES = 72;

/** Wie viele Bytes dieses Passwort in UTF-8 belegt. */
export function passwortBytes(passwort: string): number {
  return new TextEncoder().encode(passwort).length;
}

/**
 * Sieht das nach einer zustellbaren Adresse aus? Absichtlich grosszügig — sie
 * soll den Tippfehler fangen, nicht RFC 5321 nachbauen.
 */
export function siehtNachAdresseAus(email: string): boolean {
  const geputzt = email.trim();

  if (geputzt.length === 0 || geputzt.length > 254) return false;
  if (/\s/.test(geputzt)) return false;

  const teile = geputzt.split("@");
  if (teile.length !== 2) return false;

  const lokal = teile[0] ?? "";
  const domain = teile[1] ?? "";
  if (lokal.length === 0 || domain.length === 0) return false;

  const punkt = domain.lastIndexOf(".");
  return punkt > 0 && punkt < domain.length - 1;
}

/** Der Katalogschlüssel für das, was an der Adresse nicht stimmt — oder `null`. */
export function emailFehler(email: string): string | null {
  // Ein leeres Feld ist noch kein Fehler.
  if (email.trim().length === 0) return null;

  return siehtNachAdresseAus(email) ? null : "registrierung.emailUngueltig";
}

/** Der Katalogschlüssel für das, was am Passwort nicht stimmt — oder `null`. */
export function passwortFehler(passwort: string): string | null {
  if (passwort.length === 0) return null;
  if (passwort.length < PASSWORT_MINDESTLAENGE) return "registrierung.passwortZuKurz";
  if (passwortBytes(passwort) > PASSWORT_HOECHSTBYTES) return "registrierung.passwortZuLang";

  return null;
}
