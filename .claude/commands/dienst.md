---
description: Einen neuen Dienst anlegen — drei Schritte, kein neues Dockerfile
argument-hint: <name>
---

Lege den Dienst `$1-service` nach dem Muster der bestehenden elf an.

**Zuerst lesen:** `CLAUDE.md` (Abschnitte „Die Services", „Layering", „Die
Outbox"), und einen kleinen bestehenden Dienst als Vorlage — `portfolio-service`
ist der übersichtlichste.

**Aufbau** (je ein Projekt, ohne Ausnahme):

```
<Name>.Api  ->  <Name>.Application  ->  <Name>.Domain
                       |                     ^
                       └-- <Name>.Infrastructure --┘
   <Name>.Contracts — versionierte DTOs an der Grenze
```

Repository-*Schnittstellen* in Domain, Umsetzungen in Infrastructure. Alle
Verdrahtung hinter **einem** `Add<Name>Infrastructure()` (ADR-0003).

**Drei Schritte drumherum, und kein neues Dockerfile** (ADR-0028):

1. Die Datenbank in `scripts/initdb/`
2. Der Einstiegspunkt in `docker/dotnet-service.Dockerfile`
3. Ein kopierter Block in `docker-compose.yml` mit drei geänderten Werten
   — plus die Route in `src/gateway/WorkerTransfer.Gateway/ocelot.json`

**Dann sofort:**

- Eintrag in `docs/routenkarte.yml` je Endpunkt × drei Handlungsformen —
  `RoutenkarteTests` geht sonst zu Recht rot.
- Löschempfänger, sobald die erste personenbezogene Tabelle steht
  (`LoeschempfaengerTests` liest das EF-Modell).
- Eine Testreihe `tests/WorkerTransfer.<Name>.Tests` **und** ihren Namen in
  `scripts/test-dotnet.sh`.

**Fallen:** nach jeder Modelländerung sofort `dotnet ef migrations add`; ein
änderndes `SichereAsync` braucht `.AsTracking()`; Dienst-zu-Dienst-Rümpfe sind
typisierte Verträge, nie anonyme Objekte.
