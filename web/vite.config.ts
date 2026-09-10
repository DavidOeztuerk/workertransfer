/// <reference types="vitest/config" />
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  server: {
    // Der Entwicklungsserver wird durch das Gateway erreicht, und Ocelot
    // schickt den Host des ZIELS mit — also `web`, den Compose-Namen. Vite
    // weist seit 6.x jeden fremden Host ab (Schutz vor DNS-Rebinding), und die
    // Antwort war `Blocked request. This host ("web") is not allowed.`
    //
    // Nur dieser eine Name, nicht `true`: die Abwehr bleibt für alles andere
    // stehen. In Produktion stellt nginx die gebauten Dateien zu und die Frage
    // stellt sich nicht.
    allowedHosts: ["web"],

    // WOHIN DER HMR-SOCKET GEHT — und warum er nicht der Seite folgen darf.
    //
    // Ohne diese Zeile leitet Vites HMR-Client seine Adresse aus der
    // SEITENADRESSE ab. Wer über das Gateway kommt, bekommt damit
    // `ws://localhost:8090/?token=…` — und dort endet die Anfrage im Leeren:
    // ein WebSocket-Handshake trägt `Sec-Fetch-Dest: websocket` und nicht
    // `document`, wird von der `Navigation`-Zwischenschicht deshalb NICHT auf
    // `/__ui/` umgeschrieben, findet in `ocelot.json` keine Route und bekommt
    // von Kestrel ein leeres `200`. Der Browser wollte `101 Switching
    // Protocols` und meldet „There was a bad response from the server".
    //
    // Über das Gateway zu kommen ist dabei der NORMALFALL und kein Umweg: die
    // Bestätigungsmail schickt `${WEB_URL}/verify?token=…`, und das ist
    // `http://localhost:8090` — eine Plattform mit EINEM Ursprung kann in einer
    // Mail keinen Entwicklungshafen nennen. Wer sein Konto bestätigt, landet
    // also zwangsläufig hier und surft von dort weiter.
    //
    // Der Socket geht deshalb fest an den Dev-Server, der ohnehin auf 5173
    // veröffentlicht ist. KEINE `ws`-Route im Gateway: die trüge reine
    // Entwicklungskonfiguration in eine Datei, die in Produktion mitfährt — und
    // dort gibt es gar kein HMR, weil nginx gebaute Dateien zustellt.
    hmr: { host: "localhost", port: 5173 },
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test/setup.ts"],
    // Nur src/: e2e/ gehört Playwright. Vitests Voreinstellung würde die
    // .spec.ts dort einsammeln und an `import { test } from
    // "@playwright/test"` scheitern.
    include: ["src/**/*.{test,spec}.{ts,tsx}"],
    // 5 Sekunden (die Voreinstellung) reichen für die Behauptung, aber nicht
    // immer für das, was davor passiert: unter Last — laufender
    // docker-compose-Stack, parallel eine Playwright-Reise — dauern Transform
    // und Import einzelner Module länger, als der ganze Test dauern darf. Das
    // Ergebnis war ein Lauf mit 28 roten Tests, der nächste mit 18, der
    // übernächste mit einem — alle an wechselnden Stellen, alle mit
    // „Test timed out in 5000ms", und alle grün, sobald die Maschine Luft hatte.
    //
    // Eine Testsuite, deren Ergebnis von der Maschinenlast abhängt, ist keine.
    // Die höhere Grenze verlangsamt keinen grünen Lauf: sie greift nur dort,
    // wo vorher abgebrochen wurde.
    testTimeout: 20_000,
  },
});
