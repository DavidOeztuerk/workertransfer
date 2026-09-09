---
description: Alle Tore in der richtigen Reihenfolge fahren — getrennt, wie es sein muss
---

Fahre die Abnahme dieses Repositoriums, **in getrennten Aufrufen**, und melde am
Ende eine Zahl je Tor. Brich nicht ab, wenn eines rot ist — fahre durch und
berichte alles.

```bash
dotnet build                                   # 0 Warnungen erwartet
./scripts/test-dotnet.sh                       # eigener Aufruf!
cd web && pnpm check && pnpm test && pnpm build
```

Danach, nur wenn der Stapel läuft (`docker compose ps`):

```bash
make routenkarte
cd web && pnpm exec playwright test
```

**Warum getrennt:** `build && test` in einem Aufruf killt die Testcontainers,
und die Fehlschläge sehen dabei aus wie echte Testfehler. Das hat mehrfach Zeit
gekostet.

**Nie `dotnet test` über die Lösung** — fünfzehn Postgres-Behälter auf einmal,
der ResourceReaper läuft ab, und *alle* Reihen fallen binnen einer Millisekunde
mit `TypeInitializationException`. Das sieht aus wie ein kaputter Build und ist
keiner.

Melde je Tor: die Zahl, nicht „grün". Eine Reihe, die die Hälfte still
übersprungen hat, sieht sonst aus wie eine bestandene.
