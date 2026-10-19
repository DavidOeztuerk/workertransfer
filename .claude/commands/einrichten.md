---
description: Eine Drittanbieter-Anbindung im Browser einrichten — hält an jeder Anmeldung und wartet
argument-hint: <google|microsoft|dropbox|xing|apple> [ziel]
---

Richte die Anbindung **$ARGUMENTS** ein, über den Agenten
`anbieter-einrichter`.

Vorher, hier im Hauptgespräch:

1. Ist Chrome mit der Erweiterung verbunden? (`tabs_context_mcp`.) Wenn nicht:
   bitte den Nutzer, Chrome zu öffnen, und warte.
2. Steht der Anbieter in `docs/DRITTANBIETER.md` als *jetzt einrichten*? Apple
   steht dort als *aufgeschoben* — dann nicht starten, sondern daran erinnern,
   warum.
3. Läuft der Stapel (`curl -s localhost:8090/health/live`)? Ohne ihn lässt
   sich am Ende nichts prüfen.

Dann den Agenten starten — mit dem Anbieter, dem Ziel und dem Satz, dass er an
Passwort, zweitem Faktor, Bedingungen und Zahlung anhält und selbst wartet.

Kommt er mit einem Haltepunkt zurück, den er nicht abwarten konnte: dem Nutzer
die eine Zeile weitergeben, auf sein „weiter" warten und den Agenten mit
`SendMessage` fortsetzen — nicht neu starten, sonst beginnt er von vorn.
