// „Danach geht es zurück zu …" — auf der Anmelde- und der Registrierseite.
//
// Ohne diesen Satz ist die Rückkehr Magie: man klickt „Bewerben", landet
// unvermittelt beim Anmelden und weiss nicht, ob die Stelle noch bekannt ist.
// Und wenn die Absicht verloren geht — anderes Gerät, abgelaufen, gelöschter
// Speicher —, merkt es niemand, weil nie etwas versprochen wurde.
//
// Ohne Abfrage: der Titel liegt neben der ID im Speicher (siehe intent.ts).
// Ihn hier nachzuladen hätte die Anmeldeseite von einem QueryClient abhängig
// gemacht — eine Netzabfrage für einen Satz, der nichts entscheidet.

import { gemerkteStelleMitTitel } from "./intent";

export function ZurueckHinweis() {
  const gemerkt = gemerkteStelleMitTitel();
  if (gemerkt === null) return null;

  return (
    <p className="page__note">
      {gemerkt.titel === "" ? (
        "Danach geht es zurück zu der Stelle, auf die du dich bewerben wolltest."
      ) : (
        <>
          Danach geht es zurück zu: <strong>{gemerkt.titel}</strong>
        </>
      )}
    </p>
  );
}
