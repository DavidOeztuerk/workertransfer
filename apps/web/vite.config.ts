/// <reference types="vitest/config" />
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
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
