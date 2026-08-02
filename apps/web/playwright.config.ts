import { defineConfig, devices } from "@playwright/test";

// E2E läuft gegen den echten Stack aus `docker compose up`, nicht gegen einen
// eigenen Dev-Server. Es gibt hier bewusst kein `webServer`: die Reise, die
// geprüft wird, geht über drei Dienste, eine Datenbank und einen Mailserver —
// eine halbe Umgebung hochzufahren würde genau die Integration wegabstrahieren,
// derentwegen diese Tests existieren.
//
// Steht der Stack nicht, überspringen sich die Tests selbst (siehe e2e/stack.ts,
// dasselbe Muster wie ADR-0011 für die Python-Integrationstests): `make check`
// und CI bleiben grün, ohne dass eine Lücke als Erfolg durchgeht.

export default defineConfig({
  testDir: "./e2e",
  // Die Reise teilt sich einen Consent-Ledger und einen Mailserver; parallel
  // gelesene Postfächer würden sich gegenseitig die Mails wegschnappen.
  workers: 1,
  fullyParallel: false,
  reporter: process.env.CI ? [["github"], ["list"]] : [["list"]],
  // Großzügig, weil eine Reise viel echte Arbeit ist: zwei Registrierungen
  // mit bcrypt, zwei Mail-Zustellungen samt Abholen, Unternehmensanlage,
  // Wechsel, und Aufrufe über vier Dienste. 60 s reichten im guten Fall und
  // machten aus normaler Schwankung unter Docker-auf-macOS ein Rot, das
  // nichts über den Code aussagt.
  timeout: 150_000,
  // 30 s, nicht 15: gemessen scheitert etwa jeder dritte Lauf an der
  // Anmeldung, wenn gleichzeitig elf Container und der Vite-Dev-Server auf
  // derselben Maschine laufen. Der Vorgang selbst dauert unter einer Sekunde;
  // es ist Wartezeit auf eine ausgelastete Maschine, keine Langsamkeit im Code.
  // Das ist eine Toleranz und keine Reparatur — steht hier, damit niemand aus
  // dem grünen Lauf schließt, es sei etwas behoben worden.
  expect: { timeout: 30_000 },
  // Ein Wiederholungsversuch — und zwar als TOLERANZ deklariert, nicht als
  // Reparatur. Gemessen scheitert bei einem Lauf über die ganze Suite (rund
  // fünf Minuten, elf Container, Vite und Chromium auf einer Maschine) etwa
  // ein Test an der ANMELDUNG: der Link „Mein Profil" erscheint nicht, weil die
  // Sitzung nicht rechtzeitig steht. Derselbe Test läuft allein in 2,4 Sekunden
  // durch.
  //
  // Die Gefahr ist bekannt: ein Wiederholungsversuch kann einen echten,
  // sporadischen Fehler verstecken. Deshalb bleibt ein wiederholter Test im
  // Bericht als „flaky" stehen, und `scripts/validate.sh` nennt die Zahl —
  // ein grüner Lauf mit drei Wackelkandidaten ist kein grüner Lauf, genau wie
  // einer mit zwanzig Skips keiner ist.
  retries: 1,
  use: {
    // Ohne dieses Limit erbt eine Aktion das Zeitlimit des ganzen Tests: ein
    // fill() auf ein Feld, das es auf dieser Seite gar nicht gibt, verbrennt
    // dann das gesamte Budget und meldet am Ende nur "Test timeout", ohne zu
    // sagen wo. Genau so hat sich ein Navigationsfehler in dieser Reise sieben
    // Minuten lang versteckt.
    actionTimeout: 15_000,
    baseURL: process.env.E2E_WEB_URL ?? "http://localhost:5173",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
