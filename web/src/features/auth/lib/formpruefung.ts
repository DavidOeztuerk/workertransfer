/**
 * Was das Anmeldeformular schon im Browser sehen kann.
 *
 * <strong>Der Browser entscheidet nichts.</strong> Die Absage spricht immer der
 * Server aus (422) — dieselbe Regel wie bei der Freemail-Warnung daneben. Hier
 * wird nur sichtbar gemacht, was ohnehin gilt, und zwar BEVOR jemand abschickt.
 *
 * <strong>Warum es das überhaupt braucht.</strong> Das Formular trägt
 * `noValidate`, damit es seine Fehler selbst zeigt statt der Browser-Blase —
 * und damit war `type="email"` wirkungslos. Am 10.09.2026 kam so die Adresse
 * `bus` durch: der Server legte ein Konto an, die Mail scheiterte in einem
 * Hintergrundlauf, und zurück blieb ein Konto auf `pending`, das niemand je
 * bestätigen kann. `noValidate` ohne eigene Prüfung ist keine eigene Prüfung.
 */

/**
 * Die Mindestlänge, wie sie der Server kennt (`PasswordPolicy`).
 *
 * <strong>Sie steht hier ein zweites Mal, und das ist bewusst.</strong> Der
 * Server bleibt die Autorität: er weist ein zu kurzes Passwort ab, auch wenn
 * diese Zahl hier falsch wäre. Der einzige Schaden einer Abweichung ist ein
 * Hinweis, der zu früh oder zu spät erscheint — kein durchgelassenes Passwort.
 * Die Zahl ungenannt zu lassen wäre schlechter: dann tippt jemand elf Zeichen
 * und erfährt es erst nach dem Absenden.
 */
export const PASSWORT_MINDESTLAENGE = 12;

/**
 * Die Obergrenze aus bcrypt (72 Bytes), in ZEICHEN gerechnet ist sie das nicht.
 *
 * Gezählt werden Bytes, weil bcrypt Bytes zählt: „ä" ist zwei davon. Wer ein
 * langes Passwort mit Umlauten wählt, liefe sonst in eine Absage, die er sich
 * nicht erklären kann.
 */
export const PASSWORT_HOECHSTBYTES = 72;

/** Wie viele Bytes dieses Passwort in UTF-8 belegt. */
export function passwortBytes(passwort: string): number {
  return new TextEncoder().encode(passwort).length;
}

/**
 * Sieht das nach einer zustellbaren Adresse aus?
 *
 * Absichtlich GROSSZÜGIG. Diese Prüfung darf niemanden aussperren, dessen
 * Adresse ungewöhnlich aussieht und trotzdem gilt — sie soll den Tippfehler
 * fangen, nicht die Grammatik von RFC 5321 nachbauen. Der Server prüft mit
 * demselben Parser, der später versendet; das ist die eigentliche Absage.
 *
 * Verlangt wird darum nur das, woran es scheitert: genau ein `@`, etwas davor,
 * und dahinter eine Domain mit einem Punkt, der nicht am Rand steht.
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
  // Ein LEERES Feld ist noch kein Fehler: wer gerade erst anfängt zu tippen,
  // bekommt sonst Rot angezeigt, bevor er etwas falsch gemacht hat.
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
